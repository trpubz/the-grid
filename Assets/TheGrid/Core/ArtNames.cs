using System.Collections.Generic;

namespace TheGrid.Core
{
    // The name of every picture the game can ask for, in one place. The world asks by these, and a test holds the whole
    // list against the manifest the art script writes, so a picture that is renamed or dropped is found before the world
    // comes to draw it.
    public static class ArtNames
    {
        public static readonly string[] Structures = { "b-server-tower", "b-review-hall", "b-relay", "b-works", "b-depot", "fountain", "b-test-workshop" };   // in the order of Structure
        // A building a bot works in has a second picture: opened, its near walls cut away, as the workshop always is. In the
        // order of Structure; "" where there is none.
        public static readonly string[] Opened = { "b-server-tower-open", "b-review-hall-open", "", "b-works-open", "", "", "" };
        public static readonly string[] Badges = { "", "badge-question", "badge-approve", "badge-fault", "badge-done" };   // in the order of Badge; none for Badge.None
        public static readonly string[] Stances = { "stand", "type", "inspect", "off" };                                    // in the order of Stance
        public static readonly string[] Facings = { "se", "ne", "nw", "sw" };
        public static readonly string[] Blocks = { "block-a", "block-b", "block-c" };
        public static readonly string[] Crates = { "crate-draft", "crate-review", "crate-ready" };   // in the order of PullState
        public const string WalkA = "walk-a", WalkB = "walk-b", Signal = "signal";
        public const string SmallBot = "bot-small";
        public const string Paving = "tile-paving", PavingJointA = "tile-paving-joint-a", PavingJointB = "tile-paving-joint-b";
        public const string Lane = "tile-lane", ChevronA = "tile-lane-chevron-a", ChevronB = "tile-lane-chevron-b", Crossing = "tile-crossing-a";
        public const string Void = "tile-void", LineA = "line-a", LineB = "line-b";
        public const int PulseSteps = 3;

        public static string Bot(string pose, string facing) => "bot-" + pose + "-" + facing;
        public static string Bike(string facing) => "bike-" + facing;
        public static string Pulse(int step) => "pulse-" + step;

        public static IEnumerable<string> All()
        {
            foreach (var s in Structures) yield return s;
            foreach (var s in Opened) if (s != "") yield return s;
            foreach (var b in Badges) if (b != "") yield return b;
            foreach (var b in Blocks) yield return b;
            foreach (var c in Crates) yield return c;
            foreach (var f in Facings)
            {
                foreach (var pose in Stances) yield return Bot(pose, f);
                yield return Bot(WalkA, f);
                yield return Bot(WalkB, f);
                yield return Bot(Signal, f);
                yield return Bike(f);
            }
            yield return SmallBot;
            for (int i = 0; i < PulseSteps; i++) yield return Pulse(i);
            foreach (var t in new[] { Paving, PavingJointA, PavingJointB, Lane, ChevronA, ChevronB, Crossing, Void, LineA, LineB }) yield return t;
        }
    }
}
