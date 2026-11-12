using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using UnityEngine;

namespace TheGrid.Core
{
    // What an open pull request is to the owner. In the order the depot queues them, last first.
    public enum PullState { Draft, InReview, ForOwner }

    // One open pull request of a repo, as the world shows it: a crate at the depot, a row in its list.
    [Serializable]
    public sealed class PullRequest
    {
        public int number;
        public string title = "", url = "";
        public string status = "";   // why it is in that state, in words
        public PullState state;
    }

    // A repo's open pull requests as last heard from GitHub, and why not if they could not be had.
    [Serializable]
    public sealed class RepoPulls
    {
        public string repoRoot = "";
        public string problem = "";   // empty when the list is what GitHub said at the last asking
        public List<PullRequest> open = new List<PullRequest>();
    }

    // Every repo's open pull requests. Field names are the JSON keys, so that a picture can be taken from a file of them.
    // Never changed once made: the reader makes a new one each time it has asked.
    [Serializable]
    public sealed class PullsSnapshot
    {
        public List<RepoPulls> repos = new List<RepoPulls>();

        // Null for a repo that has not been asked about.
        public RepoPulls For(string repoRoot) => repos.Find(r => r.repoRoot == repoRoot);
    }

    // Reads GitHub's answer about a repo's open pull requests. The one place that knows its shape.
    public static class PullRequests
    {
        // The question about a repo: who is asking, and for each open pull request what decides its state. Fifty to a
        // page; `gh --paginate` asks again from $endCursor until there are no more, so a repo with hundreds open has them
        // all. No line breaks or quotes: it goes to `gh` as one argument.
        public const string Query =
            "query($owner:String!,$name:String!,$endCursor:String){viewer{login} repository(owner:$owner,name:$name){" +
            "pullRequests(states:OPEN,first:50,after:$endCursor,orderBy:{field:CREATED_AT,direction:ASC}){pageInfo{hasNextPage endCursor} nodes{" +
            "number title url isDraft reviewDecision mergeable author{login} " +
            "reviewRequests(first:10){nodes{requestedReviewer{... on User{login} ... on Bot{login} ... on Team{name}}}} " +
            "commits(last:1){nodes{commit{statusCheckRollup{state}}}} " +
            "reviews(last:1){nodes{state author{__typename login}}} " +
            "reactions(first:20){nodes{content user{login}}}}}}}";

#pragma warning disable 0649   // filled by JsonUtility
        [Serializable] sealed class Pages { public List<Answer> pages; }
        [Serializable] sealed class Answer { public Data data; public List<Fault> errors; }
        [Serializable] sealed class Fault { public string message; }
        [Serializable] sealed class Data { public Who viewer; public Repo repository; }
        [Serializable] sealed class Who { public string login, name, __typename; }
        [Serializable] sealed class Repo { public Pulls pullRequests; }
        [Serializable] sealed class Pulls { public List<Node> nodes; }
        [Serializable]
        sealed class Node
        {
            public int number;
            public string title, url, reviewDecision, mergeable;
            public bool isDraft;
            public Who author;
            public Reviews reviews;
            public Requests reviewRequests;
            public Commits commits;
            public Reactions reactions;
        }
        [Serializable] sealed class Reviews { public List<Review> nodes; }
        [Serializable] sealed class Review { public string state; public Who author; }
        [Serializable] sealed class Requests { public List<Request> nodes; }
        [Serializable] sealed class Request { public Who requestedReviewer; }
        [Serializable] sealed class Commits { public List<Last> nodes; }
        [Serializable] sealed class Last { public Commit commit; }
        [Serializable] sealed class Commit { public Rollup statusCheckRollup; }
        [Serializable] sealed class Rollup { public string state; }
        [Serializable] sealed class Reactions { public List<Reaction> nodes; }
        [Serializable] sealed class Reaction { public string content; public Who user; }
#pragma warning restore 0649

