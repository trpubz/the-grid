using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace TheGrid.Core
{
    // Where a session already lives, for an owner who keeps their sessions in zellij: the zellij session and the tab the
    // session's process runs in. Found from the process's environment, where zellij leaves the session's name and the
    // pane's number, and from zellij's own list of panes. Nothing else of the environment is kept, and nothing is logged.
    public static class Zellij
    {
        public static string[] UsualPlaces(string home) =>
            new[] { Path.Combine(home, ".cargo", "bin", "zellij"), "/opt/homebrew/bin/zellij", "/usr/local/bin/zellij" };

        // From a process's command and environment as `ps eww` prints them, on one line: the zellij session it runs in and
        // its pane there. False when it runs in no zellij pane.
        public static bool TryPane(string ps, out string session, out int pane)
        {
            // shortcut: a session name with a space in it is cut at the space, since ps does not quote; zellij's generated names have none
            var name = Regex.Match(ps ?? "", @"(?:^|\s)ZELLIJ_SESSION_NAME=(\S+)");
            var id = Regex.Match(ps ?? "", @"(?:^|\s)ZELLIJ_PANE_ID=(\d{1,9})(?:\s|$)");
            session = name.Success ? name.Groups[1].Value : "";
            pane = id.Success ? int.Parse(id.Groups[1].Value) : -1;
            return name.Success && id.Success;
        }

        [Serializable] sealed class Pane { public int id; public bool is_plugin; public string tab_name; }
        [Serializable] sealed class Panes { public List<Pane> items; }

        // The name of the tab a terminal pane is in, from what `zellij action list-panes --tab --json` prints; "" when the
        // pane is not in it. A plugin's pane can have the same number as a terminal's: it is not the one.
        public static string TabOf(string panesJson, int pane)
        {
            Panes panes;
            try { panes = JsonUtility.FromJson<Panes>("{\"items\":" + panesJson + "}"); }   // JsonUtility cannot read a top-level array
            catch (ArgumentException) { return ""; }
            var found = panes?.items?.Find(p => p.id == pane && !p.is_plugin);
            return found?.tab_name ?? "";
        }

        // "session › tab" for the process, the session's name alone when zellij cannot be asked about its panes, and ""
        // when the process runs in no zellij pane or cannot be asked. `run` runs a program with arguments and gives what
        // it printed.
        public static string Where(int pid, string zellijPath, Func<string, string, string> run)
        {
            if (pid <= 0) return "";
            try
            {
                if (!TryPane(run("/bin/ps", "eww -o command= -p " + pid), out var session, out int pane)) return "";
                if (string.IsNullOrEmpty(zellijPath)) return session;
                string tab;
                try { tab = TabOf(run(zellijPath, "--session " + session + " action list-panes --tab --json"), pane); }
                catch (Exception e) when (Expected(e)) { tab = ""; }
                return tab == "" ? session : session + " › " + tab;
            }
            catch (Exception e) when (Expected(e)) { return ""; }
        }

        static bool Expected(Exception e) => e is InvalidOperationException || e is TimeoutException || e is Win32Exception || e is IOException;

        // Runs a program and gives what it printed. Throws when it cannot be started, fails, or takes longer than three seconds.
        public static string Run(string exe, string arguments)
        {
            var psi = new ProcessStartInfo(exe, arguments) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            using (var p = Process.Start(psi))
            {
                var stdout = p.StandardOutput.ReadToEndAsync();
                var stderr = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(3000))
                {
                    try { p.Kill(); } catch (InvalidOperationException) { }
                    throw new TimeoutException(Path.GetFileName(exe) + " did not answer");
                }
                p.WaitForExit();   // let the output readers finish
                if (p.ExitCode != 0) throw new InvalidOperationException(Path.GetFileName(exe) + " exited with " + p.ExitCode);
                return stdout.Result;
            }
        }
    }
}
