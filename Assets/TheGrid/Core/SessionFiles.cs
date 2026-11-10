using System;
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

            foreach (var line in text.Substring(from, end - from).Split('\n'))
            {
                if (line.IndexOf("\"gitBranch\"", StringComparison.Ordinal) < 0 && line.IndexOf("\"pr-link\"", StringComparison.Ordinal) < 0) continue;
                Line r;
                try { r = JsonUtility.FromJson<Line>(line); }
                catch (ArgumentException) { continue; }
                if (r == null) continue;
                if (!string.IsNullOrEmpty(r.gitBranch)) meta.Branch = r.gitBranch;
                if (r.type == "pr-link" && r.prNumber > 0) { meta.PrNumber = r.prNumber; meta.PrUrl = r.prUrl ?? ""; }
            }
            return meta;
        }
    }
}
