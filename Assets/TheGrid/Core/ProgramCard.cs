using System.Collections.Generic;
using UnityEngine;

namespace TheGrid.Core
{
    // One line of the card: a session, in a word.
    public sealed class ProgramRow
    {
        public string key = "", name = "", word = "";
        public Badge badge;
        public int pid;
    }

    // What the card shows: the first rows of the roster, how many more there are, and whether the program is lit.
    public sealed class ProgramView
    {
        public List<ProgramRow> rows = new List<ProgramRow>();
        public int more;
        public bool lit;
    }

    // The program on the desktop: the card's rows, and the pictures made ready for AppKit.
    public static class ProgramCard
    {
        public const int MaxRows = 5, NameLetters = 24;

        // The card is the roster's first rows in its order. The program is lit for any session that needs the owner,
        // also one that is under "more": the card is a short list, the light is not.
        public static ProgramView Of(RosterSnapshot snap)
        {
            var v = new ProgramView();
            foreach (var a in snap.agents)
            {
                if (World.NeedsOwner(a.state)) v.lit = true;
                if (v.rows.Count == MaxRows) { v.more++; continue; }
                v.rows.Add(new ProgramRow
                {
                    key = a.account + "|" + a.sessionId,   // as Panels keys a bot
                    name = a.name.Length > NameLetters ? a.name.Substring(0, NameLetters - 1) + "…" : a.name,
                    word = Word(a), badge = World.BadgeFor(a.state), pid = a.pid
                });
            }
            return v;
        }

        static string Word(AgentRow a)
        {
            switch (a.state)
            {
                case "Running": return a.activity == "" ? Activity.Working : a.activity;
                case "Finished": return "Done";
                case "Offline": return "Offline";
                default: return Panels.What(a);
            }
        }

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
