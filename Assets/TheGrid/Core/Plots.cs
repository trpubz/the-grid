using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TheGrid.Core
{
    // Which repo owns which plot of the map, and which account has which colour. The world's own file; field names are the JSON keys.
    [Serializable]
    public sealed class Plots
    {
        // A repo's index in this list is its plot number, for good. "" is the plot for sessions with no repo.
        public List<string> repos = new List<string>();
        // An account's index in this list picks the colour of its bots' light, for good: the order accounts were first seen in.
        public List<string> accounts = new List<string>();

        // Beside each repo: the workspace whose region its sector stands in, and the cell of that region's own grid it
        // stands on. It keeps the cell for as long as its repo stays in that workspace, unless the owner moves it.
        public List<string> regions = new List<string>();
        public List<Vector2Int> cells = new List<Vector2Int>();
        // Where each region stands: the workspaces that have one, and beside each the tile its grid starts from. A region
        // keeps its place for good, unless the owner moves it.
        public List<string> placed = new List<string>();
        public List<Vector2Int> origins = new List<Vector2Int>();

        int saved;    // how many repos and accounts the file on disk holds; not part of the file
        bool moved;   // a region or a sector was given a place since the file was written

        public static Plots Load(string path)
        {
            Plots p = null;
            if (File.Exists(path))
            {
                try { p = JsonUtility.FromJson<Plots>(File.ReadAllText(path)); }
                catch (Exception e) when (e is ArgumentException || e is IOException || e is UnauthorizedAccessException) { }
                // Unreadable: keep the file so the next Save does not destroy the layout it held.
                if (p == null) try { File.Copy(path, path + ".bad", true); } catch (IOException) { }
            }
            p = p ?? new Plots();
            p.repos = p.repos ?? new List<string>();
            p.accounts = p.accounts ?? new List<string>();
            p.regions = p.regions ?? new List<string>();
            p.cells = p.cells ?? new List<Vector2Int>();
            p.placed = p.placed ?? new List<string>();
            p.origins = p.origins ?? new List<Vector2Int>();
            p.saved = p.repos.Count + p.accounts.Count;
            return p;
        }

        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonUtility.ToJson(this, true));
            if (File.Exists(path)) File.Replace(tmp, path, null); else File.Move(tmp, path);
            saved = repos.Count + accounts.Count;
            moved = false;
        }

        // Writes the file when plots or accounts were added, or anything was given a place, since it was last written. A write that throws leaves them owed, so the next call tries again.
        public void SaveIfGrown(string path)
        {
            if (repos.Count + accounts.Count != saved || moved) Save(path);
        }

        // The number that picks an account's colour. A new account takes the next one.
        public int ColourFor(string account)
        {
            account = account ?? "";
            int i = accounts.IndexOf(account);
            if (i < 0) { accounts.Add(account); i = accounts.Count - 1; }
            return i;
        }

        public int IndexFor(string repoRoot, out bool added)
        {
            repoRoot = repoRoot ?? "";
            int i = repos.IndexOf(repoRoot);
            added = i < 0;
            if (added) { repos.Add(repoRoot); i = repos.Count - 1; }
            return i;
        }

        // Where the region of a workspace stands; false while it has none.
        public bool TryOrigin(string workspace, out Vector2Int origin)
        {
            int i = placed.IndexOf(workspace);
            origin = i >= 0 && i < origins.Count ? origins[i] : default;
            return i >= 0 && i < origins.Count;
        }

        public void SetOrigin(string workspace, Vector2Int origin)
        {
            int i = placed.IndexOf(workspace);
            if (i < 0) { placed.Add(workspace); i = placed.Count - 1; }
            while (origins.Count < placed.Count) origins.Add(default);
            origins[i] = origin;
            moved = true;
        }

        // The cell a repo's sector stands on in the region of this workspace; false when it has none there yet, because it
        // is new or has just come from another workspace.
        public bool TryCell(int plot, string workspace, out Vector2Int cell)
        {
            bool has = plot < regions.Count && plot < cells.Count && regions[plot] == workspace && !string.IsNullOrEmpty(workspace);
            cell = has ? cells[plot] : default;
            return has;
        }

        public void SetCell(int plot, string workspace, Vector2Int cell)
        {
            while (regions.Count <= plot) regions.Add("");
            while (cells.Count < regions.Count) cells.Add(default);
            regions[plot] = workspace;
            cells[plot] = cell;
            moved = true;
        }

        // The owner's moves. Whether a move is allowed is World.CanMoveRegion and World.CanMoveSector's to say.
        public void MoveRegion(string workspace, Vector2Int by)
        {
            if (TryOrigin(workspace, out var origin)) SetOrigin(workspace, origin + by);
        }

        public void MoveSector(int plot, Vector2Int cell)
        {
            if (plot >= 0 && plot < regions.Count && regions[plot] != "") SetCell(plot, regions[plot], cell);
        }

        // A square spiral out from the centre, so the map stays roughly square however many repos there are.
        public static Vector2Int Cell(int index)
        {
            int x = 0, y = 0, dx = 1, dy = 0, run = 1, left = 1;
            bool grow = false;
            for (int i = 0; i < index; i++)
            {
                x += dx; y += dy;
                if (--left > 0) continue;
                int t = dx; dx = -dy; dy = t;   // quarter turn
                if (grow) run++;                // legs go 1, 1, 2, 2, 3, 3, ...
                grow = !grow;
                left = run;
            }
            return new Vector2Int(x, y);
        }
    }
}
