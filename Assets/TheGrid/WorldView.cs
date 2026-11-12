using System;
using System.Collections.Generic;
using TheGrid.Core;
using UnityEngine;

namespace TheGrid
{
    // The world, as the window sees it: a camera on the stage, moving and zooming, and the names drawn over the map.
    // WorldStage builds what the camera looks at. This draws nothing but the WorldModel it is shown.
    // The map is drawn into a picture and the picture is put on screen every frame; the map itself is drawn again only when
    // something in it changed. Most of the day nothing does, and several hundred sprites ten times a second is what an idle
    // world would otherwise cost.
    public sealed class WorldView : MonoBehaviour
    {
        static readonly Color Ground = new Color32(0x0b, 0x0e, 0x22, 255);   // the palette's ground colour
        const float BounceSeconds = .4f;
        const int BadgeHeight = 37;   // art pixels from where a bot stands to the foot of its badge: just over the helmet

        Camera cam;
        Transform holder;
        WorldStage stage;
        WorldTraffic traffic;
        readonly List<Rect> botNames = new List<Rect>();
        // The names the owner can take hold of, where each was last drawn: a region's (its workspace's name) and a sector's
        // (its plot). A press on one and a drag moves the region, or the sector within its region.
        readonly List<(Rect plate, string region, int plot)> handles = new List<(Rect, string, int)>();
        string heldRegion = "";   // the region the owner is dragging, by name
        int heldPlot = -1;        // or the sector, by plot
        Vector2 heldAt;           // where on the map the press was
        Vector2Int heldBy;        // how far it has been dragged: tiles for a region, cells of its region's grid for a sector
        readonly Sprite[] badges = new Sprite[5];   // in the order of Badge; none for Badge.None

        WorldModel model = new WorldModel();
        RenderTexture picture;    // the last drawing of the map
        bool stale = true;        // the map has changed since it was drawn
        Rect view, bounds;        // view: where the map is, in points
        float pointScale = 1f;
        int pixelW, pixelH, level = 2;
        bool opened;              // the view has been pointed at a sector, once there was one to point at
        Vector2 centre, pressed, last;
        bool dragging, moved;
        float fastUntil;

        public string Selected { get; private set; } = "";

        // The region whose name was clicked in this pass of Gui, as its number among the model's regions; else -1. The owner
        // wants it in the next light.
        public int RegionClicked { get; private set; } = -1;

        // The sector whose depot was clicked in this pass of Gui, as its plot; else -1. The owner wants its list.
        public int DepotClicked { get; private set; } = -1;
        // The plot of the sector whose name was clicked in the last Gui; else -1. Its repo's workspace is the owner's to pick.
        public int SectorClicked { get; private set; } = -1;

        // What the owner let go of in this pass of Gui after dragging it to a place it may stand: a region, as its number
        // among the model's regions, moved MovedBy tiles; or a sector, as its plot, moved MovedBy cells of its region's
        // grid. Else -1.
        public int MovedRegion { get; private set; } = -1;
        public int MovedPlot { get; private set; } = -1;
        public Vector2Int MovedBy { get; private set; }

        // True when one art pixel is smaller than a point: too small for names, so each sector is summed up instead.
        bool Far => Iso.Zoom[level] < pointScale;

        // Called on every Layout pass while the world is open.
        public void Show(WorldModel world, Rect viewPoints, float scale)
        {
            view = viewPoints;
            pointScale = scale;
            Lay(world, Mathf.RoundToInt(view.width * scale), Mathf.RoundToInt(view.height * scale));
            Draw();
        }

        // Draws the map into its picture, if anything in it changed since the last drawing.
        void Draw()
        {
            if (!stale) return;
            stale = false;
            var old = RenderTexture.active;
            cam.Render();
            RenderTexture.active = old;
        }

        void OnDestroy() => Drop(picture);

        // Releasing a picture frees its memory on the graphics card; the object itself has to be destroyed as well, or
        // every size a window is dragged through leaves one behind.
        static void Drop(RenderTexture old)
        {
            if (old == null) return;
            old.Release();
            if (Application.isPlaying) Destroy(old); else DestroyImmediate(old);
        }

