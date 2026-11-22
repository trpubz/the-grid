using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace TheGrid.Core
{
    // What a blocked background session is waiting on, as its job file says it.
    public static class Needs
    {
        public const int MaxLetters = 300;
        const long MaxFileBytes = 1 << 20;

        [Serializable] sealed class JobState { public string needs, detail; }

        // One line, at most MaxLetters long, cut with an ellipsis and never in the middle of a surrogate pair.
        public static string Clean(string text)
        {
            if (text == null) return "";
            var s = Regex.Replace(text, @"\s+", " ").Trim();
            if (s.Length <= MaxLetters) return s;
            int cut = MaxLetters - 1;
            if (char.IsHighSurrogate(s[cut - 1])) cut--;
            return s.Substring(0, cut) + "…";
        }

        // <dir>/jobs/<id>/state.json in each folder in turn; the first that names a need wins.
        public static bool TryOfJob(IReadOnlyList<string> configDirs, string id, out string needs, out string detail)
        {
            needs = detail = "";
            if (!IsPlainName(id)) return false;   // the id goes into a path
            foreach (var dir in configDirs)
            {
                var j = Read(Path.Combine(dir, "jobs", id, "state.json"));
                var n = Clean(j?.needs);
                if (n == "") continue;
                needs = n;
                detail = Clean(j.detail);
                return true;
            }
            return false;
        }

        static bool IsPlainName(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            foreach (char c in id)
                if (!(c < 128 && char.IsLetterOrDigit(c)) && c != '-') return false;
            return true;
        }

        static JobState Read(string path)
        {
            try
            {
                using (var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    if (f.Length > MaxFileBytes) return null;
                    using (var r = new StreamReader(f))
                    {
                        var text = r.ReadToEnd();
                        // JsonUtility turns a number into the string "5"; only a string is a need.
                        if (!Regex.IsMatch(text, "\"needs\"\\s*:\\s*\"")) return null;
                        return JsonUtility.FromJson<JobState>(text);
                    }
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException) { return null; }
        }
    }
}
