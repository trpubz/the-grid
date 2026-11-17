using UnityEngine;

namespace TheGrid.Core
{
    // The pictures of the program on the desktop, made ready for AppKit.
    public static class ProgramCard
    {
        // Each pixel becomes a `times` by `times` block, so the art stays crisp at any size. Rows are flipped: Unity's first
        // row is the bottom one, an AppKit bitmap starts at the top. A clear pixel carries no colour, so nothing bleeds round it.
        public static Color32[] Scaled(Color32[] pixels, int width, int height, int times)
        {
            int wide = width * times;
            var got = new Color32[wide * height * times];
            for (int y = 0; y < height * times; y++)
                for (int x = 0; x < wide; x++)
                {
                    var p = pixels[(height - 1 - y / times) * width + x / times];
                    got[y * wide + x] = p.a == 0 ? new Color32(0, 0, 0, 0) : p;
                }
            return got;
        }
    }
}
