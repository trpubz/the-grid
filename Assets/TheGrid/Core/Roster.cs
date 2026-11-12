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
        // What a running agent is doing: reading, editing, testing, reviewing or working. Empty in every other state.
        public string activity = "";
        public int prNumber;
        public long stateSince;
        // How to get into the conversation: the command to run in a terminal. And the session's process, 0 for a background
        // session, which has none: where a session already lives is asked of its process.
        public string enter = "";
        public int pid;
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
        // The command that opens a session's conversation in a terminal: a background session is attached to by its short
        // id, any other is resumed by its session id, in the folder it was started in, and for an account other than the
        // default one with that account's folder named. The default account is the one with no folder named; `several` says
        // the owner has more than one account, so that the terminal the command is pasted into may have another's folder
        // set, and then the command unsets it. "" when there is nothing to open it by. It is meant for the owner's shell, so
        // whatever a shell would not read as one plain word is quoted.
        public static string Enter(string kind, string id, string sessionId, string cwd, string configDir, string home, bool several)
        {
            bool background = kind == "background";
            string by = background ? id : sessionId;
            if (string.IsNullOrEmpty(by)) return "";
            bool usual = string.IsNullOrEmpty(configDir) || configDir.TrimEnd('/') == Path.Combine(home ?? "", ".claude");
            return (string.IsNullOrEmpty(cwd) ? "" : "cd " + Word(cwd) + " && ") + (!usual ? "CLAUDE_CONFIG_DIR=" + Word(configDir) + " " : several ? "env -u CLAUDE_CONFIG_DIR " : "")
                + (background ? "claude attach " : "claude --resume ") + Word(by);
        }

        static string Word(string s)
        {
            foreach (char c in s)
                if (!(char.IsLetterOrDigit(c) && c < 128) && "/._-+:@%".IndexOf(c) < 0) return "'" + s.Replace("'", "'\\''") + "'";
            return s;
        }

        // "4m", "1h 12m". Never negative.
        public static string Elapsed(long sinceMs, long nowMs)
        {
            long minutes = Math.Max(0, nowMs - sinceMs) / 60000;
            return minutes < 60 ? minutes + "m" : minutes / 60 + "h " + minutes % 60 + "m";
        }

        // A pull request link comes from a file on disk; only ever hand an https address to the system.
        public static bool IsWebLink(string url) => !string.IsNullOrEmpty(url) && url.StartsWith("https://", StringComparison.Ordinal);
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
            public string Activity = "";
        }

        readonly Settings settings;
        readonly Dictionary<string, Memory> memory = new Dictionary<string, Memory>();

        readonly string home;
        bool several;   // the owner has more than one account: asked once for each roster

        // home: the folder whose .claude is the default account, which a command need not name.
        public RosterBuilder(Settings settings, string home = null)
        {
            this.settings = settings;
            this.home = home ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        public static string AccountLabel(string configDir)
        {
            var name = Path.GetFileName((configDir ?? "").TrimEnd(Path.DirectorySeparatorChar));
            return name.StartsWith(".claude-", StringComparison.Ordinal) ? name.Substring(".claude-".Length) : name.TrimStart('.');
        }

        public RosterSnapshot Build(IReadOnlyList<LiveSession> live, IReadOnlyList<string> errors, long nowMs)
        {
            several = settings.EffectiveConfigDirs(home).Count > 1;
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
                    m.Row.activity = ""; // a session that is gone is not doing anything
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
            bool firstSight = !m.Seen;
            bool changed = firstSight || state != m.State;
            m.Seen = true;
            m.State = state;

            row.sessionId = s.SessionId;
            row.name = s.Name;
            row.account = AccountLabel(s.ConfigDir);
            row.state = state.ToString();
            row.rawStatus = LiveSessions.RawStatus(s);
            row.enter = RosterFormat.Enter(s.Kind, s.Id, s.SessionId, s.Cwd, s.ConfigDir, home, several);
            row.pid = s.Pid;

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
            // No session file (background sessions have none): a session already in this state when first seen has
            // been so since some time after it started, which is closer to the truth than "since this app launched".
            if (since > 0) row.stateSince = since;
            else if (changed) row.stateSince = firstSight && s.StartedAt > 0 ? s.StartedAt : nowMs;

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
                    m.Activity = meta.Activity;
                }
            }
            // Only a running agent is doing anything; when the transcript cannot say what, it is just working.
            row.activity = state != AgentState.Running ? "" : m.Activity != "" ? m.Activity : Activity.Working;
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