        public void Hide()
        {
            if (holder != null) holder.gameObject.SetActive(false);
            Application.targetFrameRate = 10;
        }

        // The world as a picture, without a window: what the camera sees, no text. far shows every sector; otherwise the view the world opens on.
        // at: the moment to show, in seconds, for what changes with time (the pulse of a busy building's lights); now, if left out.
        // walked: how far along its walk to show every bot that this world sets walking, from 0 to 1; where it starts, if left out.
        public Texture2D Picture(WorldModel world, int width, int height, bool far, float at = -1f, float walked = 0f)
        {
            pointScale = 2f;   // as on the screens the window is drawn for, where a point is two pixels
            opened = false;
            Lay(world, width, height);
            if (far) LookAtAll();
            Lay(world, width, height);   // again: far or near may have changed with the level
            float now = at >= 0 ? at : Time.unscaledTime;
            stage.Hurry(walked, now);
            stage.Tick(now);
            stale = true;
            Draw();
            var old = RenderTexture.active;
            RenderTexture.active = picture;
            var copy = new Texture2D(pixelW, pixelH, TextureFormat.RGB24, false);
            copy.ReadPixels(new Rect(0, 0, pixelW, pixelH), 0, 0);
            copy.Apply();
            RenderTexture.active = old;
            return copy;
        }

        // Puts every bot that is walking so far along its walk, from 0 to 1: for a picture of bots on their way.
        public void Hurry(float along) => stage?.Hurry(along, Time.unscaledTime);

        // Every sector in view, at the closest level that fits them.
        public void LookAtAll() => Fit(bounds);

        // The view the world opens on: close on the first sector that needs the owner, else on the first with anyone in it.
        // False while there is nobody anywhere to open on.
        public bool LookAtOpening()
        {
            var sector = World.Opening(model);
            if (sector == null) return false;
            LookAtSector(sector.origin);
            // In a window too small for the whole sector, the first bot that needs the owner must still be in view: the
            // view moves off the sector's middle only as far as that takes.
            var waiting = sector.bots.Find(b => b.needsOwner);
            if (waiting == null) return true;
            var at = Iso.BotSpot(sector.origin, sector.layout, waiting.place, waiting.slot) + new Vector2(0, 16);
            float zoom = Iso.Zoom[level], reachX = Mathf.Max(0, pixelW / (2 * zoom) - 48), reachY = Mathf.Max(0, pixelH / (2 * zoom) - 48);
            centre.x = Mathf.Clamp(centre.x, at.x - reachX, at.x + reachX);
            centre.y = Mathf.Clamp(centre.y, at.y - reachY, at.y + reachY);
            return true;
        }

        // One sector and its lane, at the closest level that fits: near enough to follow a bot from a doorway to a workstation.
        // Never so far out that bots cannot be made out: in a small window the sector is cut off at its edges instead.
        public void LookAtSector(Vector2Int origin)
        {
            Fit(Iso.Bounds(new[] { origin }));
            while (Far && level < Iso.Zoom.Length - 1) level++;
        }

        // Centres on a bot, close enough to make it out.
        public void LookAt(string botKey)
        {
            if (stage == null || !stage.TryGetBot(botKey, out var at)) return;
            while (Far && level < Iso.Zoom.Length - 1) level++;
            centre = at;
        }

        // Shows a message arriving: a courier between sectors, a pulse under the recipient. Returns the seconds until the pulse.
        public float Deliver(Delivery d) => traffic != null ? traffic.Add(d, Time.unscaledTime) : 0f;

        // Selects a bot as a click on it does. A key no bot has selects nothing.
        public void Select(string botKey)
        {
            if (stage != null && stage.TryGetBot(botKey, out _)) Selected = botKey;
        }

        public void ZoomBy(int steps) => level = Iso.ClampLevel(level + steps);

