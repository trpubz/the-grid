# What a blocked session needs: Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A session that waits for the owner says what it needs, in its own words: on the desktop card when the pointer is on its row, and in The Grid's window.

**Architecture:** The roster row gets two fields, `needs` and `needsDetail`. A background session's come from its job state file; an interactive session's from the open question in its transcript. `Panels` and `ProgramCard` (plain code in `Assets/TheGrid/Core`, tests first) turn them into words and layout; the two shells (`WorldPanels`, `DesktopProgram`) only draw.

**Tech Stack:** Unity 6000.4.3f1, C#, Mono, `JsonUtility`, NUnit EditMode tests (`TheGrid.Core.Tests`), AppKit through `Assets/TheGrid/Cocoa.cs`.

**Spec:** Linear GRID-50 (its description is the design). Not in the repo: it quotes real session names.

## Global Constraints

- Read only. Nothing is written under any Claude config folder, and The Grid never types into a conversation.
- No real session name, repo, account or need text in the repo. Fixtures and test strings are made up (the ones in this plan are).
- Everything that decides is in `Assets/TheGrid/Core` with a test written first and seen failing. `DesktopProgram.cs`, `WorldPanels.cs` and `Cocoa.cs` hold no rules.
- The job state file's format is the CLI's own and not documented: anything unexpected in it means "no need". Never an exception, never an error on screen.
- macOS player only for the desktop program; nothing new runs in the editor or in a picture run.
- No new dependency. No new setting.
- Builders never launch the app. Checks on the built app are the controller's.
- Tests: `/Applications/Unity/Hub/Editor/6000.4.3f1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath . -runTests -testPlatform EditMode -assemblyNames TheGrid.Core.Tests -testResults Logs/editmode.xml -logFile Logs/editmode.log`, then read `total`, `passed`, `failed` from `Logs/editmode.xml`. No XML means a compile error: `grep "error CS" Logs/editmode.log`. The suite is 674 passing before Task 1.
- Commit a new file's `.meta` with it. Commit messages: `GRID-50: <what is now true>`, ending with the line `Co-Authored-By: Claude Fable 5.2 <noreply@anthropic.com>`.
- Comments say why, in one line, in the plain words the code base uses.

## Review Focus

1. A job id from `claude agents` that holds `/`, `\` or `..`: no file outside `jobs/` is read, and there is no need. (Task 1)
2. A state file that is empty, not JSON, larger than 1 MB, or whose `needs` is a number or an object: no need, no exception. (Task 1)
3. Need text with newlines, tabs, 5,000 letters, or an emoji at the cut: one line, at most 300 letters, never half an emoji. (Task 1)
4. A word longer than a line (a link) when wrapping: broken across lines, never wider than the line, never an endless loop. (Task 4)
5. The roster shrinking while a row is open, so the open index is past the last row: the card draws closed, no exception. (Tasks 4 and 5)

---

### Task 1: A blocked background session's need is in its roster row

**Files:**
- Create: `Assets/TheGrid/Core/Needs.cs`, `Assets/TheGrid/Tests/NeedsTests.cs`
- Modify: `Assets/TheGrid/Core/Roster.cs` (`AgentRow`, `RosterBuilder.Build`'s offline block, `RosterBuilder.Update`), `docs/roster-snapshot.md`
- Test: `Assets/TheGrid/Tests/NeedsTests.cs`, `Assets/TheGrid/Tests/RosterTests.cs`

**Interfaces:**
- Produces:
  - `AgentRow.needs`, `AgentRow.needsDetail` (strings, `""` when none).
  - `Needs.MaxLetters = 300`.
  - `static string Needs.Clean(string text)`: one line (every run of white space becomes one space, ends trimmed), at most `MaxLetters` letters, cut with `…`, never inside a surrogate pair; `""` for null.
  - `static bool Needs.TryOfJob(IReadOnlyList<string> configDirs, string id, out string needs, out string detail)`: reads `<dir>/jobs/<id>/state.json` in each folder in order; the first whose `needs` is not empty wins; both outs cleaned; `false` and `""`, `""` when none.

- [ ] **Step 1: Write the failing tests** in `NeedsTests.cs` (temp folder per test, deleted in `TearDown`, as `SessionFilesTests` does):

```csharp
const string Blocked = "{\"state\":\"blocked\",\"needs\":\"approve the made-up plan\",\"detail\":\"waiting on a made-up answer\"}";
string one, two;   // two config folders under the temp folder
void Job(string dir, string id, string json) { Directory.CreateDirectory(Path.Combine(dir, "jobs", id)); File.WriteAllText(Path.Combine(dir, "jobs", id, "state.json"), json); }

