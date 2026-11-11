using System;
using System.Collections.Generic;
using TheGrid.Core;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace TheGrid
{
    // The world: one isometric map with a plot per repo. Draws nothing but the WorldModel it is shown.
    // A rider moves only when its agent's building or place changes; the only other motion is the bounce of a marker.
    public sealed class WorldView : MonoBehaviour
    {
        const float RideSeconds = 1.2f, BounceSeconds = .4f;
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
        Tile plotTile, kerbTile, alleyTile, roadTile, roadKerbTile;
        Sprite[] buildingSprites, facing;   // facing: se, ne, nw, sw
        Sprite smallSprite, markerSprite;
        float[] roof;             // how far above its ground point each building's highest pixel is
        readonly Dictionary<string, Rider> riders = new Dictionary<string, Rider>();
        readonly HashSet<int> painted = new HashSet<int>();
        readonly List<string> accounts = new List<string>();

        WorldModel model = new WorldModel();
        Rect view, bounds;        // view: where the map is, in points
        float pointScale = 1f;
        int pixelW, pixelH, level = 2;
        bool fitted;              // the view opens on every plot, once there is a plot to open on
        Vector2 centre, pressed;
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
            cam.pixelRect = new Rect(Mathf.RoundToInt(view.x * scale), Screen.height - Mathf.RoundToInt(view.yMax * scale), pixelW, pixelH);
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
            var rt = RenderTexture.GetTemporary(width, height, 0);
            cam.targetTexture = rt;
            cam.Render();
            var old = RenderTexture.active;
            RenderTexture.active = rt;
            var picture = new Texture2D(width, height, TextureFormat.RGB24, false);
            picture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            picture.Apply();
            RenderTexture.active = old;
            cam.targetTexture = null;
            RenderTexture.ReleaseTemporary(rt);
            return picture;
        }

        public void LookAtAll()
        {
            level = Iso.FitLevel(pixelW, pixelH, bounds);
            centre = bounds.center;
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
            root.gameObject.SetActive(true);
            model = world;
            pixelW = Mathf.Max(2, width & ~1);    // even, so the centre of the view falls between two pixels and art stays crisp
            pixelH = Mathf.Max(2, height & ~1);

            var cells = new List<Vector2Int>();
            foreach (var city in world.cities)
            {
                cells.Add(city.cell);
                if (painted.Add(city.plot)) Paint(city);   // a new plot is drawn once; the others are not touched
            }
            bounds = Iso.Bounds(cells);
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
            PlaceCamera();
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
            cam.depth = 1;   // over the window's own camera, inside its own rectangle
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
            for (int col = 0; col < Iso.PlotTiles; col++)
                for (int row = 0; row < Iso.PlotTiles; row++)
                    ground.SetTile(new Vector3Int(-(o.y + row), -(o.x + col), 0), Ground(col, row));
            foreach (Building b in Enum.GetValues(typeof(Building)))
                NewSprite(BuildingNames[(int)b], buildingSprites[(int)b], root).transform.localPosition = Iso.BuildingSpot(city.cell, b);
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
            if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
        }

        void Update()
        {
            if (root == null || !root.gameObject.activeSelf) return;
            float now = Time.unscaledTime;
            bool up = (int)(now / BounceSeconds) % 2 == 0;
            foreach (var r in riders.Values)
            {
                float t = Mathf.Clamp01((now - r.started) / RideSeconds);
                var p = Vector2.Lerp(r.from, r.to, t);
                r.at.localPosition = new Vector3(Mathf.Round(p.x), Mathf.Round(p.y), 0);
                var d = r.to - r.from;
                // Parked, a bike faces its building; riding, the way it travels.
                r.body.sprite = facing[t >= 1f ? 1 : d.x >= 0 ? (d.y < 0 ? 0 : 1) : (d.y >= 0 ? 2 : 3)];
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
            cam.orthographicSize = pixelH / (2f * zoom);
            cam.aspect = (float)pixelW / pixelH;
            // Whole screen pixels only, or the art shimmers as it moves.
            cam.transform.localPosition = new Vector3(Mathf.Round(centre.x * zoom) / zoom, Mathf.Round(centre.y * zoom) / zoom, 0);
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

            GUI.BeginGroup(view);
            if (model.cities.Count == 0) GUI.Label(new Rect(8, 8, view.width - 16, 20), "No live agent sessions.", label);
            bool up = (int)(Time.unscaledTime / BounceSeconds) % 2 == 0;
            foreach (var city in model.cities)
            {
                if (Far)
                {
                    // Too far to read riders: the city's name and how many of its riders need the owner.
                    var at = ToView(Iso.Bounds(new[] { city.cell }).center);
                    Text(at, city.name, label);
                    if (city.needOwner == 0) continue;
                    Text(at + new Vector2(0, 16), city.needOwner + (city.needOwner == 1 ? " needs you" : " need you"), loud);
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
                    pressed = e.mousePosition;
                    e.Use();
                    break;
                case EventType.MouseDrag when dragging:
                    if ((e.mousePosition - pressed).sqrMagnitude > 9) moved = true;
                    centre += new Vector2(-e.delta.x, e.delta.y) / k;
                    Moved(e);
                    break;
                case EventType.MouseUp when dragging:
                    dragging = false;
                    if (!moved) Selected = Pick(ToWorld(e.mousePosition - view.position));
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
