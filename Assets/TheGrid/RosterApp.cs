using System;
using System.Collections.Generic;
using System.IO;
using TheGrid.Core;
using UnityEngine;

namespace TheGrid
{
    // The roster window: starts the collector and draws its latest snapshot. Reads nothing but RosterSnapshot.
    public sealed class RosterApp : MonoBehaviour
    {
        static readonly Color Background = new Color(.035f, .055f, .085f);

        Settings settings;
        string settingsPath;
        Collector collector;
        Vector2 scroll;
        RosterSnapshot logged, drawn = new RosterSnapshot();
        readonly Dictionary<string, string> lastState = new Dictionary<string, string>();
        GUIStyle chip, title, dim, detail, error, button;
        float rowWidth, warnedWidth = -1f;
        string lastErrors = "";

        void Start()
        {
            // A lens that sits open all day: 10 frames a second is plenty. With vsync on, targetFrameRate is ignored and the app renders at the display rate.
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 10;
            Application.runInBackground = true;
            if (Camera.main != null)
            {
                Camera.main.clearFlags = CameraClearFlags.SolidColor;
                Camera.main.backgroundColor = Background;
            }
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            settingsPath = Path.Combine(Application.persistentDataPath, "settings.json");
            settings = Settings.Load(settingsPath);
            if (!File.Exists(settingsPath)) TrySave(); // so there is a file to edit: workspaces are added by hand
            var claude = ClaudeCli.ResolvePath(settings.claudePath, ClaudeCli.UsualPlaces(home));
            collector = new Collector(settings, home, dir => ClaudeCli.AgentsJson(claude, dir, home), SessionFiles.ChangeStamp);
            collector.Start();
        }

        void OnDestroy() => collector?.Dispose();

        void Update()
        {
            var snap = collector.Latest;
            if (ReferenceEquals(snap, logged) || snap.generatedAt == 0) return;
            if (logged == null) Debug.Log("[roster] first snapshot: " + snap.agents.Count + " agents, " + snap.errors.Count + " errors");
            logged = snap;
            var errors = string.Join(" | ", snap.errors);
            if (errors != lastErrors) Debug.Log(errors == "" ? "[roster] errors cleared, " + snap.agents.Count + " agents" : "[roster] errors: " + errors);
            lastErrors = errors;
            foreach (var a in snap.agents)
            {
                var key = a.account + "|" + a.sessionId;
                if (lastState.TryGetValue(key, out var old) && old != a.state) Debug.Log("[roster] " + a.name + " " + old + " -> " + a.state);
                lastState[key] = a.state;
            }
        }

        void OnGUI()
        {
            if (collector == null) return;
            if (chip == null) MakeStyles();
            // Retina screens report pixels; draw in points so text keeps its size.
            float scale = Screen.dpi >= 160f ? 2f : 1f;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            // IMGUI calls OnGUI once to lay out and again to paint; both passes must see the same rows.
            if (Event.current.type == EventType.Layout) drawn = collector.Latest;
            var snap = drawn;
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            rowWidth = Screen.width / scale - 16;
            GUILayout.BeginArea(new Rect(8, 6, rowWidth, Screen.height / scale - 12));
            foreach (var e in snap.errors) GUILayout.Label(e, error);
            scroll = GUILayout.BeginScrollView(scroll);
            if (snap.agents.Count == 0 && snap.errors.Count == 0) GUILayout.Label(snap.generatedAt == 0 ? "Looking for agent sessions..." : "No live agent sessions.", dim);
            foreach (var a in snap.agents) DrawAgent(a, now);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void DrawAgent(AgentRow a, long now)
        {
            GUILayout.BeginHorizontal();
            var old = GUI.contentColor;
            GUI.contentColor = ColorFor(a.state);
            GUILayout.Label(LabelFor(a.state), chip, GUILayout.Width(84));
            GUI.contentColor = old;
            GUILayout.Label(a.name, title);
            GUILayout.Label(a.account, dim);
            GUILayout.FlexibleSpace();
            GUILayout.Label(RosterFormat.Elapsed(a.stateSince, now), dim);
            GUILayout.EndHorizontal();
            CheckFits();

            GUILayout.BeginHorizontal();
            GUILayout.Space(88);
            var parts = new List<string>();
            if (a.state == "Unknown" || a.state == "WaitingOther") parts.Add("(" + a.rawStatus + ")");
            parts.Add(a.repoName);
            if (a.ticket != "") parts.Add(a.ticket);
            GUILayout.Label(string.Join("  ·  ", parts), detail);
            GUILayout.FlexibleSpace();
            if (a.prNumber > 0 && GUILayout.Button("PR #" + a.prNumber, button) && RosterFormat.IsWebLink(a.prUrl)) Application.OpenURL(a.prUrl);
            if (a.repoRoot == "") GUILayout.Label(a.workspace, dim);
            else if (GUILayout.Button(a.workspace, button)) CycleWorkspace(a.repoRoot);
            GUILayout.EndHorizontal();
            CheckFits();

            foreach (var s in a.subagents)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Space(88);
                GUILayout.Label("↳ " + s.description, dim);
                GUILayout.EndHorizontal();
            }
            GUILayout.Space(6);
        }

