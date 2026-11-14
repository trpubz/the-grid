using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace TheGrid.Core
{
    // Runs `claude agents --json`, the documented source of live session state.
    public static class ClaudeCli
    {
        public static string[] UsualPlaces(string home) =>
            new[] { Path.Combine(home, ".local", "bin", "claude"), "/opt/homebrew/bin/claude", "/usr/local/bin/claude" };

        // The configured path if it exists, else the first usual place that exists, else "".
        public static string ResolvePath(string configured, IEnumerable<string> places)
        {
            if (!string.IsNullOrEmpty(configured)) return File.Exists(configured) ? configured : "";
            foreach (var p in places) if (File.Exists(p)) return p;
            return "";
        }

        public static string AgentsJson(string claudePath, string configDir, string home, int timeoutMs = 5000)
        {
            if (string.IsNullOrEmpty(claudePath)) throw new InvalidOperationException("claude was not found; set claudePath in settings.json");
            var psi = new ProcessStartInfo(claudePath, "agents --json")
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
            };
            // The default directory is addressed by leaving the variable unset, as a person's shell would.
            if (configDir.TrimEnd('/') == Path.Combine(home, ".claude")) psi.EnvironmentVariables.Remove("CLAUDE_CONFIG_DIR");
            else psi.EnvironmentVariables["CLAUDE_CONFIG_DIR"] = configDir;

            using (var p = Process.Start(psi))
            {
                var stdout = p.StandardOutput.ReadToEndAsync();
                var stderr = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(timeoutMs))
                {
                    try { p.Kill(); } catch (InvalidOperationException) { }
                    // Seen when the child blocks opening a file: macOS holds it until the owner answers a folder-access prompt for this app.
                    throw new TimeoutException("claude agents did not answer within " + timeoutMs + " ms. If macOS is asking whether this app may access your folders, answer that prompt.");
                }
                p.WaitForExit(); // let the output readers finish
                if (p.ExitCode != 0) throw new InvalidOperationException("claude agents exited with " + p.ExitCode + ": " + stderr.Result.Trim().Split('\n')[0]);
                return stdout.Result;
            }
        }
    }

    // Polls every config directory and keeps the latest roster snapshot. Poll runs on a background thread once started.
    public sealed class Collector : IDisposable
    {
        readonly Settings settings;
        readonly string home;
        // A quiet directory is still asked this often: background sessions leave no session file to watch.
        public const long FullRefreshMs = 30000;

        readonly Func<string, string> agentsJsonForConfigDir;
        readonly Func<string, string> changeStampForConfigDir;
        readonly Func<long, List<AgentRow>> others;   // the rows of another app's sessions, as of a time; null for none
        List<AgentRow> othersLast = new List<AgentRow>();
        string othersWere = "";
        readonly RosterBuilder builder;
        readonly Dictionary<string, List<LiveSession>> lastGood = new Dictionary<string, List<LiveSession>>();
        readonly Dictionary<string, string> lastStamp = new Dictionary<string, string>();
        readonly Dictionary<string, long> lastRunAt = new Dictionary<string, long>();
        readonly HashSet<string> failed = new HashSet<string>();
        readonly object gate = new object();
        readonly ManualResetEventSlim stop = new ManualResetEventSlim(false);
        volatile RosterSnapshot latest = new RosterSnapshot();
        Thread thread;

        // changeStampForConfigDir returns a value that moves whenever a directory's sessions may have changed.
        // With one, the command only runs on a change, after a failure, or every FullRefreshMs. Without one it runs on every poll.
        // others gives the rows of sessions that are not Claude's (Codex threads); they stand in the roster among the rest.
        public Collector(Settings settings, string home, Func<string, string> agentsJsonForConfigDir, Func<string, string> changeStampForConfigDir = null,
            Func<long, List<AgentRow>> others = null)
        {
            this.others = others;
            this.settings = settings;
            this.home = home;
            this.agentsJsonForConfigDir = agentsJsonForConfigDir;
            this.changeStampForConfigDir = changeStampForConfigDir;
            builder = new RosterBuilder(settings, home);
        }

        public RosterSnapshot Latest => latest;

        public RosterSnapshot Poll(long nowMs)
        {
            lock (gate)
            {
                var live = new List<LiveSession>();
                var errors = new List<string>();
                bool anyRead = false;
                foreach (var dir in settings.EffectiveConfigDirs(home))
                {
                    string stamp = null;
                    if (changeStampForConfigDir != null) try { stamp = changeStampForConfigDir(dir); } catch (Exception) { stamp = null; }
                    bool due = stamp == null || failed.Contains(dir) || !lastRunAt.TryGetValue(dir, out var ranAt)
                               || nowMs - ranAt >= FullRefreshMs || !lastStamp.TryGetValue(dir, out var before) || before != stamp;
                    if (due)
                    {
                        anyRead = true;
                        lastRunAt[dir] = nowMs;
                        lastStamp[dir] = stamp;
                        List<LiveSession> sessions = null;
                        string error;
                        try { if (!LiveSessions.TryParse(agentsJsonForConfigDir(dir), dir, out sessions, out error)) sessions = null; }
                        catch (Exception e) { error = e.Message; }
                        if (sessions != null) { lastGood[dir] = sessions; failed.Remove(dir); }
                        else { failed.Add(dir); errors.Add(RosterBuilder.AccountLabel(dir) + ": " + error); }
                    }
                    // A failed read reuses the last good one, so a hiccup never looks like every agent going offline.
                    if (lastGood.TryGetValue(dir, out var keep)) live.AddRange(keep);
                }
                // Nothing changed and nobody is working: no command run, no files read. While an agent runs, the roster is
                // rebuilt from the last session lists so its activity keeps up; only transcripts that changed are read again.
                // The other app's rows: a failed read keeps the last ones and says so.
                var more = othersLast;
                if (others != null)
                    try { more = others(nowMs) ?? new List<AgentRow>(); }
                    catch (Exception e)
                    {
                        errors.Add("codex: " + e.Message);
                        anyRead = true;
                    }
                string moreIs = Signature(more);
                if (!anyRead && moreIs == othersWere && !live.Exists(s => LiveSessions.Map(s) == AgentState.Running)) return latest;
                othersLast = more;
                othersWere = moreIs;
                return latest = RosterBuilder.With(builder.Build(live, errors, nowMs), more);
            }
        }

        // Everything of those rows that the roster shows: when it is the same, the roster is.
        static string Signature(List<AgentRow> rows)
        {
            var all = new System.Text.StringBuilder();
            foreach (var a in rows)
                all.Append(a.account).Append('|').Append(a.sessionId).Append('|').Append(a.state).Append('|').Append(a.stateSince).Append('|').Append(a.activity)
                    .Append('|').Append(a.name).Append('|').Append(a.repoRoot).Append('|').Append(a.workspace).Append('\n');
            return all.ToString();
        }

        public void Start(int intervalMs = 2000)
        {
            if (thread != null) return;
            thread = new Thread(() =>
            {
                while (!stop.IsSet)
                {
                    long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    try { Poll(now); }
                    catch (Exception e)
                    {
                        // Never let the loop die: keep the rows, show the problem.
                        var keep = latest;
                        latest = new RosterSnapshot { generatedAt = now, agents = keep.agents, errors = new List<string> { "collector: " + e.Message } };
                    }
                    stop.Wait(intervalMs);
                }
            }) { IsBackground = true, Name = "TheGrid.Collector" };
            thread.Start();
        }

        public void Dispose()
        {
            stop.Set();
            if (thread != null && thread != Thread.CurrentThread) thread.Join(6000);
            thread = null;
        }
    }
}
