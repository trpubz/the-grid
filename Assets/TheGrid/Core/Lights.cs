namespace TheGrid.Core
{
    // The lights of the map. A building's picture, a line on the ground and a chevron on a lane are drawn in one light,
    // the first; the game swaps its six colours for another family's. All the lights on one building are one family:
    // its lines, its windows and the glow at its foot.
    // A region, the part of the map one workspace's sectors stand in, has a look: a primary light, which its lanes, the
    // lines on its ground and most of its buildings carry, and two accents, which a few buildings carry instead.
    // No amber and no red: amber is for badges and red is a fault. Every colour here is in the art palette, and a test
    // holds them to it.
    public static class Lights
    {
        // In each family: the bright line, the dimmer line, a lit window, and the glow on the paving from far to near.
        // 0xRRGGBB.
        static readonly int[][] Families =
        {
            new[] { 0xc9fbff, 0x35f2ff, 0x1a8f9c, 0x3f5a96, 0x4a7fb4, 0x62b4d6 },   // 0 ice: as drawn
            new[] { 0x35f2ff, 0x1a8f9c, 0x1a8f9c, 0x3f5a96, 0x4a7fb4, 0x62b4d6 },   // 1 teal
            new[] { 0xa9c8ff, 0x4d8dff, 0x2a55b0, 0x2f4a96, 0x3a62c0, 0x5b8be6 },   // 2 blue
            new[] { 0xb6ffe6, 0x3ef2b0, 0x1a9c78, 0x2f6f7a, 0x3a9c8c, 0x5cd6b0 },   // 3 mint
            new[] { 0xd9c8ff, 0xa070ff, 0x6657b8, 0x4b4696, 0x4b4696, 0x6657b8 },   // 4 violet
            new[] { 0xffffff, 0xe4ecff, 0x8791c4, 0x525a9b, 0x5f67aa, 0x6e76b7 },   // 5 white
            new[] { 0xffd0e6, 0xff6fb0, 0xb0447c, 0x6a4690, 0x9a5aa4, 0xd07ab8 },   // 6 rose
            new[] { 0xffc27a, 0xff7a1e, 0xa8501a, 0x6a4660, 0x96584e, 0xc87840 },   // 7 ember
        };

        // A region's look, in the order workspaces take them: the first workspace is cyan, the second white, and so on,
        // until the owner picks another. The names are what the settings file says.
        public static readonly string[] LookNames = { "cyan", "white", "rose", "mint", "violet", "blue", "ember" };
        // For each look: its primary family, then its two accents.
        static readonly int[][] Looks =
        {
            new[] { 0, 2, 3 }, new[] { 5, 2, 4 }, new[] { 6, 4, 5 }, new[] { 3, 1, 5 }, new[] { 4, 6, 2 }, new[] { 2, 0, 5 }, new[] { 7, 5, 6 },
        };

        public static int LookCount => Looks.Length;
        static int[] Look(int look) => Looks[((look % LookCount) + LookCount) % LookCount];
        public static int Primary(int look) => Look(look)[0];
        public static int Accent(int look, int which) => Look(look)[1 + (which & 1)];

        public const int Parts = 6;
        public static int Count => Families.Length;

        // The colours a picture is drawn in.
        public static int[] Drawn => Families[0];

        // A family a step down, for the dim half of a pulse: the bright line in the dimmer line's colour, the dimmer line
        // in the window's, and all the glow on the paving in its farthest, faintest band. Every colour is the family's own.
        public static int[] Dimmed(int light)
        {
            var f = Family(light);
            return new[] { f[1], f[2], f[2], f[3], f[3], f[3] };
        }

        public static int[] Family(int light) => Families[((light % Count) + Count) % Count];
    }
}
