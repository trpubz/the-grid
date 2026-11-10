using System;
using System.IO;
using NUnit.Framework;

namespace TheGrid.Core.Tests
{
    public class RepoResolverTests
    {
        string tmp;

        [SetUp]
        public void SetUp()
        {
            tmp = Path.Combine(Path.GetTempPath(), "grid-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(tmp, "alpha", ".git", "worktrees", "alpha-wt"));
            Directory.CreateDirectory(Path.Combine(tmp, "alpha", "src", "deep"));
            Directory.CreateDirectory(Path.Combine(tmp, "alpha-wt", "sub"));
            File.WriteAllText(Path.Combine(tmp, "alpha-wt", ".git"), "gitdir: " + Path.Combine(tmp, "alpha", ".git", "worktrees", "alpha-wt") + "\n");
            Directory.CreateDirectory(Path.Combine(tmp, "plain"));
            Directory.CreateDirectory(Path.Combine(tmp, "odd"));
            File.WriteAllText(Path.Combine(tmp, "odd", ".git"), "this is not a gitdir pointer");
        }

        [TearDown]
        public void TearDown() => Directory.Delete(tmp, true);

        [Test]
        public void FolderInsideARepoResolvesToItsRoot()
        {
            var r = RepoResolver.Resolve(Path.Combine(tmp, "alpha", "src", "deep"));
            Assert.AreEqual("alpha", r.Name);
            Assert.AreEqual(Path.Combine(tmp, "alpha"), r.Root);
            Assert.AreEqual(Path.Combine(tmp, "alpha"), RepoResolver.Resolve(Path.Combine(tmp, "alpha")).Root);
        }

        [Test]
        public void WorktreeGroupsUnderTheMainRepo()
        {
            var r = RepoResolver.Resolve(Path.Combine(tmp, "alpha-wt", "sub"));
            Assert.AreEqual(Path.Combine(tmp, "alpha"), r.Root);
            Assert.AreEqual("alpha", r.Name);
        }

        [Test]
        public void FolderOutsideAnyRepoIsNoProject()
        {
            var r = RepoResolver.Resolve(Path.Combine(tmp, "plain"));
            Assert.AreEqual(RepoResolver.NoProject, r.Name);
            Assert.AreEqual("", r.Root);
        }

        [Test]
        public void MissingFolderNullAndEmptyAreNoProject()
        {
            Assert.AreEqual("", RepoResolver.Resolve("/no/such/folder").Root);
            Assert.AreEqual(RepoResolver.NoProject, RepoResolver.Resolve(null).Name);
            Assert.AreEqual(RepoResolver.NoProject, RepoResolver.Resolve("").Name);
        }

        [Test]
        public void UnreadableGitFileFallsBackToTheFolderItself()
        {
            var r = RepoResolver.Resolve(Path.Combine(tmp, "odd"));
            Assert.AreEqual(Path.Combine(tmp, "odd"), r.Root);
            Assert.AreEqual("odd", r.Name);
        }
    }
}
