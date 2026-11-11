using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TheGrid.Core
{
    // Five buildings for kinds of work, five for riders that have stopped. See docs/roster-snapshot.md.
    public enum Building { Garage, Library, Lab, ReviewHall, Workshop, Booth, Gate, Yard, Lot, Depot }

    [Serializable]
    public sealed class WorldRider
    {
        public string key = "", sessionId = "", name = "", account = "", app = "", state = "", activity = "";
        public Building building;
        public int slot, subagents;
        public bool needsOwner;
    }

    [Serializable]
    public sealed class WorldCity
    {
        public string repoRoot = "", name = "", workspace = "";
        public int plot;
        public Vector2Int cell;
        public int needOwner, done, running;
        public List<WorldRider> riders = new List<WorldRider>();
    }

    // What the world view draws: the roster snapshot, arranged as a map. Never changed after Build returns it.
    [Serializable]
    public sealed class WorldModel
    {
        public long generatedAt;
        public List<WorldCity> cities = new List<WorldCity>();
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

        // The riders the owner has to go to: asked a question, waiting at the gate, or broken down.
        public static bool NeedsOwner(string state)
        {
            var b = BuildingFor(state, "");
            return b == Building.Booth || b == Building.Gate || b == Building.Yard;
        }

        // One city per plot, in plot order; a repo not seen before takes the next plot. A plot whose riders have all gone stays, empty.
        public static WorldModel Build(RosterSnapshot snap, Plots plots)
        {
            foreach (var a in snap.agents) plots.IndexFor(a.repoRoot, out _);
            var world = new WorldModel { generatedAt = snap.generatedAt };
            for (int i = 0; i < plots.repos.Count; i++)
            {
                var root = plots.repos[i] ?? "";
                world.cities.Add(new WorldCity
                {
                    repoRoot = root, plot = i, cell = Plots.Cell(i),
                    name = root == "" ? NoProject : Path.GetFileName(root.TrimEnd('/')),
                });
            }

            foreach (var a in snap.agents)
            {
                var city = world.cities[plots.IndexFor(a.repoRoot, out _)];
                if (!string.IsNullOrEmpty(a.repoName)) city.name = a.repoName;
                city.workspace = a.workspace;
                var r = new WorldRider
                {
                    key = a.account + "|" + a.sessionId, sessionId = a.sessionId, name = a.name, account = a.account, app = a.app,
                    state = a.state, activity = a.activity, building = BuildingFor(a.state, a.activity),
                    subagents = a.subagents.Count, needsOwner = NeedsOwner(a.state),
                };
                city.riders.Add(r);
                if (r.needsOwner) city.needOwner++;
                if (r.building == Building.Lot) city.done++;
                if (a.state == "Running") city.running++;
            }

            // A rider's place at its building follows its key, so it does not shuffle when the roster reorders.
            foreach (var city in world.cities)
            {
                var byKey = new List<WorldRider>(city.riders);
                byKey.Sort((x, y) => string.CompareOrdinal(x.key, y.key));
                var next = new int[10];
                foreach (var r in byKey) r.slot = next[(int)r.building]++;
            }
            return world;
        }
    }
}
