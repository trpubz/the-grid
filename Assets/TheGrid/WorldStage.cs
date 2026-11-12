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
        const float BeatSeconds = .7f;   // how long the lights of a building someone works in stay at each of their two levels
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

        // One building of one sector: its picture, which kind it is and which light it carries, and whether a bot is at
        // work in it. While one is, it is drawn opened and its lights pulse.
        sealed class Building
        {
            public SpriteRenderer picture;
            public Structure kind;
            public int light;
            public bool busy;
            public int held;   // how many bots that have left it for another place are still on their way out of it
        }

        sealed class Bot
        {
            public Transform at;
            public SpriteRenderer body;
            public readonly List<SpriteRenderer> small = new List<SpriteRenderer>();
            // The walk it is on, or the one point it stands at: art pixels, and with each point how much further down the
            // screen it is drawn as if it stood (a bot in a room is drawn over the room).
            public List<Vector3> path = new List<Vector3>();
            public float started = -100f, seconds = 1f;
            public Building leaving;   // the building it was in when it set off, kept opened until it has arrived
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
        readonly Dictionary<(int plot, Structure kind), Building> buildings = new Dictionary<(int, Structure), Building>();
        int beat;   // 0 or 1: which of the two levels the lights of busy buildings are at
        // The crates at each sector's depot, by its plot: what they show, and the object that holds them.
        readonly Dictionary<int, (string shown, GameObject holder)> crates = new Dictionary<int, (string, GameObject)>();
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

        // The same picture with its light a step down: the dim half of a pulse.
        Sprite Dimmed(string name, int light)
        {
            var from = new Color32[Lights.Parts];
            var to = new Color32[Lights.Parts];
            int[] dim = Lights.Dimmed(Mathf.Max(light, 0));
            for (int i = 0; i < Lights.Parts; i++)
            {
                from[i] = Hex(Lights.Drawn[i]);
                to[i] = Hex(dim[i]);
            }
            return Recoloured(name + "~" + light, name, from, to);
        }

        // A building's picture as it is to be seen now: opened if a bot works in it, or is still on its way out of it, and
        // it has an opened picture; and at the dim level if its lights are in the dim half of their pulse.
        Sprite Shown(Building b)
        {
            string name = (b.busy || b.held > 0) && ArtNames.Opened[(int)b.kind] != "" ? ArtNames.Opened[(int)b.kind] : ArtNames.Structures[(int)b.kind];
            return b.busy && beat == 1 ? Dimmed(name, b.light) : InLight(name, b.light);
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

            foreach (var sector in model.sectors) PutCrates(sector);
            // A building a bot has come to work in is opened; one the last has left is closed again.
            foreach (var sector in model.sectors)
                foreach (Structure s in Enum.GetValues(typeof(Structure)))
                {
                    if (!buildings.TryGetValue((sector.plot, s), out var b)) continue;
                    bool busy = World.Working(sector, s) > 0;
                    if (busy == b.busy) continue;
                    b.busy = busy;
                    b.picture.sprite = Shown(b);
                }

            var seen = new HashSet<string>();
            foreach (var sector in model.sectors)
                foreach (var b in sector.bots)
                {
                    seen.Add(b.key);
                    PutBot(b, sector);
                }
            var gone = new List<string>();
            foreach (var kv in bots) if (!seen.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (var key in gone)
            {
                Release(bots[key]);
                Kill(bots[key].at.gameObject);
                bots.Remove(key);
            }
            return true;
        }

        // One crate for each open pull request of the sector's repo, as far as there is room in front of its depot: those
        // for the owner nearest the door. A crate in review is lit as the depot is; one for the owner is amber everywhere.
        void PutCrates(WorldSector sector)
        {
            int count = Mathf.Min(sector.pulls.Count, Layout.Crates);
            var states = new System.Text.StringBuilder(sector.origin + " " + sector.layout.Arrangement + " " + sector.layout.Look);
            for (int i = 0; i < count; i++) states.Append(' ').Append((int)sector.pulls[i].state);
            string shown = states.ToString();
            if (crates.TryGetValue(sector.plot, out var was))
            {
                if (was.shown == shown) return;
                Kill(was.holder);
            }
            var holder = Holder("Crates " + sector.plot);
            for (int i = 0; i < count; i++)
            {
                var state = sector.pulls[i].state;
                string name = ArtNames.Crates[(int)state];
                var sprite = state == PullState.InReview ? InLight(name, sector.layout.Light(Structure.Depot)) : Art(name);
                NewSprite("crate", sprite, holder).transform.localPosition = Iso.CrateSpot(sector.origin, sector.layout, i);
            }
            crates[sector.plot] = (shown, holder.gameObject);
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
            foreach (var c in crates.Values) Kill(c.holder);
            crates.Clear();
            buildings.Clear();
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
            // The lights of every building someone works in go a step down and back, together. Nothing else in the map
            // moves for it: two pictures, swapped, and the map drawn again at each swap.
            int now2 = (int)(now / BeatSeconds) % 2;
            if (now2 != beat)
            {
                beat = now2;
                foreach (var b in buildings.Values)
                    if (b.busy)
                    {
                        b.picture.sprite = Shown(b);
                        stale = true;
                    }
            }
            foreach (var b in bots.Values)
            {
                float t = Mathf.Clamp01((now - b.started) / b.seconds);
                var p = Along(b.path, t, out var d);
                // A bot stands a little nearer the camera than the ground it is on, so that standing inside the workshop it is
                // drawn over the workshop's floor and not under it.
                var at = new Vector3(Mathf.Round(p.x), Mathf.Round(p.y), -1 - p.z);
                string pose, facing;
                if (t >= 1f)
                {
                    if (b.path.Count > 1) b.path.RemoveRange(0, b.path.Count - 1);   // it has arrived: it stands at the one point
                    if (Release(b)) stale = true;                                    // and the building it left can close
                }
                if (t < 1f)
                {
                    Walking = true;
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

        // How much further down the screen a bot is drawn as if it stood: more than nothing for a bot inside a building.
        // What lies under its feet has to come that far forward with it, or the room is drawn over it.
        public float Sink(string key) => bots.TryGetValue(key, out var b) ? -1f - b.at.localPosition.z : 0f;

        // Puts every bot that is walking so far along its walk, from 0 to 1: for a picture of a bot on its way.
        public void Hurry(float along, float now)
        {
            foreach (var b in bots.Values)
                if (b.path.Count > 1 && now < b.started + b.seconds) b.started = now - b.seconds * along;
        }

        static float Length(List<Vector3> path)
        {
            float length = 0;
            for (int i = 1; i < path.Count; i++) length += ((Vector2)path[i] - (Vector2)path[i - 1]).magnitude;
            return length;
        }

        // The point so far along a walk, from 0 to 1, and which way the bot is heading there. With `ahead`, what is left
        // of the walk from that point on.
        static Vector3 Along(List<Vector3> path, float t, out Vector2 heading, List<Vector3> ahead = null)
        {
            heading = Vector2.zero;
            float left = Length(path) * t;
            for (int i = 1; i < path.Count; i++)
            {
                heading = (Vector2)path[i] - (Vector2)path[i - 1];
                float leg = heading.magnitude;
                if (left < leg || i == path.Count - 1)
                {
                    var p = Vector3.Lerp(path[i - 1], path[i], leg > 0 ? Mathf.Clamp01(left / leg) : 1f);
                    if (ahead != null)
                    {
                        ahead.Add(p);
                        ahead.AddRange(path.GetRange(i, path.Count - i));
                    }
                    return p;
                }
                left -= leg;
            }
            ahead?.Add(path[0]);
            return path[0];
        }

        // Lets go of the building a bot was leaving. True if that changed the building's picture.
        bool Release(Bot b)
        {
            var room = b.leaving;
            if (room == null) return false;
            b.leaving = null;
            room.held--;
            room.picture.sprite = Shown(room);
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
            {
                var b = new Building { kind = s, light = sector.layout.Light(s) };
                b.picture = NewSprite(ArtNames.Structures[(int)s], Shown(b), built);
                b.picture.transform.localPosition = Iso.StructureSpot(sector.origin, sector.layout, s);
                buildings[(sector.plot, s)] = b;
            }
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

        void PutBot(WorldBot data, WorldSector sector)
        {
            var spot = Iso.BotSpot(sector.origin, sector.layout, data.place, data.slot);
            var stands = new Vector3(spot.x, spot.y, spot.y - Iso.BotDepth(sector.origin, sector.layout, data.place, data.slot));
            if (!bots.TryGetValue(data.key, out var b))
            {
                bots[data.key] = b = new Bot();
                b.path.Add(stands);
                b.body = NewSprite(data.name, null, root);
                b.at = b.body.transform;
                b.at.localPosition = new Vector3(stands.x, stands.y, -1 - stands.z);   // first sight: it is simply there
            }
            else if ((stands - b.path[b.path.Count - 1]).sqrMagnitude > .01f)
            {
                // From where it is, on along what is left of the walk it is on, and then by the doors and the stairs from
                // the place it was going to, to the new one.
                float now = Time.unscaledTime;
                var path = new List<Vector3>();
                Along(b.path, Mathf.Clamp01((now - b.started) / b.seconds), out _, path);
                var walk = Iso.Walk(sector.origin, sector.layout, b.data.place, b.data.slot, data.place, data.slot);
                if ((walk[0] - path[path.Count - 1]).sqrMagnitude < 1f) path.AddRange(walk.GetRange(1, walk.Count - 1));
                else path.Add(stands);   // it was not where this sector's arrangement has that place: straight there
                // The building it is in stays opened until it has arrived, though nobody may be working there now.
                if (b.leaving == null && sector.layout.IsInside(b.data.place, b.data.slot) && World.StructureOf(b.data.place) is Structure kind
                    && buildings.TryGetValue((sector.plot, kind), out var room))
                {
                    b.leaving = room;
                    room.held++;
                    room.picture.sprite = Shown(room);
                }
                b.path = path;
                b.started = now;
                b.seconds = Mathf.Max(.3f, Length(path) / WalkPixelsPerSecond);
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