        // The open pull requests in GitHub's answer: those for the owner first, then those in review, then drafts, each
        // lot in the order they were opened. False, with the reason in words, when the answer is not a list of them.
        public static bool TryParse(string answer, out List<PullRequest> pulls, out string problem)
        {
            pulls = new List<PullRequest>();
            problem = "";
            // One answer, or an array of them: a page each, as `gh --paginate --slurp` hands them over.
            var pages = new List<Answer>();
            try
            {
                if ((answer ?? "").TrimStart().StartsWith("[", StringComparison.Ordinal)) pages = JsonUtility.FromJson<Pages>("{\"pages\":" + answer + "}")?.pages ?? pages;
                else if (JsonUtility.FromJson<Answer>(answer) is Answer one) pages.Add(one);
            }
            catch (ArgumentException) { }
            if (pages.Count == 0) { problem = "GitHub's answer could not be read"; return false; }
            foreach (var a in pages)
            {
                if (a.errors != null && a.errors.Count > 0) { problem = "GitHub: " + a.errors[0].message; pulls.Clear(); return false; }
                var nodes = a.data?.repository?.pullRequests?.nodes;
                if (nodes == null) { problem = "GitHub does not know this repo, or this login may not see it"; pulls.Clear(); return false; }
                string owner = a.data.viewer?.login ?? "";
                foreach (var n in nodes)
                {
                    var p = new PullRequest { number = n.number, title = n.title ?? "", url = n.url ?? "" };
                    Judge(n, owner, out p.state, out p.status);
                    pulls.Add(p);
                }
            }
            pulls.Sort((x, y) => x.state != y.state ? y.state.CompareTo(x.state) : x.number.CompareTo(y.number));
            return true;
        }

        // What a pull request is to the owner, and why. The first thing that holds decides.
        // A review bot says what it is doing by a reaction on the description: eyes while it reads, a thumbs-up when it
        // found nothing. A person's reaction is not a review. What it found, it leaves as a review of comments.
        static void Judge(Node n, string owner, out PullState state, out string status)
        {
            state = PullState.InReview;
            if (n.isDraft) { state = PullState.Draft; status = "Draft"; return; }
            string asked = "";
            foreach (var r in n.reviewRequests?.nodes ?? new List<Request>())
            {
                string who = !string.IsNullOrEmpty(r.requestedReviewer?.login) ? r.requestedReviewer.login : r.requestedReviewer?.name ?? "";
                if (who == owner && owner != "") { state = PullState.ForOwner; status = "Your review is asked for"; return; }
                if (asked == "") asked = who;
            }
            if (n.mergeable == "CONFLICTING") { status = "Conflicts with its base branch"; return; }
            var commits = n.commits?.nodes;
            string checks = commits != null && commits.Count > 0 ? commits[0].commit?.statusCheckRollup?.state ?? "" : "";
            if (checks == "FAILURE" || checks == "ERROR") { status = "Checks failed"; return; }
            if (checks == "PENDING" || checks == "EXPECTED") { status = "Checks are running"; return; }
            bool botReading = false, botClean = false;
            foreach (var r in n.reactions?.nodes ?? new List<Reaction>())
            {
                if (r.user?.login == null || !r.user.login.EndsWith("[bot]", StringComparison.Ordinal)) continue;
                botReading |= r.content == "EYES";
                botClean |= r.content == "THUMBS_UP";
            }
            if (botReading) { status = "The review bot is reviewing"; return; }
            if (n.reviewDecision == "CHANGES_REQUESTED") { status = "Changes are asked for"; return; }
            // Comments left in a review, and no word from the pull request's author since: someone is owed an answer. A
            // review bot that leaves findings says nothing else, so this is the only sign of them. Its later thumbs-up is
            // its own last word and clears it.
            // shortcut: this goes by the latest review alone, so an answer on one thread answers for all of them. Reading
            // every thread's last comment costs some ten times the requests; do it if half-answered reviews get merged.
            var reviews = n.reviews?.nodes;
            var last = reviews != null && reviews.Count > 0 ? reviews[reviews.Count - 1] : null;
            if (last != null && last.state == "COMMENTED" && !string.IsNullOrEmpty(last.author?.login) && last.author.login != n.author?.login)
            {
                bool bot = last.author.__typename == "Bot";
                if (!bot || !botClean) { status = bot ? "The review bot's comments are unanswered" : last.author.login + "'s review is unanswered"; return; }
            }
            if (asked != "") { status = asked + " is asked to review"; return; }
            if (n.reviewDecision == "REVIEW_REQUIRED") { status = "Waits for a review"; return; }
            // GitHub works out whether a pull request merges only once it is asked, and answers UNKNOWN until it has.
            // That is not a yes: the next asking has the answer.
            if (n.mergeable != "MERGEABLE") { status = "GitHub is checking whether it merges"; return; }
            state = PullState.ForOwner;
            status = n.reviewDecision == "APPROVED" ? "Approved: ready to merge" : botClean ? "The review bot found nothing: ready to merge" : "Ready to merge";
        }
    }

