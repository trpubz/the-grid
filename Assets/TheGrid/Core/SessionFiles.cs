using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace TheGrid.Core
{
    public sealed class SessionFileInfo { public long StatusUpdatedAt; public string Entrypoint = ""; }

    // Activity is "" when the window read holds no record of the agent's own. Question is the open question at the end of the transcript, if any.
    // EndQuestion is the one a desktop session's last turn ended on, while no person has answered it.
    public sealed class TranscriptMeta { public string Branch = ""; public int PrNumber; public string PrUrl = ""; public string Activity = ""; public string Question = "", EndQuestion = ""; public bool ApiError; }

    // Read-only access to files Claude Code writes under a config directory.
    public static class SessionFiles
    {
        [Serializable] sealed class SessionJson { public long statusUpdatedAt; public string entrypoint; }
        [Serializable] sealed class Line { public string type, gitBranch, prUrl; public int prNumber; public bool isSidechain, isApiErrorMessage; }
        // An assistant record is parsed with its own class: a user record's content is a string, not a list of blocks.
        [Serializable] sealed class AssistantLine { public string type; public bool isSidechain; public AssistantMessage message; }
        [Serializable] sealed class AssistantMessage { public List<Block> content; }
        [Serializable] sealed class Block { public string type, name, text; public BlockInput input; }
        [Serializable] sealed class BlockInput { public string command, skill, ask; public List<AskedQuestion> questions; }
        [Serializable] sealed class AskedQuestion { public string question; }
        // Who a message came from is parsed with its own class, so another record's own "origin" cannot spoil its line.
        [Serializable] sealed class UserLine { public string type; public bool isSidechain; public Origin origin; public Attachment attachment; }
        [Serializable] sealed class Attachment { public Origin origin; }
        [Serializable] sealed class Origin { public string kind; }

        // <configDir>/sessions/<pid>.json. Undocumented by Claude Code, so every failure is just "no info".
        public static bool TryReadSessionFile(string configDir, int pid, out SessionFileInfo info)
        {
            info = null;
            try
            {
                var j = JsonUtility.FromJson<SessionJson>(File.ReadAllText(Path.Combine(configDir, "sessions", pid + ".json")));
                if (j == null) return false;
                info = new SessionFileInfo { StatusUpdatedAt = j.statusUpdatedAt, Entrypoint = j.entrypoint ?? "" };
                return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException) { return false; }
        }

        // Moves whenever a session file under <configDir>/sessions, or a background session's <configDir>/jobs/<id>/state.json,
        // is rewritten, added or removed. Claude Code rewrites a session's file when its status changes and leaves it alone
        // while idle, so this tells a poll whether anything happened.
        public static string ChangeStamp(string configDir)
        {
            try
            {
                var files = new List<string>();
                var sessions = Path.Combine(configDir, "sessions");
                if (Directory.Exists(sessions)) files.AddRange(Directory.GetFiles(sessions, "*.json"));
                var jobs = Path.Combine(configDir, "jobs");
                if (Directory.Exists(jobs))
                    foreach (var job in Directory.GetDirectories(jobs)) files.Add(Path.Combine(job, "state.json"));
                files.Sort(StringComparer.Ordinal);
                var sb = new StringBuilder();
                foreach (var f in files)
                {
                    var fi = new FileInfo(f);
                    if (fi.Exists) sb.Append(f, configDir.Length, f.Length - configDir.Length).Append(':').Append(fi.LastWriteTimeUtc.Ticks).Append(':').Append(fi.Length).Append(';');
                }
                return sb.ToString();
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { return ""; }
        }

        // <configDir>/projects/*/<sessionId>.jsonl, or null.
        public static string FindTranscript(string configDir, string sessionId)
        {
            var projects = Path.Combine(configDir, "projects");
            if (!Directory.Exists(projects)) return null;
            foreach (var dir in Directory.GetDirectories(projects))
            {
                var path = Path.Combine(dir, sessionId + ".jsonl");
                if (File.Exists(path)) return path;
            }
            return null;
        }

        // shortcut: only the last 256 KB is read, so a pull request recorded earlier is missed until it is written again; scan the whole file once if the PR column is often empty.
        public static TranscriptMeta ReadTranscriptTail(string path, int maxBytes = 262144)
        {
            var meta = ReadTail(path, maxBytes, out bool more);
            // One record of several hundred KB can stand between a question and the end of its turn. Only what closes or asks
            // is looked for that far back.
            // shortcut: 1 MB; of 455 real questions, the two that a session sat idle on past the first window were 399 and 706 KB back
            if (more && maxBytes < FarBytes) meta.EndQuestion = ReadTail(path, FarBytes, out _).EndQuestion;
            return meta;
        }

        const int FarBytes = 1 << 20;

        // `more` when there is more of the file before the window and nothing in it says whether a question is open.
        static TranscriptMeta ReadTail(string path, int maxBytes, out bool more)
        {
            more = false;
            var meta = new TranscriptMeta();
            string text;
            bool startsMidFile;
            try
            {
                // Claude Code is appending to this file while we read it.
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    long start = Math.Max(0, fs.Length - maxBytes);
                    startsMidFile = start > 0;
                    fs.Seek(start, SeekOrigin.Begin);
                    var buf = new byte[fs.Length - start];
                    int read = 0, n;
                    while (read < buf.Length && (n = fs.Read(buf, read, buf.Length - read)) > 0) read += n;
                    text = Encoding.UTF8.GetString(buf, 0, read);
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { return meta; }

            more = startsMidFile;
            int from = startsMidFile ? text.IndexOf('\n') + 1 : 0; // the first line is cut when the window starts mid-file
            int end = text.LastIndexOf('\n');                      // a last line with no newline is still being written
            if ((startsMidFile && from == 0) || end < from) return meta;

            // Newest first, and stop parsing branch lines once one is found: nearly every line carries the branch.
            var lines = text.Substring(from, end - from).Split('\n');
            bool haveBranch = false, havePr = false, haveActivity = false, haveLast = false, haveEnd = false;
            for (int i = lines.Length - 1; i >= 0 && !(haveBranch && havePr && haveActivity && haveLast && haveEnd); i--)
            {
                var line = lines[i];
                // The newest record of the agent's own decides what it is doing.
                if (!haveActivity && line.IndexOf("\"assistant\"", StringComparison.Ordinal) >= 0 && TryActivity(line, out var activity, out var question))
                {
                    meta.Activity = activity;
                    meta.Question = question;
                    haveActivity = true;
                }
                // The newer of a turn's end and a person's message says whether a question is open.
                if (!haveEnd && line.IndexOf("wrap_up\"", StringComparison.Ordinal) >= 0 && TryTurnEnd(line, out var ask))
                {
                    meta.EndQuestion = ask;
                    haveEnd = true;
                }
                else if (!haveEnd && line.IndexOf("\"human\"", StringComparison.Ordinal) >= 0 && IsPersons(line)) haveEnd = true;
                bool branchLine = !haveBranch && line.IndexOf("\"gitBranch\"", StringComparison.Ordinal) >= 0;
                bool prLine = !havePr && line.IndexOf("\"pr-link\"", StringComparison.Ordinal) >= 0;
                // The newest record of the conversation says whether it ended on an API error.
                bool talkLine = !haveLast && (line.IndexOf("\"assistant\"", StringComparison.Ordinal) >= 0 || line.IndexOf("\"user\"", StringComparison.Ordinal) >= 0);
                if (!branchLine && !prLine && !talkLine) continue;
                Line r;
                try { r = JsonUtility.FromJson<Line>(line); }
                catch (ArgumentException) { continue; }
                if (r == null) continue;
                if (talkLine && !r.isSidechain && (r.type == "assistant" || r.type == "user")) { meta.ApiError = r.isApiErrorMessage; haveLast = true; }
                if (branchLine && !string.IsNullOrEmpty(r.gitBranch)) { meta.Branch = r.gitBranch; haveBranch = true; }
                if (prLine && r.type == "pr-link" && r.prNumber > 0) { meta.PrNumber = r.prNumber; meta.PrUrl = r.prUrl ?? ""; havePr = true; }
            }
            more = startsMidFile && !haveEnd;
            return meta;
        }

        // True when the line is an assistant record of the session itself (not a subagent's). Its last tool call decides;
        // with no tool call, its last text does. A last call to AskUserQuestion is the open question.
        static bool TryActivity(string line, out string activity, out string question)
        {
            activity = question = "";
            AssistantLine r;
            try { r = JsonUtility.FromJson<AssistantLine>(line); }
            catch (ArgumentException) { return false; }
            if (r == null || r.type != "assistant" || r.isSidechain) return false;
            var blocks = r.message != null ? r.message.content : null;
            Block tool = null, text = null;
            if (blocks != null)
                foreach (var b in blocks)
                {
                    if (b == null) continue;
                    if (b.type == "tool_use") tool = b;
                    else if (b.type == "text") text = b;
                }
            if (tool != null) activity = TheGrid.Core.Activity.Classify(tool.name, tool.input == null ? "" : !string.IsNullOrEmpty(tool.input.command) ? tool.input.command : tool.input.skill, "");
            else activity = TheGrid.Core.Activity.Classify("", "", text != null ? text.text : "");
            var asked = tool != null && tool.name == "AskUserQuestion" && tool.input != null ? tool.input.questions : null;
            if (asked != null && asked.Count > 0 && asked[0] != null)
            {
                var first = Needs.Clean(asked[0].question);
                if (first != "") question = asked.Count > 1 ? first + " +" + (asked.Count - 1) : first;
            }
            return true;
        }

        // True when the line is a desktop session ending its turn with the next-step box; `ask` is the question the box leaves, or "".
        // The desktop app does not document the box, so anything unexpected in it is no question.
        static bool TryTurnEnd(string line, out string ask)
        {
            ask = "";
            AssistantLine r;
            try { r = JsonUtility.FromJson<AssistantLine>(line); }
            catch (ArgumentException) { return false; }
            if (r == null || r.type != "assistant" || r.isSidechain || r.message == null || r.message.content == null) return false;
            Block end = null;
            foreach (var b in r.message.content)
                if (b != null && b.type == "tool_use" && b.name != null && b.name.EndsWith("wrap_up", StringComparison.Ordinal)) end = b;
            if (end == null) return false;
            // JsonUtility turns a number into the string "5"; only a string is a question.
            if (end.input != null && Regex.IsMatch(line, "\"ask\"\\s*:\\s*\"")) ask = Needs.Clean(end.input.ask);
            return true;
        }

        // True when the line is a message from a person: a user record or, sent while the session is at work, an attachment that
        // holds it. What another session or the app itself says in their place has another origin, or none.
        static bool IsPersons(string line)
        {
            try
            {
                var r = JsonUtility.FromJson<UserLine>(line);
                if (r == null || r.isSidechain) return false;
                var from = r.type == "user" ? r.origin : r.type == "attachment" && r.attachment != null ? r.attachment.origin : null;
                return from != null && from.kind == "human";
            }
            catch (ArgumentException) { return false; }
        }

        // Every session transcript: <configDir>/projects/*/*.jsonl. Subagent transcripts sit deeper and are not listed.
        public static List<string> Transcripts(string configDir)
        {
            var found = new List<string>();
            try
            {
                var projects = Path.Combine(configDir, "projects");
                if (!Directory.Exists(projects)) return found;
                foreach (var dir in Directory.GetDirectories(projects)) found.AddRange(Directory.GetFiles(dir, "*.jsonl"));
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
            found.Sort(StringComparer.Ordinal);
            return found;
        }

        // Calls onLine(line, byte offset of the line) for every complete line after `offset` that contains one of `markers`,
        // and returns the offset just past the last complete line. A last line with no newline is left for the next call.
        // A file shorter than `offset` was replaced and is read from the start. Returns -1 when the file could not be read, so the
        // caller tries again rather than treating it as read. Transcripts reach a gigabyte, so markers are matched on the raw
        // bytes and only matching lines are decoded.
        public static long ReadNewLines(string path, long offset, string[] markers, Action<string, long> onLine, int bufferBytes = 1 << 20)
        {
            var needles = Array.ConvertAll(markers, m => Encoding.UTF8.GetBytes(m));
            try
            {
                // Claude Code is appending to this file while we read it.
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    if (offset > fs.Length) offset = 0;
                    fs.Seek(offset, SeekOrigin.Begin);
                    var buf = new byte[bufferBytes];
                    int have = 0, n; // buf[0..have) is the file from `offset` on, starting at a line start
                    while ((n = fs.Read(buf, have, buf.Length - have)) > 0)
                    {
                        have += n;
                        int start = 0, nl;
                        while ((nl = Array.IndexOf(buf, (byte)'\n', start, have - start)) >= 0)
                        {
                            var line = new ReadOnlySpan<byte>(buf, start, nl - start);
                            foreach (var needle in needles)
                                if (line.IndexOf(new ReadOnlySpan<byte>(needle)) >= 0)
                                {
                                    onLine(Encoding.UTF8.GetString(buf, start, nl - start), offset + start);
                                    break;
                                }
                            start = nl + 1;
                        }
                        offset += start;
                        have -= start;
                        if (have > 0 && start > 0) Buffer.BlockCopy(buf, start, buf, 0, have);
                        if (have == buf.Length) Array.Resize(ref buf, buf.Length * 2); // one line is longer than the buffer
                    }
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { return -1; }
            return offset;
        }

        // The one complete line that starts at `offset`, or null when it cannot be read.
        public static string ReadLineAt(string path, long offset, int maxBytes = 4 << 20)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    if (offset < 0 || offset >= fs.Length) return null;
                    fs.Seek(offset, SeekOrigin.Begin);
                    var buf = new byte[Math.Min(maxBytes, fs.Length - offset)];
                    int have = 0, n;
                    while (have < buf.Length && (n = fs.Read(buf, have, buf.Length - have)) > 0) have += n;
                    int nl = Array.IndexOf(buf, (byte)'\n', 0, have);
                    return nl < 0 ? null : Encoding.UTF8.GetString(buf, 0, nl);
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { return null; }
        }
    }
}
