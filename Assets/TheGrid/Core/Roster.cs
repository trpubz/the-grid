using System;
using System.Collections.Generic;
using System.IO;

namespace TheGrid.Core
{
    // One agent in the roster. Field names are the JSON keys; see docs/roster-snapshot.md.
    [Serializable]
    public sealed class AgentRow
    {
        public string sessionId = "", name = "", account = "", app = "", repoRoot = "", repoName = "", branch = "", ticket = "", prUrl = "", state = "", rawStatus = "", workspace = "";
        public int prNumber;
        public long stateSince;
        public List<SubagentRow> subagents = new List<SubagentRow>();

        internal AgentRow Copy()
        {
            var c = (AgentRow)MemberwiseClone();
            c.subagents = new List<SubagentRow>(subagents);
            return c;
        }
    }

    // What the viewer draws. A snapshot is never changed after Build returns it.
    [Serializable]
    public sealed class RosterSnapshot
    {
        public long generatedAt;
        public List<AgentRow> agents = new List<AgentRow>();
        public List<string> errors = new List<string>();
    }

    public static class RosterFormat
    {
        // "4m", "1h 12m". Never negative.
        public static string Elapsed(long sinceMs, long nowMs)
        {
            long minutes = Math.Max(0, nowMs - sinceMs) / 60000;
            return minutes < 60 ? minutes + "m" : minutes / 60 + "h " + minutes % 60 + "m";
        }
    }

    // Turns the live session list into roster rows, remembering what it has seen between calls.
    public sealed class RosterBuilder
    {
        public const long OfflineKeepMs = 43200000; // 12 hours

        sealed class Memory
        {
            public AgentRow Row = new AgentRow();
            public AgentState State;
            public bool Seen;
            public string TranscriptPath;
            public long TranscriptStamp;
        }

        readonly Settings settings;
        readonly Dictionary<string, Memory> memory = new Dictionary<string, Memory>();

        public RosterBuilder(Settings settings) { this.settings = settings; }

        public static string AccountLabel(string configDir)
        {
            var name = Path.GetFileName((configDir ?? "").TrimEnd(Path.DirectorySeparatorChar));
            return name.StartsWith(".claude-", StringComparison.Ordinal) ? name.Substring(".claude-".Length) : name.TrimStart('.');
        }

        public RosterSnapshot Build(IReadOnlyList<LiveSession> live, IReadOnlyList<string> errors, long nowMs)
        {
            var liveKeys = new HashSet<string>();
            foreach (var s in live)
            {
                var key = s.ConfigDir + "|" + s.SessionId;
                liveKeys.Add(key);
                if (!memory.TryGetValue(key, out var m)) memory[key] = m = new Memory();
                Update(m, s, nowMs);
            }

            var gone = new List<string>();
            foreach (var kv in memory)
            {
                if (liveKeys.Contains(kv.Key)) continue;
                var m = kv.Value;
                if (m.State != AgentState.Offline)
                {
                    m.State = AgentState.Offline;
                    m.Row.state = AgentState.Offline.ToString();
                    m.Row.rawStatus = "";
                    m.Row.stateSince = nowMs;
                    m.Row.subagents = new List<SubagentRow>();
                }
                else if (nowMs - m.Row.stateSince > OfflineKeepMs) gone.Add(kv.Key);
            }
            foreach (var key in gone) memory.Remove(key);

            var ordered = new List<Memory>(memory.Values);
            ordered.Sort((a, b) =>
            {
                int c = a.State.CompareTo(b.State);
                if (c == 0) c = a.Row.stateSince.CompareTo(b.Row.stateSince);
                return c != 0 ? c : string.CompareOrdinal(a.Row.name, b.Row.name);
            });

            var snap = new RosterSnapshot { generatedAt = nowMs };
            foreach (var m in ordered) snap.agents.Add(m.Row.Copy());
            if (errors != null) snap.errors.AddRange(errors);
            return snap;
        }

        void Update(Memory m, LiveSession s, long nowMs)
        {
            var row = m.Row;
            var state = LiveSessions.Map(s);
            bool changed = !m.Seen || state != m.State;
            m.Seen = true;
            m.State = state;

            row.sessionId = s.SessionId;
            row.name = s.Name;
            row.account = AccountLabel(s.ConfigDir);
            row.state = state.ToString();
            row.rawStatus = LiveSessions.RawStatus(s);

            var repo = RepoResolver.Resolve(s.Cwd);
            row.repoRoot = repo.Root;
            row.repoName = repo.Name;
            row.workspace = settings.WorkspaceFor(repo.Root);

            long since = 0;
            if (s.Pid > 0 && SessionFiles.TryReadSessionFile(s.ConfigDir, s.Pid, out var file))
            {
                row.app = file.Entrypoint;
                since = file.StatusUpdatedAt;
            }
            if (since > 0) row.stateSince = since;
            else if (changed) row.stateSince = nowMs;

            if (m.TranscriptPath == null) m.TranscriptPath = SessionFiles.FindTranscript(s.ConfigDir, s.SessionId);
            if (m.TranscriptPath != null)
            {
                long stamp = Stamp(m.TranscriptPath);
                if (stamp != m.TranscriptStamp)
                {
                    m.TranscriptStamp = stamp;
                    var meta = SessionFiles.ReadTranscriptTail(m.TranscriptPath);
                    if (meta.Branch != "") { row.branch = meta.Branch; row.ticket = Ticket.FromBranch(meta.Branch); }
                    // A pull request once seen is kept: a later tail read may no longer contain its record.
                    if (meta.PrNumber > 0) { row.prNumber = meta.PrNumber; row.prUrl = meta.PrUrl; }
                }
            }
            row.subagents = Subagents.ReadActive(m.TranscriptPath, nowMs);
        }

        // Changes whenever the file is appended to, so an unchanged transcript is not read again.
        static long Stamp(string path)
        {
            try { var fi = new FileInfo(path); return fi.LastWriteTimeUtc.Ticks ^ (fi.Length << 1); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { return 0; }
        }
    }
}