        // False to keep the map still under a pointer at its edge: for pictures.
        public bool EdgeMoves = true;
        const float EdgePoints = 28f, EdgeSpeed = 600f;   // how wide the edge is, and how fast the map moves, in the window's points
        float wheel, wheelAt = -10f;                      // what the wheel has turned toward its next level, and when it last zoomed
        Func<Vector2, bool> taken;                        // as the last Gui was told: where panels are, and whether the map listens
        bool deaf;

        // What has to be in view is more than the ground: the skyline stands above the back of a sector, with its names over it.
        void Fit(Rect ground)
        {
            if (ground.width <= 0 || stage == null) return;
            ground.yMax += Skyline;
            level = Iso.FitLevel(pixelW, pixelH, ground);
            centre = ground.center;
        }

        // How far the tallest roof, and the name over it, stand above the back corner of the ground.
        float Skyline => Mathf.Max(0, Iso.StructureSpot(Vector2Int.zero, Layout.Of(0), Structure.ServerTower).y + stage.Top(Structure.ServerTower) + 24 - Iso.Bounds(new[] { Vector2Int.zero }).yMax);

        void Lay(WorldModel world, int width, int height)
        {
            if (holder == null) Create();
            if (!holder.gameObject.activeSelf)
            {
                holder.gameObject.SetActive(true);
                stale = true;
            }
            pixelW = Mathf.Max(2, width & ~1);    // even, so the centre of the view falls between two pixels and art stays crisp
            pixelH = Mathf.Max(2, height & ~1);
            // The model is a new object only when the roster changed. The rest of the time there is nothing to lay out, only
            // the camera to keep where it should be.
            if (!ReferenceEquals(world, model))
            {
                model = world;
                var cells = new List<Vector2Int>();
                foreach (var s in model.sectors) cells.Add(s.origin);
                bounds = Iso.Bounds(cells);
                // Opened once, and not before there is someone to open on: the first roster may arrive after the first click.
                if (!opened) opened = LookAtOpening();
                stale |= stage.Sync(model);
                if (Selected != "" && !stage.TryGetBot(Selected, out _)) Selected = "";
            }
            PlaceCamera();
        }

        void Create()
        {
            holder = new GameObject("World").transform;
            holder.SetParent(transform, false);

            cam = new GameObject("World camera").AddComponent<Camera>();
            cam.transform.SetParent(holder, false);
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Ground;
            cam.enabled = false;   // it draws when Draw says so, into the picture, never on its own
            cam.nearClipPlane = -1000;   // a thing's z is how many pixels nearer it is drawn than where it stands
            cam.farClipPlane = 1000;
            // Lower on the screen is nearer the viewer, so it is drawn later and covers what stands behind it. Of two things at
            // the same height on screen, the one nearer the camera wins: a bot, over the floor it stands on.
            cam.transparencySortMode = TransparencySortMode.CustomAxis;
            cam.transparencySortAxis = new Vector3(0, 1, 1);

            stage = new WorldStage(holder);
            traffic = new WorldTraffic(holder, stage);
            for (int i = 1; i < badges.Length; i++) badges[i] = WorldStage.Art(ArtNames.Badges[i]);
        }

        void Update()
        {
            if (holder == null || !holder.gameObject.activeSelf) return;
            float now = Time.unscaledTime;
            bool riding = traffic.Tick(now);
            stale |= stage.Tick(now) | riding;
            if (stage.Walking || riding) fastUntil = now + .2f;
            // The pointer resting at an edge of the map moves the map that way: not while something is being dragged, nor
            // over a panel, nor while this is not the window in front.
            if (EdgeMoves && !deaf && !dragging && Application.isFocused)
            {
                var pointer = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y) / pointScale;
                var push = taken != null && taken(pointer) ? Vector2.zero : Iso.EdgePan(view, pointer, EdgePoints);
                if (push != Vector2.zero)
                {
                    centre += push * (EdgeSpeed * Time.unscaledDeltaTime * pointScale / Iso.Zoom[level]);
                    PlaceCamera();
                    fastUntil = now + .2f;
                }
            }
            // Ten frames a second is plenty for a lens that sits open all day; a walk or a drag gets sixty while it lasts.
            Application.targetFrameRate = now < fastUntil ? 60 : 10;
        }

