using System;
using System.Collections.Generic;
using TheGrid.Core;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace TheGrid
{
    // The world: one isometric map with a plot per repo. Draws nothing but the WorldModel it is shown.
    // The map is drawn into a picture and the picture is put on screen every frame; the map itself is drawn again only when
    // something in it changed. Most of the day nothing does, and several hundred sprites ten times a second is what an idle
    // world would otherwise cost.
    // A rider moves only when its agent's building or place changes; the only other motion is the bounce of a marker.
    public sealed class WorldView : MonoBehaviour
    {
        const float RideSeconds = 1.2f, BounceSeconds = .4f;
        const int OutlandRing = 3;   // plot-sized cells of scenery kept around the plots, enough to reach the edge of the overview
        const int MaxSmall = 3;   // subagents drawn beside a rider; the name label carries the full count

        static readonly string[] BuildingArt = { "b-garage", "b-library", "b-lab", "b-review", "b-workshop", "b-booth", "b-gate", "b-yard", "b-lot", "b-depot" };
        static readonly string[] BuildingNames = { "Garage", "Library", "Lab", "Review hall", "Workshop", "Booth", "Gate", "Yard", "Lot", "Depot" };
        // Glow colours from the art palette, one per account. Red, amber and orange are left for the buildings that need the owner.
        static readonly Color32[] Tints = { new Color32(0x35, 0xf2, 0xff, 255), new Color32(0x5c, 0xf2, 0x8a, 255), new Color32(0xa0, 0x70, 0xff, 255), new Color32(0x4d, 0x8d, 0xff, 255), new Color32(0xff, 0x4f, 0xd8, 255) };
        static readonly Color Dimmed = new Color(.45f, .45f, .5f);

        sealed class Rider
        {
            public Transform at;
            public SpriteRenderer body, marker;
            public readonly List<SpriteRenderer> small = new List<SpriteRenderer>();
            public Vector2 from, to;
            public float started = -100f;
            public WorldRider data;
        }

        Camera cam;
        Transform root;
        Tilemap ground;
        Tile plotTile, kerbTile, alleyTile, roadTile, roadKerbTile, roadAlleyTile, waterTile;
        Sprite[] outlandSprites;  // stones, outcrop, spire
        readonly Dictionary<Vector2Int, GameObject> outlands = new Dictionary<Vector2Int, GameObject>();
        int outlandsFor = -1;     // how many plots the scenery was last laid out around
        Sprite[] buildingSprites, facing;   // facing: se, ne, nw, sw
        Sprite smallSprite, markerSprite;
        float[] roof;             // how far above its ground point each building's highest pixel is
        readonly Dictionary<string, Rider> riders = new Dictionary<string, Rider>();
        readonly HashSet<int> painted = new HashSet<int>();
        readonly List<string> accounts = new List<string>();

        WorldModel model = new WorldModel();
        RenderTexture picture;    // the last drawing of the map
        bool stale = true;        // the map has changed since it was drawn
        bool syncedFar, lastUp;
        int markersShown;
        Rect view, bounds;        // view: where the map is, in points
        float pointScale = 1f;
        int pixelW, pixelH, level = 2;
        bool fitted;              // the view opens on every plot, once there is a plot to open on
        Vector2 centre, pressed, last;
        bool dragging, moved;
        float fastUntil;

        public string Selected { get; private set; } = "";

        // True when one art pixel is smaller than a point: too small for names, so each city is summed up instead.
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

        void OnDestroy()
        {
            if (picture != null) picture.Release();
        }

        public void Hide()
        {
            if (root != null) root.gameObject.SetActive(false);
            Application.targetFrameRate = 10;
        }

        // The world as a picture, without a window: what the camera sees, no text. far shows every plot; otherwise the first city that needs the owner, close up.
        public Texture2D Picture(WorldModel world, int width, int height, bool far)
        {
            pointScale = 1f;
            fitted = false;
            Lay(world, width, height);
            if (!far) LookAtFirstNeed();
            PlaceCamera();
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

        public void LookAtAll()
        {
            // The overview has to show more than the ground: the skyline stands above the back street, with its names over it.
            var sight = bounds;
            if (bounds.width > 0) sight.height += Mathf.Max(roof) + 24;
            level = Iso.FitLevel(pixelW, pixelH, sight);
            centre = sight.center;
        }

        // Two screen pixels to the art pixel, centred on the first city with a rider that needs the owner.
        public void LookAtFirstNeed()
        {
            level = 3;
            foreach (var c in model.cities)
                if (c.needOwner > 0) { centre = Iso.Bounds(new[] { c.cell }).center; return; }
        }

        public void ZoomBy(int steps) => level = Iso.ClampLevel(level + steps);

        void Lay(WorldModel world, int width, int height)
        {
            if (root == null) Create();
            if (!root.gameObject.activeSelf)
            {
                root.gameObject.SetActive(true);
                stale = true;
            }
            pixelW = Mathf.Max(2, width & ~1);    // even, so the centre of the view falls between two pixels and art stays crisp
            pixelH = Mathf.Max(2, height & ~1);
            // The model is a new object only when the roster changed, and far or near changes only on a zoom. The rest of the
            // time there is nothing to lay out, only the camera to keep where it should be.
            if (!ReferenceEquals(world, model) || Far != syncedFar) Sync(world);
            PlaceCamera();
        }

        void Sync(WorldModel world)
        {
            model = world;
            stale = true;
            var cells = new List<Vector2Int>();
            foreach (var city in world.cities)
            {
                cells.Add(city.cell);
                if (painted.Add(city.plot)) Paint(city);   // a new plot is drawn once; the others are not touched
            }
            bounds = Iso.Bounds(cells);
            LayOutlands(cells);
            if (!fitted)
            {
                LookAtAll();
                fitted = cells.Count > 0;
            }

            accounts.Clear();
            foreach (var city in world.cities)
                foreach (var r in city.riders)
                    if (!accounts.Contains(r.account)) accounts.Add(r.account);
            accounts.Sort(StringComparer.Ordinal);

            var seen = new HashSet<string>();
            foreach (var city in world.cities)
                foreach (var r in city.riders)
                {
                    seen.Add(r.key);
                    Place(r, Iso.RiderSpot(city.cell, r.building, r.slot));
                }
            var gone = new List<string>();
            foreach (var kv in riders) if (!seen.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (var key in gone)
            {
                Kill(riders[key].at.gameObject);
                riders.Remove(key);
            }
            if (Selected != "" && !riders.ContainsKey(Selected)) Selected = "";
            markersShown = 0;
            foreach (var r in riders.Values)
                if (r.marker.enabled) markersShown++;
            syncedFar = Far;
        }

        void Create()
        {
            root = new GameObject("World").transform;
            root.SetParent(transform, false);

            cam = new GameObject("World camera").AddComponent<Camera>();
            cam.transform.SetParent(root, false);
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(.035f, .055f, .085f);
            cam.enabled = false;   // it draws when Draw says so, into the picture, never on its own
            cam.nearClipPlane = -10;
            cam.farClipPlane = 10;
            // Lower on the screen is nearer the viewer, so it is drawn later and covers what stands behind it.
            cam.transparencySortMode = TransparencySortMode.CustomAxis;
            cam.transparencySortAxis = Vector3.up;

            var grid = new GameObject("Ground").AddComponent<Grid>();
            grid.transform.SetParent(root, false);
            grid.cellLayout = GridLayout.CellLayout.Isometric;
            grid.cellSize = new Vector3(Iso.TileW, Iso.TileH, 1);
            grid.transform.localPosition = new Vector3(0, -Iso.TileH / 2, 0);   // so a cell's centre is Iso.TileCentre
            ground = new GameObject("Tiles").AddComponent<Tilemap>();
            ground.transform.SetParent(grid.transform, false);
            ground.gameObject.AddComponent<TilemapRenderer>().sortingOrder = -10;
            plotTile = MakeTile("tile-plot");
            kerbTile = MakeTile("tile-kerb");
            alleyTile = MakeTile("tile-alley");
            roadTile = MakeTile("tile-road");
            roadKerbTile = MakeTile("tile-road-kerb");
            roadAlleyTile = MakeTile("tile-road-alley");
            waterTile = MakeTile("tile-water");
            outlandSprites = new[] { Art("o-stones"), Art("o-outcrop"), Art("o-spire") };

            buildingSprites = Array.ConvertAll(BuildingArt, Art);
            roof = Array.ConvertAll(buildingSprites, Top);
            facing = new[] { Art("rider-se"), Art("rider-ne"), Art("rider-nw"), Art("rider-sw") };
            smallSprite = Art("rider-small");
            markerSprite = Art("marker");
        }

        static Sprite Art(string name)
        {
            var s = Resources.Load<Sprite>("Art/" + name);
            if (s == null) Debug.LogError("[world] missing sprite Art/" + name + "; run tools/art/make_sprites.py");
            return s;
        }

        static float Top(Sprite s)
        {
            var px = s.texture.GetPixels32();
            int w = s.texture.width;
            for (int i = px.Length - 1; i >= 0; i--)
                if (px[i].a > 0) return i / w + 1 - s.pivot.y;
            return 0;
        }

        static Tile MakeTile(string name)
        {
            var t = ScriptableObject.CreateInstance<Tile>();
            t.sprite = Art(name);
            return t;
        }

        void Paint(WorldCity city)
        {
            var o = Iso.PlotOrigin(city.cell);
            // The plot, and the path round it. A neighbour paints the stretch between the two again, the same way.
            for (int col = -Iso.PathTiles; col < Iso.PlotTiles + Iso.PathTiles; col++)
                for (int row = -Iso.PathTiles; row < Iso.PlotTiles + Iso.PathTiles; row++)
                    ground.SetTile(At(o.x + col, o.y + row), Iso.OnPath(city.cell, o.x + col, o.y + row) ? Path(col, row) : Ground(col, row));
            foreach (Building b in Enum.GetValues(typeof(Building)))
                NewSprite(BuildingNames[(int)b], buildingSprites[(int)b], root).transform.localPosition = Iso.BuildingSpot(city.cell, b);
        }

        // Where a tile of the map is in Unity's isometric grid, whose axes run the other way.
        static Vector3Int At(int col, int row) => new Vector3Int(-row, -col, 0);

        // The outlands: the land does not stop at the last plot. Every empty cell near a plot gets stones, outcrops, now and then a
        // spire or a lake, so the view runs on to its edges the way a tycoon map runs on into trees and shoreline. It is scenery:
        // it says nothing about any agent, and it never moves. It keeps to the inside of its cell, clear of the paths.
        void LayOutlands(List<Vector2Int> cells)
        {
            if (cells.Count == outlandsFor || cells.Count == 0) return;
            outlandsFor = cells.Count;
            var taken = new HashSet<Vector2Int>(cells);
            int x0 = int.MaxValue, x1 = int.MinValue, y0 = int.MaxValue, y1 = int.MinValue;
            foreach (var c in cells)
            {
                x0 = Mathf.Min(x0, c.x); x1 = Mathf.Max(x1, c.x); y0 = Mathf.Min(y0, c.y); y1 = Mathf.Max(y1, c.y);
            }
            for (int x = x0 - OutlandRing; x <= x1 + OutlandRing; x++)
                for (int y = y0 - OutlandRing; y <= y1 + OutlandRing; y++)
                {
                    var cell = new Vector2Int(x, y);
                    if (!taken.Contains(cell))
                    {
                        if (!outlands.ContainsKey(cell)) outlands[cell] = Scenery(cell);
                    }
                    else if (outlands.TryGetValue(cell, out var old))
                    {
                        // A new repo was given this cell: its plot has already been painted over the ground, so only the stones go.
                        Kill(old);
                        outlands.Remove(cell);
                    }
                }
        }

        GameObject Scenery(Vector2Int cell)
        {
            var holder = new GameObject("Outlands " + cell.x + " " + cell.y).transform;
            holder.SetParent(root, false);
            var o = Iso.PlotOrigin(cell);
            var rng = new System.Random(cell.x * 73856093 ^ cell.y * 19349663);   // the same land every time the app opens

            var water = new HashSet<Vector2Int>();
            if (rng.Next(3) == 0)
            {
                int cx = rng.Next(2, 10), cy = rng.Next(2, 10);
                float rx = rng.Next(2, 5), ry = rng.Next(2, 4);
                for (int col = 0; col < Iso.PlotTiles; col++)
                    for (int row = 0; row < Iso.PlotTiles; row++)
                    {
                        float dx = (col - cx) / rx, dy = (row - cy) / ry;
                        if (dx * dx + dy * dy > 1f) continue;
                        water.Add(new Vector2Int(col, row));
                        ground.SetTile(At(o.x + col, o.y + row), waterTile);
                    }
            }

            for (int i = 0; i < 6; i++)
            {
                int kind = rng.Next(10);
                // A spire hides what stands behind it, so it keeps to the middle of its cell, clear of any plot next door.
                int col = kind < 9 ? rng.Next(0, 11) : rng.Next(3, 8), row = kind < 9 ? rng.Next(0, 11) : rng.Next(3, 8);
                bool wet = false;
                for (int c = col; c <= col + 1; c++)
                    for (int r = row; r <= row + 1; r++)
                        wet |= water.Contains(new Vector2Int(c, r));
                if (wet) continue;
                var at = (Iso.TileCentre(o.x + col, o.y + row) + Iso.TileCentre(o.x + col + 1, o.y + row + 1)) / 2;
                NewSprite("stone", outlandSprites[kind < 5 ? 0 : kind < 9 ? 1 : 2], holder).transform.localPosition = at;
            }
            return holder.gameObject;
        }

        // The path round a plot: a roadway two tiles across with one line of light down its middle, which is what tells a path from
        // a city's own streets with their two lit kerbs. Where paths meet at a corner the crossing is open road.
        Tile Path(int col, int row)
        {
            bool alongColumns = row < 0 || row >= Iso.PlotTiles, alongRows = col < 0 || col >= Iso.PlotTiles;
            if (alongColumns && alongRows) return roadTile;
            // The line is the strip on the edge the two tiles share: the nearer tile's far edge one way, the farther tile's the other.
            if (alongColumns) return (row + Iso.PathTiles) % Iso.Pitch == 1 ? roadKerbTile : roadTile;
            return (col + Iso.PathTiles) % Iso.Pitch == 0 ? roadAlleyTile : roadTile;
        }

        // Light follows the buildings, not the tiles: a strip along each kerb of the two streets, which run past the buildings' fronts,
        // and a strip down each alley between neighbouring buildings. Everything else is plain ground.
        Tile Ground(int col, int row)
        {
            switch (row % 6)
            {
                case 0:
                case 1: return col % 2 == 0 ? alleyTile : plotTile;   // a building row: footprints start on odd columns
                case 2: return roadKerbTile;                           // the kerb at the buildings' feet
                case 3: return roadTile;
                case 4: return kerbTile;                               // the kerb on the far side of the street
                default: return plotTile;
            }
        }

        static SpriteRenderer NewSprite(string name, Sprite sprite, Transform parent)
        {
            var sr = new GameObject(name).AddComponent<SpriteRenderer>();
            sr.transform.SetParent(parent, false);
            sr.sprite = sprite;
            sr.spriteSortPoint = SpriteSortPoint.Pivot;
            return sr;
        }

        void Place(WorldRider data, Vector2 spot)
        {
            if (!riders.TryGetValue(data.key, out var r))
            {
                riders[data.key] = r = new Rider { from = spot, to = spot };
                r.body = NewSprite(data.name, facing[1], root);
                r.at = r.body.transform;
                r.at.localPosition = spot;   // first sight: it is simply there
                r.marker = NewSprite("marker", markerSprite, r.at);
                r.marker.sortingOrder = 10;  // the one attention signal is never hidden behind a building
                r.marker.transform.localPosition = new Vector3(0, 44, 0);
            }
            else if (spot != r.to)
            {
                r.from = r.at.localPosition;
                r.to = spot;
                r.started = Time.unscaledTime;
            }
            r.data = data;
            r.body.color = data.building == Building.Depot ? Dimmed : (Color)Tints[Mathf.Max(0, accounts.IndexOf(data.account)) % Tints.Length];
            r.marker.enabled = data.needsOwner && !Far;

            int want = Mathf.Min(data.subagents, MaxSmall);
            while (r.small.Count > want)
            {
                Kill(r.small[r.small.Count - 1].gameObject);
                r.small.RemoveAt(r.small.Count - 1);
            }
            while (r.small.Count < want)
            {
                var s = NewSprite("subagent", smallSprite, r.at);
                s.transform.localPosition = new Vector3(14 * (r.small.Count + 1), -7 * (r.small.Count + 1), 0);
                r.small.Add(s);
            }
            foreach (var s in r.small) s.color = r.body.color;
        }

        static void Kill(GameObject go)
        {
            // Destroy only takes effect at the end of the frame, and the map may be drawn into its picture before then. Switched
            // off, the object is out of that drawing at once; otherwise it would stay in the picture until something else changed.
            go.SetActive(false);
            if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
        }

        void Update()
        {
            if (root == null || !root.gameObject.activeSelf) return;
            float now = Time.unscaledTime;
            bool up = (int)(now / BounceSeconds) % 2 == 0;
            if (up != lastUp)
            {
                lastUp = up;
                if (markersShown > 0) stale = true;   // a marker on screen has just jumped
            }
            foreach (var r in riders.Values)
            {
                float t = Mathf.Clamp01((now - r.started) / RideSeconds);
                var p = Vector2.Lerp(r.from, r.to, t);
                var at = new Vector3(Mathf.Round(p.x), Mathf.Round(p.y), 0);
                var d = r.to - r.from;
                // Parked, a bike faces its building; riding, the way it travels.
                var sprite = facing[t >= 1f ? 1 : d.x >= 0 ? (d.y < 0 ? 0 : 1) : (d.y >= 0 ? 2 : 3)];
                if (r.at.localPosition != at || r.body.sprite != sprite)
                {
                    r.at.localPosition = at;
                    r.body.sprite = sprite;
                    stale = true;
                }
                r.marker.transform.localPosition = new Vector3(0, up ? 46 : 44, 0);
                if (t < 1f) fastUntil = now + .2f;
            }
            // Ten frames a second is plenty for a lens that sits open all day; a ride or a drag gets sixty while it lasts.
            Application.targetFrameRate = now < fastUntil ? 60 : 10;
        }

        void PlaceCamera()
        {
            float zoom = Iso.Zoom[level];
            if (bounds.width > 0)
            {
                centre.x = Mathf.Clamp(centre.x, bounds.xMin, bounds.xMax);
                centre.y = Mathf.Clamp(centre.y, bounds.yMin, bounds.yMax);
            }
            if (picture == null || picture.width != pixelW || picture.height != pixelH)
            {
                if (picture != null) picture.Release();
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

        // Mouse and keys, then the names and counts, drawn as text at the screen's own sharpness. Call inside OnGUI after Show.
        public void Gui(GUIStyle label, GUIStyle loud)
        {
            if (root == null) return;
            var e = Event.current;
            Handle(e);
            if (e.type != EventType.Repaint) return;

            if (picture != null) GUI.DrawTexture(new Rect(view.x, view.y, pixelW / pointScale, pixelH / pointScale), picture, ScaleMode.StretchToFill, false);
            GUI.BeginGroup(view);
            if (model.cities.Count == 0) GUI.Label(new Rect(8, 8, view.width - 16, 20), "No live agent sessions.", label);
            bool up = (int)(Time.unscaledTime / BounceSeconds) % 2 == 0;
            foreach (var city in model.cities)
            {
                if (Far)
                {
                    // Too far to read riders: the city's name, how many of its riders need the owner and how many are done.
                    // Only a city that needs the owner is loud and gets the marker.
                    var at = ToView(Iso.Bounds(new[] { city.cell }).center);
                    Text(at, city.name, label);
                    var summary = World.Summary(city);
                    if (summary != "") Text(at + new Vector2(0, 16), summary, city.needOwner > 0 ? loud : label);
                    if (city.needOwner == 0) continue;
                    var m = new Rect(at.x - 6, at.y - 28 - (up ? 2 : 0), 12, 16);
                    GUI.DrawTextureWithTexCoords(m, markerSprite.texture, new Rect(0, 0, 1, 1));
                    continue;
                }
                // The city's name goes at the open end of the back street, on the strip no building or rider uses. A building's name floats over its
                // roof: riders park in front of buildings, so that is the one place it never covers one.
                var o = Iso.PlotOrigin(city.cell);
                Text(ToView((Iso.TileCentre(o.x, o.y + 2) + Iso.TileCentre(o.x, o.y + 3)) / 2) - new Vector2(0, 9), city.name, loud);
                foreach (Building b in Enum.GetValues(typeof(Building)))
                    Text(ToView(Iso.BuildingSpot(city.cell, b) + new Vector2(0, roof[(int)b] + 3)) - new Vector2(0, 18), BuildingNames[(int)b], label);
            }
            // Riders are named last, over everything: where two plots meet, a rider's name matters more than a roof's.
            if (!Far)
                foreach (var city in model.cities)
                    foreach (var r in city.riders)
                        if (riders.TryGetValue(r.key, out var rider))
                            Text(ToView((Vector2)rider.at.localPosition - new Vector2(0, 10)), r.subagents > 0 ? r.name + " +" + r.subagents : r.name, r.key == Selected ? loud : label);
            GUI.EndGroup();
        }

        // One line of text centred on a point, on a dark plate so it reads over the art. Skipped when off the view.
        void Text(Vector2 at, string text, GUIStyle style)
        {
            if (at.x < -80 || at.y < -20 || at.x > view.width + 80 || at.y > view.height + 20) return;
            var content = new GUIContent(text);
            // Text is drawn at the screen's scale, where it comes out a little wider than it measures; without slack the end is cut off.
            var size = style.CalcSize(content) + new Vector2(10, 0);
            var rect = new Rect(Mathf.Round(at.x - size.x / 2), Mathf.Round(at.y), size.x, size.y);
            var old = GUI.color;
            GUI.color = new Color(.02f, .03f, .06f, .8f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = old;
            GUI.Label(new Rect(rect.x + 5, rect.y, rect.width - 5, rect.height), content, style);
        }

        void Handle(Event e)
        {
            bool inside = view.Contains(e.mousePosition);
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
                        if (!moved) Selected = Pick(ToWorld(e.mousePosition - view.position));
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

        // The rider under a point of the map, or "".
        string Pick(Vector2 world)
        {
            string best = "";
            float bestD = 28f * 28f;   // about a bike's length
            foreach (var kv in riders)
            {
                float d = ((Vector2)kv.Value.at.localPosition + new Vector2(0, 14) - world).sqrMagnitude;
                if (d < bestD) { bestD = d; best = kv.Key; }
            }
            return best;
        }
    }
}
