using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TheGrid.Core
{
    [Serializable] public sealed class RepoWorkspace { public string repo; public string workspace; }

    // The owner's settings file. Field names are the JSON keys.
    [Serializable]
    public sealed class Settings
    {
        public const string DefaultWorkspace = "Personal";

        public List<string> configDirs = new List<string>();   // empty means: discover
        public string claudePath = "";                          // empty means: look in the usual places
        public List<string> workspaces = new List<string> { DefaultWorkspace };
        public List<RepoWorkspace> repoWorkspaces = new List<RepoWorkspace>();

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
            s.repoWorkspaces = s.repoWorkspaces ?? new List<RepoWorkspace>();
            if (s.workspaces == null || s.workspaces.Count == 0) s.workspaces = new List<string> { DefaultWorkspace };
            return s;
        }

        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonUtility.ToJson(this, true));
            if (File.Exists(path)) File.Replace(tmp, path, null); else File.Move(tmp, path);
        }

        public string WorkspaceFor(string repoRoot)
        {
            if (!string.IsNullOrEmpty(repoRoot))
                foreach (var rw in repoWorkspaces)
                    if (rw.repo == repoRoot && !string.IsNullOrEmpty(rw.workspace)) return rw.workspace;
            return DefaultWorkspace;
        }

        // Assigns the next entry of `workspaces` to the repo and returns it. A session with no repo cannot be assigned.
        public string CycleWorkspace(string repoRoot)
        {
            if (string.IsNullOrEmpty(repoRoot)) return DefaultWorkspace;
            var next = workspaces[(workspaces.IndexOf(WorkspaceFor(repoRoot)) + 1) % workspaces.Count];
            var entry = repoWorkspaces.Find(rw => rw.repo == repoRoot);
            if (entry == null) repoWorkspaces.Add(entry = new RepoWorkspace { repo = repoRoot });
            entry.workspace = next;
            return next;
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