[Test] public void ANeedIsOneLine() => Assert.AreEqual("merge the pull request", Needs.Clean("  merge\tthe\n pull   request "));
[Test] public void NothingGivesNothing() => Assert.AreEqual("", Needs.Clean(null));
[Test] public void ALongNeedIsCutWithAnEllipsis()
{
    var got = Needs.Clean(new string('a', 5000));
    Assert.AreEqual(300, got.Length);
    Assert.IsTrue(got.EndsWith("…"));
}
[Test] public void ANeedIsNeverCutInsideAnEmoji()
{
    var got = Needs.Clean(new string('a', 298) + "😀bbb");
    Assert.IsFalse(char.IsHighSurrogate(got[got.Length - 2]), "half an emoji is left before the ellipsis");
    Assert.LessOrEqual(got.Length, 300);
}
[Test] public void ABlockedJobSaysWhatItNeedsAndWhy()
{
    Job(one, "ab12cd34", Blocked);
    Assert.IsTrue(Needs.TryOfJob(new[] { one, two }, "ab12cd34", out var needs, out var detail));
    Assert.AreEqual("approve the made-up plan", needs);
    Assert.AreEqual("waiting on a made-up answer", detail);
}
[Test] public void TheJobMayLiveInAnotherConfigFolder()
{
    Job(one, "ab12cd34", "{\"state\":\"blocked\"}");   // listed here, with nothing asked
    Job(two, "ab12cd34", Blocked);
    Assert.IsTrue(Needs.TryOfJob(new[] { one, two }, "ab12cd34", out var needs, out _));
    Assert.AreEqual("approve the made-up plan", needs);
}
[TestCase("")] [TestCase("not json")] [TestCase("{\"needs\": 5}")] [TestCase("{\"needs\": {\"a\": 1}}")] [TestCase("{\"needs\": \"\"}")]
public void AFileThatSaysNothingUsableIsNoNeed(string json)
{
    Job(one, "ab12cd34", json);
    Assert.IsFalse(Needs.TryOfJob(new[] { one }, "ab12cd34", out var needs, out var detail));
    Assert.AreEqual("", needs); Assert.AreEqual("", detail);
}
[Test] public void NoFileIsNoNeed() => Assert.IsFalse(Needs.TryOfJob(new[] { one, two }, "ab12cd34", out _, out _));
[Test] public void AFileOverAMegabyteIsNotRead()
{
    Job(one, "ab12cd34", "{\"needs\":\"x\",\"pad\":\"" + new string('p', 1 << 20) + "\"}");
    Assert.IsFalse(Needs.TryOfJob(new[] { one }, "ab12cd34", out _, out _));
}
[TestCase("../outside")] [TestCase("a/b")] [TestCase("a\\b")] [TestCase("..")] [TestCase("")]
public void AnIdThatIsNotAPlainNameReadsNothing(string id)
{
    Directory.CreateDirectory(Path.Combine(one, "outside"));
    File.WriteAllText(Path.Combine(one, "outside", "state.json"), Blocked);   // where "../outside" would land
    Assert.IsFalse(Needs.TryOfJob(new[] { one }, id, out _, out _));
}
```

And in `RosterTests.cs` (the `Background(sessionId, configDir)` helper there has `Id = "bg000009"`, `State = "blocked"`):

```csharp
[Test]
public void ABlockedBackgroundSessionCarriesWhatItNeeds()
{
    settings.configDirs = new List<string> { cfg };
    Directory.CreateDirectory(Path.Combine(cfg, "jobs", "bg000009"));
    File.WriteAllText(Path.Combine(cfg, "jobs", "bg000009", "state.json"), "{\"needs\":\"approve the made-up plan\",\"detail\":\"waiting on a made-up answer\"}");
    var builder = new RosterBuilder(settings);
    var s = Background("s-9", cfg);
    var row = builder.Build(new[] { s }, None, Now).agents[0];
    Assert.AreEqual("approve the made-up plan", row.needs);
    Assert.AreEqual("waiting on a made-up answer", row.needsDetail);
    s.State = "working";   // no longer waiting: what it needed is not shown beside a session at work
    Assert.AreEqual("", builder.Build(new[] { s }, None, Now + 2000).agents[0].needs);
    s.State = "blocked";
    builder.Build(new[] { s }, None, Now + 4000);
    Assert.AreEqual("", builder.Build(new LiveSession[0], None, Now + 6000).agents[0].needs);   // offline
}
```

- [ ] **Step 2: Run the tests.** Expected: no results file, `error CS` for `Needs` and `AgentRow.needs`.

- [ ] **Step 3: Implement.**
  - `Needs.cs`: the two functions above. The id is used only when every character is a letter, a digit or `-`. The file is opened as `SessionFiles` opens its files (read, shared with a writer), and skipped when longer than 1 MB. Parsed with `JsonUtility` into a private class with `needs` and `detail`; `ArgumentException`, `IOException` and `UnauthorizedAccessException` mean "no need".
  - `AgentRow`: `public string needs = "", needsDetail = "";` with a one-line comment.
  - `RosterBuilder.Update`: clear both fields, then for `AgentState.BackgroundBlocked` call `Needs.TryOfJob` with the session's own config folder first and the rest of `settings.EffectiveConfigDirs(home)` after it, and `s.Id`.
  - `RosterBuilder.Build`, where a row goes offline: clear both fields.
  - `docs/roster-snapshot.md`: two rows in the agent table, after `activity`: what each field is, where it comes from, empty when none.

- [ ] **Step 4: Run the tests.** Expected: all pass, `failed 0`, total above 674.

- [ ] **Step 5: Commit** `GRID-50: a blocked background session's row carries what it needs`.