    // Runs `gh`, the GitHub command the owner is already signed in to. The Grid keeps no token of its own.
    public static class Gh
    {
        // Which repo a folder is, and which GitHub it is on: github.com, or a company's own.
        public struct Where { public string host, owner, name; }

#pragma warning disable 0649   // filled by JsonUtility
        [Serializable] sealed class View { public string nameWithOwner, url; }
#pragma warning restore 0649
        static readonly System.Text.RegularExpressions.Regex Plain = new System.Text.RegularExpressions.Regex("^[A-Za-z0-9._-]+$");
        static readonly Dictionary<string, Where> known = new Dictionary<string, Where>();   // by folder, once gh has said

        // Reads `gh repo view --json nameWithOwner,url`. False unless it names an owner, a repo and a host in plain
        // characters: they go on a command line next.
        public static bool TryWhere(string answer, out Where repo)
        {
            repo = default;
            View v = null;
            try { v = JsonUtility.FromJson<View>(answer); }
            catch (ArgumentException) { }
            if (v == null || string.IsNullOrEmpty(v.nameWithOwner) || !Uri.TryCreate(v.url, UriKind.Absolute, out var at) || at.Scheme != "https") return false;
            var parts = v.nameWithOwner.Split('/');
            if (parts.Length != 2 || !Plain.IsMatch(parts[0]) || !Plain.IsMatch(parts[1]) || !Plain.IsMatch(at.Host)) return false;
            repo = new Where { host = at.Host, owner = parts[0], name = parts[1] };
            return true;
        }

        // What gh is run with to ask about a repo's open pull requests: every page, the host by name and the repo by name. Left to
        // itself `gh api` takes the repo from the folder but sends the question to its default host, so a repo on a
        // company's GitHub would be asked about on github.com.
        public static string Arguments(Where repo) =>
            "api graphql --paginate --slurp --hostname " + repo.host + " -f \"query=" + PullRequests.Query + "\" -f owner=" + repo.owner + " -f name=" + repo.name;

        public static string[] UsualPlaces() => new[] { "/opt/homebrew/bin/gh", "/usr/local/bin/gh", "/usr/bin/gh" };

        // Why gh gave no answer, from what it wrote: the common cases in plain words, anything else as gh's own first line.
        public static string Reason(string stderr)
        {
            stderr = (stderr ?? "").Trim();
            if (stderr.Contains("not a git repository")) return "This folder is not a git repository.";
            if (stderr.Contains("none of the git remotes") || stderr.Contains("no git remotes")) return "This repo has no GitHub remote.";
            if (stderr.Contains("gh auth login")) return "gh is not signed in to GitHub: run gh auth login in a terminal.";
            return stderr == "" ? "gh gave no answer." : "gh: " + stderr.Split('\n')[0];
        }

        // GitHub's answer about the open pull requests of the repo in this folder. gh says which repo and which host the
        // folder is, from its git remote, once; after that the question goes to that host by name. Throws, saying why in
        // words, when there is no answer to be had.
        public static string Ask(string ghPath, string repoRoot, int timeoutMs = 10000)
        {
            if (string.IsNullOrEmpty(ghPath)) throw new InvalidOperationException("gh was not found: install the GitHub CLI, or set ghPath in settings.json");
            Where repo;
            bool found;
            lock (known) found = known.TryGetValue(repoRoot, out repo);
            if (!found)
            {
                if (!TryWhere(Run(ghPath, "repo view --json nameWithOwner,url", repoRoot, timeoutMs), out repo)) throw new InvalidOperationException("gh could not say which repo this folder is.");
                lock (known) known[repoRoot] = repo;
            }
            return Run(ghPath, Arguments(repo), repoRoot, timeoutMs);
        }

