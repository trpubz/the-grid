using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace TheGrid.Core
{
    // Codex threads on this Mac as roster rows, read from Codex's own session files:
    // <codex folder>/sessions/<y>/<m>/<d>/rollout-<time>-<id>.jsonl. Read only: nothing is written there, no Codex program
    // is run, and nothing is asked of the network.
    //
    // Codex keeps no list of the threads that are open, so a thread counts as open while its file has changed lately. Only
    // threads a person started are rows: reviewer threads and threads spawned by another thread are left out. What a thread
    // is doing is read from the end of its file: the last mark of a turn starting or ending, and what was done since.
    public sealed class CodexReader
    {
        public const long ShownMs = 12 * 3600000L;   // how long after its last change a thread stays in the roster
        public const long QuietMs = 15 * 60000L;     // an open turn that has written nothing for this long is waiting, not working
        const int TailBytes = 1 << 20, HeadBytes = 2 << 20, NameLetters = 28;

        [Serializable] sealed class Record { public string timestamp = "", type = ""; public Payload payload = new Payload(); }
        [Serializable] sealed class Payload
        {
            public string type = "", id = "", cwd = "", originator = "", thread_source = "";
            public Problem error = new Problem();
            public Item item = new Item();
        }
        [Serializable] sealed class Problem { public string message = ""; }
        [Serializable] sealed class Item
        {
            public string type = "";
            public List<string> command = new List<string>();
            public List<Question> questions = new List<Question>();
            public List<Part> content = new List<Part>();
        }
        [Serializable] sealed class Question { public string title = ""; }
        [Serializable] sealed class Part { public string type = "", text = ""; }

        // What is kept of one session file, and of which state of the file it was read.
        sealed class Thread
        {
            public string stamp = "", id = "", cwd = "", app = "", source = "", name = "", repoRoot = "", repoName = "";
            public bool metaRead, nameSought, cut;   // cut: the file is longer than what is read of its end
            public string turn = "", activity = "";  // the last turn mark, and what the open turn last did
            public long turnAt, askedAt;
            public bool failed, asked;
        }

        readonly Settings settings;
        readonly string codexDir;
        readonly Dictionary<string, Thread> threads = new Dictionary<string, Thread>();

        public CodexReader(Settings settings, string codexDir)
        {
            this.settings = settings;
            this.codexDir = codexDir;
        }

        // The rows of every thread that is shown, as of `nowMs`. A file is read again only when it has changed.
        public List<AgentRow> Read(long nowMs)
        {
            var rows = new List<AgentRow>();
            var dir = Path.Combine(codexDir, "sessions");
            if (!settings.codex || !Directory.Exists(dir))
            {
                threads.Clear();
                return rows;
            }
            var seen = new HashSet<string>();
            foreach (var path in Directory.EnumerateFiles(dir, "rollout-*.jsonl", SearchOption.AllDirectories))
            {
                if (!path.EndsWith(".jsonl", StringComparison.Ordinal)) continue;   // not the compressed ones Codex makes of old threads
                var file = new FileInfo(path);
                long changed = new DateTimeOffset(file.LastWriteTimeUtc).ToUnixTimeMilliseconds();
                if (nowMs - changed > ShownMs) continue;
                seen.Add(path);
                if (!threads.TryGetValue(path, out var t)) threads[path] = t = new Thread();
                string stamp = changed + ":" + file.Length;
                if (t.stamp != stamp)
                {
                    t.stamp = stamp;
                    try { Load(t, path, file.Length); }
                    catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { t.stamp = ""; }   // being written: next time
                }
                if (t.metaRead && t.source == "user" && (t.turn != "" || t.cut)) rows.Add(Row(t, changed, nowMs));
            }
            var gone = new List<string>();
            foreach (var path in threads.Keys) if (!seen.Contains(path)) gone.Add(path);
            foreach (var path in gone) threads.Remove(path);
            return rows;
        }

        AgentRow Row(Thread t, long changed, long nowMs)
        {
            AgentState state;
            long since = t.turnAt > 0 ? t.turnAt : changed;
            string raw = t.turn;
            if (t.turn == "task_complete") state = t.failed ? AgentState.Errored : AgentState.Finished;
            else if (t.turn == "turn_aborted") state = AgentState.Finished;
            else if (t.asked)
            {
                state = AgentState.BlockedOnQuestion;
                since = t.askedAt > 0 ? t.askedAt : since;
                raw = "question";
            }
            else if (nowMs - changed > QuietMs)
            {
                // From outside, an open turn that writes nothing is what a wait for the owner's approval looks like.
                state = AgentState.WaitingOther;
                since = changed;
                raw = "turn open, quiet";
            }
            else
            {
                state = AgentState.Running;
                raw = "task_started";
            }
            return new AgentRow
            {
                sessionId = t.id,
                // shortcut: the thread's first prompt stands in for its name, which is in Codex's database and not in its session files; read the database if the two differ too often
                name = t.name != "" ? t.name : "Codex " + t.id.Substring(0, Math.Min(8, t.id.Length)),
                account = "codex",
                app = t.app,
                repoRoot = t.repoRoot,
                repoName = t.repoName,
                workspace = settings.WorkspaceFor(t.repoRoot),
                state = state.ToString(),
                rawStatus = raw,
                activity = state != AgentState.Running ? "" : t.activity != "" ? t.activity : Activity.Working,
                stateSince = since,
                enter = RosterFormat.EnterCodex(t.id, t.cwd),
            };
        }

        void Load(Thread t, string path, long length)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                if (!t.metaRead || t.name == "" && !t.nameSought) Head(t, stream, length);
                if (!t.metaRead) return;
                // The end of the file: the last turn mark, and what the turn has done since it.
                t.turn = t.activity = "";
                t.turnAt = t.askedAt = 0;
                t.failed = t.asked = false;
                t.cut = length > TailBytes;
                stream.Seek(Math.Max(0, length - TailBytes), SeekOrigin.Begin);
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    if (t.cut) reader.ReadLine();   // the rest of a line that began before what is read
                    string line;
                    while ((line = reader.ReadLine()) != null) Take(t, line);
                }
            }
        }

        // The first record says whose thread it is and where it works; the first thing the owner said gives it a name.
        void Head(Thread t, FileStream stream, long length)
        {
            var reader = new StreamReader(stream, Encoding.UTF8, false, 1 << 16, true);
            long read = 0;
            string line;
            while ((line = reader.ReadLine()) != null && read < HeadBytes)
            {
                read += line.Length + 1;
                if (!t.metaRead)
                {
                    var first = Parse(line);
                    if (first == null || first.type != "session_meta") return;   // not a session file, or not all there yet
                    var meta = first.payload;
                    t.id = meta.id ?? "";
                    t.cwd = meta.cwd ?? "";
                    t.source = meta.thread_source ?? "";
                    // An older record does not say who started the thread: then a person did, unless it says another thread did.
                    if (t.source == "" && !Regex.IsMatch(line, "\"source\"\\s*:\\s*\\{\\s*\"subagent\"")) t.source = "user";
                    t.app = meta.originator == "Codex Desktop" ? "codex-desktop" : meta.originator == "codex-tui" ? "codex-cli" : "codex";
                    var repo = RepoResolver.Resolve(t.cwd);
                    t.repoRoot = repo.Root;
                    t.repoName = repo.Name;
                    t.metaRead = true;
                    if (t.source != "user") return;
                    continue;
                }
                if (line.IndexOf("\"UserMessage\"", StringComparison.Ordinal) < 0) continue;
                var said = Parse(line);
                if (said == null || said.payload.item == null || said.payload.item.type != "UserMessage" || said.payload.item.content == null) continue;
                foreach (var part in said.payload.item.content)
                {
                    t.name = Name(part.text);
                    if (t.name != "") return;
                }
            }
            t.nameSought = read >= HeadBytes;   // not in the part that is looked through: it is not looked for again
        }

        // One record of the end of the file.
        void Take(Thread t, string line)
        {
            bool turn = line.IndexOf("\"task_started\"", StringComparison.Ordinal) >= 0 || line.IndexOf("\"task_complete\"", StringComparison.Ordinal) >= 0
                        || line.IndexOf("\"turn_aborted\"", StringComparison.Ordinal) >= 0;
            if (!turn && line.IndexOf("\"item_completed\"", StringComparison.Ordinal) < 0) return;
            var r = Parse(line);
            if (r == null || r.type != "event_msg") return;
            var p = r.payload;
            switch (p.type)
            {
                case "task_started":
                    t.turn = p.type;
                    t.turnAt = Time(r.timestamp);
                    t.activity = "";
                    t.failed = t.asked = false;
                    break;
                case "task_complete":
                case "turn_aborted":
                    t.turn = p.type;
                    t.turnAt = Time(r.timestamp);
                    t.failed = p.type == "task_complete" && p.error != null && !string.IsNullOrEmpty(p.error.message);
                    t.asked = false;
                    break;
                case "item_completed":
                    if (p.item == null) break;
                    if (p.item.type == "AgentMessage")
                    {
                        t.asked = p.item.questions != null && p.item.questions.Count > 0;
                        t.askedAt = Time(r.timestamp);
                    }
                    else if (p.item.type == "UserMessage") t.asked = false;
                    else if (p.item.type == "CommandExecution") t.activity = Activity.Classify("Bash", string.Join(" ", p.item.command ?? new List<string>()), "");
                    else if (p.item.type == "FileChange") t.activity = Activity.Editing;
                    break;
            }
        }

        static Record Parse(string line)
        {
            try { return JsonUtility.FromJson<Record>(line); }
            catch (ArgumentException) { return null; }
        }

        static long Time(string stamp) =>
            DateTimeOffset.TryParse(stamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at) ? at.ToUnixTimeMilliseconds() : 0;

        // The first line with anything on it, cut to a name's length.
        static string Name(string text)
        {
            foreach (var line in (text ?? "").Split('\n'))
            {
                string words = line.Trim();
                if (words != "") return words.Length <= NameLetters ? words : words.Substring(0, NameLetters) + "…";
            }
            return "";
        }
    }
}
