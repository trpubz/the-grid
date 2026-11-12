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
        // anything else (a bot that has gone, two sessions with one name, another account) is dropped without a word.
        public List<Delivery> New(PileSnapshot pile, WorldModel world)
        {
            var found = new List<Delivery>();
            foreach (var row in pile.rows)
            {
                if (row.receivedAt == 0) continue;            // not received yet, as far as the pile knows: look again next time
                bool byKey = settled.Add("key " + row.key);
                bool byArrival = settled.Add("at " + row.receivedAt + " " + row.account + "|" + row.from + "|" + row.to);
                if (!byKey || !byArrival) continue;
                if (row.receivedAt <= openedAt) continue;     // history, not news
                if (!One(world, row.account, row.from, out var from, out var fromAt)) continue;
                if (!One(world, row.account, row.to, out var to, out var toAt)) continue;
                found.Add(new Delivery { key = row.key, fromBot = from, toBot = to, fromAt = fromAt, toAt = toAt, receivedAt = row.receivedAt, way = Roads.Hops(world, fromAt, toAt) ?? new List<Vector2Int>() });
            }
            return found;
        }

        // The one bot of that name on that account. An ended session stays on the map for hours as an offline bot, and a
        // session restarted under the same name is a second bot called the same: among namesakes, only the live ones count.
        static bool One(WorldModel world, string account, string name, out string key, out Vector2Int at)
        {
            key = "";
            at = default;
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
                        key = b.key;
                        at = sector.origin;
                    }
                }
            return live == 1 || (live == 0 && ended == 1);
        }
    }
}