        void PlaceCamera()
        {
            float zoom = Iso.Zoom[level];
            if (bounds.width > 0)
            {
                centre.x = Mathf.Clamp(centre.x, bounds.xMin, bounds.xMax);
                centre.y = Mathf.Clamp(centre.y, bounds.yMin, bounds.yMax + Skyline);
            }
            if (picture == null || picture.width != pixelW || picture.height != pixelH)
            {
                Drop(picture);
                picture = new RenderTexture(pixelW, pixelH, 16) { filterMode = FilterMode.Point };
                cam.targetTexture = picture;
                stale = true;
            }
            float size = pixelH / (2f * zoom), aspect = (float)pixelW / pixelH;
            // Whole screen pixels only, or the art shimmers as it moves.
            var at = new Vector3(Mathf.Round(centre.x * zoom) / zoom, Mathf.Round(centre.y * zoom) / zoom, 0);
            if (cam.orthographicSize != size || cam.aspect != aspect || cam.transform.localPosition != at) stale = true;
            cam.orthographicSize = size;
            cam.aspect = aspect;
            cam.transform.localPosition = at;
        }

        // Where a point of the map is in the view, in points from the view's top left corner.
        Vector2 ToView(Vector2 world)
        {
            float k = Iso.Zoom[level] / pointScale;
            Vector2 c = cam.transform.localPosition;
            return new Vector2(view.width / 2 + (world.x - c.x) * k, view.height / 2 - (world.y - c.y) * k);
        }

        Vector2 ToWorld(Vector2 viewPoint)
        {
            float k = Iso.Zoom[level] / pointScale;
            Vector2 c = cam.transform.localPosition;
            return new Vector2(c.x + (viewPoint.x - view.width / 2) / k, c.y - (viewPoint.y - view.height / 2) / k);
        }