---

### Task 2: An interactive session waiting on a question carries the question

**Files:**
- Modify: `Assets/TheGrid/Core/SessionFiles.cs` (`TranscriptMeta`, the private `BlockInput`, `TryActivity`, `ReadTranscriptTail`), `Assets/TheGrid/Core/Roster.cs` (`Memory`, `Update`), `docs/roster-snapshot.md`
- Test: `Assets/TheGrid/Tests/SessionFilesTests.cs`, `Assets/TheGrid/Tests/RosterTests.cs`

**Interfaces:**
- Consumes: `Needs.Clean`, `AgentRow.needs` (Task 1).
- Produces: `TranscriptMeta.Question` (string): when the newest record of the agent's own (the one that already decides `Activity`) ends in a call of the tool named `AskUserQuestion`, the first question's text, cleaned, followed by `" +N"` when the call holds N more; `""` otherwise.

- [ ] **Step 1: Write the failing tests.** In `SessionFilesTests.cs`, written the way its other tail tests write a transcript:

```csharp
const string Asks = "{\"type\":\"assistant\",\"message\":{\"content\":[{\"type\":\"tool_use\",\"name\":\"AskUserQuestion\",\"input\":{\"questions\":[" +
    "{\"question\":\"Which made-up colour?\",\"header\":\"Colour\",\"options\":[{\"label\":\"Red\",\"description\":\"\"}],\"multiSelect\":false}," +
    "{\"question\":\"And a second made-up one?\"}]}}]}}\n";
const string Edits = "{\"type\":\"assistant\",\"message\":{\"content\":[{\"type\":\"tool_use\",\"name\":\"Edit\",\"input\":{}}]}}\n";

[Test] public void TheOpenQuestionIsTheFirstOneAndACountOfTheRest()   // transcript: Edits, then Asks
    => Assert.AreEqual("Which made-up colour? +1", /* ReadTranscriptTail(path) */ meta.Question);
[Test] public void AQuestionTheAgentHasMovedOnFromIsNotOpen()        // transcript: Asks, then Edits
    => Assert.AreEqual("", meta.Question);
[Test] public void ASubagentsQuestionIsNotTheSessions()              // transcript: Edits, then Asks with "isSidechain":true
    => Assert.AreEqual("", meta.Question);
[Test] public void AQuestionCallWithNoQuestionsIsNoQuestion()        // "input":{"questions":[]} and "input":{}
    => Assert.AreEqual("", meta.Question);
```

