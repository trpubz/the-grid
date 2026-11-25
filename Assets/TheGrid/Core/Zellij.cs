using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
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

        [Serializable] sealed class Pane { public int id; public bool is_plugin; public int tab_id; public string tab_name; }
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

        // The id of the tab a terminal pane is in; -1 when the pane is not in the list, the list cannot be read, or it has
        // no ids at all (an older zellij: JsonUtility would read the missing number as 0, which is a real tab).
        public static int TabIdOf(string panesJson, int pane)
        {
            if (panesJson == null || !panesJson.Contains("\"tab_id\"")) return -1;
            Panes panes;
            try { panes = JsonUtility.FromJson<Panes>("{\"items\":" + panesJson + "}"); }
            catch (ArgumentException) { return -1; }
            return panes?.items?.Find(p => p.id == pane && !p.is_plugin)?.tab_id ?? -1;
        }

        // The terminal app the process was started from: its bundle id, from `ps eww` output; "" when absent or not plain.
        public static string TerminalApp(string ps)
        {
            var m = Regex.Match(ps ?? "", @"(?:^|\s)__CFBundleIdentifier=([A-Za-z0-9.\-]+)(?:\s|$)");
            return m.Success ? m.Groups[1].Value : "";
        }

        // Tells zellij to go to the tab the process's pane is in. True when it was told; `app` is then the terminal's
        // bundle id, else "". False when the process runs in no zellij pane, zellij is not there, the tab is unknown, or a
        // program fails. It uses go-to-tab-by-id: from outside zellij, focus-pane-id exits 0 and moves nothing.
        public static bool Jump(int pid, string zellijPath, Func<string, string, string> run, out string app)
        {
            app = "";
            string session;
            if (pid <= 0 || string.IsNullOrEmpty(zellijPath)) return false;
            try
            {
                var ps = run("/bin/ps", "eww -o command= -p " + pid);
                if (!TryPane(ps, out session, out int pane)) return false;
                int tab = TabIdOf(run(zellijPath, "--session " + session + " action list-panes --tab --json"), pane);
                if (tab < 0) return false;
                run(zellijPath, "--session " + session + " action go-to-tab-by-id " + tab);
                app = TerminalApp(ps);
            }
            catch (Exception e) when (Expected(e)) { return false; }
            // A terminal shows one zellij session in each of its own tabs. Not told which, it stays on the one last in front.
            if (app == Ghostty)
                try
                {
                    var ttys = ClientTtys(run("/bin/ps", "-ao tty=,command="), session);
                    if (ttys.Count > 0) run(Osascript, FocusScript(ttys));
                }
                catch (TimeoutException) { app = ""; }       // macOS's question was left standing: by now the owner is elsewhere
                catch (Exception e) when (Expected(e)) { }   // an older Ghostty, or the owner said no: the app still comes forward
            return true;
        }

        public const string Ghostty = "com.mitchellh.ghostty", Osascript = "/usr/bin/osascript";

        // The terminals in which a zellij session is shown: the ttys of the zellij programs that attached to it, or started
        // it by name, from `ps -ao tty=,command=`. Nothing else of that list is kept.
        // shortcut: what a client was started with. One started with no name, or with the beginning of one, is missed, and one that was since switched to another session is still taken for the first's; ask zellij once it says which tty a client has
        public static List<string> ClientTtys(string ps, string session)
        {
            var found = new List<string>();
            foreach (var line in (ps ?? "").Split('\n'))
            {
                var w = line.Split(new[] { ' ', '\t', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                if (w.Length < 4 || !Regex.IsMatch(w[0], @"^ttys\d+$") || Path.GetFileName(w[1]).TrimStart('-') != "zellij") continue;   // "-zellij" when it is the terminal's own command
                int verb = 2, named = -1;   // the first word that is no option and no option's value: attach, or a command about a session, or none
                for (; verb < w.Length && (w[verb][0] == '-' || w[verb - 1][0] == '-' && verb > 2); verb++)
                    if (w[verb] == "-s" || w[verb] == "--session") named = verb + 1;
                bool attached = verb < w.Length && (w[verb] == "attach" || w[verb] == "a") && Array.IndexOf(w, session, verb + 1) >= 0;
                bool started = verb == w.Length && named >= 0 && named < w.Length && w[named] == session;
                if (attached || started) found.Add(w[0]);
            }
            return found;
        }

        static string FocusScript(List<string> ttys)
        {
            var lines = new[]
            {
                "if application id \"" + Ghostty + "\" is running then",   // else asking it would start it
                "tell application id \"" + Ghostty + "\"",
                "repeat with t in (get terminals)",
                "if {" + string.Join(", ", ttys.ConvertAll(t => "\"/dev/" + t + "\"")) + "} contains (tty of t) then",
                "focus (contents of t)",
                "return",
                "end if",
                "end repeat",
                "end tell",
                "end if"
            };
            return string.Join(" ", Array.ConvertAll(lines, l => "-e \"" + l.Replace("\"", "\\\"") + "\""));
        }

        static bool Expected(Exception e) => e is InvalidOperationException || e is TimeoutException || e is Win32Exception || e is IOException;

        // Runs a program and gives what it printed. Throws when it cannot be started, fails, or takes longer than three
        // seconds; a script waits for a minute, since the first time macOS asks the owner whether it may.
        public static string Run(string exe, string arguments) => Run(exe, arguments, CancellationToken.None);

        // Stopped, and as good as failed, when `drop` says so.
        public static string Run(string exe, string arguments, CancellationToken drop)
        {
            if (drop.IsCancellationRequested) throw new InvalidOperationException("dropped");
            int wait = exe == Osascript ? 60000 : 3000;
            var psi = new ProcessStartInfo(exe, arguments) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            using (var p = Process.Start(psi))
            using (drop.Register(() => { try { p.Kill(); } catch (Exception e) when (Expected(e)) { } }))
            {
                var stdout = p.StandardOutput.ReadToEndAsync();
                var stderr = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(wait))
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
