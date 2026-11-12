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
        public Vector2Int fromCell, toCell;
        public long receivedAt;
    }

    // Picks out of the review pile the messages the world should show arriving. The Grid only shows deliveries: it
    // sends nothing and holds nothing back.
    public sealed class Deliveries
    {
        readonly long openedAt;
        // Rows shown, or found unfit to show: never looked at again. A row is remembered twice: by its key, and by who it
        // was between and when it arrived. The pile can fold a receipt into the row of its send, found later, and then the
        // same message comes back under the send's key.
        readonly HashSet<string> settled = new HashSet<string>();

        public Deliveries(long openedAtMs) => openedAt = openedAtMs;

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
                if (!One(world, row.account, row.from, out var from, out var fromCell)) continue;
                if (!One(world, row.account, row.to, out var to, out var toCell)) continue;
                found.Add(new Delivery { key = row.key, fromBot = from, toBot = to, fromCell = fromCell, toCell = toCell, receivedAt = row.receivedAt });
            }
            return found;
        }

        static bool One(WorldModel world, string account, string name, out string key, out Vector2Int cell)
        {
            key = "";
            cell = default;
            if (account == "" || name == "") return false;
            int n = 0;
            foreach (var sector in world.sectors)
                foreach (var b in sector.bots)
                    if (b.account == account && b.name == name)
                    {
                        n++;
                        key = b.key;
                        cell = sector.cell;
                    }
            return n == 1;
        }
    }
}
