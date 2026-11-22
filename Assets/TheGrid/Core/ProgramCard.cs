using System;
using System.Collections.Generic;
using UnityEngine;

namespace TheGrid.Core
{
    // One line of the card: a session, in a word.
    public sealed class ProgramRow
    {
        public string key = "", name = "", word = "", full = "", needs = "";
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
        public Rect frame, figure, badge, card, pin, more, open;   // `open` is the room under the open row, `default` with none
        public int openRow = -1;
        public List<Rect> rows = new List<Rect>();
    }

    // Where a row's parts sit, in the same points as its row: the badge's picture, or a dot where it has none; the name; the word.
    public sealed class ProgramRowParts
    {
        public Rect badge, dot, name, word;
    }

    public enum ProgramHit { None, Figure, Row, More, Pin }

    // The program on the desktop: the card's rows, and the pictures made ready for AppKit.
    public static class ProgramCard
    {
        public const int MaxRows = 5, NameLetters = 24, NeedLines = 3;

        // Sizes in points. The badge sits over the head: the picture leaves only 6 of its 48 rows clear there and the badge
        // is 20 high, so the frame is taller than the figure by the 15 rows that do not fit (45 points).
        public static readonly Vector2 FigureSize = new Vector2(120, 144), BadgeSize = new Vector2(48, 60);
        public const float OpenHeight = 70, BadgeLift = 45, Margin = 16, CardWidth = 280, RowHeight = 22, Pad = 24;
        public const long HoldMs = 800;
        const float TitleHeight = 28, PinSize = 20, Inset = 8, CardGap = 6, CardFoot = 6;
        // Inside a row: room for the badge before the name, the gap before the word, a line of text, the dot, the pin's square.
        const float NameAt = 22, WordGap = 8, LineHeight = 16, DotSize = 6, PinMarkSize = 10;
        public static readonly Vector2 RowBadgeSize = new Vector2(16, 20);   // one point to each pixel of the badge's art

        // The figure stays where it is; the card only adds room above it. `rows` is how many lines the card has.
        public static ProgramLayout Layout(Rect screenVisible, int rows, bool more, bool cardShown, int open = -1)
        {
            var l = new ProgramLayout();
            bool card = cardShown && rows > 0;
            float top = FigureSize.y + BadgeLift;   // the badge's top edge
            float wide = card ? CardWidth : FigureSize.x;
            if (!card || open < 0 || open >= rows) open = -1;
            float cardH = card ? TitleHeight + (rows + (more ? 1 : 0)) * RowHeight + CardFoot + (open >= 0 ? OpenHeight : 0) : 0;
            float high = card ? top + CardGap + cardH : top;
            l.frame = new Rect(screenVisible.xMax - Margin - wide, screenVisible.y + Margin, wide, high);
            l.figure = new Rect(wide - FigureSize.x, 0, FigureSize.x, FigureSize.y);
            l.badge = new Rect(l.figure.center.x - BadgeSize.x / 2, FigureSize.y - BadgeSize.y + BadgeLift, BadgeSize.x, BadgeSize.y);
            if (!card) return l;
            l.card = new Rect(wide - CardWidth, top + CardGap, CardWidth, cardH);
            l.pin = new Rect(l.card.xMax - Inset - PinSize, l.card.yMax - (TitleHeight + PinSize) / 2, PinSize, PinSize);
            float y = l.card.yMax - TitleHeight;
            for (int i = 0; i < rows; i++, y -= RowHeight)
            {
                l.rows.Add(new Rect(l.card.x + Inset, y - RowHeight, CardWidth - 2 * Inset, RowHeight));
                if (i != open) continue;
                l.openRow = i;
                l.open = new Rect(l.card.x + Inset, y - RowHeight - OpenHeight, CardWidth - 2 * Inset, OpenHeight);
                y -= OpenHeight;
            }
            if (more) l.more = new Rect(l.card.x + Inset, y - RowHeight, CardWidth - 2 * Inset, RowHeight);
            return l;
        }

        // A row's parts, given how wide its word is when drawn: the word at the right, never more than half the row, so the
        // name between the badge and the word keeps some room.
        public static ProgramRowParts Parts(Rect row, float wordWidth)
        {
            float word = Mathf.Clamp(wordWidth, 0, row.width / 2);
            var badge = new Rect(row.x, row.center.y - RowBadgeSize.y / 2, RowBadgeSize.x, RowBadgeSize.y);
            return new ProgramRowParts
            {
                badge = badge,
                dot = new Rect(badge.center.x - DotSize / 2, badge.center.y - DotSize / 2, DotSize, DotSize),
                name = Line(row, row.x + NameAt, row.width - NameAt - word - WordGap),
                word = Line(row, row.xMax - word, word)
            };
        }