In `RosterTests.cs` (uses its `transcript`, `S2`, `cfg`):

```csharp
[Test]
public void ASessionWaitingOnAQuestionCarriesTheQuestion()
{
    var s = new LiveSession { SessionId = S2, Name = "alpha-02", Cwd = "/work/alpha", Kind = "interactive", Status = "waiting", WaitingFor = "input needed", State = "", ConfigDir = cfg };
    var builder = new RosterBuilder(settings);
    File.AppendAllText(transcript, Assistant("AskUserQuestion", "{\"questions\":[{\"question\":\"Which made-up colour?\"}]}"));
    Assert.AreEqual("Which made-up colour?", builder.Build(new[] { s }, None, Now).agents[0].needs);
    s.Status = "busy"; s.WaitingFor = "";   // answered and at work; the transcript has not moved yet
    Assert.AreEqual("", builder.Build(new[] { s }, None, Now + 2000).agents[0].needs);
}
```

- [ ] **Step 2: Run the tests.** Expected: `error CS` for `TranscriptMeta.Question`.

- [ ] **Step 3: Implement.** `BlockInput` gains `questions`, a list of a private class with `question`. `TryActivity` also gives the question of the record it parsed. `Memory` keeps it beside `Activity`; `Update` sets `row.needs` from it only for `AgentState.BlockedOnQuestion`. `docs/roster-snapshot.md`: the `needs` row names this second source.

- [ ] **Step 4: Run the tests.** Expected: all pass.

- [ ] **Step 5: Commit** `GRID-50: a session waiting on a question carries the question`.

---

### Task 3: The Grid's window says what is needed

