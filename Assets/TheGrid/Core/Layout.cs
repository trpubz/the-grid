using UnityEngine;

namespace TheGrid.Core
{
    // How one sector is arranged: where its six buildings and its fountain stand, where the bots of each place stand,
    // and which light each building carries. Every sector has the same things in it; the arrangement and the lights
    // follow from the sector's plot number, so a repo's sector looks the same every time and unlike its neighbours.
    //
    // Two rules hold in every arrangement, and LayoutTests proves them for all of them:
    // - Tall buildings stand along the back, so they hide nothing of their own sector. The two low ones stand in the
    //   middle, far enough forward that a bot at a back door is seen over their roofs.
    // - No bot's spot is behind a building, its own sector's or the sector's in front. Bots that need the owner stand
    //   in the open at the front, seen whole, clear of the names of the bots at the doors.
    public sealed class Layout
    {
        public const int Count = 24;   // two templates, six orders of the three tall buildings, the two low ones either way round

        // A row of spots: the first, how many to a row, the step along the row and the step to the next row.
        readonly struct Row
        {
            public readonly float col, row, along, away;
            public readonly int perRow;
            public Row(float col, float row, int perRow, float along, float away) { this.col = col; this.row = row; this.perRow = perRow; this.along = along; this.away = away; }
            public Vector2 Spot(int slot) => new Vector2(col + slot % perRow * along, row + slot / perRow * away);
        }

        sealed class Template
        {
            public Vector2Int[] tall, mid;          // where a 3 by 3 building can stand, and a 4 by 3 one
            public Vector2Int relay, fountain;
            public Row[] tallDoor, midDoor;         // where bots stand at the building in each of those positions
            public Row plaza, forecourt, dock;
        }

        // Bots that need the owner stand at the front, in the open between the two low buildings: far enough from every
        // door that no other bot's name is drawn over them.
        // shortcut: twelve fit on the plot; a thirteenth stands on the lane, where a tower of the sector in front can hide
        // its boots. Close the rows up if one repo often has more than twelve sessions waiting on the owner at once.
        static readonly Row Forecourt = new Row(4.75f, 10.8f, 4, 1f, .55f);

        static readonly Template[] Templates =
        {
            // The back row, left to right: tall, tall, relay, tall.
            new Template
            {
                tall = new[] { new Vector2Int(0, 0), new Vector2Int(4, 0), new Vector2Int(9, 0) }, relay = new Vector2Int(7, 0),
                mid = new[] { new Vector2Int(0, 6), new Vector2Int(8, 6) }, fountain = new Vector2Int(5, 6),
                tallDoor = new[] { new Row(0.25f, 3.25f, 3, 1f, .4f), new Row(4.25f, 3.25f, 3, 1f, .4f), new Row(9.25f, 3.25f, 3, 1f, .4f) },
                midDoor = new[] { new Row(0.2f, 9.25f, 3, 1f, .5f), new Row(8.4f, 9.25f, 4, .95f, .5f) },
                plaza = new Row(4.4f, 8.2f, 3, .7f, .45f), forecourt = Forecourt, dock = new Row(6.9f, 2.3f, 2, .6f, .5f),
            },
            // The back row: tall, relay, tall, tall.
            new Template
            {
                tall = new[] { new Vector2Int(0, 0), new Vector2Int(5, 0), new Vector2Int(9, 0) }, relay = new Vector2Int(3, 0),
                mid = new[] { new Vector2Int(0, 6), new Vector2Int(8, 6) }, fountain = new Vector2Int(5, 5),
                tallDoor = new[] { new Row(0.25f, 3.25f, 3, 1f, .4f), new Row(5.25f, 3.25f, 3, 1f, .4f), new Row(9.25f, 3.25f, 3, 1f, .4f) },
                midDoor = new[] { new Row(0.2f, 9.25f, 3, 1f, .5f), new Row(8.4f, 9.25f, 4, .95f, .5f) },
                plaza = new Row(4.4f, 7.4f, 3, .7f, .45f), forecourt = Forecourt, dock = new Row(3.0f, 2.3f, 2, .6f, .5f),
            },
        };

        static readonly int[][] Orders = { new[] { 0, 1, 2 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 }, new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 } };

        // The workshop's walls are cut away so its first four bots can be seen inside: at the terminal, at the bench, and
        // two on the open floor. From the corner of its footprint.
        static readonly Vector2[] InTheWorkshop = { new Vector2(1.5f, 1.0f), new Vector2(2.5f, 1.6f), new Vector2(2.0f, 2.0f), new Vector2(2.8f, 2.3f) };

