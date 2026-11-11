using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace TheGrid.Core
{
    public sealed class SessionFileInfo { public long StatusUpdatedAt; public string Entrypoint = ""; }

    public sealed class TranscriptMeta { public string Branch = ""; public int PrNumber; public string PrUrl = ""; }

    // Read-only access to files Claude Code writes under a config directory.
    public static class SessionFiles
    {
        [Serializable] sealed class SessionJson { public long statusUpdatedAt; public string entrypoint; }
        [Serializable] sealed class Line { public string type, gitBranch, prUrl; public int prNumber; }

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

        // Moves whenever a session file under <configDir>/sessions is rewritten, added or removed. Claude Code rewrites a
        // session's file when its status changes and leaves it alone while idle, so this tells a poll whether anything happened.
        public static string ChangeStamp(string configDir)
        {
            try
            {
                var dir = Path.Combine(configDir, "sessions");
                if (!Directory.Exists(dir)) return "";
                var files = Directory.GetFiles(dir, "*.json");
                Array.Sort(files, StringComparer.Ordinal);
                var sb = new StringBuilder();
                foreach (var f in files)
                {
                    var fi = new FileInfo(f);
                    sb.Append(fi.Name).Append(':').Append(fi.LastWriteTimeUtc.Ticks).Append(':').Append(fi.Length).Append(';');
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

            int from = startsMidFile ? text.IndexOf('\n') + 1 : 0; // the first line is cut when the window starts mid-file
            int end = text.LastIndexOf('\n');                      // a last line with no newline is still being written
            if ((startsMidFile && from == 0) || end < from) return meta;

            // Newest first, and stop parsing branch lines once one is found: nearly every line carries the branch.
            var lines = text.Substring(from, end - from).Split('\n');
            bool haveBranch = false, havePr = false;
            for (int i = lines.Length - 1; i >= 0 && !(haveBranch && havePr); i--)
            {
                var line = lines[i];
                bool branchLine = !haveBranch && line.IndexOf("\"gitBranch\"", StringComparison.Ordinal) >= 0;
                bool prLine = !havePr && line.IndexOf("\"pr-link\"", StringComparison.Ordinal) >= 0;
                if (!branchLine && !prLine) continue;
                Line r;
                try { r = JsonUtility.FromJson<Line>(line); }
                catch (ArgumentException) { continue; }
                if (r == null) continue;
                if (branchLine && !string.IsNullOrEmpty(r.gitBranch)) { meta.Branch = r.gitBranch; haveBranch = true; }
                if (prLine && r.type == "pr-link" && r.prNumber > 0) { meta.PrNumber = r.prNumber; meta.PrUrl = r.prUrl ?? ""; havePr = true; }
            }
            return meta;
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
        // A file shorter than `offset` was replaced and is read from the start. Transcripts reach a gigabyte, so markers are
        // matched on the raw bytes and only matching lines are decoded.
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
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
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
