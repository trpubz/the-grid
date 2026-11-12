using System.Collections.Generic;
using TheGrid.Core;
using UnityEngine;

namespace TheGrid
{
    // Messages arriving, as the world shows them: a courier rides the lanes from the sender's crossing to the recipient's,
    // and a ring of light pulses under the recipient. Within one sector there is only the pulse. Nothing here decides
    // what is shown; Deliveries does that.
    public sealed class WorldTraffic
    {
        const float TilesPerSecond = 8f, LeastRide = 1.5f, PulseStep = .3f, SenderSignals = .9f;
        const int PulseSteps = ArtNames.PulseSteps;

        sealed class Trip
        {
            public Delivery d;
            public List<Vector2> route = new List<Vector2>();   // pixels
            public float started, rides, length;                 // rides: seconds on the road, 0 within one sector
            public SpriteRenderer bike, pulse;
        }

        readonly Transform root;
        readonly WorldStage stage;
        readonly List<Trip> trips = new List<Trip>();

        public WorldTraffic(Transform parent, WorldStage stage)
        {
            root = new GameObject("Traffic").transform;
            root.SetParent(parent, false);
            this.stage = stage;
        }

        // Returns how long the courier is on the road: the pulse starts when it arrives.
        public float Add(Delivery d, float now)
        {
            var trip = new Trip { d = d, started = now };
            // Along the roads, through the sectors the delivery says. Within one sector there is no way to ride: only the pulse.
            foreach (var corner in Iso.Route(d.way)) trip.route.Add(Iso.Point(corner.x, corner.y));
            for (int i = 1; i < trip.route.Count; i++) trip.length += (trip.route[i] - trip.route[i - 1]).magnitude;
            if (trip.route.Count > 1) trip.rides = Mathf.Max(LeastRide, trip.length / (TilesPerSecond * Iso.TileStep));
            trips.Add(trip);
            // The recipient holds a hand to its helmet until the pulse is over; the sender only as the courier leaves.
            stage.Signal(d.toBot, now + trip.rides + PulseSteps * PulseStep);
            stage.Signal(d.fromBot, now + SenderSignals);
            return trip.rides;
        }

        // Moves couriers and pulses on. Returns true while there is anything to move: the map has to be drawn again.
        public bool Tick(float now)
        {
            if (trips.Count == 0) return false;
            for (int i = trips.Count - 1; i >= 0; i--)
            {
                var t = trips[i];
                float since = now - t.started;
                if (since < t.rides)
                {
                    Ride(t, since / t.rides);
                    continue;
                }
                if (t.bike != null)
                {
                    WorldStage.Kill(t.bike.gameObject);
                    t.bike = null;
                }
                int step = (int)((since - t.rides) / PulseStep);
                // The pulse is under the recipient where it stands now. If it has gone, there is nothing to pulse under.
                if (step >= PulseSteps || !stage.TryGetBot(t.d.toBot, out var at))
                {
                    if (t.pulse != null) WorldStage.Kill(t.pulse.gameObject);
                    trips.RemoveAt(i);
                    continue;
                }
                if (t.pulse == null) t.pulse = New("pulse");
                t.pulse.sprite = WorldStage.Art(ArtNames.Pulse(step));
                // On the ground, under the bot's feet: over the floor and under the bot. A bot inside a building is drawn
                // forward of where it stands, so that the room is not drawn over it, and its pulse comes forward with it.
                t.pulse.transform.localPosition = new Vector3(at.x, at.y, -.5f - stage.Sink(t.d.toBot));
            }
            return true;
        }

        void Ride(Trip t, float part)
        {
            float gone = part * t.length;
            for (int i = 1; i < t.route.Count; i++)
            {
                Vector2 a = t.route[i - 1], b = t.route[i];
                float leg = (b - a).magnitude;
                if (gone > leg && i < t.route.Count - 1)
                {
                    gone -= leg;
                    continue;
                }
                var p = Vector2.Lerp(a, b, leg <= 0 ? 1 : Mathf.Clamp01(gone / leg));
                if (t.bike == null)
                {
                    t.bike = New("courier");
                    // Over everything, buildings included. A lane runs behind whatever sector stands in front of it, and a
                    // courier drawn in its true place would be out of sight for most of most rides.
                    t.bike.sortingOrder = 5;
                }
                // It faces the way it rides, and carries its sender's colours.
                string facing = b.x >= a.x ? (b.y < a.y ? "se" : "ne") : (b.y >= a.y ? "nw" : "sw");
                t.bike.sprite = stage.BotArt(t.d.fromBot, ArtNames.Bike(facing));
                t.bike.transform.localPosition = new Vector3(Mathf.Round(p.x), Mathf.Round(p.y), -1);
                return;
            }
        }

        SpriteRenderer New(string name)
        {
            var sr = new GameObject(name).AddComponent<SpriteRenderer>();
            sr.transform.SetParent(root, false);
            sr.spriteSortPoint = SpriteSortPoint.Pivot;
            return sr;
        }
    }
}