        static readonly Layout[] All = Make();

        readonly Template template;
        readonly int[] order;      // which tall position the server tower, the review hall and the works stand in
        readonly int swap;         // 0: the depot in the first low position and the workshop in the second; 1: the other way round
        readonly int plot, look;

        public int Arrangement { get; }

        Layout(int arrangement, int plot, int look)
        {
            Arrangement = arrangement;
            this.plot = plot;
            this.look = look;
            template = Templates[arrangement % 2];
            order = Orders[arrangement / 2 % 6];
            swap = arrangement / 12 % 2;
        }

        static Layout[] Make()
        {
            var all = new Layout[Count];
            for (int a = 0; a < Count; a++) all[a] = new Layout(a, 0, 0);
            return all;
        }

        // One of the arrangements, by number.
        public static Layout Of(int arrangement) => All[((arrangement % Count) + Count) % Count];

        // The arrangement and lights of the sector on a plot, standing on a cell of the map in a region of that look. The
        // arrangement goes by where the sector is: five steps for each column and seven for each row, so a sector differs
        // from all eight round it however large the map, and the two templates alternate like the squares of a chessboard.
        public static Layout For(int plot, Vector2Int cell, int look) => new Layout(((cell.x * 5 + cell.y * 7) % Count + Count) % Count, plot, look);

        // The same for a map of one region in the first look, where a plot's cell is its place on the one spiral.
        public static Layout For(int plot) => For(plot, Plots.Cell(plot), 0);

        // How many crates the depot shows: one for each open pull request, as far as there is room in front of it. The
        // depot's list has them all.
        public const int Crates = 9;

        // Where a crate stands, in tiles from the plot's first tile: in rows in front of the depot's door.
        public Vector2 Crate(int slot) => template.midDoor[swap].Spot(slot);

        static int Tall(Structure s) => s == Structure.ServerTower ? 0 : s == Structure.ReviewHall ? 1 : 2;

        public RectInt Footprint(Structure s)
        {
            switch (s)
            {
                case Structure.Relay: return new RectInt(template.relay, new Vector2Int(2, 2));
                case Structure.Fountain: return new RectInt(template.fountain, new Vector2Int(2, 2));
                case Structure.Depot: return new RectInt(template.mid[swap], new Vector2Int(4, 3));
                case Structure.Workshop: return new RectInt(template.mid[1 - swap], new Vector2Int(4, 3));
                default: return new RectInt(template.tall[order[Tall(s)]], new Vector2Int(3, 3));
            }
        }

        // Where a bot stands, in tiles from the plot's first tile.
        public Vector2 Spot(Place place, int slot)
        {
            switch (place)
            {
                case Place.ServerTower: return template.tallDoor[order[0]].Spot(slot);
                case Place.ReviewHall: return template.tallDoor[order[1]].Spot(slot);
                case Place.Works: return template.tallDoor[order[2]].Spot(slot);
                case Place.Workshop:
                    if (slot < InTheWorkshop.Length) return (Vector2)template.mid[1 - swap] + InTheWorkshop[slot];
                    // shortcut: a fifth bot waits outside in front, shown whole but not at a terminal. Give the workshop a
                    // bigger floor if five sessions testing at once in one repo turns out to be common.
                    return template.midDoor[1 - swap].Spot(slot - InTheWorkshop.Length);
                case Place.Plaza: return template.plaza.Spot(slot);
                case Place.Dock: return template.dock.Spot(slot);
                default: return template.forecourt.Spot(slot);
            }
        }

        // The look of the region this sector stands in.
        public int Look => look;

        // Which of Lights' families a building carries here; -1 for the fountain, which is violet everywhere. Most carry
        // the primary light of the sector's region; one or two carry each of its accents. Three steps for each plot and
        // two for each kind of building, so which buildings carry an accent changes from sector to sector.
        public int Light(Structure s)
        {
            if (s == Structure.Fountain) return -1;
            int v = (plot * 3 + (int)s * 2) % 5;
            return v < 3 ? Lights.Primary(look) : Lights.Accent(look, v - 3);
        }

        // How high a structure's body stands above its ground, in art pixels: what can hide a bot behind it. Masts, stacks
        // and the fountain's column are too thin to hide one and are left out. ArtTests holds these against the pictures.
        public static int Wall(Structure s)
        {
            switch (s)
            {
                case Structure.ServerTower: return 198;
                case Structure.ReviewHall: return 136;
                case Structure.Works: return 70;
                case Structure.Relay: return 56;
                case Structure.Depot: return 60;
                case Structure.Workshop: return 47;
                default: return 8;
            }
        }
    }
}
