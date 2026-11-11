using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TheGrid.Core
{
    // Five buildings for kinds of work, five for riders that have stopped. See docs/roster-snapshot.md.
    public enum Building { Garage, Library, Lab, ReviewHall, Workshop, Booth, Gate, Yard, Lot, Depot }

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
        public bool needsOwner;
        // For the first sprite set, until the world stops drawing it.
        public Building building;
        public int buildingSlot;
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

        public static Building BuildingFor(string state, string activity)
        {
            switch (state)
            {
                case "Running":
                    switch (activity)
                    {
                        case Activity.Editing: return Building.Garage;
                        case Activity.Reading: return Building.Library;
                        case Activity.Testing: return Building.Lab;
                        case Activity.Reviewing: return Building.ReviewHall;
                        default: return Building.Workshop;
                    }
                case "BlockedOnQuestion": return Building.Booth;
                case "WaitingForPermission":
                case "WaitingOther":
                case "BackgroundBlocked": return Building.Gate;
                case "Finished": return Building.Lot;
                case "Offline": return Building.Depot;
                default: return Building.Yard;   // errored, unknown, or a state this build has never heard of
            }
        }

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

        // What a sector says about itself when it is too far off to make out bots: "2 need you · 1 done". Empty when neither applies.
        public static string Summary(WorldSector city)
        {
            var parts = new List<string>();
            if (city.needOwner > 0) parts.Add(city.needOwner + (city.needOwner == 1 ? " needs you" : " need you"));
            if (city.done > 0) parts.Add(city.done + " done");
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
                    repoRoot = root, plot = i, cell = Plots.Cell(i),
                    name = root == "" ? NoProject : Path.GetFileName(root.TrimEnd('/')),
                });
            }

            foreach (var a in snap.agents)
            {
                var city = world.sectors[plots.IndexFor(a.repoRoot, out _)];
                if (!string.IsNullOrEmpty(a.repoName)) city.name = a.repoName;
                city.workspace = a.workspace;
                var r = new WorldBot
                {
                    key = a.account + "|" + a.sessionId, sessionId = a.sessionId, name = a.name, account = a.account, app = a.app,
                    state = a.state, activity = a.activity, building = BuildingFor(a.state, a.activity),
                    place = PlaceFor(a.state, a.activity), stance = StanceFor(a.state, a.activity), badge = BadgeFor(a.state),
                    subagents = a.subagents.Count, needsOwner = NeedsOwner(a.state),
                };
                city.bots.Add(r);
                if (r.needsOwner) city.needOwner++;
                if (r.badge == Badge.Done) city.done++;
                if (a.state == "Running") city.running++;
            }

            // A bot's place among the others follows its key, so it does not shuffle when the roster reorders.
            foreach (var city in world.sectors)
            {
                var byKey = new List<WorldBot>(city.bots);
                byKey.Sort((x, y) => string.CompareOrdinal(x.key, y.key));
                var atBuilding = new int[10];
                var atPlace = new int[8];
                foreach (var r in byKey)
                {
                    r.buildingSlot = atBuilding[(int)r.building]++;
                    r.slot = atPlace[(int)r.place]++;
                }
            }
            return world;
        }
    }
}
