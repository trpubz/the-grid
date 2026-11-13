using System.Collections.Generic;
using TheGrid.Core;
using UnityEngine;

namespace TheGrid
{
    // Messages arriving, as the world shows them. A courier appears at the front of the sender's building, rides to the
    // front of the recipient's (down the middle of the lanes and roads when they are in different sectors) and rides
    // back the way it came. Behind it stands a ribbon of light as tall as the bike, see-through, in the sender's colour:
    // it stays for two minutes after the courier is back, fading, so that a ride nobody watched can still be read. As
    // the courier arrives, a ring of light pulses under the recipient. Bike and ribbon are drawn in their true place: a
    // road behind a building is behind it. Nothing here decides what is shown; Deliveries does that.
    public sealed class WorldTraffic
    {
        const float Leaves = .6f, PulseStep = .3f, SenderSignals = .9f;   // Leaves: how long a courier stands before it rides
        const float SliceWide = 4f, RibbonHigh = 26f;                     // art pixels: the ribbon's slices, and the bike's height
        const int PulseSteps = ArtNames.PulseSteps;
        // How much of what is behind it the ribbon hides, at each step of its fading (Deliveries.Trail).
        static readonly byte[] Fades = { 128, 96, 64, 36 };
        static Sprite white;   // one pixel, standing on its lower edge: every slice of every ribbon

        sealed class Trip
        {
            public Delivery d;
            public List<Vector2> route = new List<Vector2>();   // pixels, the way out
            public float started, oneWay, length;                // oneWay: seconds on the road, each way
            public SpriteRenderer bike, pulse;
            public List<Iso.RibbonSlice> cut;                    // the ribbon, in slices, in the order the courier lays them
            public readonly List<SpriteRenderer> ribbon = new List<SpriteRenderer>();   // those laid so far
            public Transform holder;
            public Color32 light;
            public int fade = -2;                                // the step of its fading the ribbon is drawn at; -1 is gone
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

        // Returns how long the courier takes to reach the recipient: the pulse starts when it does.
        public float Add(Delivery d, float now)
        {
            var trip = new Trip { d = d, started = now, route = d.ride, light = stage.Light(d.fromBot) };
            for (int i = 1; i < trip.route.Count; i++) trip.length += (trip.route[i] - trip.route[i - 1]).magnitude;
            trip.oneWay = Iso.RideSeconds(trip.length);
            trip.cut = Iso.Ribbon(trip.route, SliceWide);
            trip.holder = new GameObject("ribbon").transform;
            trip.holder.SetParent(root, false);
            trips.Add(trip);
            // The recipient holds a hand to its helmet until the pulse is over; the sender only as the courier leaves.
            stage.Signal(d.toBot, now + Leaves + trip.oneWay + PulseSteps * PulseStep);
            stage.Signal(d.fromBot, now + SenderSignals);
            return Leaves + trip.oneWay;
        }

        // Moves couriers and pulses on, and fades ribbons. Returns true when the map has to be drawn again: all the while a
        // courier rides or a pulse runs, and after that only at each step of a ribbon's fading.
        public bool Tick(float now)
        {
            bool changed = false;
            for (int i = trips.Count - 1; i >= 0; i--)
            {
                var t = trips[i];
                float since = now - t.started, there = Leaves + t.oneWay, home = there + t.oneWay;
                if (since < home)
                {
                    // Out, laying the ribbon; then back along it.
                    bool back = since >= there;
                    float gone = t.length * (back ? 1f - (since - there) / t.oneWay : Mathf.Clamp01((since - Leaves) / t.oneWay));
                    Ride(t, gone, back);
                    Lay(t, back ? t.length : gone);
                    changed = true;
                }
                else if (t.bike != null)
                {
                    WorldStage.Kill(t.bike.gameObject);
                    t.bike = null;
                    changed = true;
                }
                // The pulse is under the recipient where it stands now. If it has gone, there is nothing to pulse under.
                int step = since < there ? -1 : (int)((since - there) / PulseStep);
                if (step >= 0 && step < PulseSteps && stage.TryGetBot(t.d.toBot, out var at))
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
                if (since < home) continue;
                // Back where it started: the ribbon stays, all of it, also if nobody saw the ride because the app stood still.
                Lay(t, t.length);
                int fade = Deliveries.Trail(since - home);
                if (fade == t.fade) continue;
                changed = true;
                t.fade = fade;
                if (fade >= 0)
                    foreach (var slice in t.ribbon) slice.color = new Color32(t.light.r, t.light.g, t.light.b, Fades[fade]);
                else
                {
                    // Two minutes on: the ribbon is gone, and with it the last of this message.
                    WorldStage.Kill(t.holder.gameObject);
                    trips.RemoveAt(i);
                }
            }
            return changed;
        }

        // Puts the courier so many pixels along its way, facing the way it rides.
        void Ride(Trip t, float gone, bool back)
        {
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
                if (t.bike == null) t.bike = New("courier");
                var ahead = back ? a - b : b - a;
                // It carries its sender's colours.
                string facing = ahead.x >= 0 ? (ahead.y < 0 ? "se" : "ne") : (ahead.y >= 0 ? "nw" : "sw");
                t.bike.sprite = stage.BotArt(t.d.fromBot, ArtNames.Bike(facing));
                // In its true place, as a bot is: before a building it rides in front of, behind one it rides behind.
                t.bike.transform.localPosition = new Vector3(Mathf.Round(p.x), Mathf.Round(p.y), -1);
                return;
            }
        }

        // Stands up the slices of the ribbon as far as the courier has ridden.
        void Lay(Trip t, float gone)
        {
            if (white == null) white = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), new Vector2(.5f, 0f), 1f);
            while (t.ribbon.Count < t.cut.Count && t.cut[t.ribbon.Count].at <= gone)
            {
                var cut = t.cut[t.ribbon.Count];
                var slice = New("slice");
                slice.transform.SetParent(t.holder, false);
                slice.sprite = white;
                slice.color = new Color32(t.light.r, t.light.g, t.light.b, Fades[0]);
                // Each in its own place among the buildings, just behind a bot standing where it stands.
                slice.transform.localPosition = new Vector3(Mathf.Round(cut.x), Mathf.Round(cut.y), -.9f);
                slice.transform.localScale = new Vector3(cut.wide, RibbonHigh + cut.rise, 1);
                t.ribbon.Add(slice);
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