        // Mouse and keys, then the picture of the map and the names over it, drawn as text at the screen's own sharpness.
        // Call inside OnGUI after Show. covered: true for a point that something drawn over the map has taken; a press or a
        // scroll there is not the map's.
        public void Gui(GUIStyle label, GUIStyle loud, Func<Vector2, bool> covered = null, bool listens = true)
        {
            if (holder == null) return;
            var e = Event.current;
            RegionClicked = MovedRegion = MovedPlot = DepotClicked = SectorClicked = -1;
            taken = covered;
            deaf = !listens;
            // With something open over the whole window, the map takes no press, scroll or key: they are for what is open.
            if (listens) Handle(e, covered);
            if (e.type != EventType.Repaint) return;

            if (picture != null) GUI.DrawTexture(new Rect(view.x, view.y, pixelW / pointScale, pixelH / pointScale), picture, ScaleMode.StretchToFill, false);
            GUI.BeginGroup(view);
            if (model.sectors.Count == 0) GUI.Label(new Rect(8, 8, view.width - 16, 20), "No live agent sessions.", label);
            bool up = (int)(Time.unscaledTime / BounceSeconds) % 2 == 0;
            // Where the bots' names will go: a building's name gives way to them.
            botNames.Clear();
            handles.Clear();
            if (!Far)
                foreach (var sector in model.sectors)
                    foreach (var b in sector.bots)
                        if (stage.TryGetBot(b.key, out var at))
                        {
                            var plate = Plate(ToView(at - new Vector2(0, 8)), BotName(b), label);
                            if (plate.width > 0) botNames.Add(plate);
                        }
            foreach (var sector in model.sectors)
            {
                var o = sector.origin;
                if (Far)
                {
                    // Too far to make out bots: the sector's name, how many of its bots need the owner and how many are done.
                    // Only a sector that needs the owner is loud and gets a badge.
                    var at = ToView(Iso.Bounds(new[] { sector.origin }).center);
                    Text(at, sector.name, label);
                    handles.Add((Plate(at, sector.name, label), "", sector.plot));
                    var summary = World.Summary(sector);
                    if (summary != "") Text(at + new Vector2(0, 16), summary, sector.needOwner > 0 ? loud : label);
                    if (sector.needOwner > 0 && badges[(int)Badge.Question] != null)
                        GUI.DrawTexture(new Rect(at.x - 8, at.y - 26 - (up ? 2 : 0), 16, 20), badges[(int)Badge.Question].texture);
                    continue;
                }
                // The sector's name lies on the lane along its front. A building's name floats over its roof. A bot at a
                // door behind a low building has its name there too: then the building goes unnamed, and is known by sight.
                var onLane = ToView(Iso.Point(o.x + 5.5f, o.y + Iso.PlotTiles + .5f)) - new Vector2(0, 9);
                Text(onLane, sector.name, loud);
                handles.Add((Plate(onLane, sector.name, loud), "", sector.plot));
                foreach (Structure s in Enum.GetValues(typeof(Structure)))
                {
                    if (WorldStage.StructureNames[(int)s] == "") continue;
                    // The depot says how many pull requests are open, when any are.
                    string called = WorldStage.StructureNames[(int)s] + (s == Structure.Depot && sector.pulls.Count > 0 ? " · " + sector.pulls.Count : "");
                    var at = ToView(Iso.StructureSpot(sector.origin, sector.layout, s) + new Vector2(0, stage.Top(s) + 3)) - new Vector2(0, 18);
                    var plate = Plate(at, called, label);
                    plate = new Rect(plate.x + 6, plate.y + 3, plate.width - 12, plate.height - 6);   // plates that only touch both stay
                    bool taken = false;
                    foreach (var name in botNames) taken |= name.Overlaps(plate);
                    if (!taken) Text(at, called, label);
                }
            }
            // Far out, and with more than one region, each region is named after its workspace, over the back of its ground.
            if (Far && model.regions.Count > 1)
                for (int r = 0; r < model.regions.Count; r++)
                {
                    var cells = new List<Vector2Int>();
                    foreach (var sector in model.sectors) if (sector.region == r) cells.Add(sector.origin);
                    var ground = Iso.Bounds(cells);
                    var at = ToView(new Vector2(ground.center.x, ground.yMax + Skyline)) - new Vector2(0, 20);
                    handles.Add((Plate(at, model.regions[r].name, loud), model.regions[r].name, -1));
                    Text(at, model.regions[r].name, loud);
                }
            // Bots are named last, over everything: where two sectors meet, a bot's name matters more than a roof's.
            if (!Far)
                foreach (var sector in model.sectors)
                    foreach (var b in sector.bots)
                        if (stage.TryGetBot(b.key, out var at))
                            Text(ToView(at - new Vector2(0, 8)), BotName(b), b.key == Selected ? loud : label);
            // Badges go on last of all, over buildings and over names: what a bot needs from the owner is never covered.
            // The three that need the owner bounce; done does not.
            float k = Iso.Zoom[level] / pointScale;
            if (!Far)
                foreach (var sector in model.sectors)
                    foreach (var b in sector.bots)
                        if (b.badge != Badge.None && badges[(int)b.badge] != null && stage.TryGetBot(b.key, out var at))
                        {
                            var foot = ToView(at + new Vector2(0, BadgeHeight + (b.needsOwner && up ? 2 : 0)));
                            GUI.DrawTexture(new Rect(Mathf.Round(foot.x - 8 * k), Mathf.Round(foot.y - 18 * k), 16 * k, 20 * k), badges[(int)b.badge].texture);
                        }
            Outline();
            GUI.EndGroup();
        }

