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

    // Where everything sits, in points with y growing upward (AppKit's way). `frame` is on the screen; the rest is inside
    // it, from its bottom left. `card`, `pin` and `more` are `default` when not there; `rows` is empty with no card.
    public sealed class ProgramLayout
    {
        public Rect frame, figure, badge, card, pin, more;
        public List<Rect> rows = new List<Rect>();
    }

    public enum ProgramHit { None, Figure, Row, More, Pin }

    // The program on the desktop: the card's rows, and the pictures made ready for AppKit.
    public static class ProgramCard
    {
        public const int MaxRows = 5, NameLetters = 24;

        // Sizes in points. The badge sits over the head: the picture leaves only 6 of its 48 rows clear there and the badge
        // is 20 high, so the frame is taller than the figure by the 15 rows that do not fit (45 points).
        public static readonly Vector2 FigureSize = new Vector2(120, 144), BadgeSize = new Vector2(48, 60);
        public const float BadgeLift = 45, Margin = 16, CardWidth = 280, RowHeight = 22, Pad = 24;
        public const long HoldMs = 800;
        const float TitleHeight = 28, PinSize = 20, Inset = 8, CardGap = 6, CardFoot = 6;

        // The figure stays where it is; the card only adds room above it. `rows` is how many lines the card has.
        public static ProgramLayout Layout(Rect screenVisible, int rows, bool more, bool cardShown)
        {
            var l = new ProgramLayout();
            bool card = cardShown && rows > 0;
            float top = FigureSize.y + BadgeLift;   // the badge's top edge
            float wide = card ? CardWidth : FigureSize.x;
            float cardH = card ? TitleHeight + (rows + (more ? 1 : 0)) * RowHeight + CardFoot : 0;
            float high = card ? top + CardGap + cardH : top;
            l.frame = new Rect(screenVisible.xMax - Margin - wide, screenVisible.y + Margin, wide, high);
            l.figure = new Rect(wide - FigureSize.x, 0, FigureSize.x, FigureSize.y);
            l.badge = new Rect(l.figure.center.x - BadgeSize.x / 2, FigureSize.y - BadgeSize.y + BadgeLift, BadgeSize.x, BadgeSize.y);
            if (!card) return l;
            l.card = new Rect(wide - CardWidth, top + CardGap, CardWidth, cardH);
            l.pin = new Rect(l.card.xMax - Inset - PinSize, l.card.yMax - (TitleHeight + PinSize) / 2, PinSize, PinSize);
            float y = l.card.yMax - TitleHeight;
            for (int i = 0; i < rows; i++, y -= RowHeight)
                l.rows.Add(new Rect(l.card.x + Inset, y - RowHeight, CardWidth - 2 * Inset, RowHeight));
            if (more) l.more = new Rect(l.card.x + Inset, y - RowHeight, CardWidth - 2 * Inset, RowHeight);
            return l;
        }

        // What is under a point given inside the frame. The card's parts come before the figure.
        public static ProgramHit Hit(ProgramLayout l, Vector2 inFrame, out int row)
        {
            row = -1;
            if (l.pin.Contains(inFrame)) return ProgramHit.Pin;
            for (int i = 0; i < l.rows.Count; i++)
                if (l.rows[i].Contains(inFrame)) { row = i; return ProgramHit.Row; }
            if (l.more.Contains(inFrame)) return ProgramHit.More;
            return l.figure.Contains(inFrame) ? ProgramHit.Figure : ProgramHit.None;
        }

        // Near is the figure with its badge and a little around, and the card while it is there.
        public static bool Near(ProgramLayout l, Vector2 onScreen)
        {
            var p = onScreen - l.frame.position;
            return Grow(l.figure).Contains(p) || Grow(l.badge).Contains(p) || l.card.Contains(p);
        }

        static Rect Grow(Rect r) => new Rect(r.x - Pad, r.y - Pad, r.width + 2 * Pad, r.height + 2 * Pad);

        // With rows to show, the card stays up while pinned or near, and a moment after the pointer left so it can reach the card.
        public static bool Shows(bool pinned, int rows, bool near, long nowMs, long leftAtMs) =>
            rows > 0 && (pinned || near || nowMs - leftAtMs < HoldMs);

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