        // Self-check: the row just drawn must fit the window. Logged once per window width.
        void CheckFits()
        {
            if (Event.current.type != EventType.Repaint || Mathf.Approximately(warnedWidth, rowWidth)) return;
            var r = GUILayoutUtility.GetLastRect();
            if (r.xMax <= rowWidth + 1f) return;
            warnedWidth = rowWidth;
            Debug.LogWarning("[roster] layout overflow: a row needs " + Mathf.CeilToInt(r.xMax) + " points, the window gives " + Mathf.FloorToInt(rowWidth));
        }

        // The collector reads these settings on its own thread; a read that overlaps this change shows up as one error line for one pass.
        void CycleWorkspace(string repoRoot)
        {
            settings.Reload(settingsPath); // pick up workspaces added by hand since the app started
            settings.CycleWorkspace(repoRoot);
            TrySave();
        }

        void TrySave()
        {
            try { settings.Save(settingsPath); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { Debug.LogWarning("[roster] could not save settings: " + e.Message); }
        }

        static string LabelFor(string state)
        {
            switch (state)
            {
                case "BlockedOnQuestion": return "QUESTION";
                case "WaitingForPermission": return "PERMISSION";
                case "WaitingOther": return "WAITING";
                case "BackgroundBlocked": return "BG BLOCKED";
                case "Errored": return "ERROR";
                case "Finished": return "DONE";
                case "Running": return "RUNNING";
                case "Offline": return "OFFLINE";
                default: return "UNKNOWN";
            }
        }

        static Color ColorFor(string state)
        {
            switch (state)
            {
                case "BlockedOnQuestion": return new Color(1f, .35f, .35f);
                case "WaitingForPermission":
                case "BackgroundBlocked": return new Color(1f, .62f, .2f);
                case "WaitingOther": return new Color(1f, .9f, .3f);
                case "Errored": return new Color(1f, .4f, .9f);
                case "Finished": return new Color(.3f, .95f, 1f);
                case "Running": return new Color(.45f, .9f, .5f);
                case "Offline": return new Color(.45f, .5f, .55f);
                default: return new Color(.7f, .7f, .7f);
            }
        }

        void MakeStyles()
        {
            var text = new Color(.9f, .93f, .96f);
            var faint = new Color(.55f, .62f, .7f);
            chip = new GUIStyle(GUI.skin.label) { fontSize = 11, fontStyle = FontStyle.Bold, clipping = TextClipping.Clip };
            chip.normal.textColor = Color.white;
            title = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, clipping = TextClipping.Clip };
            title.normal.textColor = text;
            detail = new GUIStyle(GUI.skin.label) { fontSize = 12, clipping = TextClipping.Clip };
            detail.normal.textColor = text;
            dim = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true };
            dim.normal.textColor = faint;
            error = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true };
            error.normal.textColor = new Color(1f, .5f, .4f);
            button = new GUIStyle(GUI.skin.button) { fontSize = 11 };
        }
    }
}