        // While the owner drags a region or a sector: a dotted outline of where each sector would stand, plot and lane.
        // In the colour of a fault where it may not stand.
        void Outline()
        {
            if (!moved || heldBy == Vector2Int.zero || !Held(out int region, out var sector)) return;
            bool may = sector != null ? World.CanMoveSector(model, sector, sector.cell + heldBy) : World.CanMoveRegion(model, region, heldBy);
            var old = GUI.color;
            GUI.color = may ? Color.white : new Color32(0xff, 0x4d, 0x4d, 255);
            float a = -Iso.LaneTiles - .5f, b = Iso.PlotTiles + Iso.LaneTiles - .5f;   // a tile's centre is at whole numbers, its edge half a tile off
            foreach (var s in model.sectors)
            {
                if (sector != null ? s != sector : s.region != region) continue;
                var o = s.origin + (sector != null ? Iso.PlotOrigin(heldBy) : heldBy);
                var corners = new[] { ToView(Iso.Point(o.x + a, o.y + a)), ToView(Iso.Point(o.x + b, o.y + a)), ToView(Iso.Point(o.x + b, o.y + b)), ToView(Iso.Point(o.x + a, o.y + b)) };
                for (int i = 0; i < 4; i++)
                {
                    Vector2 from = corners[i], to = corners[(i + 1) % 4];
                    int dots = Mathf.Max(1, Mathf.RoundToInt((to - from).magnitude / 8f));
                    for (int k = 0; k < dots; k++)
                    {
                        var at = Vector2.Lerp(from, to, k / (float)dots);
                        GUI.DrawTexture(new Rect(Mathf.Round(at.x) - 1, Mathf.Round(at.y) - 1, 3, 3), Texture2D.whiteTexture);
                    }
                }
            }
            GUI.color = old;
        }

        // The plot of the sector whose depot, or one of whose crates, is at this point of the map; -1 for none.
        int DepotAt(Vector2 world)
        {
            foreach (var sector in model.sectors)
                if (Iso.OnDepot(sector.origin, sector.layout, sector.pulls.Count, world)) return sector.plot;
            return -1;
        }

        // What the owner has hold of, in the model as it is now: the region's number, or the sector. False when it has
        // gone from the map since the press.
        bool Held(out int region, out WorldSector sector)
        {
            region = heldRegion == "" ? -1 : model.regions.FindIndex(r => r.name == heldRegion);
            sector = heldPlot < 0 ? null : model.sectors.Find(s => s.plot == heldPlot);
            return region >= 0 || sector != null;
        }

        static string BotName(WorldBot b) => b.subagents > 0 ? b.name + " +" + b.subagents : b.name;

        // The dark plate a line of text centred on a point is drawn on. Nothing when off the view.
        Rect Plate(Vector2 at, string text, GUIStyle style)
        {
            if (at.x < -80 || at.y < -20 || at.x > view.width + 80 || at.y > view.height + 20) return default;
            // Text is drawn at the screen's scale, where it comes out a little wider than it measures; without slack the end is cut off.
            var size = style.CalcSize(new GUIContent(text)) + new Vector2(10, 0);
            return new Rect(Mathf.Round(at.x - size.x / 2), Mathf.Round(at.y), size.x, size.y);
        }

