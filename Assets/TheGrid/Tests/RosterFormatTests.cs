using NUnit.Framework;

namespace TheGrid.Core.Tests
{
    public class RosterFormatTests
    {
        [TestCase(0L, 59000L, "0m")]
        [TestCase(0L, 240000L, "4m")]
        [TestCase(0L, 3600000L, "1h 0m")]
        [TestCase(0L, 4320000L, "1h 12m")]
        [TestCase(5000L, 1000L, "0m")]
        public void Elapsed(long since, long now, string expected) => Assert.AreEqual(expected, RosterFormat.Elapsed(since, now));

        [TestCase("https://example.invalid/demo/alpha/pull/7", true)]
        [TestCase("http://example.invalid/x", false)]
        [TestCase("file:///etc/passwd", false)]
        [TestCase("javascript:alert(1)", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void OnlyHttpsLinksAreOpened(string url, bool expected) => Assert.AreEqual(expected, RosterFormat.IsWebLink(url));

        // a background session is attached to by its short id; any other is resumed by its session id
        [TestCase("background", "bg000001", "0000-4", "/work/beta", "/home/me/.claude", "cd /work/beta && claude attach bg000001")]
        [TestCase("interactive", "", "0000-4", "/work/alpha", "/home/me/.claude", "cd /work/alpha && claude --resume 0000-4")]
        [TestCase("interactive", "", "0000-4", "/work/alpha", "/home/me/.claude/", "cd /work/alpha && claude --resume 0000-4")]
        // an account other than the default one is named, as a person's shell would name it
        [TestCase("interactive", "", "0000-4", "/work/alpha", "/home/me/.claude-two", "cd /work/alpha && CLAUDE_CONFIG_DIR=/home/me/.claude-two claude --resume 0000-4")]
        // whatever a shell would not take as one plain word is quoted
        [TestCase("interactive", "", "0000-4", "/work/my app/it's", "/home/me/.claude", "cd '/work/my app/it'\\''s' && claude --resume 0000-4")]
        [TestCase("background", "x; rm -rf ~", "0000-4", "/work/beta", "/home/me/.claude", "cd /work/beta && claude attach 'x; rm -rf ~'")]
        // no folder: no cd. Nothing to open it by: no command
        [TestCase("interactive", "", "0000-4", "", "/home/me/.claude", "claude --resume 0000-4")]
        [TestCase("background", "", "0000-4", "/work/beta", "/home/me/.claude", "")]
        [TestCase("interactive", "", "", "/work/alpha", "/home/me/.claude", "")]
        public void TheCommandThatOpensAConversation(string kind, string id, string sessionId, string cwd, string configDir, string expected) =>
            Assert.AreEqual(expected, RosterFormat.Enter(kind, id, sessionId, cwd, configDir, "/home/me", false));

        // With more than one account, the terminal the command is pasted into may have another account's folder set: the
        // default account, which is the one with none set, then says so.
        [TestCase("/home/me/.claude", "cd /work/alpha && env -u CLAUDE_CONFIG_DIR claude --resume 0000-4")]
        [TestCase("/home/me/.claude-two", "cd /work/alpha && CLAUDE_CONFIG_DIR=/home/me/.claude-two claude --resume 0000-4")]
        public void WithSeveralAccountsTheCommandNamesTheDefaultOneToo(string configDir, string expected) =>
            Assert.AreEqual(expected, RosterFormat.Enter("interactive", "", "0000-4", "/work/alpha", configDir, "/home/me", true));

        const string Cloud = "cse_01AbCdEfGhJkMnPqRsTuVwXy";

        // the id itself, of either kind, with whatever was copied around it
        [TestCase("cse_01AbCdEfGhJkMnPqRsTuVwXy", Cloud)]
        [TestCase("  cse_01AbCdEfGhJkMnPqRsTuVwXy\n", Cloud)]
        [TestCase("session_01AbCdEfGhJkMnPqRsTuVwXy", "session_01AbCdEfGhJkMnPqRsTuVwXy")]
        // the line a cloud session prints when asked how to bring it to a terminal
        [TestCase("You must run claude --teleport cse_01AbCdEfGhJkMnPqRsTuVwXy from a checkout of demo/alpha.", Cloud)]
        // a link to the session, with or without what follows the id
        [TestCase("https://claude.ai/code/session_01AbCdEfGhJkMnPqRsTuVwXy?from=cli&m=0", "session_01AbCdEfGhJkMnPqRsTuVwXy")]
        [TestCase("claude.ai/code/cse_01AbCdEfGhJkMnPqRsTuVwXy", Cloud)]
        // the link the browser shows carries a longer id of another kind: the session's id is its first 24 characters
        [TestCase("https://claude.ai/code/cshk_01AbCdEfGhJkMnPqRsTuVwXyZz11Yy22Xx33Ww44Vv55Uu66", Cloud)]
        // nothing that names a session
        [TestCase("", "")]
        [TestCase(null, "")]
        [TestCase("https://claude.ai/chat/0000-4", "")]
        [TestCase("https://claude.ai/code/cshk_tooShort", "")]
        [TestCase("recse_01AbCdEfGhJkMnPqRsTuVwXy", "")]
        [TestCase("cse_; rm -rf ~", "")]
        public void TheCloudSessionThatWasPasted(string pasted, string expected) => Assert.AreEqual(expected, RosterFormat.CloudId(pasted));

        // in the repo's folder, under the account that owns the session, by the same rule as the command of a card
        [TestCase("/work/alpha", "/home/me/.claude", false, "cd /work/alpha && claude --teleport " + Cloud)]
        [TestCase("/work/alpha", "/home/me/.claude", true, "cd /work/alpha && env -u CLAUDE_CONFIG_DIR claude --teleport " + Cloud)]
        [TestCase("/work/alpha", "/home/me/.claude-two", false, "cd /work/alpha && CLAUDE_CONFIG_DIR=/home/me/.claude-two claude --teleport " + Cloud)]
        [TestCase("/work/my app", "/home/me/.claude-two", true, "cd '/work/my app' && CLAUDE_CONFIG_DIR=/home/me/.claude-two claude --teleport " + Cloud)]
        public void TheCommandThatBringsACloudSessionHere(string cwd, string configDir, bool several, string expected) =>
            Assert.AreEqual(expected, RosterFormat.Teleport(Cloud, cwd, configDir, "/home/me", several, false));

        // The copy is written with no terminal: one short question, and nothing the answer may do.
        [Test]
        public void TheCommandThatOnlyDownloadsACloudSession() =>
            Assert.AreEqual("cd /work/alpha && claude --teleport " + Cloud + " -p 'Reply with the single word: ok. Do nothing else.' --tools '' --permission-mode plan",
                RosterFormat.Teleport(Cloud, "/work/alpha", "/home/me/.claude", "/home/me", false, true));

        [Test]
        public void NoCloudSessionNoCommand() => Assert.AreEqual("", RosterFormat.Teleport("", "/work/alpha", "/home/me/.claude", "/home/me", false, false));

        // In a code block every step of a command has a line of its own; what is copied stays the one line.
        [TestCase("cd /work/alpha && claude --teleport cse_01", "cd /work/alpha &&\nclaude --teleport cse_01")]
        [TestCase("claude --teleport cse_01", "claude --teleport cse_01")]
        [TestCase("", "")]
        public void ACommandAsItIsShown(string command, string expected) => Assert.AreEqual(expected, RosterFormat.Shown(command));

        // An account's folder from its name, for a snapshot, which has names only: the reverse of AccountLabel.
        [TestCase("claude", "/home/demo/.claude")]
        [TestCase("two", "/home/demo/.claude-two")]
        public void AnAccountsFolderFromItsName(string label, string expected)
        {
            Assert.AreEqual(expected, RosterBuilder.ConfigDir(label, "/home/demo"));
            Assert.AreEqual(label, RosterBuilder.AccountLabel(expected));
        }
    }
}
