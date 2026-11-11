using System;
using System.Collections.Generic;

namespace TheGrid.Core
{
    // What kind of work a running agent is doing, guessed from the last tool it used or, failing that, from what it last said.
    // A guess is never shown with confidence: anything unrecognised is Working.
    public static class Activity
    {
        public const string Reading = "reading", Editing = "editing", Testing = "testing", Reviewing = "reviewing", Working = "working";

        static readonly string[] EditTools = { "Edit", "Write", "NotebookEdit", "MultiEdit" };
        static readonly string[] ReadTools = { "Read", "Grep", "Glob", "WebFetch", "WebSearch", "LSP" };
        // Shell commands that look at, record or publish changes.
        static readonly string[] ReviewCommands = { "git diff", "git log", "git show", "git status", "git add", "git commit", "git push", "gh pr" };
        // All matched as whole words, so "latest" is not "test" and "ready" is not "read".
        static readonly string[] TestWords = { "test", "tests", "testing", "tested", "pytest", "jest", "vitest", "rspec", "xctest", "runtests" };
        static readonly string[] EditWords = { "edit", "edits", "editing", "edited", "writing", "implementing", "fixing" };
        static readonly string[] ReadWords = { "read", "reads", "reading", "searching", "inspecting" };

        public static string Classify(string tool, string command, string text)
        {
            tool = tool ?? "";
            command = (command ?? "").ToLowerInvariant();
            if (tool != "")
            {
                if (Array.IndexOf(EditTools, tool) >= 0) return Editing;
                if (Array.IndexOf(ReadTools, tool) >= 0) return Reading;
                if (tool.IndexOf("review", StringComparison.OrdinalIgnoreCase) >= 0) return Reviewing;
                if (tool == "Skill") return command.Contains("review") ? Reviewing : Working;
                if (tool == "Bash")
                {
                    // Review commands first: a commit message that mentions tests is still a commit.
                    foreach (var c in ReviewCommands) if (command.Contains(c)) return Reviewing;
                    foreach (var w in Words(command)) if (Array.IndexOf(TestWords, w) >= 0) return Testing;
                }
                return Working;
            }
            string found = Working;
            int rank = 0; // reviewing beats testing beats editing beats reading when one sentence mentions several
            foreach (var w in Words((text ?? "").ToLowerInvariant()))
            {
                int r = w.StartsWith("review", StringComparison.Ordinal) ? 4
                      : Array.IndexOf(TestWords, w) >= 0 ? 3
                      : Array.IndexOf(EditWords, w) >= 0 ? 2
                      : Array.IndexOf(ReadWords, w) >= 0 ? 1 : 0;
                if (r > rank) { rank = r; found = r == 4 ? Reviewing : r == 3 ? Testing : r == 2 ? Editing : Reading; }
            }
            return found;
        }

        static IEnumerable<string> Words(string s)
        {
            int start = -1;
            for (int i = 0; i <= s.Length; i++)
            {
                bool letter = i < s.Length && char.IsLetterOrDigit(s[i]);
                if (letter && start < 0) start = i;
                else if (!letter && start >= 0) { yield return s.Substring(start, i - start); start = -1; }
            }
        }
    }
}
