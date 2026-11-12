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
        readonly List<Rect> regionNames = new List<Rect>();   // where each region's name was last drawn, in the order of the model's regions
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
        public Texture2D Picture(WorldModel world, int width, int height, bool far)
        {
            pointScale = 2f;   // as on the screens the window is drawn for, where a point is two pixels
            opened = false;
            Lay(world, width, height);
            if (far) LookAtAll();
            Lay(world, width, height);   // again: far or near may have changed with the level
            stage.Tick(Time.unscaledTime);
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

        // Every sector in view, at the closest level that fits them.
        public void LookAtAll() => Fit(bounds);

        // The view the world opens on: close on the first sector that needs the owner, else on the first with anyone in it.
        // False while there is nobody anywhere to open on.
        public bool LookAtOpening()
        {
            var sector = World.Opening(model);
            if (sector == null) return false;
            LookAtSector(sector.cell);
            // In a window too small for the whole sector, the first bot that needs the owner must still be in view: the
            // view moves off the sector's middle only as far as that takes.
            var waiting = sector.bots.Find(b => b.needsOwner);
            if (waiting == null) return true;
            var at = Iso.BotSpot(sector.cell, sector.layout, waiting.place, waiting.slot) + new Vector2(0, 16);
            float zoom = Iso.Zoom[level], reachX = Mathf.Max(0, pixelW / (2 * zoom) - 48), reachY = Mathf.Max(0, pixelH / (2 * zoom) - 48);
            centre.x = Mathf.Clamp(centre.x, at.x - reachX, at.x + reachX);
            centre.y = Mathf.Clamp(centre.y, at.y - reachY, at.y + reachY);
            return true;
        }

        // One sector and its lane, at the closest level that fits: near enough to follow a bot from a doorway to a workstation.
        // Never so far out that bots cannot be made out: in a small window the sector is cut off at its edges instead.
        public void LookAtSector(Vector2Int cell)
        {
            Fit(Iso.Bounds(new[] { cell }));
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
                foreach (var s in model.sectors) cells.Add(s.cell);
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
            cam.nearClipPlane = -10;
            cam.farClipPlane = 10;
            // Lower on the screen is nearer the viewer, so it is drawn later and covers what stands behind it. Of two things at
            // the same height on screen, the one nearer the camera wins: a bot, over the floor it stands on.
            cam.transparencySortMode = TransparencySortMode.CustomAxis;
            cam.transparencySortAxis = new Vector3(0, 1, .001f);

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
        public void Gui(GUIStyle label, GUIStyle loud, Func<Vector2, bool> covered = null)
        {
            if (holder == null) return;
            var e = Event.current;
            RegionClicked = -1;
            Handle(e, covered);
            if (e.type != EventType.Repaint) return;

            if (picture != null) GUI.DrawTexture(new Rect(view.x, view.y, pixelW / pointScale, pixelH / pointScale), picture, ScaleMode.StretchToFill, false);
            GUI.BeginGroup(view);
            if (model.sectors.Count == 0) GUI.Label(new Rect(8, 8, view.width - 16, 20), "No live agent sessions.", label);
            bool up = (int)(Time.unscaledTime / BounceSeconds) % 2 == 0;
            // Where the bots' names will go: a building's name gives way to them.
            botNames.Clear();
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
                var o = Iso.PlotOrigin(sector.cell);
                if (Far)
                {
                    // Too far to make out bots: the sector's name, how many of its bots need the owner and how many are done.
                    // Only a sector that needs the owner is loud and gets a badge.
                    var at = ToView(Iso.Bounds(new[] { sector.cell }).center);
                    Text(at, sector.name, label);
                    var summary = World.Summary(sector);
                    if (summary != "") Text(at + new Vector2(0, 16), summary, sector.needOwner > 0 ? loud : label);
                    if (sector.needOwner > 0 && badges[(int)Badge.Question] != null)
                        GUI.DrawTexture(new Rect(at.x - 8, at.y - 26 - (up ? 2 : 0), 16, 20), badges[(int)Badge.Question].texture);
                    continue;
                }
                // The sector's name lies on the lane along its front. A building's name floats over its roof. A bot at a
                // door behind a low building has its name there too: then the building goes unnamed, and is known by sight.
                Text(ToView(Iso.Point(o.x + 5.5f, o.y + Iso.PlotTiles + .5f)) - new Vector2(0, 9), sector.name, loud);
                foreach (Structure s in Enum.GetValues(typeof(Structure)))
                {
                    if (WorldStage.StructureNames[(int)s] == "") continue;
                    var at = ToView(Iso.StructureSpot(sector.cell, sector.layout, s) + new Vector2(0, stage.Top(s) + 3)) - new Vector2(0, 18);
                    var plate = Plate(at, WorldStage.StructureNames[(int)s], label);
                    plate = new Rect(plate.x + 6, plate.y + 3, plate.width - 12, plate.height - 6);   // plates that only touch both stay
                    bool taken = false;
                    foreach (var name in botNames) taken |= name.Overlaps(plate);
                    if (!taken) Text(at, WorldStage.StructureNames[(int)s], label);
                }
            }
            // Far out, and with more than one region, each region is named after its workspace, over the back of its ground.
            regionNames.Clear();
            if (Far && model.regions.Count > 1)
                for (int r = 0; r < model.regions.Count; r++)
                {
                    var cells = new List<Vector2Int>();
                    foreach (var sector in model.sectors) if (sector.region == r) cells.Add(sector.cell);
                    var ground = Iso.Bounds(cells);
                    var at = ToView(new Vector2(ground.center.x, ground.yMax + Skyline)) - new Vector2(0, 20);
                    regionNames.Add(Plate(at, model.regions[r].name, loud));
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
            GUI.EndGroup();
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
                    e.Use();
                    break;
                case EventType.MouseDrag when dragging:
                case EventType.MouseUp when dragging:
                    // The map follows where the pointer is, not the movement the event reports: a scripted or remote pointer leaves
                    // that empty. A press that never strays three points is a click.
                    if (moved || (e.mousePosition - pressed).sqrMagnitude > 9)
                    {
                        moved = true;
                        var step = e.mousePosition - last;
                        last = e.mousePosition;
                        centre += new Vector2(-step.x, step.y) / k;
                        PlaceCamera();
                        fastUntil = Time.unscaledTime + .5f;
                    }
                    if (e.type == EventType.MouseUp)
                    {
                        dragging = false;
                        if (!moved)
                        {
                            // A click on a region's name is for the region; anywhere else, for the bot under it.
                            RegionClicked = regionNames.FindIndex(plate => plate.Contains(e.mousePosition - view.position));
                            if (RegionClicked < 0) Selected = stage.Pick(ToWorld(e.mousePosition - view.position));
                        }
                    }
                    e.Use();
                    break;
                case EventType.ScrollWheel when inside:
                    if (e.command || e.control) ZoomBy(e.delta.y < 0 ? 1 : -1);
                    else centre += new Vector2(e.delta.x, -e.delta.y) * 12f / k;
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
