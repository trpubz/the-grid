using System;
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
        static readonly Color Dim = new Color(1, 1, 1, .6f), Well = new Color32(0x05, 0x07, 0x12, 255);   // Well: the ground of a code block

        readonly Texture2D[] badges = new Texture2D[5];   // in the order of Badge
        bool folded, foldChosen;
        Vector2 scroll;
        Rect needs, card, depot;   // where the panels are; zero when not drawn
        Vector2 depotScroll;
        GUIStyle wrapped, code;
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
                float h = CardHigh + (subagents.Count + Entry(about)) * LineHigh;
                card = new Rect(map.xMax - Margin - w, map.yMax - Margin - h, w, h);
            }
            if (depotOf != null)
            {
                int lines = Mathf.Clamp(depotOf.pulls.Count, 1, map.height < 300 ? RowsWhenShort : MostPulls);
                bool stale = depotOf.pulls.Count > 0 && depotOf.pullsProblem != "";   // an old list, and a line saying why it is old
                float wanted = TitleHigh + lines * RowHigh + (stale ? LineHigh + 4 : 0) + 1;
                // Over the card, under the title of "Needs you", and no higher than the room between them: in a short map
                // with a tall card it shows fewer rows and scrolls, and its title, which closes it, stays in view.
                float ceiling = map.y + Margin + (rows > 0 ? TitleHigh + 1 + Margin : 0);
                depot = Panels.Above(map, hasCard ? card.y : map.yMax, ceiling, DepotWide, wanted, TitleHigh + RowHigh / 2 + 1, Margin);
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
                float y = card.y + 86;
                // The way into the conversation: where it already lives, if it is in a zellij pane, and the command that
                // opens it in a terminal. Of the command the card shows the end, which says which conversation; Copy takes
                // all of it. Nothing is run.
                if (about.where != "")
                {
                    Faint(new Rect(card.x + 10, y, card.width - 20, 20), "zellij: " + about.where, text);
                    y += LineHigh;
                }
                if (about.enter != "")
                {
                    var copy = new Rect(card.xMax - 62, y + 1, 52, 16);
                    GUI.Label(new Rect(card.x + 10, y, copy.x - card.x - 16, 20), about.enter.Substring(Mathf.Max(0, about.enter.LastIndexOf("claude ", StringComparison.Ordinal))), text);
                    Fill(copy, Edge);
                    bool copied = copiedKey == about.key && Time.unscaledTime < copiedUntil;
                    GUI.Label(new Rect(copy.x + 6, copy.y - 2, copy.width - 8, 20), copied ? "Copied" : "Copy", text);
                    if (GUI.Button(copy, GUIContent.none, GUIStyle.none))
                    {
                        GUIUtility.systemCopyBuffer = about.enter;
                        copiedKey = about.key;
                        copiedUntil = Time.unscaledTime + 1.5f;
                    }
                    y += LineHigh;
                }
                for (int i = 0; i < subagents.Count; i++)
                    Faint(new Rect(card.x + 10, y + i * LineHigh, card.width - 20, 20), subagents[i], text);
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
        // The list a repo's workspace is picked from, drawn over everything else, at `at` or as near as fits the window. A
        // click on a workspace gives it to the repo; a name typed in the last line makes a new one. `give` is called with
        // the name, `close` when the owner puts the list away. Until one of them is, nothing under the list takes a click.
        // Its last line is not a workspace: `cloud` is called when the owner wants a cloud session brought to this repo.
        public void Workspaces(Rect window, Vector2 at, string repo, string current, List<string> names, GUIStyle text, GUIStyle strong, Action<string> give, Action close, Action cloud)
        {
            float high = TitleHigh + names.Count * ListRow + 34 + ListRow;
            var list = new Rect(Mathf.Clamp(at.x, window.x, window.xMax - ListWide), Mathf.Clamp(at.y, window.y, window.yMax - high), ListWide, high);
            GUI.ModalWindow(0x6c697374, list, id =>
            {
                var e = Event.current;
                var all = new Rect(0, 0, list.width, list.height);
                bool away = e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape || e.type == EventType.MouseDown && !all.Contains(e.mousePosition);
                // Seen before the text field is drawn, which may take the key for itself.
                bool enter = e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter);
                Back(all);
                GUI.Label(new Rect(8, 3, all.width - 40, 22), repo + " · workspace", strong);
                GUI.Label(new Rect(all.width - 22, 3, 16, 22), "×", strong);
                away |= GUI.Button(new Rect(all.width - 30, 0, 30, TitleHigh), GUIContent.none, GUIStyle.none);
                string picked = "";
                for (int i = 0; i < names.Count; i++)
                {
                    var row = new Rect(1, TitleHigh + i * ListRow, all.width - 2, ListRow);
                    if (names[i] == current) Fill(new Rect(10, row.y + 9, 6, 6), Lit);   // the one the repo is in
                    GUI.Label(new Rect(24, row.y + 2, row.width - 30, 20), names[i], text);
                    if (GUI.Button(row, GUIContent.none, GUIStyle.none)) picked = names[i];
                }
                var field = new Rect(8, TitleHigh + names.Count * ListRow + 5, all.width - 64, 22);
                typed = GUI.TextField(field, typed, 24);
                if (typed == "") Faint(new Rect(field.x + 5, field.y + 1, field.width - 6, 20), "New workspace", text);
                bool add = GUI.Button(new Rect(all.width - 50, field.y, 42, 22), "Add");
                if ((enter || add) && typed.Trim() != "") picked = typed;
                var bring = new Rect(1, field.yMax + 6, all.width - 2, ListRow);
                Fill(new Rect(bring.x, bring.y, bring.width, 1), Edge);
                GUI.Label(new Rect(24, bring.y + 2, bring.width - 30, 20), "Bring a cloud session here", text);
                bool toCloud = picked == "" && GUI.Button(bring, GUIContent.none, GUIStyle.none);
                if (picked == "" && !away && !toCloud) return;
                typed = "";
                if (e.type != EventType.Repaint && e.type != EventType.Layout) e.Use();
                if (picked != "") give(picked); else if (toCloud) cloud(); else close();
            }, GUIContent.none, GUIStyle.none);
        }

        // Starts the cloud panel afresh: what is in its box, and which account is chosen.
        public void OpenCloud(string text, int account)
        {
            pasted = text ?? "";
            cloudAccount = account;
        }

        // The panel that makes the command which brings a cloud session to a repo's folder, drawn over everything else as
        // the list of workspaces is. The owner pastes what names the session and picks the account that owns it; `command`
        // gives the command for an id, an account and whether it only downloads. The app runs neither: each has a Copy.
        public void Cloud(Rect window, Vector2 at, string repo, List<string> accounts, Func<string, int, bool, string> command, GUIStyle text, GUIStyle strong, Action close)
        {
            if (wrapped == null) wrapped = new GUIStyle(text) { wordWrap = true, clipping = TextClipping.Clip };
            string id = RosterFormat.CloudId(pasted);
            bool wrong = id == "" && pasted.Trim() != "";
            string open = id == "" ? "" : command(id, cloudAccount, false), download = id == "" ? "" : command(id, cloudAccount, true);
            float openHigh = CodeHigh(CloudWide - 20, open, text), downloadHigh = CodeHigh(CloudWide - 20, download, text);
            float high = TitleHigh + 30 + (wrong ? 36 : 0) + 26 + (id == "" ? 0 : openHigh + downloadHigh + 4 * LineHigh + 24) + 58;
            var panel = new Rect(Mathf.Clamp(at.x, window.x, window.xMax - CloudWide), Mathf.Clamp(at.y, window.y, window.yMax - high), CloudWide, high);
            GUI.ModalWindow(0x636c6f75, panel, _ =>
            {
                var e = Event.current;
                var all = new Rect(0, 0, panel.width, panel.height);
                bool away = e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape || e.type == EventType.MouseDown && !all.Contains(e.mousePosition);
                Back(all);
                GUI.Label(new Rect(8, 3, all.width - 40, 22), repo + " · cloud session", strong);
                GUI.Label(new Rect(all.width - 22, 3, 16, 22), "×", strong);
                away |= GUI.Button(new Rect(all.width - 30, 0, 30, TitleHigh), GUIContent.none, GUIStyle.none);
                float y = TitleHigh + 4;
                var field = new Rect(8, y, all.width - 72, 22);
                pasted = GUI.TextField(field, pasted, 300);
                if (pasted == "") Faint(new Rect(field.x + 5, field.y + 1, field.width - 6, 20), "Session id, its /teleport line, or its link", text);
                if (GUI.Button(new Rect(all.width - 58, y, 50, 22), "Paste")) pasted = GUIUtility.systemCopyBuffer ?? "";
                y += 26;
                if (wrong)
                {
                    Faint(new Rect(10, y, all.width - 20, 34), "That names no cloud session. Give a cse_… or session_… id, the line the session prints for /teleport, or a claude.ai/code link.", wrapped);
                    y += 36;
                }
                // The account that owns the session: the command is run under it.
                float each = Mathf.Min(120, (all.width - 16) / Mathf.Max(1, accounts.Count));
                for (int i = 0; i < accounts.Count; i++)
                {
                    var pick = new Rect(8 + i * each, y, each, 22);
                    if (i == cloudAccount) Fill(new Rect(pick.x + 2, pick.y + 8, 6, 6), Lit);
                    GUI.Label(new Rect(pick.x + 14, pick.y + 1, pick.width - 16, 20), accounts[i], text);
                    if (GUI.Button(pick, GUIContent.none, GUIStyle.none)) cloudAccount = i;
                }
                y += 26;
                if (id != "")
                {
                    GUI.Label(new Rect(10, y, all.width - 20, 20), "Open it here", text);
                    Code(new Rect(10, y + LineHigh + 2, all.width - 20, openHigh), "cloud-open", open, text);
                    Faint(new Rect(10, y + LineHigh + 4 + openHigh, all.width - 20, 20), "Nothing is kept on this Mac until you say something in it.", text);
                    y += openHigh + 2 * LineHigh + 12;
                    GUI.Label(new Rect(10, y, all.width - 20, 20), "Download only", text);
                    Code(new Rect(10, y + LineHigh + 2, all.width - 20, downloadHigh), "cloud-download", download, text);
                    Faint(new Rect(10, y + LineHigh + 4 + downloadHigh, all.width - 20, 20), "One model turn over the whole conversation: it can be large.", text);
                    y += downloadHigh + 2 * LineHigh + 12;
                }
                Faint(new Rect(10, y, all.width - 20, 54), "The folder must have no uncommitted changes: teleport switches it to the session's branch. \"Session not found\" usually means the other account. The Grid runs neither command.", wrapped);
                if (!away) return;
                if (e.type != EventType.Repaint && e.type != EventType.Layout) e.Use();
                close();
            }, GUIContent.none, GUIStyle.none);
        }

        // The letters of a code block: fixed-width, from a font every Mac has, so the app brings none of its own.
        GUIStyle CodeStyle(GUIStyle text)
        {
            if (code == null)
                code = new GUIStyle(text) { font = Font.CreateDynamicFontFromOSFont(new[] { "Menlo", "Monaco", "Courier New" }, 11), fontSize = 11, wordWrap = true, clipping = TextClipping.Clip };
            return code;
        }

        // How high a code block of that width is for that command: its lines, wrapped, clear of the mark in its corner.
        float CodeHigh(float wide, string command, GUIStyle text) =>
            command == "" ? 0 : CodeStyle(text).CalcHeight(new GUIContent(RosterFormat.Shown(command)), wide - 2 * CodePad - CopyRoom) + 2 * CodePad;

        // A command as a code block: a darker plate, fixed-width letters, every step on a line of its own, and in its upper
        // right corner the mark that copies the whole of it: two sheets, one over the other. For a moment after a click the
        // mark is lit and says so.
        void Code(Rect block, string key, string command, GUIStyle text)
        {
            Fill(block, Edge);
            Fill(new Rect(block.x + 1, block.y + 1, block.width - 2, block.height - 2), Well);
            GUI.Label(new Rect(block.x + CodePad, block.y + CodePad, block.width - 2 * CodePad - CopyRoom, block.height - 2 * CodePad), RosterFormat.Shown(command), CodeStyle(text));
            bool copied = copiedKey == key && Time.unscaledTime < copiedUntil;
            var mark = new Rect(block.xMax - 22, block.y + 6, 16, 16);
            var ink = copied ? Lit : Dim;
            Outline(new Rect(mark.x + 5, mark.y + 1, 9, 11), ink);                      // the sheet behind
            Fill(new Rect(mark.x + 2, mark.y + 4, 9, 11), Well);                        // the one in front covers its corner
            Outline(new Rect(mark.x + 2, mark.y + 4, 9, 11), ink);
            if (copied) Faint(new Rect(mark.x - 52, mark.y - 1, 50, 18), "Copied", text);
            if (!GUI.Button(new Rect(mark.x - 3, mark.y - 3, 22, 22), GUIContent.none, GUIStyle.none)) return;
            GUIUtility.systemCopyBuffer = command;
            copiedKey = key;
            copiedUntil = Time.unscaledTime + 1.5f;
        }

        static void Outline(Rect r, Color c)
        {
            Fill(new Rect(r.x, r.y, r.width, 1), c);
            Fill(new Rect(r.x, r.yMax - 1, r.width, 1), c);
            Fill(new Rect(r.x, r.y, 1, r.height), c);
            Fill(new Rect(r.xMax - 1, r.y, 1, r.height), c);
        }

        // How many lines of the card are about getting into the conversation.
        static int Entry(BotCard about) => (about.where != "" ? 1 : 0) + (about.enter != "" ? 1 : 0);

        string copiedKey = "";   // the bot whose command was last copied, and until when the card says so
        float copiedUntil;

        const float ListWide = 236, ListRow = 24, CloudWide = 520, CodePad = 8, CopyRoom = 22;
        string typed = "";   // the name of a new workspace, as far as it has been typed
        string pasted = "";  // what the owner put in the cloud panel's box
        int cloudAccount;    // and the account chosen there

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
