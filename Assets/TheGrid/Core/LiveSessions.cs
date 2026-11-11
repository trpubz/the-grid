using System;
using System.Collections.Generic;
using UnityEngine;

namespace TheGrid.Core
{
    // Declared in urgency order: the roster sorts by this.
    public enum AgentState { BlockedOnQuestion, WaitingForPermission, WaitingOther, BackgroundBlocked, Errored, Finished, Unknown, Running, Offline }

    // One row of `claude agents --json`.
    public sealed class LiveSession
    {
        public string SessionId, Name, Cwd, Kind, Status, WaitingFor, State, ConfigDir;
        public int Pid;
        public long StartedAt;
    }

    public static class LiveSessions
    {
        // JsonUtility matches fields by exact name and cannot read a top-level array.
        [Serializable] sealed class Row { public string sessionId, name, cwd, kind, status, waitingFor, state; public int pid; public long startedAt; }
        [Serializable] sealed class Rows { public List<Row> items; }

        public static bool TryParse(string json, string configDir, out List<LiveSession> sessions, out string error)
        {
            sessions = new List<LiveSession>();
            error = "";
            Rows rows;
            try { rows = JsonUtility.FromJson<Rows>("{\"items\":" + json + "}"); }
            catch (ArgumentException e) { error = "could not read the live session list: " + e.Message; return false; }
            if (rows == null || rows.items == null) { error = "the live session list was not a list"; return false; }
            foreach (var r in rows.items)
                sessions.Add(new LiveSession
                {
                    SessionId = r.sessionId ?? "", Name = r.name ?? "", Cwd = r.cwd ?? "", Kind = r.kind ?? "",
                    Status = r.status ?? "", WaitingFor = r.waitingFor ?? "", State = r.state ?? "",
                    ConfigDir = configDir, Pid = r.pid, StartedAt = r.startedAt,
                });
            return true;
        }

        public static AgentState Map(LiveSession s)
        {
            if (s.Kind == "background")
                switch (s.State)
                {
                    case "blocked": return AgentState.BackgroundBlocked;
                    case "working": return AgentState.Running;
                    case "done":
                    case "stopped": return AgentState.Finished;
                    case "failed": return AgentState.Errored;
                    default: return AgentState.Unknown;
                }
            switch (s.Status)
            {
                case "busy": return AgentState.Running;
                case "idle": return AgentState.Finished;
                case "waiting":
                    switch (s.WaitingFor)
                    {
                        case "input needed": return AgentState.BlockedOnQuestion;
                        case "permission prompt":
                        case "sandbox request": return AgentState.WaitingForPermission;
                        default: return AgentState.WaitingOther;
                    }
                default: return AgentState.Unknown;
            }
        }

        // The app's own words, kept so an unrecognised status is still shown.
        public static string RawStatus(LiveSession s)
        {
            if (!string.IsNullOrEmpty(s.Status)) return string.IsNullOrEmpty(s.WaitingFor) ? s.Status : s.Status + " / " + s.WaitingFor;
            return s.State ?? "";
        }
    }
}
