using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TheGrid.Core
{
    // Where a bot stands in its sector. Five places are buildings, one for each kind of work; the rest are open ground.
    public enum Place { ServerTower, ReviewHall, Works, AssemblyBay, Workshop, Plaza, Forecourt, Dock }

    // How a bot holds itself. (Walking and signalling are the view's: they happen while it moves or a message arrives.)
    public enum Stance { Stand, Type, Inspect, Off }

    // What a bot carries over its helmet. Question, Approve and Fault are the ones that need the owner.
    public enum Badge { None, Question, Approve, Fault, Done }

    // One agent session, as the world shows it. State is on the bot: where it stands, how, and its badge.
    [Serializable]
    public sealed class WorldBot
    {
        public string key = "", sessionId = "", name = "", account = "", app = "", state = "", activity = "";
        public Place place;
        public Stance stance;
        public Badge badge;
        public int slot, subagents;       // slot: its number among the bots of its sector at the same place, in key order
        public int colour;                // which colour its light is: its account's number, which the account keeps for good
        public bool needsOwner;
    }

    // One repo's area of the map.
    [Serializable]
    public sealed class WorldSector
    {
        public string repoRoot = "", name = "", workspace = "";
        public int plot;
        public Vector2Int cell;
        public int needOwner, done, running;
        public List<WorldBot> bots = new List<WorldBot>();
        [NonSerialized] public Layout layout = Layout.Of(0);   // how this sector is arranged and lit: follows from its plot
    }

    // What the world view draws: the roster snapshot, arranged as a map. Never changed after Build returns it.
    [Serializable]
    public sealed class WorldModel
    {
        public long generatedAt;
        public List<WorldSector> sectors = new List<WorldSector>();
    }

    public static class World
    {
        public const string NoProject = "No project";

        public static Place PlaceFor(string state, string activity)
        {
            switch (state)
            {
                case "Running":
                    switch (activity)
                    {
                        case Activity.Reading: return Place.ServerTower;
                        case Activity.Editing: return Place.AssemblyBay;
                        case Activity.Testing: return Place.Workshop;
                        case Activity.Reviewing: return Place.ReviewHall;
                        default: return Place.Works;
                    }
                case "Finished": return Place.Plaza;
                case "Offline": return Place.Dock;
                default: return Place.Forecourt;   // every bot that needs the owner stands in the open, at the front
            }
        }

        public static Stance StanceFor(string state, string activity)
        {
            if (state == "Offline") return Stance.Off;
            if (state != "Running") return Stance.Stand;
            switch (activity)
            {
                case Activity.Editing:
                case Activity.Testing: return Stance.Type;
                case Activity.Reading:
                case Activity.Reviewing: return Stance.Inspect;
                default: return Stance.Stand;
            }
        }

        public static Badge BadgeFor(string state)
        {
            switch (state)
            {
                case "Running":
                case "Offline": return Badge.None;
                case "Finished": return Badge.Done;
                case "BlockedOnQuestion": return Badge.Question;
                case "WaitingForPermission":
                case "WaitingOther":
                case "BackgroundBlocked": return Badge.Approve;
                default: return Badge.Fault;   // errored, unknown, or a state this build has never heard of
            }
        }

        // The bots the owner has to go to: one that asked a question, one waiting for a go-ahead, one that has broken down.
        public static bool NeedsOwner(string state)
        {
            var b = BadgeFor(state);
            return b == Badge.Question || b == Badge.Approve || b == Badge.Fault;
        }

        // The sector the view opens on: the first with a bot that needs the owner, else the first with anyone in it. A repo
        // keeps its plot after its sessions are gone, so the first sector may well be empty. Null while there is nobody anywhere.
        public static WorldSector Opening(WorldModel world) =>
            world.sectors.Find(s => s.needOwner > 0) ?? world.sectors.Find(s => s.bots.Count > 0);

        // What a sector says about itself when it is too far off to make out bots: "2 need you · 1 done". Empty when neither applies.
        public static string Summary(WorldSector sector)
        {
            var parts = new List<string>();
            if (sector.needOwner > 0) parts.Add(sector.needOwner + (sector.needOwner == 1 ? " needs you" : " need you"));
            if (sector.done > 0) parts.Add(sector.done + " done");
            return string.Join(" · ", parts);
        }

        // One sector per plot, in plot order; a repo not seen before takes the next plot. A plot whose bots have all gone stays, empty.
        public static WorldModel Build(RosterSnapshot snap, Plots plots)
        {
            foreach (var a in snap.agents) plots.IndexFor(a.repoRoot, out _);
            var world = new WorldModel { generatedAt = snap.generatedAt };
            for (int i = 0; i < plots.repos.Count; i++)
            {
                var root = plots.repos[i] ?? "";
                world.sectors.Add(new WorldSector
                {
                    repoRoot = root, plot = i, cell = Plots.Cell(i), layout = Layout.For(i),
                    name = root == "" ? NoProject : Path.GetFileName(root.TrimEnd('/')),
                });
            }

            foreach (var a in snap.agents)
            {
                var sector = world.sectors[plots.IndexFor(a.repoRoot, out _)];
                if (!string.IsNullOrEmpty(a.repoName)) sector.name = a.repoName;
                sector.workspace = a.workspace;
                var r = new WorldBot
                {
                    key = a.account + "|" + a.sessionId, sessionId = a.sessionId, name = a.name, account = a.account, app = a.app,
                    state = a.state, activity = a.activity,
                    place = PlaceFor(a.state, a.activity), stance = StanceFor(a.state, a.activity), badge = BadgeFor(a.state),
                    subagents = a.subagents.Count, needsOwner = NeedsOwner(a.state), colour = plots.ColourFor(a.account),
                };
                sector.bots.Add(r);
                if (r.needsOwner) sector.needOwner++;
                if (r.badge == Badge.Done) sector.done++;
                if (a.state == "Running") sector.running++;
            }

            // A bot's place among the others follows its key, so it does not shuffle when the roster reorders.
            foreach (var sector in world.sectors)
            {
                var byKey = new List<WorldBot>(sector.bots);
                byKey.Sort((x, y) => string.CompareOrdinal(x.key, y.key));
                var atPlace = new int[8];
                foreach (var b in byKey) b.slot = atPlace[(int)b.place]++;
            }
            return world;
        }
    }
}
