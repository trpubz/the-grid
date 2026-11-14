using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TheGrid.Core
{
    [Serializable] public sealed class RepoWorkspace { public string repo; public string workspace; }

    [Serializable] public sealed class WorkspaceLight { public string workspace; public string light; }

    // The owner's settings file. Field names are the JSON keys.
    [Serializable]
    public sealed class Settings
    {
        public const string DefaultWorkspace = "Personal";

        public List<string> configDirs = new List<string>();   // empty means: discover
        public string claudePath = "";                          // empty means: look in the usual places
        public bool pullRequests = true;                        // ask GitHub, through gh, about the open pull requests of the repos on the map
        public string ghPath = "";                              // empty means: look in the usual places
        public string zellijPath = "";                          // the same
        public string font = "";                                // the family all text is set in; empty means: the first Nerd Font this Mac has
        public List<string> workspaces = new List<string> { DefaultWorkspace };
        public List<RepoWorkspace> repoWorkspaces = new List<RepoWorkspace>();
        public List<WorkspaceLight> workspaceLights = new List<WorkspaceLight>();   // a light: one of Lights.LookNames

        public static Settings Load(string path)
        {
            Settings s = null;
            if (File.Exists(path))
            {
                try { s = JsonUtility.FromJson<Settings>(File.ReadAllText(path)); }
                catch (Exception e) when (e is ArgumentException || e is IOException || e is UnauthorizedAccessException) { }
                // Unreadable: keep the owner's file so the next Save does not destroy it.
                if (s == null) try { File.Copy(path, path + ".bad", true); } catch (IOException) { }
            }
            s = s ?? new Settings();
            s.configDirs = s.configDirs ?? new List<string>();
            s.claudePath = s.claudePath ?? "";
            s.ghPath = s.ghPath ?? "";
            s.zellijPath = s.zellijPath ?? "";
            s.font = s.font ?? "";
            s.repoWorkspaces = s.repoWorkspaces ?? new List<RepoWorkspace>();
            s.workspaceLights = s.workspaceLights ?? new List<WorkspaceLight>();
            if (s.workspaces == null || s.workspaces.Count == 0) s.workspaces = new List<string> { DefaultWorkspace };
            return s;
        }

        // Re-reads the file into this object, so a hand edit made while the app runs is not overwritten by the next Save.
        public void Reload(string path)
        {
            var fresh = Load(path);
            configDirs = fresh.configDirs;
            claudePath = fresh.claudePath;
            pullRequests = fresh.pullRequests;
            ghPath = fresh.ghPath;
            zellijPath = fresh.zellijPath;
            font = fresh.font;
            workspaces = fresh.workspaces;
            repoWorkspaces = fresh.repoWorkspaces;
            workspaceLights = fresh.workspaceLights;
        }

        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonUtility.ToJson(this, true));
            if (File.Exists(path)) File.Replace(tmp, path, null); else File.Move(tmp, path);
        }

        // The look of a workspace's region on the map: the one the owner picked, by name; with none picked, or a name
        // that is no light, `otherwise`, which is the workspace's place among the workspaces.
        public int LookFor(string workspace, int otherwise)
        {
            var picked = workspaceLights.Find(wl => wl.workspace == workspace);
            int look = picked == null ? -1 : Array.IndexOf(Lights.LookNames, picked.light);
            return look >= 0 ? look : ((otherwise % Lights.LookCount) + Lights.LookCount) % Lights.LookCount;
        }

        // Gives the workspace's region the look after `current`, and returns it.
        public int CycleLight(string workspace, int current)
        {
            int next = (current + 1) % Lights.LookCount;
            var picked = workspaceLights.Find(wl => wl.workspace == workspace);
            if (picked == null) workspaceLights.Add(picked = new WorkspaceLight { workspace = workspace });
            picked.light = Lights.LookNames[next];
            return next;
        }

        public string WorkspaceFor(string repoRoot)
        {
            if (!string.IsNullOrEmpty(repoRoot))
                foreach (var rw in repoWorkspaces)
                    if (rw.repo == repoRoot && !string.IsNullOrEmpty(rw.workspace)) return rw.workspace;
            return DefaultWorkspace;
        }

        // Gives the repo a workspace by name and returns the name it now has. A name there is no workspace of makes one, at
        // the end of the list; one that differs from a workspace's only by case is that workspace. No name changes nothing.
        // A session with no repo cannot be given one.
        public string SetWorkspace(string repoRoot, string name)
        {
            name = (name ?? "").Trim();
            if (string.IsNullOrEmpty(repoRoot) || name == "") return WorkspaceFor(repoRoot);
            string known = workspaces.Find(w => string.Equals(w, name, StringComparison.OrdinalIgnoreCase));
            if (known == null) workspaces.Add(known = name);
            var entry = repoWorkspaces.Find(rw => rw.repo == repoRoot);
            if (entry == null) repoWorkspaces.Add(entry = new RepoWorkspace { repo = repoRoot });
            entry.workspace = known;
            return known;
        }

        // ~/.claude and every ~/.claude-* that holds a projects folder.
        public static List<string> DiscoverConfigDirs(string home)
        {
            var found = new List<string>();
            if (!Directory.Exists(home)) return found;
            var candidates = new List<string> { Path.Combine(home, ".claude") };
            var more = Directory.GetDirectories(home, ".claude-*");
            Array.Sort(more, StringComparer.Ordinal);
            candidates.AddRange(more);
            foreach (var dir in candidates)
                if (Directory.Exists(Path.Combine(dir, "projects"))) found.Add(dir);
            return found;
        }

        public List<string> EffectiveConfigDirs(string home)
        {
            if (configDirs.Count == 0) return DiscoverConfigDirs(home);
            return configDirs.ConvertAll(d => d.StartsWith("~/", StringComparison.Ordinal) ? Path.Combine(home, d.Substring(2)) : d);
        }
    }
}
