using System.Collections.Generic;
using UnityEngine;

namespace TheGrid.Core
{
    // One beat of a job. at: where the bot is, from its own place, in tiles (column, row). pose and facing: the picture
    // it is drawn with. job: what it is doing, in words.
    public struct JobFrame { public Vector2 at; public string pose, facing, job; }

    // What a bot at work inside a building is doing. Each place inside a building has two jobs, each a short round of
    // beats: a bot does the one for a while, then the other. Which, and where in its round, follows from the bot and the
    // beat alone, so the same moment always gives the same picture, and two bots in one building are out of step.
    // Nothing moves between beats: a bot that carries steps from beat to beat. The beat is the one a busy building's
    // lights pulse on, so the map is not drawn any more often for it.
    public static class Jobs
    {
        public const float BeatSeconds = .7f;
        public const int Beats = 24;   // a bot keeps to one job for so many beats, about seventeen seconds

        const string Stand = "stand", Type = "type", Inspect = "inspect", Back = "ne", Left = "nw", Right = "se", Front = "sw";

        static JobFrame F(string job, float column, float row, string pose, string facing) => new JobFrame { job = job, at = new Vector2(column, row), pose = pose, facing = facing };

        // An arm out to a button or a socket, and drawn back.
        static JobFrame[] Press(string job, string facing, float column = 0, float row = 0) =>
            new[] { F(job, column, row, ArtNames.PressA, facing), F(job, column, row, ArtNames.PressB, facing) };

        // An arm up to a lever, and down with it.
        static JobFrame[] Pull(string facing, float column = 0, float row = 0) =>
            new[] { F("pulls a lever", column, row, ArtNames.PullA, facing), F("pulls a lever", column, row, ArtNames.PullB, facing) };

        // Its disc held up to what it reads, and now and then put back.
        static JobFrame[] Disc(string facing, float column = 0, float row = 0) =>
            new[] { F("holds its disc up", column, row, Inspect, facing), F("holds its disc up", column, row, Inspect, facing), F("holds its disc up", column, row, Inspect, facing), F("holds its disc up", column, row, Stand, facing) };

        // Something taken up at one place, carried to another in two strides, put down, and the walk back: facing the
        // furniture at each end, and the way it goes between.
        static JobFrame[] Carry(string job, Vector2 from, Vector2 to, string facing)
        {
            Vector2 d = to - from, near = from + d / 3, far = from + d * 2 / 3;
            bool across = Mathf.Abs(d.x) >= Mathf.Abs(d.y);
            string there = across ? (d.x > 0 ? Right : Left) : (d.y > 0 ? Front : Back), back = across ? (d.x > 0 ? Left : Right) : (d.y > 0 ? Back : Front);
            return new[]
            {
                F(job, from.x, from.y, ArtNames.PressA, facing), F(job, near.x, near.y, ArtNames.CarryA, there), F(job, far.x, far.y, ArtNames.CarryB, there),
                F(job, to.x, to.y, ArtNames.PressA, facing), F(job, to.x, to.y, ArtNames.PressB, facing),
                F(job, far.x, far.y, ArtNames.WalkA, back), F(job, near.x, near.y, ArtNames.WalkB, back), F(job, from.x, from.y, ArtNames.PressB, facing),
            };
        }

        static Vector2 V(float column, float row) => new Vector2(column, row);

        // The jobs of each place inside each building, in the order of its slots (Layout). Where a bot stands for a job is
        // told from its own place, so these go with the furniture the art script puts there (tools/art/buildings.py).
        static readonly Dictionary<Place, JobFrame[][][]> At = new Dictionary<Place, JobFrame[][][]>
        {
            // The server tower: racks along the back wall on the ground; a rack at the right of each floor above.
            [Place.ServerTower] = new[]
            {
                new[] { Press("plugs in a cable", Back, .2f, -.2f), Disc(Back, .2f, -.2f) },
                new[] { Disc(Back), Carry("swaps a drive", V(-.25f, 0), V(.2f, 0), Right) },
                new[] { Disc(Back), Press("plugs in a cable", Back) },
                new[] { Carry("swaps a drive", V(-.35f, -.2f), V(.15f, -.2f), Back), Disc(Back, -.1f, -.2f) },
                new[] { Press("plugs in a cable", Right), Disc(Back) },
                new[] { Press("plugs in a cable", Right), Disc(Back) },
            },
            // The review hall: the console of buttons on the ground; the two screens on the wall upstairs.
            [Place.ReviewHall] = new[]
            {
                new[] { Press("pushes buttons", Back, 0, -.1f), Disc(Back, 0, -.1f) },
                new[] { Disc(Back), Press("reads a screen", Back) },
                new[] { Disc(Back, 0, -.1f), Press("pushes buttons", Back, 0, -.1f) },
                new[] { Press("reads a screen", Back), Disc(Back) },
                new[] { Press("pushes buttons", Back, 0, -.1f), Disc(Back, 0, -.1f) },
                new[] { Disc(Back), Press("reads a screen", Back) },
            },
            // The works: two reactors along the back wall, the panel of levers on the left one.
            [Place.Works] = new[]
            {
                new[] { Carry("moves a core", V(-.35f, -.1f), V(.25f, -.1f), Back), Press("tends a reactor", Back, -.35f, -.1f) },
                new[] { Pull(Left, -.1f, 0), Disc(Left, -.1f, 0) },
                new[] { Carry("moves a core", V(0, -.3f), V(0, .2f), Back), Disc(Back, 0, -.3f) },
                new[] { Pull(Left, -.1f, .15f), Press("tends a reactor", Back, .2f, -.1f) },
                new[] { Disc(Back), Carry("moves a core", V(-.3f, 0), V(.3f, 0), Back) },
                new[] { Carry("moves a core", V(0, -.3f), V(0, .2f), Back), Disc(Back, 0, -.3f) },
            },
            // The test workshop: the terminal under the wall screen, the bench to its right.
            [Place.Workshop] = new[]
            {
                new[] { new[] { F("types", 0, 0, Type, Back), F("types", 0, 0, Type, Back), F("types", 0, 0, Type, Back), F("types", 0, 0, Stand, Back) }, Press("pushes buttons", Back) },
                new[] { Pull(Back, .1f, -.55f), Disc(Back, .1f, -.55f) },
                new[] { Carry("moves a crate", V(-.35f, 0), V(.3f, 0), Back), Disc(Back, -.35f, 0) },
                new[] { Disc(Left), Carry("moves a crate", V(0, -.35f), V(0, 0), Left) },
            },
        };

        static readonly List<JobFrame[]> None = new List<JobFrame[]>();

        // The jobs of a place inside a building; none for any other place.
        public static IReadOnlyList<JobFrame[]> Of(Place place, int slot) =>
            slot >= 0 && At.TryGetValue(place, out var slots) && slot < slots.Length ? slots[slot] : (IReadOnlyList<JobFrame[]>)None;

        // What the bot with this key, at that place, is doing at this beat. False for a bot that is at no job.
        public static bool TryFrame(Place place, int slot, string bot, int beat, out JobFrame frame)
        {
            frame = default;
            var jobs = Of(place, slot);
            if (jobs.Count == 0) return false;
            // A number of the bot's own, the same on every run: it sets the bot's step apart from its neighbours'.
            uint own = 2166136261;
            foreach (char c in bot ?? "") own = (own ^ c) * 16777619;
            long at = (long)beat + own % 4093;
            var job = jobs[(int)(at / Beats % jobs.Count)];
            frame = job[(int)(at % job.Length)];
            return true;
        }
    }
}
