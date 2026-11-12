using System;
using System.Collections.Generic;
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
    }
}
