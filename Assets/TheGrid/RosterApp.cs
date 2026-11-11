using System;
using System.Collections.Generic;
using System.IO;
using TheGrid.Core;
using UnityEngine;

namespace TheGrid
{
    // The window: the roster of agents and the review pile of their messages. Draws nothing but RosterSnapshot and PileSnapshot.
    public sealed class RosterApp : MonoBehaviour
    {
        static readonly Color Background = new Color(.035f, .055f, .085f);

        Settings settings;
        string settingsPath;
        // At this width or wider the roster and the pile sit side by side; narrower, they are two tabs.
        const float SideBySideWidth = 720f;
        // IMGUI lays out every row on every frame, so only the newest messages are drawn.
        const int MaxMessages = 200;

        Collector collector;
        Pile pile;
        Vector2 scroll, pileScroll;
        RosterSnapshot logged, drawn = new RosterSnapshot();
        PileSnapshot pileDrawn = new PileSnapshot();
        // A click changes the "next" value; it takes effect on the following Layout event, so both IMGUI passes draw the same controls.
        int tab, nextTab;
        string openKey = "", nextOpenKey = "", openBody = "";
        bool pileLogged;
        readonly Dictionary<string, string> lastState = new Dictionary<string, string>();
        GUIStyle chip, title, dim, detail, error, button, body;
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
            // The pile's file sits beside settings.json, never under an agent app's folder.
            pile = new Pile(settings, home, Path.Combine(Application.persistentDataPath, "pile.json"));
            pile.Start();
        }

        void OnDestroy()
        {
            collector?.Dispose();
            pile?.Dispose();
        }

        void Update()
        {
            if (!pileLogged && !pile.Latest.reading)
            {
                pileLogged = true;
                Debug.Log("[pile] " + pile.Latest.rows.Count + " messages, " + pile.Latest.archived + " archived");
            }
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
            if (Event.current.type == EventType.Layout)
            {
                drawn = collector.Latest;
                pileDrawn = pile.Latest;
                tab = nextTab;
                if (openKey != nextOpenKey)
                {
                    openKey = nextOpenKey;
                    openBody = openKey == "" ? "" : pile.ReadBody(openKey) ?? "The transcript this message was read from is no longer there.";
                }
            }
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            float width = Screen.width / scale - 16, height = Screen.height / scale - 12;
            if (Screen.width / scale >= SideBySideWidth)
            {
                float half = (width - 12) / 2;
                DrawRoster(new Rect(8, 6, half, height), drawn, now);
                DrawPile(new Rect(8 + half + 12, 6, half, height), pileDrawn, now);
            }
            else
            {
                int unticked = 0;
                foreach (var r in pileDrawn.rows) if (!r.ticked) unticked++;
                GUILayout.BeginArea(new Rect(8, 6, width, 24));
                nextTab = GUILayout.Toolbar(tab, new[] { "Agents (" + drawn.agents.Count + ")", "Messages (" + unticked + ")" }, button);
                GUILayout.EndArea();
                var area = new Rect(8, 32, width, height - 26);
                if (tab == 0) DrawRoster(area, drawn, now); else DrawPile(area, pileDrawn, now);
            }
        }

        void DrawRoster(Rect area, RosterSnapshot snap, long now)
        {
            rowWidth = area.width;
            GUILayout.BeginArea(area);
            foreach (var e in snap.errors) GUILayout.Label(e, error);
            scroll = GUILayout.BeginScrollView(scroll);
            if (snap.agents.Count == 0 && snap.errors.Count == 0) GUILayout.Label(snap.generatedAt == 0 ? "Looking for agent sessions..." : "No live agent sessions.", dim);
            foreach (var a in snap.agents) DrawAgent(a, now);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        // The review pile: every message between sessions, newest first. A tick marks one as reviewed; Clear archives the ticked ones.
        void DrawPile(Rect area, PileSnapshot snap, long now)
        {
            rowWidth = area.width;
            int shown = Math.Min(snap.rows.Count, MaxMessages), ticked = 0, shownTicked = 0;
            foreach (var r in snap.rows) if (r.ticked) ticked++;
            for (int i = 0; i < shown; i++) if (snap.rows[i].ticked) shownTicked++;
            bool allShownTicked = shown > 0 && shownTicked == shown; // then the button unticks, so a tick-all can be taken back
            GUILayout.BeginArea(area);
            foreach (var e in snap.errors) GUILayout.Label(e, error);
            GUILayout.Label("Messages: " + (snap.rows.Count - ticked) + " to review  ·  " + snap.archived + " archived" + (snap.reading ? "  ·  reading history…" : ""), dim);
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(allShownTicked ? "Untick shown" : "Tick shown", button))
            {
                var keys = new List<string>();
                for (int i = 0; i < shown; i++) keys.Add(snap.rows[i].key);
                pile.SetTicked(keys, !allShownTicked);
            }
            GUI.enabled = ticked > 0;
            if (GUILayout.Button("Clear ticked (" + ticked + ")", button)) pile.Clear();
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            pileScroll = GUILayout.BeginScrollView(pileScroll);
            if (snap.rows.Count == 0) GUILayout.Label(snap.reading ? "Reading message history..." : "No messages to review.", dim);
            for (int i = 0; i < shown; i++) DrawMessage(snap.rows[i], now);
            if (snap.rows.Count > shown) GUILayout.Label("… and " + (snap.rows.Count - shown) + " older", dim);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void DrawMessage(PileRow m, long now)
        {
            GUILayout.BeginHorizontal();
            bool ticked = GUILayout.Toggle(m.ticked, GUIContent.none, GUILayout.Width(18));
            if (ticked != m.ticked) pile.SetTicked(m.key, ticked);
            // Clicking the names opens the whole message under the row; clicking again closes it.
            if (GUILayout.Button(Who(m.from, m.fromRepoName) + "  →  " + Who(m.to, m.toRepoName), title, GUILayout.Width(rowWidth - 96)))
                nextOpenKey = openKey == m.key ? "" : m.key;
            GUILayout.FlexibleSpace();
            GUILayout.Label(PileFormat.Age(m.at, now), dim);
            GUILayout.EndHorizontal();
            CheckFits();

            GUILayout.BeginHorizontal();
            GUILayout.Space(24);
            var old = GUI.contentColor;
            var text = m.summary != "" ? m.summary : m.firstLine;
            if (m.crossWorkspace) // a message that crossed from one workspace into another is the one to look at
            {
                GUI.contentColor = ColorFor("BlockedOnQuestion");
                text = m.fromWorkspace + " → " + m.toWorkspace + "  ·  " + text;
            }
            GUILayout.Label(text, detail, GUILayout.Width(rowWidth - 48));
            GUI.contentColor = old;
            GUILayout.EndHorizontal();
            CheckFits();

            if (openKey == m.key)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Space(24);
                GUILayout.Label(openBody, body, GUILayout.Width(rowWidth - 48));
                GUILayout.EndHorizontal();
            }
            GUILayout.Space(6);
        }

        // A session with no name of its own is shown by its repo.
        static string Who(string name, string repoName) => name != "" ? name : repoName != "" ? repoName : "unknown";

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
            if (a.activity != "") parts.Add(a.activity);
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
            pile.Refresh(); // the cross-workspace flags follow the new assignment
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
            body = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
            body.normal.textColor = text;
        }
    }
}