        // One line of text centred on a point, on a dark plate so it reads over the art. Skipped when off the view.
        void Text(Vector2 at, string text, GUIStyle style)
        {
            var rect = Plate(at, text, style);
            if (rect.width <= 0) return;
            var content = new GUIContent(text);
            var old = GUI.color;
            GUI.color = new Color(.02f, .03f, .06f, .8f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = old;
            GUI.Label(new Rect(rect.x + 5, rect.y, rect.width - 5, rect.height), content, style);
        }

        void Handle(Event e, Func<Vector2, bool> covered)
        {
            bool inside = view.Contains(e.mousePosition) && (covered == null || !covered(e.mousePosition));
            float k = Iso.Zoom[level] / pointScale;   // points per art pixel
            switch (e.type)
            {
                case EventType.MouseDown when inside && e.button == 0:
                    dragging = true;
                    moved = false;
                    pressed = last = e.mousePosition;
                    // A press on a name takes hold of what it names; anywhere else, of the map.
                    var held = handles.Find(h => h.plate.Contains(e.mousePosition - view.position));
                    heldRegion = held.region ?? "";
                    heldPlot = held.plate.width > 0 ? held.plot : -1;
                    heldAt = ToWorld(e.mousePosition - view.position);
                    heldBy = Vector2Int.zero;
                    e.Use();
                    break;
                case EventType.MouseDrag when dragging:
                case EventType.MouseUp when dragging:
                    // The map follows where the pointer is, not the movement the event reports: a scripted or remote pointer leaves
                    // that empty. A press that never strays three points is a click.
                    bool holding = heldRegion != "" || heldPlot >= 0;
                    if (moved || (e.mousePosition - pressed).sqrMagnitude > 9)
                    {
                        moved = true;
                        if (holding)
                        {
                            // A region goes anywhere, tile by tile. A sector goes from cell to cell of its region's grid.
                            var tiles = Iso.Tiles(ToWorld(e.mousePosition - view.position) - heldAt);
                            heldBy = heldPlot < 0 ? tiles : new Vector2Int(Mathf.RoundToInt(tiles.x / (float)Iso.Pitch), Mathf.RoundToInt(tiles.y / (float)Iso.Pitch));
                        }
                        else
                        {
                            var step = e.mousePosition - last;
                            centre += new Vector2(-step.x, step.y) / k;
                            PlaceCamera();
                        }
                        last = e.mousePosition;
                        fastUntil = Time.unscaledTime + .5f;
                    }
                    if (e.type == EventType.MouseUp)
                    {
                        dragging = false;
                        if (!moved)
                        {
                            // A click on a region's name is for the region, one on a sector's name for the sector; anywhere
                            // else, for the bot under it.
                            RegionClicked = heldRegion == "" ? -1 : model.regions.FindIndex(r => r.name == heldRegion);
                            SectorClicked = heldPlot;
                            bool onName = RegionClicked >= 0 || SectorClicked >= 0;
                            var at = ToWorld(e.mousePosition - view.position);
                            string bot = onName ? "" : stage.Pick(at);
                            if (!onName) Selected = bot;
                            // A click on a depot that is on no bot, close enough in to tell buildings apart: its list.
                            if (!onName && bot == "" && !Far) DepotClicked = DepotAt(at);
                        }
                        else if (holding && heldBy != Vector2Int.zero && Held(out int region, out var sector))
                        {
                            // Let go where it may stand: it is moved. Anywhere else, it stays where it was.
                            bool may = sector != null ? World.CanMoveSector(model, sector, sector.cell + heldBy) : World.CanMoveRegion(model, region, heldBy);
                            if (may && sector != null) MovedPlot = sector.plot;
                            if (may && sector == null) MovedRegion = region;
                            MovedBy = heldBy;
                        }
                        heldRegion = "";
                        heldPlot = -1;
                        heldBy = Vector2Int.zero;
                    }
                    e.Use();
                    break;
                case EventType.ScrollWheel when inside:
                    // The wheel zooms, a level at a time, and what is under the pointer stays under it.
                    int levels = Iso.WheelStep(ref wheel, e.delta.y, Time.unscaledTime - wheelAt);
                    if (levels != 0)
                    {
                        wheelAt = Time.unscaledTime;
                        var under = ToWorld(e.mousePosition - view.position);
                        ZoomBy(levels);
                        centre += under - ToWorld(e.mousePosition - view.position);
                    }
                    Moved(e);
                    break;
                case EventType.KeyDown when e.keyCode == KeyCode.Equals || e.keyCode == KeyCode.Plus || e.keyCode == KeyCode.KeypadPlus:
                    ZoomBy(1);
                    Moved(e);
                    break;
                case EventType.KeyDown when e.keyCode == KeyCode.Minus || e.keyCode == KeyCode.KeypadMinus:
                    ZoomBy(-1);
                    Moved(e);
                    break;
            }
        }

        void Moved(Event e)
        {
            PlaceCamera();
            fastUntil = Time.unscaledTime + .5f;
            e.Use();
        }
    }
}
