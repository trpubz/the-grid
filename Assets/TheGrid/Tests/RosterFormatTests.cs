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
            Assert.AreEqual(expected, RosterFormat.Enter(kind, id, sessionId, cwd, configDir, "/home/me"));
    }
}
