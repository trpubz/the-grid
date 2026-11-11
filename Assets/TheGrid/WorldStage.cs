using System;
using System.Collections.Generic;
using TheGrid.Core;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace TheGrid
{
    // The scene's objects, built from the WorldModel: ground, lit lines, structures, bots, and the land beyond. It has no
    // camera, no input and no text; WorldView has those. What the art should look like is in docs/art-direction.md.
    // A bot moves only when its agent's place changes; the only other motion is the bounce of a badge that needs the owner.
    public sealed class WorldStage
    {
        public const float BounceSeconds = .4f;
        const float WalkPixelsPerSecond = 4f * 36f;   // four tiles a second
        const float StepSeconds = .2f;
        const int OutlandRing = 3;   // plot-sized cells of city kept beyond the sectors, enough to reach the edge of the overview
        const int MaxSmall = 3;      // subagents drawn beside a bot; its name carries the full count

        // In the order of Structure.
        static readonly string[] StructureArt = { "b-server-tower", "b-review-hall", "b-relay", "b-works", "b-assembly-bay", "fountain", "b-test-workshop" };
        public static readonly string[] StructureNames = { "Server tower", "Review hall", "Relay", "Works", "Assembly bay", "", "Test workshop" };
        static readonly string[] BadgeArt = { "", "badge-question", "badge-approve", "badge-fault", "badge-done" };   // in the order of Badge
        static readonly string[] StanceArt = { "stand", "type", "inspect", "off" };                                    // in the order of Stance
        // A bot's light is drawn in three marker colours: the light, its highlight, its dim partner. They become one of
        // these colourways, one an account: blue, white, orange. All from the art palette, and none of them amber: amber
        // is for badges.
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
            public SpriteRenderer body, badge;
            public readonly List<SpriteRenderer> small = new List<SpriteRenderer>();
            public Vector2 from, to;
            public float started = -100f, seconds = 1f;
            public WorldBot data;
            public string shown = "";
        }

        readonly Transform root;
        readonly Tilemap ground, lines;
        readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
        readonly Dictionary<string, Tile> tiles = new Dictionary<string, Tile>();
        readonly Dictionary<string, Bot> bots = new Dictionary<string, Bot>();
        readonly HashSet<int> painted = new HashSet<int>();
        readonly Dictionary<Vector2Int, GameObject> outlands = new Dictionary<Vector2Int, GameObject>();
        readonly List<string> accounts = new List<string>();
        readonly float[] tops = new float[StructureArt.Length];
        int outlandsFor = -1, bouncing;
        bool lastUp;

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
            for (int i = 0; i < tops.Length; i++) tops[i] = Top(Art(StructureArt[i]));
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

        // A sprite that is missing is said once, by name, and the world is drawn without it.
        Sprite Art(string name)
        {
            if (sprites.TryGetValue(name, out var s)) return s;
            s = Resources.Load<Sprite>("Art/" + name);
            if (s == null) Debug.LogError("[world] missing sprite Art/" + name + "; run tools/art/make_sprites.py");
            return sprites[name] = s;
        }

        Tile T(string name)
        {
            if (tiles.TryGetValue(name, out var t)) return t;
            t = ScriptableObject.CreateInstance<Tile>();
            t.sprite = Art(name);
            return tiles[name] = t;
        }

        // A bot's picture with its light turned to the account's colourway. The suit stays as drawn: only the light is the account's.
        Sprite Lit(string name, int account)
        {
            string key = name + "#" + account;
            if (sprites.TryGetValue(key, out var s)) return s;
            var plain = Art(name);
            if (plain == null) return sprites[key] = null;
            var px = plain.texture.GetPixels32();
            var way = Colourways[account % Colourways.Length];
            for (int i = 0; i < px.Length; i++)
                for (int k = 0; k < Markers.Length; k++)
                    if (px[i].Equals(Markers[k])) { px[i] = way[k]; break; }
            var copy = new Texture2D(plain.texture.width, plain.texture.height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            copy.SetPixels32(px);
            copy.Apply(false, true);
            var pivot = new Vector2(plain.pivot.x / plain.rect.width, plain.pivot.y / plain.rect.height);
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

        // Lays out whatever changed in the model. far: the view is too far out for badges to be made out, so they are left
        // off and the window draws one for each sector instead. Returns true: the picture has to be drawn again.
        public bool Sync(WorldModel model, bool far)
        {
            var cells = new List<Vector2Int>();
            foreach (var sector in model.sectors)
            {
                cells.Add(sector.cell);
                if (painted.Add(sector.plot)) Paint(sector);   // a new sector is drawn once; the others are not touched
            }
            LayOutlands(cells);

            accounts.Clear();
            foreach (var sector in model.sectors)
                foreach (var b in sector.bots)
                    if (!accounts.Contains(b.account)) accounts.Add(b.account);
            accounts.Sort(StringComparer.Ordinal);

            var seen = new HashSet<string>();
            foreach (var sector in model.sectors)
                foreach (var b in sector.bots)
                {
                    seen.Add(b.key);
                    PutBot(b, Iso.BotSpot(sector.cell, b.place, b.slot), far);
                }
            var gone = new List<string>();
            foreach (var kv in bots) if (!seen.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (var key in gone)
            {
                Kill(bots[key].at.gameObject);
                bots.Remove(key);
            }
            bouncing = 0;
            foreach (var b in bots.Values)
                if (b.badge.enabled && b.data.needsOwner) bouncing++;
            return true;
        }

        // Walks and the badge bounce. Returns true when the picture has to be drawn again.
        public bool Tick(float now)
        {
            bool stale = false;
            Walking = false;
            bool up = (int)(now / BounceSeconds) % 2 == 0;
            if (up != lastUp)
            {
                lastUp = up;
                if (bouncing > 0) stale = true;   // a badge on screen has just jumped
            }
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
                    pose = (int)((now - b.started) / StepSeconds) % 2 == 0 ? "walk-a" : "walk-b";
                    facing = d.x >= 0 ? (d.y < 0 ? "se" : "ne") : (d.y >= 0 ? "nw" : "sw");
                }
                else
                {
                    pose = StanceArt[(int)b.data.stance];
                    // At a building a bot faces it; in the open it faces us.
                    facing = b.data.place <= Place.Workshop ? "ne" : "se";
                }
                int account = Mathf.Max(0, accounts.IndexOf(b.data.account));
                string shown = "bot-" + pose + "-" + facing + "#" + account;
                if (shown != b.shown || b.at.localPosition != at)
                {
                    b.at.localPosition = at;
                    if (shown != b.shown) b.body.sprite = Lit("bot-" + pose + "-" + facing, account);
                    b.shown = shown;
                    stale = true;
                }
                b.badge.transform.localPosition = new Vector3(0, b.data.needsOwner && up ? 46 : 44, 0);
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

        void Paint(WorldSector sector)
        {
            var o = Iso.PlotOrigin(sector.cell);
            int last = Iso.PlotTiles + Iso.LaneTiles - 1;
            // The sector's paving, and the lane round it. A neighbour paints the stretch between the two again, the same way.
            for (int col = -Iso.LaneTiles; col <= last; col++)
                for (int row = -Iso.LaneTiles; row <= last; row++)
                    ground.SetTile(At(o.x + col, o.y + row), T(Iso.OnLane(sector.cell, o.x + col, o.y + row) ? Lane(col, row) : Paving(col, row)));

            // A line of light down each edge of the lane. line-a runs along a tile's far edge one way, line-b the other; the
            // lane's outer edge is the far edge of the tile beyond it.
            for (int i = -Iso.LaneTiles; i <= last; i++)
            {
                bool inner = i >= 0 && i < Iso.PlotTiles;
                lines.SetTile(At(o.x + i, o.y - Iso.LaneTiles), T("line-a"));
                lines.SetTile(At(o.x + i, o.y + last + 1), T("line-a"));
                lines.SetTile(At(o.x - Iso.LaneTiles - 1, o.y + i), T("line-b"));
                lines.SetTile(At(o.x + last, o.y + i), T("line-b"));
                if (!inner) continue;
                lines.SetTile(At(o.x + i, o.y), T("line-a"));
                lines.SetTile(At(o.x + i, o.y + Iso.PlotTiles), T("line-a"));
                lines.SetTile(At(o.x - 1, o.y + i), T("line-b"));
                lines.SetTile(At(o.x + Iso.PlotTiles - 1, o.y + i), T("line-b"));
            }

            foreach (Structure s in Enum.GetValues(typeof(Structure)))
                NewSprite(StructureArt[(int)s], Art(StructureArt[(int)s]), root).transform.localPosition = Iso.StructureSpot(sector.cell, s);
        }

        // Paving is plain, with a joint every third tile each way.
        static string Paving(int col, int row) => row % 3 == 0 ? "tile-paving-joint-a" : col % 3 == 2 ? "tile-paving-joint-b" : "tile-paving";

        // The lane: two tiles across, chevrons on its inner side every fourth tile, a crossing opposite the forecourt.
        static string Lane(int col, int row)
        {
            bool alongColumns = row < 0 || row >= Iso.PlotTiles, alongRows = col < 0 || col >= Iso.PlotTiles;
            if (alongColumns && alongRows) return "tile-lane";
            if (alongColumns)
            {
                if (row >= Iso.PlotTiles && (col == 5 || col == 6)) return "tile-crossing-a";
                return (row == -1 || row == Iso.PlotTiles) && col % 4 == 1 ? "tile-lane-chevron-a" : "tile-lane";
            }
            return (col == -1 || col == Iso.PlotTiles) && row % 4 == 1 ? "tile-lane-chevron-b" : "tile-lane";
        }

        // Beyond the sectors the city goes on: dark blocks on dark ground, out to the edge of the view. It is scenery: it says
        // nothing about any agent, it never moves, and it is the same every time the app opens.
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
                        if (!outlands.ContainsKey(cell)) outlands[cell] = Blocks(cell);
                    }
                    else if (outlands.TryGetValue(cell, out var old))
                    {
                        // A new repo was given this cell: its sector has already been painted over the ground, so only the blocks go.
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
            for (int col = 0; col < Iso.PlotTiles; col++)
                for (int row = 0; row < Iso.PlotTiles; row++)
                    ground.SetTile(At(o.x + col, o.y + row), T("tile-void"));
            var rng = new System.Random(cell.x * 73856093 ^ cell.y * 19349663);
            for (int i = 0; i < 7; i++)
            {
                int kind = rng.Next(10);
                // The tallest block hides what stands behind it, so it keeps to the middle of its cell, clear of any sector next door.
                int col = kind < 9 ? rng.Next(0, 11) : rng.Next(3, 8), row = kind < 9 ? rng.Next(0, 11) : rng.Next(3, 8);
                var at = (Iso.TileCentre(o.x + col, o.y + row) + Iso.TileCentre(o.x + col + 1, o.y + row + 1)) / 2;
                NewSprite("block", Art(kind < 5 ? "block-a" : kind < 9 ? "block-b" : "block-c"), holder).transform.localPosition = at;
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

        void PutBot(WorldBot data, Vector2 spot, bool far)
        {
            if (!bots.TryGetValue(data.key, out var b))
            {
                bots[data.key] = b = new Bot { from = spot, to = spot };
                b.body = NewSprite(data.name, null, root);
                b.at = b.body.transform;
                b.at.localPosition = new Vector3(spot.x, spot.y, -1);   // first sight: it is simply there
                b.badge = NewSprite("badge", null, b.at);
                b.badge.sortingOrder = 10;   // what a bot needs from the owner is never hidden behind a building
                b.badge.transform.localPosition = new Vector3(0, 44, 0);
            }
            else if (spot != b.to)
            {
                b.from = b.at.localPosition;
                b.to = spot;
                b.started = Time.unscaledTime;
                b.seconds = Mathf.Max(.3f, (b.to - b.from).magnitude / WalkPixelsPerSecond);
            }
            b.data = data;
            b.badge.sprite = data.badge == Badge.None ? null : Art(BadgeArt[(int)data.badge]);
            b.badge.enabled = data.badge != Badge.None && !far;

            int want = Mathf.Min(data.subagents, MaxSmall);
            while (b.small.Count > want)
            {
                Kill(b.small[b.small.Count - 1].gameObject);
                b.small.RemoveAt(b.small.Count - 1);
            }
            while (b.small.Count < want)
            {
                var s = NewSprite("subagent", Lit("bot-small", Mathf.Max(0, accounts.IndexOf(data.account))), b.at);
                s.transform.localPosition = new Vector3(11 * (b.small.Count + 1), -5 * (b.small.Count + 1), 0);
                b.small.Add(s);
            }
        }

        static void Kill(GameObject go)
        {
            // Destroy only takes effect at the end of the frame, and the map may be drawn into its picture before then. Switched
            // off, the object is out of that drawing at once; otherwise it would stay in the picture until something else changed.
            go.SetActive(false);
            if (Application.isPlaying) UnityEngine.Object.Destroy(go); else UnityEngine.Object.DestroyImmediate(go);
        }
    }
}