**Files:**
- Modify: `Assets/TheGrid/Core/Panels.cs` (`BotCard`, `NeedsYou`, `Card`), `Assets/TheGrid/WorldPanels.cs` (the bot's card), one fixture under `fixtures/world/` used by picture runs
- Test: `Assets/TheGrid/Tests/PanelsTests.cs`

**Interfaces:**
- Consumes: `AgentRow.needs`, `AgentRow.needsDetail`.
- Produces: `NeedsYouRow.what` is the need when the row has one, else the state's words as today. `BotCard.needs`, `BotCard.needsDetail` (strings, `""` when none).

- [ ] **Step 1: Write the failing tests** in `PanelsTests.cs` (its `Only(AgentRow)` helper makes a one-row snapshot):

```csharp
static AgentRow Waiting(string needs, string detail) => new AgentRow { sessionId = "s-1", account = "claude", name = "made-up-job", state = "BackgroundBlocked", needs = needs, needsDetail = detail };

[Test] public void ARowThatSaysWhatItNeedsSaysItInPlaceOfTheStatesWords() =>
    Assert.AreEqual("approve the made-up plan", Panels.NeedsYou(Only(Waiting("approve the made-up plan", "")))[0].what);
[Test] public void ARowThatSaysNothingKeepsTheStatesWords() =>
    Assert.AreEqual("Blocked in the background", Panels.NeedsYou(Only(Waiting("", "")))[0].what);
[Test] public void TheCardCarriesTheNeedAndTheStatusBehindIt()
{
    var card = Panels.Card(Only(Waiting("approve the made-up plan", "waiting on a made-up answer")), "claude|s-1", 0);
    Assert.AreEqual("approve the made-up plan", card.needs);
    Assert.AreEqual("waiting on a made-up answer", card.needsDetail);
    Assert.AreEqual("", Panels.Card(Only(Waiting("", "")), "claude|s-1", 0).needs);
}
```

- [ ] **Step 2: Run the tests.** Expected: `error CS` for `BotCard.needs`.

- [ ] **Step 3: Implement** the three changes in `Panels.cs`.

- [ ] **Step 4: Draw it in `WorldPanels.cs`.** On the bot's card, under the lines it has today and above the subagent lines: the need, wrapped to the card's width with the wrapped style the file already has, then the status in the faint style. The card is taller by exactly what the two take (measured with `CalcHeight`, as the command block is); with neither, the card is as it is today. The "Needs you" row needs no change: it already draws `what`, clipped to the row.

- [ ] **Step 5: Give a picture something to show.** In the `fixtures/world/` snapshot that picture runs load, add `"needs"` and `"needsDetail"` with made-up text to one row whose state needs the owner. Run the tests: any test that counted on that row's words is updated to the new truth.

- [ ] **Step 6: Run the tests.** Expected: all pass.

- [ ] **Step 7: Commit** `GRID-50: "Needs you" and the bot's card say what is needed`.

---

### Task 4: A row of the desktop card opens under the pointer

**Files:**
- Modify: `Assets/TheGrid/Core/ProgramCard.cs`
- Test: `Assets/TheGrid/Tests/ProgramCardTests.cs`

**Interfaces:**
- Consumes: `AgentRow.needs`, `Needs.Clean`.
- Produces:
  - `ProgramRow.full` (the name, cleaned, not cut to `NameLetters`) and `ProgramRow.needs`; `ProgramCard.Of` fills both.
  - `ProgramCard.NeedLines = 3`, `ProgramCard.OpenHeight = 70f` (four lines of 16 and 6 of air).
  - `ProgramLayout.open` (`Rect`, `default` when no row is open), `ProgramLayout.openRow` (`int`, `-1` when none).
  - `static ProgramLayout Layout(Rect screenVisible, int rows, bool more, bool cardShown, int open = -1)`: with `open` a row of a shown card, the card is `OpenHeight` taller, rows `0..open` sit `OpenHeight` higher, and `l.open` is the room right under row `open`, as wide as a row. Every open row takes the same room, whatever it says: the card must not jump as the pointer moves from row to row. Any other `open` gives the closed layout.
  - `Hit`: a point in `l.open` is `ProgramHit.Row` with `row = l.openRow`.
  - `static int Hover(ProgramLayout l, Vector2 onScreen)`: the row under a point on the screen, its open part included; `-1` otherwise.
  - `static Rect OpenLine(Rect open, int line)`: line `0..NeedLines` of the open part, top down, in line with the names, ending at the row's right edge.
  - `static List<string> Wrap(string text, Func<string, float> width, float wide, int lines)`: words on as few lines as fit `wide`; a word wider than a line is broken; at most `lines`, and when text is left over the last line ends with `…` and still fits.
  - `static List<string> OpenLines(ProgramRow row, Func<string, float> width, float wide)`: the full name on one line (cut with `…` to fit, never inside a surrogate pair), then `Wrap` of the need over `NeedLines`; with no need, of `row.word`, so the state's words are read in full.

- [ ] **Step 1: Write the failing tests** in `ProgramCardTests.cs` (`Screen` is already there):

```csharp
static readonly Func<string, float> Seven = s => s.Length * 7f;   // a font with every letter 7 wide

[Test]
public void AnOpenRowTakesRoomRightUnderItAndNothingBelowMoves()
{
    var closed = ProgramCard.Layout(Screen, 5, true, true);
    var open = ProgramCard.Layout(Screen, 5, true, true, 2);
    Assert.AreEqual(closed.frame.height + ProgramCard.OpenHeight, open.frame.height);
    Assert.AreEqual(closed.frame.y, open.frame.y);
    Assert.AreEqual(closed.figure, open.figure);
    Assert.AreEqual(closed.more, open.more);
    for (int i = 3; i < 5; i++) Assert.AreEqual(closed.rows[i], open.rows[i]);
    for (int i = 0; i <= 2; i++) Assert.AreEqual(closed.rows[i].y + ProgramCard.OpenHeight, open.rows[i].y);
    Assert.AreEqual(2, open.openRow);
    Assert.AreEqual(new Rect(closed.rows[2].x, closed.rows[2].y, closed.rows[2].width, ProgramCard.OpenHeight), open.open);
    Assert.AreEqual(open.rows[2].y, open.open.yMax);
    Assert.IsTrue(open.card.Contains(open.open.center) && open.card.Contains(open.pin.center));
}

[Test]
public void ThePointerOnARowIsStillOnThatRowOnceItHasOpened()
{
    var closed = ProgramCard.Layout(Screen, 5, true, true);
    for (int i = 0; i < 5; i++)
    {
        var open = ProgramCard.Layout(Screen, 5, true, true, i);
        Assert.AreEqual(ProgramHit.Row, ProgramCard.Hit(open, closed.rows[i].center, out int row));
        Assert.AreEqual(i, row);
        Assert.AreEqual(i, ProgramCard.Hover(open, open.frame.position + closed.rows[i].center));
    }
}

[Test]
public void MovingFromAnOpenRowToTheOneAboveOpensThatOne()
{
    var two = ProgramCard.Layout(Screen, 5, true, true, 2);
    var at = two.frame.position + two.rows[1].center;   // where row 1 is drawn while row 2 is open
    Assert.AreEqual(1, ProgramCard.Hover(two, at));
    Assert.AreEqual(1, ProgramCard.Hover(ProgramCard.Layout(Screen, 5, true, true, 1), at));   // and it is still under the pointer once open
}

[TestCase(-1)] [TestCase(5)] [TestCase(99)]
public void AnOpenRowThatIsNotThereIsNoOpenRow(int open)
{
    var l = ProgramCard.Layout(Screen, 5, true, true, open);
    Assert.AreEqual(ProgramCard.Layout(Screen, 5, true, true).frame, l.frame);
    Assert.AreEqual(-1, l.openRow);
    Assert.AreEqual(default(Rect), l.open);
    Assert.AreEqual(-1, ProgramCard.Layout(Screen, 5, true, false, 2).openRow);   // no card, nothing open
}

[Test]
public void ThePointerOffTheRowsIsOnNoRow()
{
    var l = ProgramCard.Layout(Screen, 5, true, true, 2);
    Assert.AreEqual(-1, ProgramCard.Hover(l, l.frame.position + l.figure.center));
    Assert.AreEqual(-1, ProgramCard.Hover(l, l.frame.position + l.pin.center));
    Assert.AreEqual(-1, ProgramCard.Hover(l, l.frame.position - new Vector2(50, 50)));
}

[Test]
public void TheOpenPartsLinesAreInsideItTopDownInLineWithTheNames()
{
    var l = ProgramCard.Layout(Screen, 5, true, true, 2);
    float nameAt = ProgramCard.Parts(l.rows[2], 0).name.x;
    for (int i = 0; i <= ProgramCard.NeedLines; i++)
    {
        var line = ProgramCard.OpenLine(l.open, i);
        Assert.AreEqual(nameAt, line.x);
        Assert.AreEqual(l.open.xMax, line.xMax);
        Assert.IsTrue(line.yMin >= l.open.yMin && line.yMax <= l.open.yMax, "line " + i + " is outside the open part");
        if (i > 0) Assert.LessOrEqual(line.yMax, ProgramCard.OpenLine(l.open, i - 1).yMin);
    }
}

[Test]
public void WordsAreWrappedAndWhatDoesNotFitEndsInAnEllipsis() =>
    CollectionAssert.AreEqual(new[] { "merge the", "made-up", "pull…" }, ProgramCard.Wrap("merge the made-up pull request", Seven, 70, 3));

[Test] public void ShortTextIsOneLine() => CollectionAssert.AreEqual(new[] { "merge it" }, ProgramCard.Wrap("merge it", Seven, 70, 3));
[Test] public void NoTextOrNoLinesIsNoLines() { Assert.AreEqual(0, ProgramCard.Wrap("", Seven, 70, 3).Count); Assert.AreEqual(0, ProgramCard.Wrap("merge it", Seven, 70, 0).Count); }

[Test]
public void AWordLongerThanALineIsBroken() =>
    CollectionAssert.AreEqual(new[] { "abcdefghij", "klmnopqrst", "uvwxyz" }, ProgramCard.Wrap("abcdefghijklmnopqrstuvwxyz", Seven, 70, 3));

[Test]
public void NoLineIsEverWiderThanItsRoom()
{
    var text = string.Join(" ", Enumerable.Range(0, 60).Select(i => new string((char)('a' + i % 26), 1 + i * 7 % 23)));
    var lines = ProgramCard.Wrap(text, Seven, 70, 3);
    Assert.AreEqual(3, lines.Count);
    foreach (var line in lines) Assert.LessOrEqual(Seven(line), 70f, line);
    Assert.IsTrue(lines[2].EndsWith("…"));
    Assert.AreEqual(0, ProgramCard.Wrap("abc", Seven, 3, 3).Count);   // room for less than one letter: nothing, and no endless loop
}

[Test]
public void AnOpenRowSaysItsFullNameThenWhatItNeeds() =>
    CollectionAssert.AreEqual(new[] { "A made-up…", "merge it" },
        ProgramCard.OpenLines(new ProgramRow { full = "A made-up session with a long name", needs = "merge it", word = "Blocked in the background" }, Seven, 70));

[Test]
public void AnOpenRowWithNoNeedSaysItsStateInFull() =>
    CollectionAssert.AreEqual(new[] { "A made-up…", "Blocked in", "the", "background" },
        ProgramCard.OpenLines(new ProgramRow { full = "A made-up session with a long name", needs = "", word = "Blocked in the background" }, Seven, 70));

[Test]
public void TheCardsRowsCarryTheFullNameAndTheNeed()
{
    var v = ProgramCard.Of(Only(new AgentRow { sessionId = "s-1", account = "claude", name = "A made-up session with a long name", state = "BackgroundBlocked", needs = "merge it" }));
    Assert.AreEqual("A made-up session with a long name", v.rows[0].full);
    Assert.AreEqual("merge it", v.rows[0].needs);
}
```

- [ ] **Step 2: Run the tests.** Expected: `error CS` for the new names.

- [ ] **Step 3: Implement** in `ProgramCard.cs`, keeping every existing test green: the closed layout must not change by a point.

- [ ] **Step 4: Run the tests.** Expected: all pass.

- [ ] **Step 5: Commit** `GRID-50: a row of the card opens under the pointer, in Core`.

---

### Task 5: The desktop program draws the open row; the README says so

**Files:**
- Modify: `Assets/TheGrid/DesktopProgram.cs`, `Assets/TheGrid/Cocoa.cs` (only if a string cannot yet be measured without a layer), `README.md`
- Test: none new (shell); the whole suite must stay green.

**Interfaces:**
- Consumes: `ProgramCard.Layout(..., open)`, `Hover`, `OpenLine`, `OpenLines`, `ProgramLayout.open`, `ProgramLayout.openRow`, `ProgramRow.full`, `ProgramRow.needs` (Task 4).

- [ ] **Step 1: The shell.** In `DesktopProgram.Update`, after the card's "shows" is known: the open row is `ProgramCard.Hover(layout, pointer)` while the card is shown, `-1` when it is not. When it differs from the open row on screen, lay out again with it and draw. `Draw` places four more text layers (made once in `Start`, as the others are) on `ProgramCard.OpenLine(layout.open, i)`, with the strings from `ProgramCard.OpenLines(view.rows[layout.openRow], measure, line width)`, and hides them when no row is open. `measure` is the width of a string in the card's font, asked of AppKit. No rule is added here: no arithmetic on rects, no cutting of text.
- [ ] **Step 2: The README.** In "The program on the desktop": the pointer on a row opens it, and what the open row says. In the roster's part, where it says what The Grid reads: a blocked background session's job state file (`<config folder>/jobs/<id>/state.json`, fields `needs` and `detail`) and the open question in a session's transcript; read only, and anything unexpected means the row says what it said before.
- [ ] **Step 3: Run the tests.** Expected: all pass, same total as after Task 4.
- [ ] **Step 4: Commit** `GRID-50: the desktop card's rows open under the pointer`.
- [ ] **Step 5 (controller, on the built app):** the pointer on a blocked row shows its full name and its need; moving to the row above or below opens that one without the card jumping; moving off the rows closes it; a click on the open part does what a click on its row does; typing stays in the app in front.
