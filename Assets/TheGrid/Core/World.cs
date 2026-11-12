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
        public int region;   // which of the world's regions it stands in
        public Vector2Int cell;     // its cell of its region's own grid
        public Vector2Int origin;   // where it stands on the map: the tile of its plot's north corner
        public int needOwner, done, running;
        public List<WorldBot> bots = new List<WorldBot>();
        [NonSerialized] public Layout layout = Layout.Of(0);   // how this sector is arranged and lit: follows from its plot
    }

    // The part of the map one workspace's sectors stand in: together, in lights of their own, with open ground between
    // them and the next region.
    [Serializable]
    public sealed class WorldRegion
    {
        public string name = "";    // the workspace's
        public int look;            // which of Lights' looks it is lit in
        public Vector2Int origin;   // the tile its sectors' grid starts from: where the sector on cell (0, 0) stands
    }

    // What the world view draws: the roster snapshot, arranged as a map. Never changed after Build returns it.
    [Serializable]
    public sealed class WorldModel
    {
        public long generatedAt;
        public List<WorldSector> sectors = new List<WorldSector>();
        public List<WorldRegion> regions = new List<WorldRegion>();   // only those with a sector in them
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

        // Whether the owner may move a region, with all its sectors, by so many tiles: every sector of it must still stand
        // the gap between regions from every sector of another.
        public static bool CanMoveRegion(WorldModel world, int region, Vector2Int by)
        {
            foreach (var mine in world.sectors)
                foreach (var other in world.sectors)
                    if (mine.region == region && other.region != region && !Iso.Apart(mine.origin + by, other.origin)) return false;
            return true;
        }

        // Whether the owner may move a sector to a cell of its region's grid: no other sector of the region stands there,
        // and it would still stand the gap between regions from every sector of another.
        public static bool CanMoveSector(WorldModel world, WorldSector sector, Vector2Int cell)
        {
            var at = sector.origin + Iso.PlotOrigin(cell - sector.cell);
            foreach (var other in world.sectors)
            {
                if (other == sector) continue;
                if (other.region == sector.region ? other.cell == cell : !Iso.Apart(at, other.origin)) return false;
            }
            return true;
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

        // Where a new region is put: beside the map, the gap between regions away from the sector it stands next to, in
        // line with it. Of all such places, the one nearest the middle of the map.
        static Vector2Int Beside(List<Vector2Int> sectors)
        {
            if (sectors.Count == 0) return Vector2Int.zero;
            var sum = Vector2Int.zero;
            foreach (var s in sectors) sum += s;
            int step = Iso.PlotTiles + 2 * Iso.LaneTiles + Iso.RegionGap;
            long best = long.MaxValue;
            var spot = Vector2Int.zero;
            foreach (var s in sectors)
                foreach (var side in new[] { Vector2Int.right, Vector2Int.up, Vector2Int.left, Vector2Int.down })
                {
                    var at = s + side * step;
                    if (!sectors.TrueForAll(other => Iso.Apart(at, other))) continue;
                    var d = at * sectors.Count - sum;   // from the middle, times the count, to stay in whole numbers
                    long far = (long)d.x * d.x + (long)d.y * d.y;
                    if (far < best) { best = far; spot = at; }
                }
            return spot;
        }

        // One sector per plot, in plot order; a repo not seen before takes the next plot. A plot whose bots have all gone stays, empty.
        // Sectors stand by workspace. Each workspace with a repo has a region: sectors on a grid of its own, the first in
        // the middle and the rest out along a spiral. A new region is put beside the map (Beside). A new sector takes the
        // first cell of its region's spiral that is free and keeps the gap to every other region.
        // Once put, nothing moves unless the owner moves it: all of it is kept in `plots`. With one workspace that is the
        // one spiral, from the middle.
        // settings: where a repo with no session belongs, the order of the workspaces, and the look the owner picked for
        // each. Left out, everything is the default.
        public static WorldModel Build(RosterSnapshot snap, Plots plots, Settings settings = null)
        {
            settings = settings ?? new Settings();
            foreach (var a in snap.agents) plots.IndexFor(a.repoRoot, out _);
            int count = plots.repos.Count;
            // A repo is in the workspace its sessions say, which is what the lists and the card show. With no session,
            // in the one the settings give it.
            var workspace = new string[count];
            foreach (var a in snap.agents)
                if (!string.IsNullOrEmpty(a.workspace)) workspace[plots.IndexFor(a.repoRoot, out _)] = a.workspace;
            var names = new List<string>(settings.workspaces);
            for (int i = 0; i < count; i++)
            {
                workspace[i] = workspace[i] ?? settings.WorkspaceFor(plots.repos[i]);
                if (!names.Contains(workspace[i])) names.Add(workspace[i]);
            }

            // First what already has a place, then the rest in plot order, each put clear of everything before it.
            var cell = new Vector2Int[count];
            var origin = new Vector2Int[count];
            var put = new bool[count];
            var standing = new List<Vector2Int>();
            for (int i = 0; i < count; i++)
            {
                if (!plots.TryOrigin(workspace[i], out var region) || !plots.TryCell(i, workspace[i], out cell[i])) continue;
                origin[i] = region + Iso.PlotOrigin(cell[i]);
                put[i] = true;
                standing.Add(origin[i]);
            }
            for (int i = 0; i < count; i++)
            {
                if (put[i]) continue;
                if (!plots.TryOrigin(workspace[i], out var region)) plots.SetOrigin(workspace[i], region = Beside(standing));
                for (int k = 0; ; k++)
                {
                    cell[i] = Plots.Cell(k);
                    origin[i] = region + Iso.PlotOrigin(cell[i]);
                    bool free = true;
                    for (int j = 0; j < count && free; j++)
                        if (put[j]) free = workspace[j] == workspace[i] ? cell[j] != cell[i] : Iso.Apart(origin[i], origin[j]);
                    if (free) break;
                }
                plots.SetCell(i, workspace[i], cell[i]);
                put[i] = true;
                standing.Add(origin[i]);
            }

            var world = new WorldModel { generatedAt = snap.generatedAt };
            // The regions, in the order of the workspaces, leaving out those with no repo.
            var regionOf = new Dictionary<string, int>();
            for (int n = 0; n < names.Count; n++)
            {
                if (Array.IndexOf(workspace, names[n]) < 0) continue;
                regionOf[names[n]] = world.regions.Count;
                plots.TryOrigin(names[n], out var at);
                world.regions.Add(new WorldRegion { name = names[n], look = settings.LookFor(names[n], n), origin = at });
            }
            for (int i = 0; i < count; i++)
            {
                var root = plots.repos[i] ?? "";
                world.sectors.Add(new WorldSector
                {
                    repoRoot = root, plot = i, region = regionOf[workspace[i]], workspace = workspace[i], cell = cell[i], origin = origin[i],
                    layout = Layout.For(i, cell[i], world.regions[regionOf[workspace[i]]].look),
                    name = root == "" ? NoProject : Path.GetFileName(root.TrimEnd('/')),
                });
            }

            foreach (var a in snap.agents)
            {
                var sector = world.sectors[plots.IndexFor(a.repoRoot, out _)];
                if (!string.IsNullOrEmpty(a.repoName)) sector.name = a.repoName;
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
