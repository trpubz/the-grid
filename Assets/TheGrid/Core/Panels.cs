using System;
using System.Collections.Generic;
using UnityEngine;

namespace TheGrid.Core
{
    // One line of the "Needs you" panel: a bot that is waiting on the owner, and what it wants.
    [Serializable]
    public sealed class NeedsYouRow
    {
        public string key = "", sector = "", name = "", what = "";
        public Badge badge;
    }

    // What the card of a selected bot says.
    [Serializable]
    public sealed class BotCard
    {
        public string key = "", title = "", who = "", doing = "", since = "", ticket = "", prUrl = "";
        public string workspace = "", repoRoot = "";               // repoRoot: empty for a session with no repo, whose workspace cannot be changed
        public int prNumber;
        public List<string> subagents = new List<string>();        // what each subagent is doing, in the roster's order
    }

    // The words of the two panels drawn over the world. Like the world itself, made from the roster snapshot and nothing else.
    public static class Panels
    {
        // Where a list goes that stands over something else on the map's right side: against the right edge, its foot
        // `margin` above `floor`, as high as it `wanted` when there is that much room under `ceiling`, else as high as
        // there is room for, and what does not fit is scrolled. Nothing, when not even `least` fits. Never wider than the
        // map has room for.
        public static Rect Above(Rect map, float floor, float ceiling, float wide, float wanted, float least, float margin)
        {
            float room = floor - margin - ceiling;
            if (room < least) return default;
            float w = Mathf.Min(wide, map.width - 2 * margin), h = Mathf.Min(wanted, room);
            return new Rect(map.xMax - margin - w, floor - margin - h, w, h);
        }

        // In the snapshot's own order, which is most urgent first.
        public static List<NeedsYouRow> NeedsYou(RosterSnapshot snap)
        {
            var rows = new List<NeedsYouRow>();
            foreach (var a in snap.agents)
                if (World.NeedsOwner(a.state))
                    rows.Add(new NeedsYouRow { key = Key(a), sector = Sector(a), name = a.name, badge = World.BadgeFor(a.state), what = What(a) });
            return rows;
        }

        // Null when no agent has that key.
        public static BotCard Card(RosterSnapshot snap, string key, long nowMs)
        {
            var a = snap.agents.Find(x => Key(x) == key);
            if (a == null) return null;
            return new BotCard
            {
                key = key, title = Title(a), who = a.name + " · " + a.account,
                doing = a.ticket == "" ? Sector(a) : Sector(a) + " · " + a.ticket,
                since = RosterFormat.Elapsed(a.stateSince, nowMs), ticket = a.ticket,
                prUrl = RosterFormat.IsWebLink(a.prUrl) ? a.prUrl : "", prNumber = a.prNumber,   // only a web link is ever opened
                workspace = a.workspace, repoRoot = a.repoRoot, subagents = a.subagents.ConvertAll(s => s.description ?? ""),
            };
        }

        // The card's lines about subagents, when there is room for `lines` of them: one each while they fit, else all but
        // the last line and a count of the rest. With room for a single line and more than one subagent, only the count.
        public static List<string> SubagentLines(BotCard card, int lines)
        {
            var said = new List<string>();
            int n = card.subagents.Count;
            if (n == 0 || lines <= 0) return said;
            if (n > lines && lines == 1)
            {
                said.Add(n + " subagents");
                return said;
            }
            int named = n <= lines ? n : lines - 1;
            for (int i = 0; i < named; i++) said.Add("↳ " + card.subagents[i]);
            if (named < n) said.Add("and " + (n - named) + " more");
            return said;
        }

        static string Key(AgentRow a) => a.account + "|" + a.sessionId;   // as World.Build keys a bot

        static string Sector(AgentRow a) => a.repoName == "" ? World.NoProject : a.repoName;

        static string What(AgentRow a)
        {
            switch (a.state)
            {
                case "BlockedOnQuestion": return "Answer a question";
                case "WaitingForPermission": return "Approve a step";
                case "WaitingOther": return "Waiting";
                case "BackgroundBlocked": return "Blocked in the background";
                case "Errored": return "Failed";
                // Unknown, or a state this build has never heard of: the agent app's own words, or failing those the state's name.
                default: return "Unknown: " + (a.rawStatus == "" ? a.state : a.rawStatus);
            }
        }

        static string Title(AgentRow a)
        {
            switch (a.state)
            {
                case "Finished": return "Done";
                case "Offline": return "Offline";
                case "Running":
                    switch (a.activity)
                    {
                        case Activity.Reading: return "Reading";
                        case Activity.Editing: return "Editing";
                        case Activity.Testing: return "Testing";
                        case Activity.Reviewing: return "Reviewing";
                        default: return "Working";
                    }
                default: return What(a);
            }
        }
    }
}
