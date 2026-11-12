using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace TheGrid.Core.Tests
{
    // What GitHub says about a repo's open pull requests, and what the world makes of it. Recorded answers only: no test
    // talks to GitHub.
    public class PullRequestsTests
    {
        static string Fixture(string name) => File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "fixtures", "github", name));

        [Test]
        public void EveryOpenPullRequestIsADraftInReviewOrForTheOwnerAndSaysWhyInWords()
        {
            Assert.IsTrue(PullRequests.TryParse(Fixture("pulls.json"), out var pulls, out var problem), problem);
            var got = pulls.ToDictionary(p => p.number, p => p.state + ": " + p.status);
            var want = new Dictionary<int, string>
            {
                { 1, "Draft: Draft" },                                                    // a draft is a draft whatever else is true of it
                { 2, "ForOwner: The review bot found nothing: ready to merge" },
                { 3, "InReview: The review bot is reviewing" },                           // a person's thumbs-up is not a review
                { 4, "ForOwner: Approved: ready to merge" },
                { 5, "InReview: Checks are running" },
                { 6, "InReview: Checks failed" },
                { 7, "InReview: Conflicts with its base branch" },
                { 8, "InReview: pat is asked to review" },
                { 9, "ForOwner: Your review is asked for" },                              // before anything else about it
                { 10, "InReview: Changes are asked for" },
                { 11, "InReview: Waits for a review" },
                { 12, "ForOwner: Ready to merge" },                                       // nothing asked, nothing running, nobody to wait for
                { 13, "InReview: reviewers is asked to review" },
            };
            CollectionAssert.AreEquivalent(want.Select(kv => kv.Key + " " + kv.Value), got.Select(kv => kv.Key + " " + kv.Value));
            Assert.AreEqual("https://github.com/example/alpha/pull/4", pulls.Single(p => p.number == 4).url);
            Assert.AreEqual("Approved by a person", pulls.Single(p => p.number == 4).title);
        }

        [Test]
        public void TheOnesForTheOwnerComeFirstThenThoseInReviewThenDrafts()
        {
            PullRequests.TryParse(Fixture("pulls.json"), out var pulls, out _);
            CollectionAssert.AreEqual(new[] { 2, 4, 9, 12, 3, 5, 6, 7, 8, 10, 11, 13, 1 }, pulls.Select(p => p.number).ToArray());
        }

        [TestCase("{ broken", "GitHub's answer could not be read")]
        [TestCase("{\"data\":{\"viewer\":{\"login\":\"owner\"},\"repository\":null}}", "GitHub does not know this repo, or this login may not see it")]
        [TestCase("{\"errors\":[{\"message\":\"Bad credentials\"}]}", "GitHub: Bad credentials")]
        public void AnAnswerThatIsNotAListOfPullRequestsIsAProblemInWords(string answer, string problem)
        {
            Assert.IsFalse(PullRequests.TryParse(answer, out var pulls, out var got));
            Assert.AreEqual(problem, got);
            Assert.AreEqual(0, pulls.Count);
        }

        [Test]
        public void ARepoWithNoneOpenIsAnEmptyListAndNoProblem()
        {
            Assert.IsTrue(PullRequests.TryParse("{\"data\":{\"viewer\":{\"login\":\"owner\"},\"repository\":{\"pullRequests\":{\"nodes\":[]}}}}", out var pulls, out var problem));
            Assert.AreEqual(0, pulls.Count);
            Assert.AreEqual("", problem);
        }

        [Test]
        public void TheReaderAsksOncePerRepoAndKeepsTheLastGoodAnswerThroughAFailure()
        {
            var asked = new List<string>();
            bool fail = false;
            var reader = new PullReader(root =>
            {
                asked.Add(root);
                if (root == "/work/beta") throw new InvalidOperationException("gh: not a GitHub repo");
                if (fail) throw new TimeoutException("gh did not answer");
                return Fixture("pulls.json");
            });
            Assert.AreEqual(0, reader.Latest.repos.Count);
            reader.Refresh(new[] { "/work/alpha", "/work/beta", "" });   // sessions with no repo have none to ask about
            CollectionAssert.AreEqual(new[] { "/work/alpha", "/work/beta" }, asked);
            var first = reader.Latest;
            Assert.AreEqual(13, first.For("/work/alpha").open.Count);
            Assert.AreEqual("", first.For("/work/alpha").problem);
            Assert.AreEqual(0, first.For("/work/beta").open.Count);
            Assert.AreEqual("gh: not a GitHub repo", first.For("/work/beta").problem);
            Assert.IsNull(first.For("/work/gamma"));

            fail = true;
            reader.Refresh(new[] { "/work/alpha" });
            Assert.AreNotSame(first, reader.Latest);
            Assert.AreEqual(13, reader.Latest.For("/work/alpha").open.Count, "the list from before the failure");
            Assert.AreEqual("gh did not answer", reader.Latest.For("/work/alpha").problem);
            Assert.IsNull(reader.Latest.For("/work/beta"), "a repo no longer on the map is dropped");
        }

        [TestCase("error parsing \"owner\" value: failed to run git: fatal: not a git repository (or any of the parent directories): .git", "This folder is not a git repository.")]
        [TestCase("error parsing \"owner\" value: none of the git remotes configured for this repository point to a known GitHub host. To tell gh about a new GitHub host, please use `gh auth login`", "This repo has no GitHub remote.")]
        [TestCase("To get started with GitHub CLI, please run:  gh auth login\nAlternatively, populate the GH_TOKEN environment variable", "gh is not signed in to GitHub: run gh auth login in a terminal.")]
        [TestCase("error connecting to api.github.com\ncheck your internet connection", "gh: error connecting to api.github.com")]
        [TestCase("", "gh gave no answer.")]
        public void WhyGhGaveNoAnswerIsSaidInPlainWords(string stderr, string reason) => Assert.AreEqual(reason, Gh.Reason(stderr));

        [Test]
        public void GhIsLookedForWhereItIsUsuallyInstalled()
        {
            CollectionAssert.Contains(Gh.UsualPlaces(), "/opt/homebrew/bin/gh");
            Assert.Throws<InvalidOperationException>(() => Gh.Ask("", "/work/alpha"));
            StringAssert.Contains("pullRequests(states:OPEN", PullRequests.Query);
        }
    }
}