        static string Run(string ghPath, string arguments, string folder, int timeoutMs)
        {
            var psi = new ProcessStartInfo(ghPath, arguments)
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = folder,
            };
            // An app started from the Finder has a bare PATH; gh runs git to read the remote.
            psi.EnvironmentVariables["PATH"] = (psi.EnvironmentVariables["PATH"] ?? "/usr/bin:/bin") + ":/opt/homebrew/bin:/usr/local/bin";
            psi.EnvironmentVariables["GH_PROMPT_DISABLED"] = "1";
            psi.EnvironmentVariables["NO_COLOR"] = "1";
            using (var p = Process.Start(psi))
            {
                var stdout = p.StandardOutput.ReadToEndAsync();
                var stderr = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(timeoutMs))
                {
                    try { p.Kill(); } catch (InvalidOperationException) { }
                    throw new TimeoutException("gh did not answer within " + timeoutMs / 1000 + " s");
                }
                p.WaitForExit();   // let the output readers finish
                // A GraphQL error comes on standard output with a failing exit code: the answer says more than the code.
                if (p.ExitCode != 0 && stdout.Result.Trim() == "") throw new InvalidOperationException(Reason(stderr.Result));
                return stdout.Result;
            }
        }
    }

    // Asks about every repo on the map, every few minutes, on a thread of its own, and keeps the latest answers.
    public sealed class PullReader : IDisposable
    {
        public const int EveryMs = 180000;

        readonly Func<string, string> ask;   // a repo's folder in, GitHub's answer out; throws when there is none
        readonly Func<bool> wanted;          // false while the owner has asking turned off; looked at before every round
        readonly ManualResetEventSlim stop = new ManualResetEventSlim(false);
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        volatile PullsSnapshot latest = new PullsSnapshot();
        volatile string[] watched = new string[0];
        Thread thread;

        public PullReader(Func<string, string> ask, Func<bool> wanted = null)
        {
            this.ask = ask;
            this.wanted = wanted;
        }

        public PullsSnapshot Latest => latest;

        // Asks once about each of these repos and makes the new snapshot, unless asking is turned off. A repo that cannot be asked keeps the list it
        // had, with the reason beside it. A repo not among these is dropped.
        public void Refresh(IEnumerable<string> repoRoots)
        {
            // Turned off: nothing is asked, and what was heard before goes, so no depot shows an answer nobody is keeping up.
            if (wanted != null && !wanted())
            {
                if (latest.repos.Count > 0) latest = new PullsSnapshot();
                return;
            }
            var before = latest;
            var next = new PullsSnapshot();
            foreach (var root in repoRoots)
            {
                if (string.IsNullOrEmpty(root) || next.For(root) != null) continue;
                var repo = new RepoPulls { repoRoot = root };
                try
                {
                    if (!PullRequests.TryParse(ask(root), out repo.open, out repo.problem)) repo.open = before.For(root)?.open ?? repo.open;
                }
                catch (Exception e)
                {
                    repo.problem = e.Message;
                    repo.open = before.For(root)?.open ?? repo.open;
                }
                next.repos.Add(repo);
            }
            latest = next;
        }

        // The repos to ask about from now on: those on the map. A change is asked about at once, not at the next round.
        public void Watch(List<string> repoRoots)
        {
            var now = watched;
            bool same = now.Length == repoRoots.Count;
            for (int i = 0; same && i < now.Length; i++) same = now[i] == repoRoots[i];
            if (same) return;
            watched = repoRoots.ToArray();
            wake.Set();
        }

        // A round now, not at the next three minutes: the owner's settings were read again.
        public void Wake() => wake.Set();

        public void Start()
        {
            if (thread != null) return;
            thread = new Thread(() =>
            {
                while (!stop.IsSet)
                {
                    try { Refresh(watched); }
                    catch (Exception) { }   // never let the loop die; the next round asks again
                    WaitHandle.WaitAny(new[] { stop.WaitHandle, wake }, EveryMs);
                }
            }) { IsBackground = true, Name = "TheGrid.PullReader" };
            thread.Start();
        }

        public void Dispose()
        {
            stop.Set();
            if (thread != null && thread != Thread.CurrentThread) thread.Join(12000);
            thread = null;
        }
    }
}
