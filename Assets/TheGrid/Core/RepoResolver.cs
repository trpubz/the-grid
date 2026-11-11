using System;
using System.IO;

namespace TheGrid.Core
{
    public sealed class RepoInfo { public string Root = ""; public string Name = RepoResolver.NoProject; }

    // Maps a session's working folder to the repo it belongs to, without calling git.
    public static class RepoResolver
    {
        public const string NoProject = "No project";

        public static RepoInfo Resolve(string cwd)
        {
            if (string.IsNullOrEmpty(cwd)) return new RepoInfo();
            try
            {
                for (var dir = new DirectoryInfo(cwd); dir != null; dir = dir.Parent)
                {
                    var git = Path.Combine(dir.FullName, ".git");
                    if (Directory.Exists(git)) return Make(dir.FullName);
                    if (File.Exists(git)) return Make(MainRepoOf(git) ?? dir.FullName);
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException) { }
            return new RepoInfo();
        }

        static RepoInfo Make(string root) => new RepoInfo { Root = root, Name = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar)) };

        // A worktree's .git is a file: "gitdir: <main repo>/.git/worktrees/<name>". Worktrees group under the main repo.
        static string MainRepoOf(string gitFile)
        {
            var text = File.ReadAllText(gitFile).Trim();
            if (!text.StartsWith("gitdir:", StringComparison.Ordinal)) return null;
            var gitdir = text.Substring("gitdir:".Length).Trim();
            var s = Path.DirectorySeparatorChar;
            int i = gitdir.LastIndexOf(s + ".git" + s + "worktrees" + s, StringComparison.Ordinal);
            return i > 0 ? gitdir.Substring(0, i) : null;
        }
    }
}
