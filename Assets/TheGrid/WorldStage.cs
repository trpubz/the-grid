using System;
using System.Collections.Generic;
using TheGrid.Core;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace TheGrid
{
    // The scene's objects, built from the WorldModel: ground, lit lines, structures, bots, and the land beyond. It has no
    // camera, no input and no text; WorldView has those. What the art should look like is in docs/art-direction.md.
    // A bot moves only when its agent's place changes. Badges are not here: the window draws them over the names.
    public sealed class WorldStage
    {
        static readonly float WalkPixelsPerSecond = 4f * Iso.TileStep;   // four tiles a second
        const float StepSeconds = .2f;
        const int OutlandRing = 3;   // plot-sized cells of city kept beyond the sectors of a small map; more for a wide one
        const int MaxSmall = 3;      // subagents drawn beside a bot; its name carries the full count

        // In the order of Structure.
        public static readonly string[] StructureNames = { "Server tower", "Review hall", "Relay", "Works", "PR depot", "", "Test workshop" };
        // A bot's light is drawn in three marker colours: the light, its highlight, its dim partner. They become one of
        // these colourways: blue, white, orange. Which one is the account's number, kept in the world's file, so an account
        // keeps its colour. All from the art palette, and none of them amber: amber is for badges.
        // shortcut: three colourways, so a fourth account looks like the first. Add colourways if people run that many.
        static readonly Color32[] Markers = { Hex(0xffffff), Hex(0xc9fbff), Hex(0x1a8f9c) };
        static readonly Color32[][] Colourways =
        {
            new[] { Hex(0x35f2ff), Hex(0xc9fbff), Hex(0x1a8f9c) },
            new[] { Hex(0xe4ecff), Hex(0xffffff), Hex(0x8791c4) },
            new[] { Hex(0xff7a1e), Hex(0xffc27a), Hex(0xa8501a) },
        };

        static Color32 Hex(int rgb) => new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);

        sealed class Bot
        {
            public Transform at;
            public SpriteRenderer body;
            public readonly List<SpriteRenderer> small = new List<SpriteRenderer>();
            public Vector2 from, to;
            public float started = -100f, seconds = 1f;
            public WorldBot data;
            public string shown = "";
        }

        readonly Transform root;
        Transform built;   // the buildings of every sector: all of them go when the map is laid out again
        readonly Tilemap ground, lines, roadLinesA, roadLinesB;
        static readonly Dictionary<string, Sprite> plain = new Dictionary<string, Sprite>();   // pictures as drawn, by name
        readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();          // pictures in an account's colours
        readonly Dictionary<string, Tile> tiles = new Dictionary<string, Tile>();
        readonly Dictionary<string, Bot> bots = new Dictionary<string, Bot>();
        // What has been laid: each sector by its plot, with where it stood and how it was arranged and lit; the roads,
        // links among them; and the squares of the map whose open ground is down.
        readonly Dictionary<int, (Vector2Int origin, int arrangement, int look)> painted = new Dictionary<int, (Vector2Int, int, int)>();
        readonly HashSet<(Vector2Int, Vector2Int)> roads = new HashSet<(Vector2Int, Vector2Int)>();
        readonly HashSet<Vector2Int> opened = new HashSet<Vector2Int>();
        readonly Dictionary<Vector2Int, GameObject> outlands = new Dictionary<Vector2Int, GameObject>();
        readonly Dictionary<string, float> signals = new Dictionary<string, float>();   // bot key: signalling until this time
        readonly float[] tops = new float[ArtNames.Structures.Length];
        RectInt outlandsFor;   // the tiles the sectors covered when the land beyond was last laid

        // True while any bot is walking: the window draws faster while it lasts.
        public bool Walking { get; private set; }

        public WorldStage(Transform parent)
        {
            root = new GameObject("Stage").transform;
            root.SetParent(parent, false);
            var grid = new GameObject("Ground").AddComponent<Grid>();
            grid.transform.SetParent(root, false);
            grid.cellLayout = GridLayout.CellLayout.Isometric;
            grid.cellSize = new Vector3(Iso.TileW, Iso.TileH, 1);
            grid.transform.localPosition = new Vector3(0, -Iso.TileH / 2, 0);   // so a cell's centre is Iso.TileCentre
            ground = Layer(grid, "Tiles", -10);
            lines = Layer(grid, "Light", -9);   // lines of light lie over the ground, never in it
            // A road's lines have layers of their own, one for each way a line can run: where a road meets a lane, or turns,
            // one tile needs a line of the lane's and one or two of the road's.
            roadLinesA = Layer(grid, "Road light a", -9);
            roadLinesB = Layer(grid, "Road light b", -9);
            for (int i = 0; i < tops.Length; i++) tops[i] = Top(Art(ArtNames.Structures[i]));
            built = Holder("Buildings");
        }

        Transform Holder(string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(root, false);
            return t;
        }

        static Tilemap Layer(Grid grid, string name, int order)
        {
            var map = new GameObject(name).AddComponent<Tilemap>();
            map.transform.SetParent(grid.transform, false);
            map.gameObject.AddComponent<TilemapRenderer>().sortingOrder = order;
            return map;
        }

        public void SetActive(bool on) => root.gameObject.SetActive(on);

        // How far above its ground point a structure's highest pixel is: where its name goes.
        public float Top(Structure s) => tops[(int)s];

        // A bot holds a hand to its helmet until then: a message is on its way to it, or has just left it.
        public void Signal(string botKey, float until)
        {
            if (!signals.TryGetValue(botKey, out var already) || until > already) signals[botKey] = until;
        }

        // A picture in the colourway of a bot's account: the courier's bike is its sender's. Plain if the bot has gone.
        public Sprite BotArt(string botKey, string name) =>
            bots.TryGetValue(botKey, out var b) ? Lit(name, b.data.colour) : Art(name);

        // Every picture the world, its panels and its traffic draw comes through here. One that is missing is said once,
        // by name, and the world is drawn without it.
        public static Sprite Art(string name)
        {
            if (plain.TryGetValue(name, out var s)) return s;
            s = Resources.Load<Sprite>("Art/" + name);
            if (s == null) Debug.LogError("[world] missing sprite Art/" + name + "; run tools/art/make_sprites.py");
            return plain[name] = s;
        }

        // A ground tile or a line of light, in one of the lights: as drawn for the first.
        Tile T(string name, int light = 0)
        {
            string key = light <= 0 ? name : name + "@" + light;
            if (tiles.TryGetValue(key, out var t)) return t;
            t = ScriptableObject.CreateInstance<Tile>();
            t.sprite = InLight(name, light);
            return tiles[key] = t;
        }

        // A bot's picture with its light turned to the account's colourway. The suit stays as drawn: only the light is the account's.
        Sprite Lit(string name, int account) => Recoloured(name + "#" + account, name, Markers, Colourways[account % Colourways.Length]);

        // A picture in one of the lights: a building's lines, its windows and the glow at its foot; a line on the ground
        // and its glow; a lane's chevrons. As drawn for the first.
        Sprite InLight(string name, int light)
        {
            if (light <= 0) return Art(name);
            var from = new Color32[Lights.Parts];
            var to = new Color32[Lights.Parts];
            for (int i = 0; i < Lights.Parts; i++)
            {
                from[i] = Hex(Lights.Drawn[i]);
                to[i] = Hex(Lights.Family(light)[i]);
            }
            return Recoloured(name + "@" + light, name, from, to);
        }

        // A copy of a picture with some colours swapped for others. Made once and kept.
        Sprite Recoloured(string key, string name, Color32[] from, Color32[] to)
        {
            if (sprites.TryGetValue(key, out var s)) return s;
            var drawn = Art(name);
            if (drawn == null) return sprites[key] = null;
            var px = drawn.texture.GetPixels32();
            for (int i = 0; i < px.Length; i++)
                for (int k = 0; k < from.Length; k++)
                    if (px[i].Equals(from[k])) { px[i] = to[k]; break; }
            var copy = new Texture2D(drawn.texture.width, drawn.texture.height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            copy.SetPixels32(px);
            copy.Apply(false, true);
            var pivot = new Vector2(drawn.pivot.x / drawn.rect.width, drawn.pivot.y / drawn.rect.height);
            return sprites[key] = Sprite.Create(copy, new Rect(0, 0, copy.width, copy.height), pivot, 1f, 0, SpriteMeshType.FullRect);
        }

        static float Top(Sprite s)
        {
            if (s == null) return 0;
            var px = s.texture.GetPixels32();
            int w = s.texture.width;
            for (int i = px.Length - 1; i >= 0; i--)
                if (px[i].a > 0) return i / w + 1 - s.pivot.y;
            return 0;
        }

        // Lays out whatever changed in the model. Returns true: the picture has to be drawn again.
        public bool Sync(WorldModel model)
        {
            // A sector that stands somewhere else than it did, or is lit otherwise, or a road that is no longer one of the
            // map's roads: the owner moved something or picked another light, a repo changed workspace, or a new sector
            // made another road the shorter. That is rare, and then the whole map is laid again rather than mended.
            var all = Roads.All(model);
            bool again = false;
            foreach (var sector in model.sectors)
                again |= painted.TryGetValue(sector.plot, out var was) && was != (sector.origin, sector.layout.Arrangement, sector.layout.Look);
            foreach (var road in roads) again |= !all.Contains(road);
            if (again) Clear();

            // The ground first, then what stands on it: open ground laid later would cover a sector.
            var origins = new List<Vector2Int>();
            var at = new Dictionary<Vector2Int, WorldSector>();
            foreach (var sector in model.sectors)
            {
                origins.Add(sector.origin);
                at[sector.origin] = sector;
            }
            LayOutlands(origins);
            foreach (var sector in model.sectors)
            {
                if (painted.ContainsKey(sector.plot)) continue;   // a new sector is drawn once; the others are not touched
                painted[sector.plot] = (sector.origin, sector.layout.Arrangement, sector.layout.Look);
                Paint(sector);
            }
            // A road within a region, a link above all, is in the region's light. A road between two regions is in no
            // region's: the light the ground is drawn in.
            foreach (var road in all)
                if (roads.Add(road)) Road(road.from, road.to, at[road.from].region == at[road.to].region ? Lights.Primary(at[road.from].layout.Look) : 0);

            var seen = new HashSet<string>();
            foreach (var sector in model.sectors)
                foreach (var b in sector.bots)
                {
                    seen.Add(b.key);
                    PutBot(b, Iso.BotSpot(sector.origin, sector.layout, b.place, b.slot));
                }
            var gone = new List<string>();
            foreach (var kv in bots) if (!seen.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (var key in gone)
            {
                Kill(bots[key].at.gameObject);
                bots.Remove(key);
            }
            return true;
        }

        // Takes everything off the map, bots too: they are put back where they now stand, not walked there.
        void Clear()
        {
            ground.ClearAllTiles();
            lines.ClearAllTiles();
            roadLinesA.ClearAllTiles();
            roadLinesB.ClearAllTiles();
            Kill(built.gameObject);
            built = Holder("Buildings");
            foreach (var blocks in outlands.Values) Kill(blocks);
            foreach (var b in bots.Values) Kill(b.at.gameObject);
            outlands.Clear();
            bots.Clear();
            painted.Clear();
            roads.Clear();
            opened.Clear();
            outlandsFor = default;
        }

        // Walks. Returns true when the picture has to be drawn again.
        public bool Tick(float now)
        {
            bool stale = false;
            Walking = false;
            foreach (var b in bots.Values)
            {
                float t = Mathf.Clamp01((now - b.started) / b.seconds);
                var p = Vector2.Lerp(b.from, b.to, t);
                // A bot stands a little nearer the camera than the ground it is on, so that standing inside the workshop it is
                // drawn over the workshop's floor and not under it.
                var at = new Vector3(Mathf.Round(p.x), Mathf.Round(p.y), -1);
                string pose, facing;
                if (t < 1f)
                {
                    Walking = true;
                    var d = b.to - b.from;
                    pose = (int)((now - b.started) / StepSeconds) % 2 == 0 ? ArtNames.WalkA : ArtNames.WalkB;
                    facing = d.x >= 0 ? (d.y < 0 ? "se" : "ne") : (d.y >= 0 ? "nw" : "sw");
                }
                else if (signals.TryGetValue(b.data.key, out var until) && now < until && b.data.stance != Stance.Off)
                {
                    pose = ArtNames.Signal;
                    facing = "se";   // toward us, so the raised hand shows
                }
                else
                {
                    pose = ArtNames.Stances[(int)b.data.stance];
                    // At a building a bot faces it; in the open it faces us.
                    facing = b.data.place <= Place.Workshop ? "ne" : "se";
                }
                int account = b.data.colour;
                string shown = ArtNames.Bot(pose, facing) + "#" + account;
                if (shown != b.shown || b.at.localPosition != at)
                {
                    b.at.localPosition = at;
                    if (shown != b.shown) b.body.sprite = Lit(ArtNames.Bot(pose, facing), account);
                    b.shown = shown;
                    stale = true;
                }
            }
            return stale;
        }

        public bool TryGetBot(string key, out Vector2 at)
        {
            at = default;
            if (!bots.TryGetValue(key, out var b)) return false;
            at = b.at.localPosition;
            return true;
        }

        // The bot under a point of the map, or "".
        public string Pick(Vector2 world)
        {
            string best = "";
            float bestD = 20f * 20f;   // about a bot's height
            foreach (var kv in bots)
            {
                float d = ((Vector2)kv.Value.at.localPosition + new Vector2(0, 16) - world).sqrMagnitude;
                if (d < bestD) { bestD = d; best = kv.Key; }
            }
            return best;
        }

        // Where a tile of the map is in Unity's isometric grid, whose axes run the other way.
        static Vector3Int At(int col, int row) => new Vector3Int(-row, -col, 0);

        // A sector's plot and lane run from First to Ring. The map's open ground is laid in squares of a grid of its own,
        // a sector's pitch across, each from First to Edge of its place on that grid; sectors stand anywhere on it.
        const int First = -Iso.LaneTiles, Ring = Iso.PlotTiles + Iso.LaneTiles, Edge = Ring + Iso.GapTiles;

        void OpenGround(Vector2Int cell)
        {
            if (!opened.Add(cell)) return;   // laid once: sectors and roads have been laid over it since
            var origin = Iso.PlotOrigin(cell);
            for (int col = First; col < Edge; col++)
                for (int row = First; row < Edge; row++)
                    ground.SetTile(At(origin.x + col, origin.y + row), T(ArtNames.Void));
        }

        void Paint(WorldSector sector)
        {
            var o = sector.origin;
            int light = Lights.Primary(sector.layout.Look);   // the lane's chevrons and every line on the ground are the region's primary light
            // The sector's paving, and its own lane round it. Beyond the lane the ground stays open: a sector stands apart.
            for (int col = First; col < Ring; col++)
                for (int row = First; row < Ring; row++)
                    ground.SetTile(At(o.x + col, o.y + row), Iso.OnLane(o, o.x + col, o.y + row) ? T(Lane(col, row), light) : T(Paving(col, row)));

            // A line of light down each edge of the lane. line-a runs along a tile's far edge one way, line-b the other; the
            // lane's outer edge is the far edge of the tile beyond it.
            for (int i = First; i < Ring; i++)
            {
                bool inner = i >= 0 && i < Iso.PlotTiles;
                lines.SetTile(At(o.x + i, o.y + First), T(ArtNames.LineA, light));
                lines.SetTile(At(o.x + i, o.y + Ring), T(ArtNames.LineA, light));
                lines.SetTile(At(o.x + First - 1, o.y + i), T(ArtNames.LineB, light));
                lines.SetTile(At(o.x + Ring - 1, o.y + i), T(ArtNames.LineB, light));
                if (!inner) continue;
                lines.SetTile(At(o.x + i, o.y), T(ArtNames.LineA, light));
                lines.SetTile(At(o.x + i, o.y + Iso.PlotTiles), T(ArtNames.LineA, light));
                lines.SetTile(At(o.x - 1, o.y + i), T(ArtNames.LineB, light));
                lines.SetTile(At(o.x + Iso.PlotTiles - 1, o.y + i), T(ArtNames.LineB, light));
            }

            // Every sector has the same buildings; where each stands and how it is lit is the sector's own.
            foreach (Structure s in Enum.GetValues(typeof(Structure)))
                NewSprite(ArtNames.Structures[(int)s], InLight(ArtNames.Structures[(int)s], sector.layout.Light(s)), built).transform.localPosition = Iso.StructureSpot(sector.origin, sector.layout, s);
        }

        // The road across the open ground from one sector's lane to another's: lane tiles where Iso.RoadTiles says, and a
        // line of light along every edge of it that is not the lane it runs into. line-a lies along the far edge of a tile's
        // row, line-b along the near edge of its column.
        void Road(Vector2Int from, Vector2Int to, int light)
        {
            var tiles = new HashSet<Vector2Int>(Iso.RoadTiles(from, to));
            bool Open(int col, int row) => tiles.Contains(new Vector2Int(col, row)) || Iso.OnLane(from, col, row) || Iso.OnLane(to, col, row);
            foreach (var t in tiles)
            {
                ground.SetTile(At(t.x, t.y), T(ArtNames.Lane));
                if (!Open(t.x, t.y - 1)) roadLinesA.SetTile(At(t.x, t.y), T(ArtNames.LineA, light));
                if (!Open(t.x, t.y + 1)) roadLinesA.SetTile(At(t.x, t.y + 1), T(ArtNames.LineA, light));
                if (!Open(t.x - 1, t.y)) roadLinesB.SetTile(At(t.x - 1, t.y), T(ArtNames.LineB, light));
                if (!Open(t.x + 1, t.y)) roadLinesB.SetTile(At(t.x, t.y), T(ArtNames.LineB, light));
            }
        }

        // Paving is plain, with a joint every third tile each way.
        static string Paving(int col, int row) => row % 3 == 0 ? ArtNames.PavingJointA : col % 3 == 2 ? ArtNames.PavingJointB : ArtNames.Paving;

        // The lane: two tiles across, chevrons on its inner side every fourth tile, a crossing opposite the forecourt.
        static string Lane(int col, int row)
        {
            bool alongColumns = row < 0 || row >= Iso.PlotTiles, alongRows = col < 0 || col >= Iso.PlotTiles;
            if (alongColumns && alongRows) return ArtNames.Lane;
            if (alongColumns)
            {
                if (row >= Iso.PlotTiles && (col == Iso.Crossing || col == Iso.Crossing + 1)) return ArtNames.Crossing;
                return (row == -1 || row == Iso.PlotTiles) && col % 4 == 1 ? ArtNames.ChevronA : ArtNames.Lane;
            }
            return (col == -1 || col == Iso.PlotTiles) && row % 4 == 1 ? ArtNames.ChevronB : ArtNames.Lane;
        }

        // Open ground under the whole map and for some way round it, and beyond the sectors the city going on: dark blocks
        // on dark ground, out to the edge of the view. It is scenery: it says nothing about any agent, it never moves, and
        // it is the same every time the app opens. Blocks stand only clear of the rectangle the sectors cover: the open
        // ground among the sectors has nothing on it, and that is the gap that sets one region apart from the next.
        void LayOutlands(List<Vector2Int> origins)
        {
            if (origins.Count == 0) return;
            int x0 = int.MaxValue, x1 = int.MinValue, y0 = int.MaxValue, y1 = int.MinValue;
            foreach (var o in origins)
            {
                x0 = Mathf.Min(x0, o.x + First); x1 = Mathf.Max(x1, o.x + Ring); y0 = Mathf.Min(y0, o.y + First); y1 = Mathf.Max(y1, o.y + Ring);
            }
            var covered = new RectInt(x0, y0, x1 - x0, y1 - y0);
            if (covered.Equals(outlandsFor)) return;
            outlandsFor = covered;
            // The squares of the ground's own grid that the sectors reach into, and a ring more each way. The ring widens
            // with the map: the sectors' rectangle is a diamond on screen, and the further out the view that shows all of
            // it, the more land beyond its edges that view takes in.
            int ring = OutlandRing + (covered.width + covered.height) / (4 * Iso.Pitch);
            int cx0 = Mathf.FloorToInt((x0 - First) / (float)Iso.Pitch) - ring, cx1 = Mathf.FloorToInt((x1 - 1 - First) / (float)Iso.Pitch) + ring;
            int cy0 = Mathf.FloorToInt((y0 - First) / (float)Iso.Pitch) - ring, cy1 = Mathf.FloorToInt((y1 - 1 - First) / (float)Iso.Pitch) + ring;
            for (int x = cx0; x <= cx1; x++)
                for (int y = cy0; y <= cy1; y++)
                {
                    var cell = new Vector2Int(x, y);
                    OpenGround(cell);
                    var o = Iso.PlotOrigin(cell);
                    bool beyond = !covered.Overlaps(new RectInt(o.x + First, o.y + First, Iso.Pitch, Iso.Pitch));
                    if (beyond && !outlands.ContainsKey(cell)) outlands[cell] = Blocks(cell);
                    if (!beyond && outlands.TryGetValue(cell, out var old))
                    {
                        // The map grew over this square: its blocks go.
                        Kill(old);
                        outlands.Remove(cell);
                    }
                }
        }

        GameObject Blocks(Vector2Int cell)
        {
            var holder = new GameObject("City " + cell.x + " " + cell.y).transform;
            holder.SetParent(root, false);
            var o = Iso.PlotOrigin(cell);
            var rng = new System.Random(cell.x * 73856093 ^ cell.y * 19349663);
            for (int i = 0; i < 7; i++)
            {
                int kind = rng.Next(10);
                // The tallest block hides what stands behind it, so it keeps to the middle of its cell, clear of any sector next door.
                int col = kind < 9 ? rng.Next(0, 11) : rng.Next(3, 8), row = kind < 9 ? rng.Next(0, 11) : rng.Next(3, 8);
                var at = (Iso.TileCentre(o.x + col, o.y + row) + Iso.TileCentre(o.x + col + 1, o.y + row + 1)) / 2;
                NewSprite("block", Art(ArtNames.Blocks[kind < 5 ? 0 : kind < 9 ? 1 : 2]), holder).transform.localPosition = at;
            }
            return holder.gameObject;
        }

        static SpriteRenderer NewSprite(string name, Sprite sprite, Transform parent)
        {
            var sr = new GameObject(name).AddComponent<SpriteRenderer>();
            sr.transform.SetParent(parent, false);
            sr.sprite = sprite;
            sr.spriteSortPoint = SpriteSortPoint.Pivot;
            return sr;
        }

        void PutBot(WorldBot data, Vector2 spot)
        {
            if (!bots.TryGetValue(data.key, out var b))
            {
                bots[data.key] = b = new Bot { from = spot, to = spot };
                b.body = NewSprite(data.name, null, root);
                b.at = b.body.transform;
                b.at.localPosition = new Vector3(spot.x, spot.y, -1);   // first sight: it is simply there
            }
            else if (spot != b.to)
            {
                b.from = b.at.localPosition;
                b.to = spot;
                b.started = Time.unscaledTime;
                b.seconds = Mathf.Max(.3f, (b.to - b.from).magnitude / WalkPixelsPerSecond);
            }
            b.data = data;

            int want = Mathf.Min(data.subagents, MaxSmall);
            while (b.small.Count > want)
            {
                Kill(b.small[b.small.Count - 1].gameObject);
                b.small.RemoveAt(b.small.Count - 1);
            }
            while (b.small.Count < want)
            {
                var s = NewSprite("subagent", null, b.at);
                s.transform.localPosition = new Vector3(11 * (b.small.Count + 1), -5 * (b.small.Count + 1), 0);
                b.small.Add(s);
            }
            var small = Lit(ArtNames.SmallBot, data.colour);
            foreach (var s in b.small) s.sprite = small;
        }

        public static void Kill(GameObject go)
        {
            // Destroy only takes effect at the end of the frame, and the map may be drawn into its picture before then. Switched
            // off, the object is out of that drawing at once; otherwise it would stay in the picture until something else changed.
            go.SetActive(false);
            if (Application.isPlaying) UnityEngine.Object.Destroy(go); else UnityEngine.Object.DestroyImmediate(go);
        }
    }
}
