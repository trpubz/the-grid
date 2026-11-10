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
                    throw new TimeoutException("claude agents did not answer within " + timeoutMs + " ms");
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
        readonly Func<string, string> agentsJsonForConfigDir;
        readonly RosterBuilder builder;
        readonly Dictionary<string, List<LiveSession>> lastGood = new Dictionary<string, List<LiveSession>>();
        readonly object gate = new object();
        readonly ManualResetEventSlim stop = new ManualResetEventSlim(false);
        volatile RosterSnapshot latest = new RosterSnapshot();
        Thread thread;

        public Collector(Settings settings, string home, Func<string, string> agentsJsonForConfigDir)
        {
            this.settings = settings;
            this.home = home;
            this.agentsJsonForConfigDir = agentsJsonForConfigDir;
            builder = new RosterBuilder(settings);
        }

        public RosterSnapshot Latest => latest;

        public RosterSnapshot Poll(long nowMs)
        {
            lock (gate)
            {
                var live = new List<LiveSession>();
                var errors = new List<string>();
                foreach (var dir in settings.EffectiveConfigDirs(home))
                {
                    List<LiveSession> sessions = null;
                    string error;
                    try { if (!LiveSessions.TryParse(agentsJsonForConfigDir(dir), dir, out sessions, out error)) sessions = null; }
                    catch (Exception e) { error = e.Message; }
                    if (sessions != null) lastGood[dir] = sessions;
                    else errors.Add(RosterBuilder.AccountLabel(dir) + ": " + error);
                    // A failed read reuses the last good one, so a hiccup never looks like every agent going offline.
                    if (lastGood.TryGetValue(dir, out var keep)) live.AddRange(keep);
                }
                return latest = builder.Build(live, errors, nowMs);
            }
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