        // The pin's square, filled when pinned and hollow when not, in the middle of the pin.
        public static Rect PinMark(Rect pin) => new Rect(pin.center.x - PinMarkSize / 2, pin.center.y - PinMarkSize / 2, PinMarkSize, PinMarkSize);

        // "+N more", in line with the names.
        public static Rect MoreLine(Rect more) => Line(more, more.x + NameAt, more.width - NameAt);

        static Rect Line(Rect row, float x, float wide) => new Rect(x, row.center.y - LineHeight / 2, wide, LineHeight);   // in the middle of the row

        // What is under a point given inside the frame. The card's parts come before the figure.
        public static ProgramHit Hit(ProgramLayout l, Vector2 inFrame, out int row)
        {
            row = -1;
            if (l.pin.Contains(inFrame)) return ProgramHit.Pin;
            if (l.open.Contains(inFrame)) { row = l.openRow; return ProgramHit.Row; }
            for (int i = 0; i < l.rows.Count; i++)
                if (l.rows[i].Contains(inFrame)) { row = i; return ProgramHit.Row; }
            if (l.more.Contains(inFrame)) return ProgramHit.More;
            return l.figure.Contains(inFrame) ? ProgramHit.Figure : ProgramHit.None;
        }

        // The row under a point on the screen, its open part included; -1 when none.
        public static int Hover(ProgramLayout l, Vector2 onScreen) =>
            Hit(l, onScreen - l.frame.position, out int row) == ProgramHit.Row ? row : -1;

        // Line 0..NeedLines of the open part, top down, in line with the names and ending at the row's right edge.
        public static Rect OpenLine(Rect open, int line)
        {
            const float Air = 3;   // half of the 6 left over by four lines of 16 in OpenHeight
            return new Rect(open.x + NameAt, open.yMax - Air - (line + 1) * LineHeight, open.width - NameAt, LineHeight);
        }

        // Words on as few lines as fit `wide`; a word wider than a line is broken. At most `lines`; with text left over the
        // last line ends with "…" and still fits.
        public static List<string> Wrap(string text, Func<string, float> width, float wide, int lines)
        {
            var got = new List<string>();
            var words = new List<string>((text ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
            while (words.Count > 0 && got.Count < lines)
            {
                var rest = string.Join(" ", words);
                if (width(rest) <= wide) { got.Add(rest); break; }
                bool last = got.Count == lines - 1;
                string line = "";
                int n = 0;
                while (n < words.Count)
                {
                    var next = line == "" ? words[n] : line + " " + words[n];
                    if (width(next + (last ? "…" : "")) > wide) break;
                    line = next;
                    n++;
                }
                words.RemoveRange(0, n);
                if (n == 0)   // the first word alone is too wide: break it
                {
                    if (width(Letter(words[0])) > wide) return new List<string>();   // not even one letter fits
                    int keep = Fit(words[0], width, wide, last ? "…" : "");
                    line = words[0].Substring(0, keep) + (last ? "…" : "");
                    words[0] = words[0].Substring(keep);
                }
                else if (last) line += "…";
                got.Add(line);
            }
            return got;
        }

        static string Letter(string s) => s.Substring(0, s.Length > 1 && char.IsHighSurrogate(s[0]) ? 2 : 1);

        // How many letters of `s` fit in `wide` with `tail` after them, at least one, never inside a surrogate pair.
        static int Fit(string s, Func<string, float> width, float wide, string tail)
        {
            int keep = Letter(s).Length;
            while (keep < s.Length)
            {
                int more = keep + Letter(s.Substring(keep)).Length;
                if (width(s.Substring(0, more) + tail) > wide) break;
                keep = more;
            }
            return keep;
        }

        // What an open row says: its full name on one line, then what it needs, or its state's words when it needs nothing.
        public static List<string> OpenLines(ProgramRow row, Func<string, float> width, float wide)
        {
            var name = row.full;
            if (width(name) > wide) name = name.Substring(0, Fit(name, width, wide, "…")) + "…";
            var got = new List<string> { name };
            got.AddRange(Wrap(row.needs != "" ? row.needs : row.word, width, wide, NeedLines));
            return got;
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
                    name = Cut(a.name), full = Needs.Clean(a.name), needs = a.needs,
                    word = Word(a), badge = World.BadgeFor(a.state), pid = a.pid
                });
            }
            return v;
        }

        // A cut must not leave half an emoji: a lone surrogate cannot be sent to the shell as UTF-8.
        static string Cut(string name)
        {
            if (name.Length <= NameLetters) return name;
            var keep = NameLetters - 1;
            if (char.IsHighSurrogate(name[keep - 1])) keep--;
            return name.Substring(0, keep) + "…";
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
