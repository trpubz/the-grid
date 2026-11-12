using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using TheGrid.Core;
using UnityEngine;

namespace TheGrid
{
    // The window: the roster of agents, the review pile of their messages, and the world. Draws nothing but RosterSnapshot and PileSnapshot.
    // Started with -snapshot <file>, it shows the world from that roster snapshot and reads no agent data; with -shots <folder> too,
    // it saves far.png and near.png there and quits.
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
        PullReader pullReader;                 // asks GitHub about open pull requests, while that is turned on; null on a snapshot
        PullsSnapshot pullsFixed, pullsFrom;   // a snapshot's pull requests from a file; and the ones worldDrawn was built from
        string workspaceFor = "";              // the repo whose workspace list is open, by its root; "" for none
        Vector2 workspaceAt;                   // where the list was asked for, in screen points
        int depotOpen = -1;                    // the plot of the sector whose depot list is open
        Pile pile;
        Vector2 scroll, pileScroll;
        RosterSnapshot logged, drawn = new RosterSnapshot();
        PileSnapshot pileDrawn = new PileSnapshot();
        // A click changes the "next" value; it takes effect on the following Layout event, so both IMGUI passes draw the same controls.
        int tab, nextTab;
        bool worldOpen, nextWorldOpen;
        WorldView world;
        WorldModel worldDrawn = new WorldModel();
        RosterSnapshot worldFrom;   // the snapshot worldDrawn was built from
        readonly WorldPanels panels = new WorldPanels();
        List<NeedsYouRow> needsYou = new List<NeedsYouRow>();
        BotCard card;
        // Where the session of the card that is open lives: asked once when the card opens, on a thread of its own.
        string whereFor = "", whereFixed = "", zellij = "";
        Task<string> where;
        Deliveries deliveries;      // made when the world is first opened: only what arrives after that is shown
        PileSnapshot deliveredFrom; // the pile snapshot last looked through for deliveries
        string lastSender = "", lastRecipient = "";  // picture mode: the sender and the recipient of the longest ride, how long that ride is and when it ends
        float longestRide = -1f, rideEnds;
        bool showArrivals;          // picture mode with -messages: there are deliveries to take pictures of
        bool leftWorld;             // the world has been closed since it was last drawn
        Plots plots;
        string plotsPath, selected = "", shotsDir;
        int plotsWarned = -1;
        RosterSnapshot fixedSnap;   // picture mode: the one snapshot there is
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
                Camera.main.cullingMask = 0;   // it only clears the window; the world has a camera of its own
            }
            world = gameObject.AddComponent<WorldView>();
            var snapshotFile = Arg("-snapshot");
            if (snapshotFile != "")
            {
                fixedSnap = JsonUtility.FromJson<RosterSnapshot>(File.ReadAllText(snapshotFile));
                plots = new Plots();   // never the owner's world.json
                settings = new Settings();   // nor the owner's settings: lights picked and things moved last until the app closes
                worldOpen = nextWorldOpen = true;
                // A pile to show arriving, for pictures of couriers: everything in it counts as arriving as the world opens.
                if (Arg("-messages") != "")
                {
                    pileDrawn = JsonUtility.FromJson<PileSnapshot>(File.ReadAllText(Arg("-messages")));
                    deliveries = new Deliveries(0);
                    showArrivals = true;
                }
                // Pull requests for a picture of the depot come from a file: a snapshot never asks GitHub.
                if (Arg("-pulls") != "") pullsFixed = JsonUtility.FromJson<PullsSnapshot>(File.ReadAllText(Arg("-pulls")));
                whereFixed = Arg("-where");   // nor does it ask zellij where a session lives: for a picture, the card is told
                shotsDir = Arg("-shots");
                if (shotsDir != "") StartCoroutine(Shots());
                return;
            }
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            settingsPath = Path.Combine(Application.persistentDataPath, "settings.json");
            settings = Settings.Load(settingsPath);
            if (!File.Exists(settingsPath)) TrySave(); // so there is a file to edit
            var claude = ClaudeCli.ResolvePath(settings.claudePath, ClaudeCli.UsualPlaces(home));
            collector = new Collector(settings, home, dir => ClaudeCli.AgentsJson(claude, dir, home), SessionFiles.ChangeStamp);
            collector.Start();
            // The one thing the app asks of the network: GitHub, through the owner's own gh, about the repos on the map.
            // The reader looks at the setting before every round, so turning it off in the file stops the asking as soon as
            // the file is read again.
            var gh = ClaudeCli.ResolvePath(settings.ghPath, Gh.UsualPlaces());
            pullReader = new PullReader(root => Gh.Ask(gh, root), () => settings.pullRequests);
            pullReader.Start();
            zellij = ClaudeCli.ResolvePath(settings.zellijPath, Zellij.UsualPlaces(home));
            // The pile's file sits beside settings.json, never under an agent app's folder.
            pile = new Pile(settings, home, Path.Combine(Application.persistentDataPath, "pile.json"));
            pile.Start();
            // The world's own file: which repo has which plot.
            plotsPath = Path.Combine(Application.persistentDataPath, "world.json");
            plots = Plots.Load(plotsPath);
        }

        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : "";
        }

        IEnumerator Shots()
        {
            Directory.CreateDirectory(shotsDir);
            for (int i = 0; i < 5; i++) yield return null;   // let the window open and the first layout run
            world.LookAtAll();
            if (showArrivals)
            {
                // The courier is caught as it leaves its sender, a short trail behind it; in the sample world a second one is
                // then on its way between two bots of that sector. Then three fifths of its way, where it comes off the road
                // between two regions onto the recipient's lane; its recipient pulses for nine tenths of a second once it arrives.
                world.LookAt(lastSender);
                yield return new WaitForSecondsRealtime(Mathf.Max(0f, rideEnds - longestRide * .86f - Time.unscaledTime));
                yield return Shot("start.png");
                world.LookAt(lastRecipient);   // close enough to make out the bike
                yield return new WaitForSecondsRealtime(Mathf.Max(0f, rideEnds - longestRide * .4f - Time.unscaledTime));
                yield return Shot("courier.png");
                yield return new WaitForSecondsRealtime(Mathf.Max(0f, rideEnds + .2f - Time.unscaledTime));   // a picture takes two more frames
                yield return Shot("pulse.png");
                world.LookAtAll();
            }
            yield return Shot("far.png");
            // A picture of the same map after the owner has moved things: the world's file is swapped for one in which
            // regions and sectors stand elsewhere, as a drop does, and the map is laid again.
            if (Arg("-moved") != "")
            {
                plots = Plots.Load(Arg("-moved"));
                worldFrom = null;
                yield return null;
                yield return null;
                world.LookAtAll();
                yield return Shot("moved.png");
            }
            world.LookAtOpening();
            world.Select(Arg("-select"));   // a picture with a bot's card in it
            var depot = worldDrawn.sectors.Find(s => s.name == Arg("-depot"));   // and with that sector's depot list open
            if (depot != null) depotOpen = depot.plot;
            var listed = worldDrawn.sectors.Find(s => s.name == Arg("-workspace"));   // or with that sector's list of workspaces open
            if (listed != null && listed.repoRoot != "")
            {
                workspaceFor = listed.repoRoot;
                workspaceAt = new Vector2(Screen.width * .3f, Screen.height * .3f);
            }
            yield return Shot("near.png");
            // A picture of the map after that sector's repo was given a workspace, as a click in the list does.
            if (listed != null && Arg("-give") != "")
            {
                GiveWorkspace(listed.repoRoot, Arg("-give"));
                workspaceFor = "";
                yield return null;
                yield return null;
                world.LookAtAll();
                yield return Shot("given.png");
            }
            // A picture of bots on their way: the snapshot is swapped for a later one, in which some have gone to work on
            // another floor, and every one of them is put three fifths of its way there.
            if (Arg("-then") != "")
            {
                fixedSnap = JsonUtility.FromJson<RosterSnapshot>(File.ReadAllText(Arg("-then")));
                yield return null;
                yield return null;
                world.Hurry(.6f);
                yield return Shot("stairs.png");
            }
            Application.Quit();
        }

        IEnumerator Shot(string file)
        {
            yield return null;
            yield return null;
            ScreenCapture.CaptureScreenshot(Path.Combine(shotsDir, file));
            yield return null;
            yield return null;
        }

        void OnDestroy()
        {
            collector?.Dispose();
            pullReader?.Dispose();
            pile?.Dispose();
        }

        void Update()
        {
            if (collector == null) return;
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
            if (collector == null && fixedSnap == null) return;
            if (chip == null) MakeStyles();
            // Retina screens report pixels; draw in points so text keeps its size.
            float scale = Screen.dpi >= 160f ? 2f : 1f;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            // IMGUI calls OnGUI once to lay out and again to paint; both passes must see the same rows.
            if (Event.current.type == EventType.Layout)
            {
                drawn = fixedSnap ?? collector.Latest;
                if (pile != null) pileDrawn = pile.Latest;
                tab = nextTab;
                worldOpen = nextWorldOpen;
                selected = world.Selected;
                if (openKey != nextOpenKey)
                {
                    openKey = nextOpenKey;
                    openBody = openKey == "" ? "" : pile.ReadBody(openKey) ?? "The transcript this message was read from is no longer there.";
                }
            }
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            float width = Screen.width / scale - 16, height = Screen.height / scale - 12;
            bool wide = Screen.width / scale >= SideBySideWidth;
            var area = new Rect(8, 32, width, height - 26);

            // The view switch. Wide: the two lists side by side, or the world. Narrow: one of the three.
            GUILayout.BeginArea(new Rect(8, 6, width, 24));
            GUILayout.BeginHorizontal();
            if (fixedSnap != null) GUILayout.Label("Snapshot", dim);
            else if (wide) nextWorldOpen = GUILayout.Toolbar(worldOpen ? 1 : 0, new[] { "Lists", "World" }, button, GUILayout.Width(160)) == 1;
            else
            {
                int unticked = 0;
                foreach (var r in pileDrawn.rows) if (!r.ticked) unticked++;
                int picked = GUILayout.Toolbar(worldOpen ? 2 : tab, new[] { "Agents (" + drawn.agents.Count + ")", "Messages (" + unticked + ")", "World" }, button);
                nextWorldOpen = picked == 2;
                if (picked < 2) nextTab = picked;
            }
            GUILayout.FlexibleSpace();
            if (worldOpen)
            {
                if (GUILayout.Button("−", button, GUILayout.Width(28))) world.ZoomBy(-1);
                if (GUILayout.Button("+", button, GUILayout.Width(28))) world.ZoomBy(1);
            }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();

            if (worldOpen) DrawWorld(area, scale, now);
            else
            {
                if (Event.current.type == EventType.Layout)
                {
                    world.Hide();
                    leftWorld = true;
                }
                if (wide)
                {
                    float half = (width - 12) / 2;
                    DrawRoster(new Rect(8, 32, half, area.height), drawn, now);
                    DrawPile(new Rect(8 + half + 12, 32, half, area.height), pileDrawn, now);
                }
                else if (tab == 0) DrawRoster(area, drawn, now);
                else DrawPile(area, pileDrawn, now);
            }
            if (workspaceFor != "") DrawWorkspaces(new Rect(0, 0, width + 16, height + 12));
        }

        // The map, and over it who needs the owner and the card of the bot that was clicked. All from the snapshot the lists draw.
        void DrawWorld(Rect map, float scale, long now)
        {
            if (Event.current.type == EventType.Layout)
            {
                // The same snapshot gives the same world: build it once, so the view can tell that nothing changed.
                var pullsNow = pullReader == null ? pullsFixed : settings.pullRequests ? pullReader.Latest : null;   // turned off: none, at once
                if (!ReferenceEquals(drawn, worldFrom) || !ReferenceEquals(pullsNow, pullsFrom))
                {
                    worldFrom = drawn;
                    pullsFrom = pullsNow;
                    worldDrawn = World.Build(drawn, plots, settings, pullsNow);
                    pullReader?.Watch(plots.repos);   // a repo new to the map is asked about at once
                    needsYou = Panels.NeedsYou(drawn);
                }
                if (fixedSnap == null)
                {
                    // A failed write is tried again on the next pass, so new plots are not lost when the disk recovers; it is logged once, not ten times a second.
                    try { plots.SaveIfGrown(plotsPath); }
                    catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                    {
                        if (plotsWarned != plots.repos.Count) Debug.LogWarning("[world] could not save plots, will keep trying: " + e.Message);
                        plotsWarned = plots.repos.Count;
                    }
                }
                world.Show(worldDrawn, map, scale);
                card = selected == "" ? null : Panels.Card(drawn, selected, now);
                if (card == null) whereFor = "";
                else
                {
                    // shortcut: asked when the card opens and not again while it stays open, so a session moved to another tab meanwhile is shown in the old one; click the bot again
                    if (card.key != whereFor)
                    {
                        whereFor = card.key;
                        int pid = card.pid;
                        string path = zellij;
                        where = fixedSnap != null || pid <= 0 ? null : Task.Run(() => Zellij.Where(pid, path, Zellij.Run));
                    }
                    card.where = fixedSnap != null ? whereFixed : where != null && where.Status == TaskStatus.RanToCompletion ? where.Result : "";
                }
                // Messages that arrived since the world was opened, each shown once. Looked for only when the pile has changed.
                // Coming back from the lists, what arrived meanwhile is old mail and is not replayed.
                if (leftWorld && deliveries != null) deliveries.Reopen(now);
                leftWorld = false;
                if (deliveries == null) deliveries = new Deliveries(now);
                if (!ReferenceEquals(pileDrawn, deliveredFrom))
                {
                    deliveredFrom = pileDrawn;
                    var said = new List<string>();
                    foreach (var d in deliveries.New(pileDrawn, worldDrawn, said))
                    {
                        float ride = world.Deliver(d);
                        if (ride <= longestRide) continue;
                        longestRide = ride;
                        rideEnds = Time.unscaledTime + ride;
                        lastSender = d.fromBot;
                        lastRecipient = d.toBot;
                    }
                    // One line for each message that arrived: its courier, or why it has none.
                    foreach (var line in said) Debug.Log("[mail] " + line);
                }
            }
            var depotOf = depotOpen < 0 ? null : worldDrawn.sectors.Find(s => s.plot == depotOpen);
            panels.Place(map, needsYou.Count, card, depotOf);
            world.Gui(detail, title, panels.Covers, workspaceFor == "");   // while the list of workspaces is open, the map is deaf
            if (drawn.errors.Count > 0) GUI.Label(new Rect(map.x + 4, map.y + 4, map.width - 8, 32), string.Join(" | ", drawn.errors), error);
            string notAsked = depotOf == null ? "" : depotOf.repoRoot == "" ? "Sessions with no repo have no pull requests."
                : fixedSnap != null ? "This snapshot has no pull requests for this repo."
                : !settings.pullRequests ? "Asking GitHub is turned off: pullRequests in settings.json." : "GitHub has not answered yet.";
            string clicked = panels.Draw(map, needsYou, card, detail, title, depotOf, notAsked);
            // A click on a depot opens its list, or closes it if it is the open one. A click on a row opens that pull request.
            if (world.DepotClicked >= 0) depotOpen = depotOpen == world.DepotClicked ? -1 : world.DepotClicked;
            if (panels.DepotClosed) depotOpen = -1;
            if (RosterFormat.IsWebLink(panels.PullClicked)) Application.OpenURL(panels.PullClicked);
            if (clicked != "")
            {
                world.Select(clicked);
                world.LookAt(clicked);
            }
            // A click on the card's workspace, or on a sector's name: the list of workspaces, for that repo.
            if (panels.WorkspaceClicked && card != null) OpenWorkspaces(card.repoRoot);
            var named = world.SectorClicked < 0 ? null : worldDrawn.sectors.Find(s => s.plot == world.SectorClicked);
            if (named != null) OpenWorkspaces(named.repoRoot);
            if (world.RegionClicked >= 0 && world.RegionClicked < worldDrawn.regions.Count) CycleLight(worldDrawn.regions[world.RegionClicked]);
            // The owner dragged a region or a sector to a new place: it is kept there. The view has seen that it may stand there.
            if (world.MovedRegion >= 0 && world.MovedRegion < worldDrawn.regions.Count) plots.MoveRegion(worldDrawn.regions[world.MovedRegion].name, world.MovedBy);
            var movedSector = world.MovedPlot < 0 ? null : worldDrawn.sectors.Find(s => s.plot == world.MovedPlot);
            if (movedSector != null) plots.MoveSector(movedSector.plot, movedSector.cell + world.MovedBy);
            if (world.MovedRegion >= 0 || movedSector != null) worldFrom = null;   // the same roster, laid out otherwise: build the world again
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
            else if (GUILayout.Button(a.workspace, button)) OpenWorkspaces(a.repoRoot);
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

        // Opens the list of workspaces for a repo, where the pointer is. A session with no repo has no workspace to pick.
        void OpenWorkspaces(string repoRoot)
        {
            if (repoRoot == "") return;
            workspaceFor = repoRoot;
            workspaceAt = GUIUtility.GUIToScreenPoint(Event.current.mousePosition);
        }

        // The open list of workspaces, over whichever view is showing.
        void DrawWorkspaces(Rect window)
        {
            var row = drawn.agents.Find(a => a.repoRoot == workspaceFor);
            string current = row != null ? row.workspace : settings.WorkspaceFor(workspaceFor);
            // Every workspace the settings know, and any other that is in use: a snapshot brings its own.
            var names = new List<string>(settings.workspaces);
            foreach (var a in drawn.agents)
                if (a.workspace != "" && !names.Contains(a.workspace)) names.Add(a.workspace);
            if (!names.Contains(current)) names.Add(current);
            string repo = row != null ? row.repoName : Path.GetFileName(workspaceFor.TrimEnd('/'));
            panels.Workspaces(window, GUIUtility.ScreenToGUIPoint(workspaceAt), repo, current, names, detail, title,
                name => { GiveWorkspace(workspaceFor, name); workspaceFor = ""; }, () => workspaceFor = "");
        }

        // The collector reads these settings on its own thread; a read that overlaps this change shows up as one error line for one pass.
        void GiveWorkspace(string repoRoot, string name)
        {
            // On a snapshot the repo changes workspace for as long as the app is open: there is no settings file to keep it in.
            if (fixedSnap == null) settings.Reload(settingsPath); // pick up what was changed by hand since the app started
            pullReader?.Wake();                                   // and the pull request setting: off stops the asking, on starts it
            // Every workspace in use is in the list first, so that a new one comes after them all and takes the next light.
            foreach (var a in drawn.agents)
                if (a.workspace != "" && !settings.workspaces.Contains(a.workspace)) settings.workspaces.Add(a.workspace);
            string given = settings.SetWorkspace(repoRoot, name);
            if (fixedSnap == null) TrySave();
            // The lists and the card show it at once; the collector's next pass will say the same.
            foreach (var a in drawn.agents)
                if (a.repoRoot == repoRoot) a.workspace = given;
            pile?.Refresh();   // the cross-workspace flags follow the new assignment
            worldFrom = null;  // the same roster, in other regions: build the world again
        }

        // A click on a region's name: the region takes the next light, and keeps it.
        void CycleLight(WorldRegion region)
        {
            // On a snapshot the light changes for as long as the app is open: there is no settings file to keep it in.
            if (fixedSnap == null) settings.Reload(settingsPath);
            pullReader?.Wake();
            settings.CycleLight(region.name, region.look);
            if (fixedSnap == null) TrySave();
            worldFrom = null;   // the same roster, lit otherwise: build the world again
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
