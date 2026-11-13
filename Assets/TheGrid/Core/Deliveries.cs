using System;
using System.Collections.Generic;
using UnityEngine;

namespace TheGrid.Core
{
    // One message that reached its recipient while the world was open: who sent it, who got it, and where each of them is.
    [Serializable]
    public sealed class Delivery
    {
        public string key = "", fromBot = "", toBot = "";
        public Vector2Int fromAt, toAt;   // the origins of the sender's sector and the recipient's
        public List<Vector2> ride = new List<Vector2>();   // the way its courier rides out, in art pixels (Iso.Ride)
        public long receivedAt;
        public List<Vector2Int> way = new List<Vector2Int>();   // the sectors its courier passes, by their origins, first to last; one when both bots are in the same sector
    }

    // Picks out of the review pile the messages the world should show arriving. The Grid only shows deliveries: it
    // sends nothing and holds nothing back.
    public sealed class Deliveries
    {
        long openedAt;
        // Rows shown, or found unfit to show: never looked at again. A row is remembered twice: by its key, and by who it
        // was between and when it arrived. The pile can fold a receipt into the row of its send, found later, and then the
        // same message comes back under the send's key.
        readonly HashSet<string> settled = new HashSet<string>();

        public Deliveries(long openedAtMs) => openedAt = openedAtMs;

        // The world was closed for a while and is open again. What arrived meanwhile is old mail, not mail arriving now:
        // only what is received from this moment on is shown.
        public void Reopen(long nowMs) => openedAt = nowMs;

        // The rows that were received after the world opened and have not been returned before. Each row at most once, ever.
        // A row is shown only when its sender and its recipient each match exactly one bot, by account and session name;
        // anything else (a bot that has gone, two sessions with one name, another account) is dropped. `said` gets a line
        // for each message that arrived: its courier, or why it has none. That is for the log, so that "I saw no courier"
        // can be answered from it.
        // A courier leaves a trail of light along the way it rode, so that a ride nobody was watching can still be read. It
        // fades in steps and is gone after two minutes. Trail: which step it is at, so many seconds after the courier
        // arrived: 0 the brightest, also while the courier is still riding; -1 once it is gone.
        public const int TrailSteps = 4;
        public const float TrailSeconds = 120f;
        public static int Trail(float sinceArrival) => sinceArrival >= TrailSeconds ? -1 : Mathf.Max(0, (int)(sinceArrival / (TrailSeconds / TrailSteps)));

        public List<Delivery> New(PileSnapshot pile, WorldModel world, List<string> said = null)
        {
            var found = new List<Delivery>();
            foreach (var row in pile.rows)
            {
                if (row.receivedAt == 0) continue;            // not received yet, as far as the pile knows: look again next time
                bool byKey = settled.Add("key " + row.key);
                bool byArrival = settled.Add("at " + row.receivedAt + " " + row.account + "|" + row.from + "|" + row.to);
                if (!byKey || !byArrival) continue;
                if (row.receivedAt <= openedAt) continue;     // history, not news
                string who = row.from + " to " + row.to;
                bool sender = One(world, row.account, row.from, out var from, out var fromIn), recipient = One(world, row.account, row.to, out var to, out var toIn);
                if (!sender || !recipient)
                {
                    said?.Add("no courier: " + who + ": the " + (sender ? "recipient" : "sender") + " is not one bot on the map");
                    continue;
                }
                Vector2Int fromAt = fromIn.origin, toAt = toIn.origin;
                var way = Roads.Hops(world, fromAt, toAt) ?? new List<Vector2Int>();
                said?.Add("courier: " + who + (way.Count > 1 ? ", through " + way.Count + " sectors" : fromAt == toAt ? ", within one sector" : ", across open ground"));
                found.Add(new Delivery { key = row.key, fromBot = from.key, toBot = to.key, fromAt = fromAt, toAt = toAt, receivedAt = row.receivedAt, way = way,
                    ride = Iso.Ride(fromAt, fromIn.layout, from.place, from.slot, toAt, toIn.layout, to.place, to.slot, way) });
            }
            return found;
        }

        // The one bot of that name on that account. An ended session stays on the map for hours as an offline bot, and a
        // session restarted under the same name is a second bot called the same: among namesakes, only the live ones count.
        static bool One(WorldModel world, string account, string name, out WorldBot bot, out WorldSector at)
        {
            bot = null;
            at = null;
            if (account == "" || name == "") return false;
            int live = 0, ended = 0;
            foreach (var sector in world.sectors)
                foreach (var b in sector.bots)
                {
                    if (b.account != account || b.name != name) continue;
                    bool isLive = b.state != "Offline";
                    if (isLive) live++; else ended++;
                    if (isLive || live == 0)
                    {
                        bot = b;
                        at = sector;
                    }
                }
            return live == 1 || (live == 0 && ended == 1);
        }
    }
}
