using System.Collections.Generic;
using TheGrid.Core;
using UnityEngine;

namespace TheGrid
{
    // Messages arriving, as the world shows them: a courier appears beside the sender, rides to the recipient (by the lanes
    // and roads when they are in different sectors) and leaves a trail of light behind it; a ring of light then pulses
    // under the recipient, and the trail fades over two minutes. Nothing here decides what is shown; Deliveries does that.
    public sealed class WorldTraffic
    {
        const float Leaves = .6f, PulseStep = .3f, SenderSignals = .9f;   // Leaves: how long a courier stands beside its sender before it rides
        const int PulseSteps = ArtNames.PulseSteps;
        // The trail, brightest first: the violet of the pulse and of its glow (tools/art/kit.py: violetw, violet, vg1, vg0).
        static readonly Color32[] Fades = { new Color32(0xd9, 0xc8, 0xff, 255), new Color32(0xa0, 0x70, 0xff, 255), new Color32(0x66, 0x57, 0xb8, 255), new Color32(0x4b, 0x46, 0x96, 255) };
        static Material lit;

        sealed class Trip
        {
            public Delivery d;
            public List<Vector2> route = new List<Vector2>();   // pixels
            public float started, rides, length;                 // rides: seconds until it arrives, standing beside the sender included
            public SpriteRenderer bike, pulse;
            public LineRenderer trail;
            public int fade = -2;                                // the step of its fading the trail is drawn at; -1 is gone
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

        // Returns how long the courier takes to arrive: the pulse starts when it does.
        public float Add(Delivery d, float now)
        {
            // From where the sender stands to where the recipient stands. One of them not drawn: nothing to ride between.
            if (!stage.TryGetBot(d.fromBot, out var from) || !stage.TryGetBot(d.toBot, out var to)) return 0f;
            var trip = new Trip { d = d, started = now, route = Iso.Ride(from, d.way, to) };
            for (int i = 1; i < trip.route.Count; i++) trip.length += (trip.route[i] - trip.route[i - 1]).magnitude;
            trip.rides = Leaves + Iso.RideSeconds(trip.length);
            trips.Add(trip);
            // The recipient holds a hand to its helmet until the pulse is over; the sender only as the courier leaves.
            stage.Signal(d.toBot, now + trip.rides + PulseSteps * PulseStep);
            stage.Signal(d.fromBot, now + SenderSignals);
            return trip.rides;
        }

        // Moves couriers and pulses on, and fades trails. Returns true when the map has to be drawn again: all the while a
        // courier rides or a pulse runs, and after that only at each step of a trail's fading.
        public bool Tick(float now)
        {
            bool changed = false;
            for (int i = trips.Count - 1; i >= 0; i--)
            {
                var t = trips[i];
                float since = now - t.started;
                if (since < t.rides)
                {
                    Ride(t, Mathf.Clamp01((since - Leaves) / (t.rides - Leaves)));
                    changed = true;
                    continue;
                }
                if (t.bike != null)
                {
                    WorldStage.Kill(t.bike.gameObject);
                    t.bike = null;
                    changed = true;
                }
                // The whole way it came, also for a courier that was never seen riding because the app stood still meanwhile.
                if (t.fade == -2) Trail(t, t.route.Count - 1, t.route[t.route.Count - 1]);
                int step = (int)((since - t.rides) / PulseStep);
                // The pulse is under the recipient where it stands now. If it has gone, there is nothing to pulse under.
                if (step < PulseSteps && stage.TryGetBot(t.d.toBot, out var at))
                {
                    if (t.pulse == null) t.pulse = New("pulse");
                    t.pulse.sprite = WorldStage.Art(ArtNames.Pulse(step));
                    // On the ground, under the bot's feet: over the floor and under the bot. A bot inside a building is drawn
                    // forward of where it stands, so that the room is not drawn over it, and its pulse comes forward with it.
                    t.pulse.transform.localPosition = new Vector3(at.x, at.y, -.5f - stage.Sink(t.d.toBot));
                    changed = true;
                }
                else if (t.pulse != null)
                {
                    WorldStage.Kill(t.pulse.gameObject);
                    t.pulse = null;
                    changed = true;
                }
                int fade = Deliveries.Trail(since - t.rides);
                if (fade == t.fade) continue;
                changed = true;
                t.fade = fade;
                if (fade >= 0) t.trail.startColor = t.trail.endColor = Fades[fade];
                else
                {
                    // Two minutes on: the trail is gone, and with it the last of this message.
                    if (t.trail != null) WorldStage.Kill(t.trail.gameObject);
                    if (t.pulse != null) WorldStage.Kill(t.pulse.gameObject);
                    trips.RemoveAt(i);
                }
            }
            return changed;
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
                Trail(t, i, p);
                return;
            }
        }

        // Draws a trip's trail: along the first `corners` points of its way, and on to `end`.
        void Trail(Trip t, int corners, Vector2 end)
        {
            if (t.trail == null)
            {
                if (lit == null) lit = new Material(Shader.Find("Sprites/Default"));
                t.trail = new GameObject("trail").AddComponent<LineRenderer>();
                t.trail.transform.SetParent(root, false);
                t.trail.useWorldSpace = false;
                t.trail.sharedMaterial = lit;
                t.trail.alignment = LineAlignment.TransformZ;
                // Wide enough to be a line still, far out, where four art pixels are one of the screen's.
                t.trail.widthMultiplier = 4f;
                t.trail.sortingOrder = 4;   // over the map, as the courier is, and under the courier
                t.trail.startColor = t.trail.endColor = Fades[0];
            }
            t.trail.positionCount = corners + 1;
            for (int i = 0; i < corners; i++) t.trail.SetPosition(i, new Vector3(t.route[i].x, t.route[i].y, -1));
            t.trail.SetPosition(corners, new Vector3(end.x, end.y, -1));
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
