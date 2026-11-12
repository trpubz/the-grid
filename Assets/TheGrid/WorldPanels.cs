using System.Collections.Generic;
using TheGrid.Core;
using UnityEngine;

namespace TheGrid
{
    // The panels drawn over the world, down its right side: who needs the owner at the top, the card of the selected bot at
    // the bottom, and over the card the list of a depot the owner clicked: its repo's open pull requests. The left and
    // the middle stay clear: that is where a sector's waiting bots stand.
    // Flat plates in palette colours. They say only what Panels made from the roster snapshot.
    public sealed class WorldPanels
    {
        const float Wide = 220, CardWide = 260, DepotWide = 330, Margin = 8, TitleHigh = 26, RowHigh = 38, CardHigh = 92, LineHigh = 18;
        const int SubagentLines = 4, SubagentLinesWhenShort = 1;
        const int MostRows = 6, RowsWhenShort = 2, MostPulls = 5;
        static readonly Color Plate = new Color32(0x0c, 0x10, 0x24, 255), Edge = new Color32(0x3a, 0x41, 0x78, 255), Lit = new Color32(0x35, 0xf2, 0xff, 255);
        static readonly Color Dim = new Color(1, 1, 1, .6f);

        readonly Texture2D[] badges = new Texture2D[5];   // in the order of Badge
        bool folded, foldChosen;
        Vector2 scroll;
        Rect needs, card, depot;   // where the panels are; zero when not drawn
        Vector2 depotScroll;
        GUIStyle wrapped;
        readonly Texture2D[] crates = new Texture2D[ArtNames.Crates.Length];   // in the order of PullState
        List<string> subagents = new List<string>();   // the card's lines about subagents, as many as this map has room for

        // True when the last Draw saw a click on the card's workspace: the owner wants the repo in its next workspace.
        public bool WorkspaceClicked { get; private set; }

        // True when the last Draw saw a click on the depot list's title: the owner is done with it.
        public bool DepotClosed { get; private set; }

        // The address of the pull request whose row was clicked in the last Draw; else "".
        public string PullClicked { get; private set; } = "";

        // Works out where the panels go. Call on every event before the map handles input, so that what is drawn, what is
        // clicked and what the map leaves alone are the same rectangles.
        public void Place(Rect map, int rows, BotCard about, WorldSector depotOf = null)
        {
            bool hasCard = about != null;
            bool narrow = map.width < 520;
            if (!foldChosen)
            {
                folded = narrow;   // in a narrow window the panel starts as its title only; after that it is the owner's choice
                foldChosen = true;
            }
            needs = card = depot = default;
            if (hasCard)
            {
                float w = narrow ? map.width - 2 * Margin : CardWide;
                // A short map has room for one line about subagents, so that the card never takes most of it.
                subagents = Panels.SubagentLines(about, map.height < 300 ? SubagentLinesWhenShort : SubagentLines);
                float h = CardHigh + subagents.Count * LineHigh;
                card = new Rect(map.xMax - Margin - w, map.yMax - Margin - h, w, h);
            }
            if (depotOf != null)
            {
                float w = Mathf.Min(DepotWide, map.width - 2 * Margin);   // wide enough for the longest reason in full
                int lines = Mathf.Clamp(depotOf.pulls.Count, 1, map.height < 300 ? RowsWhenShort : MostPulls);
                bool stale = depotOf.pulls.Count > 0 && depotOf.pullsProblem != "";   // an old list, and a line saying why it is old
                float h = TitleHigh + lines * RowHigh + (stale ? LineHigh + 4 : 0) + 1;
                depot = new Rect(map.xMax - Margin - w, (hasCard ? card.y : map.yMax) - Margin - h, w, h);
            }
            if (rows > 0)
            {
                float w = Mathf.Min(Wide, map.width - 2 * Margin);
                int showing = folded ? 0 : Mathf.Min(rows, map.height < 300 ? RowsWhenShort : MostRows);
                // Never down into the card, and never the whole height of the map: there is always map to click.
                float room = (depot.width > 0 ? depot.y : hasCard ? card.y : map.yMax) - map.y - 2 * Margin - TitleHigh - 1;
                float h = TitleHigh + Mathf.Clamp(showing * RowHigh, 0, Mathf.Max(0, room)) + 1;   // and its bottom edge
                needs = new Rect(map.xMax - Margin - w, map.y + Margin, w, h);
            }
        }

        // True where a panel is: a press there is the panel's, never the map's.
        public bool Covers(Vector2 point) => needs.Contains(point) || card.Contains(point) || depot.Contains(point);

