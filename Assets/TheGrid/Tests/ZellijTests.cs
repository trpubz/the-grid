using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace TheGrid.Core.Tests
{
    // Where a session already lives: the zellij session and tab its process runs in.
    public class ZellijTests
    {
        // What `ps eww` prints for one process: its command, then its environment, on one line.
        const string InAPane = "claude --resume HOME=/home/me XZELLIJ_PANE_ID=9 ZELLIJ=0 ZELLIJ_PANE_ID=2 TERM=xterm-256color ZELLIJ_SESSION_NAME=main PATH=/usr/bin:/bin";
        const string Bare = "claude HOME=/home/me TERM=xterm-256color PATH=/usr/bin:/bin";
        // What `zellij action list-panes --tab --json` prints, cut down: a plugin and a terminal can have the same id.
        const string Panes = "[{\"id\":2,\"is_plugin\":true,\"title\":\"tab-bar\",\"tab_name\":\"shell\",\"exit_status\":null},"
            + "{\"id\":0,\"is_plugin\":false,\"title\":\"zsh\",\"tab_name\":\"shell\",\"exit_status\":null},"
            + "{\"id\":2,\"is_plugin\":false,\"title\":\"claude\",\"tab_name\":\"agents\",\"exit_status\":null}]";

        const string InAPaneOfATerminal = InAPane + " __CFBundleIdentifier=com.example.term";
        // The same, from a Zellij that gives tab ids: terminal pane 2 is in tab 3, a plugin pane 2 in tab 0.
        const string PanesWithIds = "[{\"id\":2,\"is_plugin\":true,\"title\":\"tab-bar\",\"tab_id\":0,\"tab_name\":\"shell\"},"
            + "{\"id\":2,\"is_plugin\":false,\"title\":\"claude\",\"tab_id\":3,\"tab_name\":\"agents\"}]";

        [Test]
        public void TheSessionAndPaneAreReadFromTheProcessEnvironmentAndNothingElseIs()
        {
            Assert.IsTrue(Zellij.TryPane(InAPane, out var session, out int pane));
            Assert.AreEqual("main", session);
            Assert.AreEqual(2, pane);
            Assert.IsFalse(Zellij.TryPane(Bare, out _, out _));
            Assert.IsFalse(Zellij.TryPane("", out _, out _));
            Assert.IsFalse(Zellij.TryPane("claude ZELLIJ_SESSION_NAME=main", out _, out _), "a session with no pane is no place");
        }

        [Test]
        public void TheTabIsThatOfTheTerminalPaneWithThatNumber()
        {
            Assert.AreEqual("agents", Zellij.TabOf(Panes, 2));
            Assert.AreEqual("shell", Zellij.TabOf(Panes, 0));
            Assert.AreEqual("", Zellij.TabOf(Panes, 7));
            Assert.AreEqual("", Zellij.TabOf("not json", 2));
            Assert.AreEqual("", Zellij.TabOf("", 2));
        }

        [Test]
        public void WhereASessionLivesIsItsZellijSessionAndTab()
        {
            var asked = new List<string>();
            string Run(string exe, string arguments)
            {
                asked.Add(exe + " " + arguments);
                return exe == "/bin/ps" ? InAPane : Panes;
            }
            Assert.AreEqual("main › agents", Zellij.Where(4242, "/opt/zellij", Run));
            CollectionAssert.AreEqual(new[] { "/bin/ps eww -o command= -p 4242", "/opt/zellij --session main action list-panes --tab --json" }, asked);

            // zellij not found, or not answering: the session's name is still a pointer
            Assert.AreEqual("main", Zellij.Where(4242, "", Run));
            Assert.AreEqual("main", Zellij.Where(4242, "/opt/zellij", (exe, a) => exe == "/bin/ps" ? InAPane : throw new InvalidOperationException("no such session")));
            // not in zellij, the process gone, or no process at all: nothing, and for no process nothing is run
            Assert.AreEqual("", Zellij.Where(4242, "/opt/zellij", (exe, a) => Bare));
            Assert.AreEqual("", Zellij.Where(4242, "/opt/zellij", (exe, a) => throw new TimeoutException()));
            asked.Clear();
            Assert.AreEqual("", Zellij.Where(0, "/opt/zellij", Run));
            Assert.AreEqual(0, asked.Count);
        }

        [Test]
        public void TheTabsIdIsThatOfTheTerminalPaneAndThereIsNoneWhenTheListDoesNotGiveIds()
        {
            Assert.AreEqual(3, Zellij.TabIdOf(PanesWithIds, 2));
            Assert.AreEqual(-1, Zellij.TabIdOf(PanesWithIds, 77));
            Assert.AreEqual(-1, Zellij.TabIdOf(Panes, 2));          // the older list: names, no ids. Not tab 0.
            Assert.AreEqual(-1, Zellij.TabIdOf("not json", 2));
        }

        [TestCase(InAPane + " __CFBundleIdentifier=com.example.term", "com.example.term")]
        [TestCase(InAPane, "")]
        [TestCase(InAPane + " __CFBundleIdentifier=bad;id", "")]
        [TestCase(InAPane + " X__CFBundleIdentifier=com.example.other", "")]
        public void TheTerminalAppIsReadFromTheSessionsOwnProcess(string ps, string app) => Assert.AreEqual(app, Zellij.TerminalApp(ps));

        [Test]
        public void AJumpTellsZellijToGoToTheSessionsTabAndNamesTheTerminal()
        {
            var ran = new List<string>();
            string Run(string exe, string args) { ran.Add(exe + " " + args); return exe == "/bin/ps" ? InAPaneOfATerminal : args.Contains("list-panes") ? PanesWithIds : ""; }
            Assert.IsTrue(Zellij.Jump(41, "/z", Run, out var app));
            Assert.AreEqual("com.example.term", app);
            Assert.AreEqual("/z --session main action go-to-tab-by-id 3", ran.Last());
            Assert.IsFalse(ran.Any(r => r.Contains("focus-pane")));   // from outside it moves nothing the owner sees
        }

        [Test]
        public void ThereIsNoJumpForASessionNotInZellijWithNoProcessWithNoTabIdOrWhenAProgramFails()
        {
            var ran = new List<string>();
            Func<string, string, string> bare = (exe, args) => { ran.Add(args); return Bare; };
            Assert.IsFalse(Zellij.Jump(41, "/z", bare, out _)); Assert.AreEqual(1, ran.Count);      // ps only: zellij is not run
            Assert.IsFalse(Zellij.Jump(0, "/z", bare, out _)); Assert.AreEqual(1, ran.Count);       // a background session: nothing is run
            Assert.IsFalse(Zellij.Jump(41, "", bare, out _));                                        // no zellij on this Mac
            Func<string, string, string> old = (exe, args) => exe == "/bin/ps" ? InAPane : args.Contains("list-panes") ? Panes : throw new InvalidOperationException("must not go to a tab");
            Assert.IsFalse(Zellij.Jump(41, "/z", old, out _));                                       // no tab id: no go-to
            Func<string, string, string> gone = (exe, args) => throw new InvalidOperationException("no such process");
            Assert.IsFalse(Zellij.Jump(41, "/z", gone, out _));                                      // the process went away: no exception
        }

        // What `ps -ao tty=,command=` prints, cut down.
        const string Terminals = "ttys001  zellij attach main-two\nttys003  /opt/bin/zellij attach main\nttys004  vi zellij main\n"
            + "ttys005  zellij --session main action list-panes\n??       zellij attach main\nttys006  -zsh\n\nttys007  zellij a -c main\nttys008  zellij -s main\nttys009  zellij\n";

        [TestCase("main", "ttys003 ttys007 ttys008")]   // attached twice, and where it was started
        [TestCase("main-two", "ttys001")]               // one name begins with the other
        [TestCase("mai", "")]
        [TestCase("none", "")]
        [TestCase("action", "")]
        public void TheTerminalsThatShowASessionAreItsZellijClients(string session, string ttys) =>
            Assert.AreEqual(ttys, string.Join(" ", Zellij.ClientTtys(Terminals, session)));

        const string InGhostty = InAPane + " __CFBundleIdentifier=" + Zellij.Ghostty;

        static List<string> Jumped(string ps, string terminals, Func<string> script, out bool told, out string app)
        {
            var ran = new List<string>();
            string Run(string exe, string args)
            {
                ran.Add(exe + " " + args);
                if (exe == "/usr/bin/osascript") return script();
                return exe != "/bin/ps" ? args.Contains("list-panes") ? PanesWithIds : "" : args.StartsWith("-ao") ? terminals : ps;
            }
            told = Zellij.Jump(41, "/z", Run, out app);
            return ran;
        }

        [Test]
        public void GhosttyIsAskedToFocusTheTerminalThatShowsTheSessionOnceZellijIsOnTheTab()
        {
            var ran = Jumped(InGhostty, Terminals, () => "", out bool told, out var app);
            Assert.IsTrue(told);
            Assert.AreEqual(Zellij.Ghostty, app);
            Assert.AreEqual("/z --session main action go-to-tab-by-id 3", ran[ran.Count - 3]);
            Assert.AreEqual("/bin/ps -ao tty=,command=", ran[ran.Count - 2]);
            StringAssert.StartsWith("/usr/bin/osascript -e ", ran.Last());
            StringAssert.Contains("{\\\"/dev/ttys003\\\", \\\"/dev/ttys007\\\", \\\"/dev/ttys008\\\"}", ran.Last());
        }

        [Test]
        public void ARefusalNoClientOrAnotherTerminalLeavesTheJumpAsItWas()
        {
            Jumped(InGhostty, Terminals, () => throw new InvalidOperationException("not allowed"), out bool told, out var app);
            Assert.IsTrue(told); Assert.AreEqual(Zellij.Ghostty, app);
            var ran = Jumped(InGhostty, "ttys001  zellij attach main-two\n", () => "", out told, out app);
            Assert.IsTrue(told); Assert.AreEqual(Zellij.Ghostty, app);
            Assert.IsFalse(ran.Any(r => r.Contains("osascript")));
            ran = Jumped(InAPaneOfATerminal, Terminals, () => "", out told, out app);
            Assert.IsTrue(told); Assert.AreEqual("com.example.term", app);
            Assert.IsFalse(ran.Any(r => r.Contains("osascript") || r.Contains("-ao")));   // only Ghostty is asked, and only for it are the terminals looked through
        }
    }
}
