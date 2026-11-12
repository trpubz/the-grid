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

        // Beside each repo: the workspace whose region its sector stands in, and its place there. A region has a spiral of
        // its own, and a sector keeps its place on it for as long as its repo stays in that workspace.
        public List<string> regions = new List<string>();
        public List<int> places = new List<int>();

        int saved;    // how many repos and accounts the file on disk holds; not part of the file
        bool moved;   // a sector was given a place since the file was written

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
            p.places = p.places ?? new List<int>();
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

        // Writes the file when plots or accounts were added, or a sector was given a place, since it was last written. A write that throws leaves them owed, so the next call tries again.
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

        // A repo's place in the region of its workspace: 0 for the first there, and so on along the region's spiral. It
        // keeps the place while it stays in that workspace. Moved to another, it takes the first free place there, and the
        // one it left is the next given out.
        public int PlaceFor(int plot, string workspace)
        {
            while (regions.Count < repos.Count) { regions.Add(""); places.Add(-1); }
            while (places.Count < regions.Count) places.Add(-1);
            if (regions[plot] == workspace && places[plot] >= 0) return places[plot];
            var taken = new HashSet<int>();
            for (int i = 0; i < repos.Count; i++)
                if (i != plot && regions[i] == workspace) taken.Add(places[i]);
            int place = 0;
            while (taken.Contains(place)) place++;
            regions[plot] = workspace;
            places[plot] = place;
            moved = true;
            return place;
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