        // Draws the panels. Returns the key of a row of "Needs you" that was clicked, or "".
        // depotOf: the sector whose depot list is open, or null. notAsked: what to say when GitHub was never asked about it.
        public string Draw(Rect map, List<NeedsYouRow> rows, BotCard about, GUIStyle text, GUIStyle strong, WorldSector depotOf = null, string notAsked = "")
        {
            string clicked = "";
            WorkspaceClicked = DepotClosed = false;
            PullClicked = "";
            if (depot.width > 0 && depotOf != null) DrawDepot(depotOf, notAsked, text, strong);
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
                Faint(new Rect(card.x + 10, card.y + 63, 70, 20), about.since, text);
                // The workspace: a button that moves the repo to its next workspace, as in the lists. A session with no
                // repo has none to move.
                if (about.workspace != "")
                {
                    bool pr = about.prNumber > 0 && about.prUrl != "";
                    var ws = new Rect(card.x + 84, card.y + 60, Mathf.Min(110, card.width - 84 - (pr ? 94 : 10)), 22);
                    if (about.repoRoot == "") Faint(new Rect(ws.x + 8, ws.y + 2, ws.width - 10, 20), about.workspace, text);
                    else
                    {
                        Fill(ws, Edge);
                        GUI.Label(new Rect(ws.x + 8, ws.y + 1, ws.width - 10, 20), about.workspace, text);
                        if (GUI.Button(ws, GUIContent.none, GUIStyle.none)) WorkspaceClicked = true;
                    }
                }
                for (int i = 0; i < subagents.Count; i++)
                    Faint(new Rect(card.x + 10, card.y + 86 + i * LineHigh, card.width - 20, 20), subagents[i], text);
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

        // A depot's list: every open pull request of its repo, those for the owner first, each with why it is where it is.
        // A click on a row is for that pull request; a click on the title closes the list.
        void DrawDepot(WorldSector sector, string notAsked, GUIStyle text, GUIStyle strong)
        {
            Back(depot);
            var title = new Rect(depot.x, depot.y, depot.width, TitleHigh);
            GUI.Label(new Rect(title.x + 8, title.y + 3, title.width - 40, 22), "PR depot · " + sector.name, strong);
            GUI.Label(new Rect(title.xMax - 22, title.y + 3, 16, 22), "×", strong);
            if (GUI.Button(title, GUIContent.none, GUIStyle.none)) DepotClosed = true;

            var pulls = sector.pulls;
            bool stale = pulls.Count > 0 && sector.pullsProblem != "";
            var list = new Rect(depot.x + 1, depot.y + TitleHigh, depot.width - 2, depot.height - TitleHigh - 1 - (stale ? LineHigh + 4 : 0));
            if (pulls.Count == 0)
            {
                // Nothing to list, and why: never asked, could not be had, or none open.
                if (wrapped == null) wrapped = new GUIStyle(text) { wordWrap = true, clipping = TextClipping.Clip };
                string why = !sector.pullsAsked ? notAsked : sector.pullsProblem != "" ? sector.pullsProblem : "No open pull requests.";
                Faint(new Rect(list.x + 9, list.y + 2, list.width - 18, list.height - 4), why, wrapped);
                return;
            }
            bool scrolls = pulls.Count * RowHigh > list.height;
            float w = list.width - (scrolls ? 18 : 0);
            depotScroll = GUI.BeginScrollView(list, depotScroll, new Rect(0, 0, w, pulls.Count * RowHigh));
            for (int i = 0; i < pulls.Count; i++)
            {
                var row = new Rect(0, i * RowHigh, w, RowHigh);
                var crate = Crate(pulls[i].state);
                if (crate != null) GUI.DrawTexture(new Rect(4, row.y + 7, 24, 24), crate);
                GUI.Label(new Rect(32, row.y + 1, w - 36, 20), "#" + pulls[i].number + "  " + pulls[i].title, text);
                Faint(new Rect(32, row.y + 17, w - 36, 20), pulls[i].status, text);
                if (GUI.Button(row, GUIContent.none, GUIStyle.none)) PullClicked = pulls[i].url;
            }
            GUI.EndScrollView();
            if (stale) Faint(new Rect(depot.x + 10, list.yMax + 2, depot.width - 20, LineHigh), "Not refreshed: " + sector.pullsProblem, text);
        }

        Texture2D Crate(PullState state)
        {
            if (crates[(int)state] == null)
            {
                var s = WorldStage.Art(ArtNames.Crates[(int)state]);
                if (s != null) crates[(int)state] = s.texture;
            }
            return crates[(int)state];
        }

        Texture2D Badge(Badge b)
        {
            if (b == Core.Badge.None) return null;
            if (badges[(int)b] == null)
            {
                var s = WorldStage.Art(ArtNames.Badges[(int)b]);   // which says so, once, if the picture is missing
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
