namespace TheGrid.Core
{
    // The lights a building can carry. A building's picture is drawn in one of them, the first; the game swaps its six
    // colours for another family's, so the same kind of building is lit differently from sector to sector. All the
    // lights on one building are one family: its lines, its windows and the glow at its foot.
    // Blues, teals, white and violet. No amber: amber is for badges. Every colour here is in the art palette, and a
    // test holds them to it.
    public static class Lights
    {
        // In each family: the bright line, the dimmer line, a lit window, and the glow on the paving from far to near.
        // 0xRRGGBB.
        static readonly int[][] Families =
        {
            new[] { 0xc9fbff, 0x35f2ff, 0x1a8f9c, 0x3f5a96, 0x4a7fb4, 0x62b4d6 },   // white: as drawn
            new[] { 0x35f2ff, 0x1a8f9c, 0x1a8f9c, 0x3f5a96, 0x4a7fb4, 0x62b4d6 },   // cyan
            new[] { 0xa9c8ff, 0x4d8dff, 0x2a55b0, 0x2f4a96, 0x3a62c0, 0x5b8be6 },   // blue
            new[] { 0xb6ffe6, 0x3ef2b0, 0x1a9c78, 0x2f6f7a, 0x3a9c8c, 0x5cd6b0 },   // mint
            new[] { 0xd9c8ff, 0xa070ff, 0x6657b8, 0x4b4696, 0x4b4696, 0x6657b8 },   // violet
        };

        public const int Parts = 6;
        public static int Count => Families.Length;

        // The colours a building's picture is drawn in.
        public static int[] Drawn => Families[0];

        public static int[] Family(int light) => Families[((light % Count) + Count) % Count];
    }
}
