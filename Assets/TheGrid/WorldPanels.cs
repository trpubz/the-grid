using System.Collections.Generic;
using TheGrid.Core;
using UnityEngine;

namespace TheGrid
{
    // The two panels drawn over the world, down its right side: who needs the owner at the top, and the card of the selected
    // bot at the bottom. The left and the middle stay clear: that is where a sector's waiting bots stand.
    // Flat plates in palette colours. They say only what Panels made from the roster snapshot.
    public sealed class WorldPanels
    {
        const float Wide = 220, CardWide = 260, Margin = 8, TitleHigh = 26, RowHigh = 38, CardHigh = 92;
        const int MostRows = 6, RowsWhenShort = 2;
        static readonly Color Plate = new Color32(0x0c, 0x10, 0x24, 255), Edge = new Color32(0x3a, 0x41, 0x78, 255), Lit = new Color32(0x35, 0xf2, 0xff, 255);
        static readonly Color Dim = new Color(1, 1, 1, .6f);

        readonly Texture2D[] badges = new Texture2D[5];   // in the order of Badge
        bool folded, foldChosen;
        Vector2 scroll;
        Rect needs, card;   // where the panels are; zero when not drawn

        // Works out where the panels go. Call on every event before the map handles input, so that what is drawn, what is
        // clicked and what the map leaves alone are the same rectangles.
        public void Place(Rect map, int rows, bool hasCard)
        {
            bool narrow = map.width < 520;
            if (!foldChosen)
            {
                folded = narrow;   // in a narrow window the panel starts as its title only; after that it is the owner's choice
                foldChosen = true;
            }
            needs = card = default;
            if (hasCard)
            {
                float w = narrow ? map.width - 2 * Margin : CardWide;
                card = new Rect(map.xMax - Margin - w, map.yMax - Margin - CardHigh, w, CardHigh);
            }
            if (rows > 0)
            {
                float w = Mathf.Min(Wide, map.width - 2 * Margin);
                int showing = folded ? 0 : Mathf.Min(rows, map.height < 300 ? RowsWhenShort : MostRows);
                // Never down into the card, and never the whole height of the map: there is always map to click.
                float room = (hasCard ? card.y : map.yMax) - map.y - 2 * Margin - TitleHigh - 1;
                float h = TitleHigh + Mathf.Clamp(showing * RowHigh, 0, Mathf.Max(0, room)) + 1;   // and its bottom edge
                needs = new Rect(map.xMax - Margin - w, map.y + Margin, w, h);
            }
        }

        // True where a panel is: a press there is the panel's, never the map's.
        public bool Covers(Vector2 point) => needs.Contains(point) || card.Contains(point);

        // Draws both panels. Returns the key of a row that was clicked, or "".
        public string Draw(Rect map, List<NeedsYouRow> rows, BotCard about, GUIStyle text, GUIStyle strong)
        {
            string clicked = "";
            if (needs.width > 0)
            {
                Back(needs);
                var title = new Rect(needs.x, needs.y, needs.width, TitleHigh);
                GUI.Label(new Rect(title.x + 8, title.y + 3, title.width - 40, 22), "Needs you  " + rows.Count, strong);
                GUI.Label(new Rect(title.xMax - 22, title.y + 3, 16, 22), folded ? "+" : "−", strong);
                if (GUI.Button(title, GUIContent.none, GUIStyle.none)) folded = !folded;

                var list = new Rect(needs.x + 1, needs.y + TitleHigh, needs.width - 2, needs.height - TitleHigh - 1);
                if (list.height > 0)
                {
                    bool scrolls = rows.Count * RowHigh > list.height;
                    float w = list.width - (scrolls ? 18 : 0);   // room for the scroll bar, or a second one appears along the bottom
                    scroll = GUI.BeginScrollView(list, scroll, new Rect(0, 0, w, rows.Count * RowHigh));
                    for (int i = 0; i < rows.Count; i++)
                    {
                        var row = new Rect(0, i * RowHigh, w, RowHigh);
                        var badge = Badge(rows[i].badge);
                        if (badge != null) GUI.DrawTexture(new Rect(8, row.y + 9, 16, 20), badge);
                        GUI.Label(new Rect(32, row.y + 1, w - 36, 20), rows[i].what, text);
                        Faint(new Rect(32, row.y + 17, w - 36, 20), rows[i].name + " · " + rows[i].sector, text);
                        if (GUI.Button(row, GUIContent.none, GUIStyle.none))
                        {
                            clicked = rows[i].key;
                            // The view goes to that bot, at its middle. In a narrow map the open list lies over the middle,
                            // so it folds, or the bot would be behind it.
                            if (map.width < 520) folded = true;
                        }
                    }
                    GUI.EndScrollView();
                }
            }
            if (card.width > 0 && about != null)
            {
                Back(card);
                GUI.Label(new Rect(card.x + 10, card.y + 6, card.width - 20, 22), about.title, strong);
                GUI.Label(new Rect(card.x + 10, card.y + 27, card.width - 20, 20), about.who, text);
                Faint(new Rect(card.x + 10, card.y + 45, card.width - 20, 20), about.doing, text);
                Faint(new Rect(card.x + 10, card.y + 63, card.width - 100, 20), about.since, text);
                if (about.prNumber > 0 && about.prUrl != "")
                {
                    var pr = new Rect(card.xMax - 84, card.y + 60, 74, 22);
                    Fill(pr, Edge);
                    GUI.Label(new Rect(pr.x + 8, pr.y + 2, pr.width - 10, 18), "PR #" + about.prNumber, text);
                    if (GUI.Button(pr, GUIContent.none, GUIStyle.none)) Application.OpenURL(about.prUrl);   // Panels.Card kept only a web link
                }
            }
            return clicked;
        }

        Texture2D Badge(Badge b)
        {
            string[] art = { "", "badge-question", "badge-approve", "badge-fault", "badge-done" };
            if (b == Core.Badge.None) return null;
            if (badges[(int)b] == null)
            {
                var s = Resources.Load<Sprite>("Art/" + art[(int)b]);
                if (s != null) badges[(int)b] = s.texture;
            }
            return badges[(int)b];
        }

        // A plate: solid, a one-pixel edge, and a line of light along its top.
        static void Back(Rect r)
        {
            Fill(r, Edge);
            Fill(new Rect(r.x + 1, r.y + 1, r.width - 2, r.height - 2), Plate);
            Fill(new Rect(r.x, r.y, r.width, 1), Lit);
        }

        static void Fill(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        static void Faint(Rect r, string words, GUIStyle style)
        {
            var old = GUI.color;
            GUI.color = Dim;
            GUI.Label(r, words, style);
            GUI.color = old;
        }
    }
}
