using System.Collections.Generic;
using UnityEngine;

namespace TheGrid.Core
{
    // The bots' names on the map, closer in: which show, and where.
    public static class MapNames
    {
        // The bots whose names show: the selected one and the one under the pointer ("" for none), in the order they take
        // their places. The selected one is first, so that its name stands still while the pointer goes from bot to bot.
        public static List<string> Shown(string selected, string pointed)
        {
            var shown = new List<string>();
            if (selected != "") shown.Add(selected);
            if (pointed != "" && pointed != selected) shown.Add(pointed);
            return shown;
        }

        // Where a name's plate goes that is wanted there: there, or straight down from there until it covers none of the
        // plates that are taken. Touching is not covering.
        public static Rect Clear(Rect wanted, IReadOnlyList<Rect> taken)
        {
            bool moved;
            do
            {
                moved = false;
                foreach (var t in taken)
                    if (t.width > 0 && t.Overlaps(wanted))
                    {
                        wanted.y = t.yMax;
                        moved = true;
                    }
            }
            while (moved);
            return wanted;
        }
    }
}
