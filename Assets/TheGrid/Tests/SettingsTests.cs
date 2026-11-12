using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace TheGrid.Core.Tests
{
    public class SettingsTests
    {
        string tmp, file, home;

        [SetUp]
        public void SetUp()
        {
            tmp = Path.Combine(Path.GetTempPath(), "grid-tests-" + Guid.NewGuid().ToString("N"));
            home = Path.Combine(tmp, "home");
            Directory.CreateDirectory(Path.Combine(home, ".claude", "projects"));
            Directory.CreateDirectory(Path.Combine(home, ".claude-two", "projects"));
            Directory.CreateDirectory(Path.Combine(home, ".claude-router"));
            Directory.CreateDirectory(Path.Combine(home, ".clauded", "projects"));
            file = Path.Combine(tmp, "settings.json");
        }

        [TearDown]
        public void TearDown() => Directory.Delete(tmp, true);

        [Test]
        public void MissingFileGivesDefaults()
        {
            var s = Settings.Load(Path.Combine(tmp, "missing.json"));
            CollectionAssert.AreEqual(new[] { "Personal" }, s.workspaces);
            Assert.AreEqual("Personal", s.WorkspaceFor("/work/alpha"));
            Assert.AreEqual("Personal", s.WorkspaceFor(""));
            Assert.AreEqual("", s.claudePath);
            Assert.IsTrue(s.pullRequests, "GitHub is asked unless the owner says not to");
            Assert.AreEqual("", s.ghPath);
        }

        [Test]
        public void AskingGitHubIsOnUnlessTheFileTurnsItOff()
        {
            File.WriteAllText(file, "{\"workspaces\":[\"Personal\"]}");
            Assert.IsTrue(Settings.Load(file).pullRequests, "a file from before the setting existed");
            File.WriteAllText(file, "{\"pullRequests\":false,\"ghPath\":\"/opt/gh\"}");
            var s = Settings.Load(file);
            Assert.IsFalse(s.pullRequests);
            Assert.AreEqual("/opt/gh", s.ghPath);
            s.Save(file);
            Assert.IsFalse(Settings.Load(file).pullRequests);
        }

        [Test]
        public void ARepoIsGivenAWorkspaceWhichIsMadeIfItIsNewAndSurvivesSaveAndLoad()
        {
            var s = Settings.Load(file);   // a first run: Personal alone
            CollectionAssert.AreEqual(new[] { "Personal" }, s.workspaces);
            // a new name makes the workspace; what is typed round it does not count
            Assert.AreEqual("Open source", s.SetWorkspace("/work/alpha", "  Open source "));
            CollectionAssert.AreEqual(new[] { "Personal", "Open source" }, s.workspaces);
            Assert.AreEqual("Open source", s.WorkspaceFor("/work/alpha"));
            Assert.AreEqual("Personal", s.WorkspaceFor("/work/beta"));
            // a name that differs from a workspace's only by case is that workspace
            Assert.AreEqual("Open source", s.SetWorkspace("/work/beta", "open SOURCE"));
            CollectionAssert.AreEqual(new[] { "Personal", "Open source" }, s.workspaces);
            // no name changes nothing
            Assert.AreEqual("Open source", s.SetWorkspace("/work/alpha", "   "));
            Assert.AreEqual("Open source", s.SetWorkspace("/work/alpha", null));
            // and back to one there already is
            Assert.AreEqual("Personal", s.SetWorkspace("/work/beta", "Personal"));
            Assert.AreEqual(2, s.repoWorkspaces.Count, "one entry a repo, however often it moves");
            s.Save(file);
            var again = Settings.Load(file);
            Assert.AreEqual("Open source", again.WorkspaceFor("/work/alpha"));
            Assert.AreEqual("Personal", again.WorkspaceFor("/work/beta"));
            CollectionAssert.AreEqual(new[] { "Personal", "Open source" }, again.workspaces);
        }

        [Test]
        public void ARegionsLightIsItsPlaceInTheListUntilTheOwnerPicksOneAndThePickSurvivesSaveAndLoad()
        {
            var s = Settings.Load(file);
            Assert.AreEqual(1, s.LookFor("Work", 1));
            Assert.AreEqual(1, s.LookFor("Work", 1 + Lights.LookCount), "past the last light the list starts again");
            Assert.AreEqual(2, s.CycleLight("Work", 1));
            Assert.AreEqual(2, s.LookFor("Work", 1));
            Assert.AreEqual(0, s.LookFor("Personal", 0), "another workspace keeps its own");
            s.Save(file);
            var again = Settings.Load(file);
            Assert.AreEqual(2, again.LookFor("Work", 1));
            Assert.AreEqual(0, again.CycleLight("Work", Lights.LookCount - 1), "after the last light comes the first");
            // a light named by hand in the file that is no light at all is ignored
            again.workspaceLights[0].light = "plaid";
            Assert.AreEqual(1, again.LookFor("Work", 1));
        }

        [Test]
        public void ASessionWithNoRepoCannotBeAssigned()
        {
            var s = Settings.Load(file);
            s.workspaces = new List<string> { "Personal", "Work" };
            Assert.AreEqual("Personal", s.SetWorkspace("", "Elsewhere"));
            Assert.AreEqual(0, s.repoWorkspaces.Count);
            CollectionAssert.AreEqual(new[] { "Personal", "Work" }, s.workspaces, "and no workspace is made for it");
        }

        [Test]
        public void CorruptFileGivesDefaultsAndIsKeptAsBad()
        {
            File.WriteAllText(file, "{ broken");
            var d = Settings.Load(file);
            Assert.AreEqual(1, d.workspaces.Count);
            Assert.AreEqual("{ broken", File.ReadAllText(file + ".bad"));
            d.Save(file);
            Assert.AreEqual("Personal", Settings.Load(file).WorkspaceFor("/x"));
        }

        [Test]
        public void FileWithAnEmptyWorkspaceListStillHasTheDefault()
        {
            File.WriteAllText(file, "{\"workspaces\":[]}");
            CollectionAssert.AreEqual(new[] { "Personal" }, Settings.Load(file).workspaces);
        }

        [Test]
        public void DiscoversConfigDirectoriesThatHoldProjects()
        {
            CollectionAssert.AreEqual(new[] { Path.Combine(home, ".claude"), Path.Combine(home, ".claude-two") }, Settings.DiscoverConfigDirs(home));
            Assert.AreEqual(0, Settings.DiscoverConfigDirs(Path.Combine(tmp, "nobody")).Count);
        }

        [Test]
        public void ConfiguredDirectoriesReplaceDiscovery()
        {
            var s = Settings.Load(file);
            CollectionAssert.AreEqual(Settings.DiscoverConfigDirs(home), s.EffectiveConfigDirs(home));
            s.configDirs = new List<string> { "/only/this", "~/mine" };
            CollectionAssert.AreEqual(new[] { "/only/this", Path.Combine(home, "mine") }, s.EffectiveConfigDirs(home));
        }

        [Test]
        public void ReloadPicksUpAHandEditSoALaterSaveDoesNotLoseIt()
        {
            var s = Settings.Load(file);
            s.Save(file);
            File.WriteAllText(file, "{\"workspaces\":[\"Personal\",\"Work\"],\"claudePath\":\"/opt/claude\"}");
            s.Reload(file);
            Assert.AreEqual("Work", s.SetWorkspace("/work/alpha", "Work"));
            s.Save(file);
            var again = Settings.Load(file);
            CollectionAssert.AreEqual(new[] { "Personal", "Work" }, again.workspaces);
            Assert.AreEqual("/opt/claude", again.claudePath);
            Assert.AreEqual("Work", again.WorkspaceFor("/work/alpha"));
        }
    }
}
