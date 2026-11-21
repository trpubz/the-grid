# The Program on the Desktop Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A small bot that stands on the desktop above other windows, with a card of the first five roster rows that shows when the pointer is near; a click on a row goes to that session's terminal tab.

**Architecture:** Two layers. Every decision is plain code in `Assets/TheGrid/Core` with tests written first: which rows, the words, lit or dark, where things sit, what a point hits, when the card shows, how to jump. A thin shell in `Assets/TheGrid` makes a frameless macOS panel by calling AppKit through the Objective-C runtime from C#, draws what the plain code says, and hands clicks back. The shell holds no rules and is checked by hand on the built app.

**Tech Stack:** Unity 6000.4.3f1, C#, Mono backend, `System.Runtime.InteropServices` against `/usr/lib/libobjc.A.dylib`, AppKit (`NSPanel`, `NSView`, `NSImageView`, `NSTextField`), Unity Test Framework (EditMode). No new package, no compiled add-on.

**Spec:** Linear ticket GRID-45, whose description is the approved design, including "The window recipe that was tested". Read it first: the recipe is not repeated here.

## Global Constraints

- The repo is public. No real names of people, companies, accounts, repos or sessions in code, tests, fixtures, docs or pictures. A screenshot of the real card shows real session names: it goes on the ticket, never in the repo.
- No other project's art, names or code. The figure is The Grid's own bot.
- macOS player only. The program is off in the Unity editor, on any other platform, in picture mode (`-snapshot`), and when `program` is `false` in `settings.json`.
- It never takes the keyboard and never types into a terminal or a conversation. The one thing it does in a terminal: tell Zellij to go to a tab when the owner clicks.
- Only the session's own process is read to learn where it lives (`ps eww -p <pid>`), as today. No list of processes.
- 10 frames a second stays. The shell asks macOS for the pointer once a frame and does nothing else when nothing changed.
- Nothing is written under an agent app's folders. The pin's state lives in The Grid's own `settings.json`.
- The 635 existing EditMode tests pass.
- Only one copy of the app runs at a time on the owner's Mac. Ask before launching a build; if `UserSettings/TheGridSignIdentity.txt` exists in the main checkout, copy it into the worktree first, so macOS does not ask about folders again.
- After opening the pull request, post `@codex review`; post it again after every push that answers findings.

**RUN TESTS** in this plan means, from the worktree:

```bash
/Applications/Unity/Hub/Editor/6000.4.3f1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath . -runTests -testPlatform EditMode -assemblyNames TheGrid.Core.Tests -testResults Logs/editmode.xml -logFile Logs/editmode.log
```

then read `total`, `passed`, `failed` and each failed case from `Logs/editmode.xml`. No XML means a compile error: `grep "error CS" Logs/editmode.log`.

**BUILD** means: `Unity -batchmode -projectPath . -executeMethod TheGrid.EditorTools.GridMenu.BuildMac -quit -logFile <scratch>/build.log`, expecting `Build Finished, Result: Success.`

## Review Focus

What the design implies and a person will meet. Each has its test in the task named.

1. **No sessions, or fewer than five.** No empty card: with no rows the card never shows, and the frame is the figure alone. (Task 3)
2. **A very long session name.** The row keeps its width; the name is cut with an ellipsis. (Task 2, the cut; Task 5, by eye)
3. **A Zellij whose list of panes has no `tab_id`.** No jump to tab 0 by mistake: no jump at all, and The Grid opens at the bot. (Task 4)
4. **The session's process is gone by the time of the click**, or it is a background session with no process. The Grid opens at the bot; no exception. (Task 4)
5. **The screen changes**: the Dock moves, the resolution changes, the main screen is another one. The program stands at the bottom right of whatever the main screen's visible part is now. (Task 3, the rule; Task 5, re-read once a second)

## File Structure

| File | Responsibility |
|---|---|
| Create `Assets/TheGrid/Core/ProgramCard.cs` | The program's rules: rows and words, lit or dark, layout, hit, near, shows, pixel scaling |
| Modify `Assets/TheGrid/Core/Zellij.cs` | The tab's id, the terminal app's bundle id, the jump |
| Modify `Assets/TheGrid/Core/Settings.cs` | `program`, `programPinned` |
| Modify `Assets/TheGrid/Core/Panels.cs` | `What` becomes `internal`, so the card uses the same words as "Needs you" |
| Create `Assets/TheGrid/Cocoa.cs` | The AppKit calls, each with its exact argument types. No rules |
| Create `Assets/TheGrid/DesktopProgram.cs` | The shell: panel, figure, card, pointer, clicks |
| Modify `Assets/TheGrid/RosterApp.cs` | Makes the program, gives it each snapshot, acts on what it reports |
| Create `Assets/TheGrid/Tests/ProgramCardTests.cs` | Tests of the rules |
| Modify `Assets/TheGrid/Tests/ZellijTests.cs`, `SettingsTests.cs` | Tests of the additions |
| Modify `README.md` | What the program is, what a click does, the rule that changed, the switch |

---

### Task 1: The bare program, for the owner to click

The one thing never shown: a real click arrives and the app in front keeps the keyboard. Nothing else is built until the owner has seen it.

**Files:** Create `Assets/TheGrid/Cocoa.cs`, `Assets/TheGrid/DesktopProgram.cs`. Modify `Assets/TheGrid/Core/Settings.cs`, `Assets/TheGrid/RosterApp.cs`, `Assets/TheGrid/Tests/SettingsTests.cs`. Create `Assets/TheGrid/Core/ProgramCard.cs`, `Assets/TheGrid/Tests/ProgramCardTests.cs`.

**Interfaces:**
- Produces: `Settings.program` (bool, default `true`), `Settings.programPinned` (bool, default `false`), both copied by `Reload`.
- Produces: `ProgramCard.Scaled(Color32[] pixels, int width, int height, int times) -> Color32[]`: each pixel becomes a `times` by `times` block; rows are flipped top to bottom (Unity's first row is the bottom one, AppKit's bitmap starts at the top); a pixel with alpha 0 comes out as `(0,0,0,0)`.
- Produces: `DesktopProgram : MonoBehaviour` with `public bool Lit` and `public event Action FigureClicked`.

- [ ] **Step 1: Write the failing tests**

```csharp
// SettingsTests.cs
[Test]
public void TheProgramIsOnAndUnpinnedUnlessTheFileSaysOtherwiseAndBothSurviveSaveAndLoad()
{
    var s = Settings.Load(path);                    // no file
    Assert.IsTrue(s.program); Assert.IsFalse(s.programPinned);
    s.program = false; s.programPinned = true; s.Save(path);
    var again = Settings.Load(path);
    Assert.IsFalse(again.program); Assert.IsTrue(again.programPinned);
    var live = new Settings(); live.Reload(path);   // a hand edit made while the app runs is picked up
    Assert.IsFalse(live.program); Assert.IsTrue(live.programPinned);
}

// ProgramCardTests.cs
[Test]
public void APictureIsEnlargedInWholePixelsTopRowFirstAndClearPixelsCarryNoColour()
{
    Color32 a = new Color32(1, 2, 3, 255), b = new Color32(9, 9, 9, 0), c = new Color32(4, 5, 6, 255), d = new Color32(7, 8, 9, 255);
    var got = ProgramCard.Scaled(new[] { a, b, c, d }, 2, 2, 2);   // Unity order: bottom row a b, top row c d
    var clear = new Color32(0, 0, 0, 0);
    CollectionAssert.AreEqual(new[] { c, c, d, d, c, c, d, d, a, a, clear, clear, a, a, clear, clear }, got);
}
```

- [ ] **Step 2: RUN TESTS.** Expected: no XML; `error CS` for `program`, `programPinned`, `ProgramCard`.
- [ ] **Step 3: Add the two settings fields (with `Reload`) and `ProgramCard.Scaled`.**
- [ ] **Step 4: RUN TESTS.** Expected: `failed 0`, total 637.
- [ ] **Step 5: Write `Cocoa.cs`**: `internal static class Cocoa`, the `objc_msgSend` declarations the recipe needs, each with exact argument and return types, plus `Class(name)`, `Sel(name)`, `NSString(text)`. Every call the shell makes goes through a named method here (`Cocoa.MakePanel(Rect) -> IntPtr`, `Cocoa.SetFrame(IntPtr, Rect)`, `Cocoa.ImageFrom(Color32[] rgba, int width, int height, Vector2 points) -> IntPtr`, `Cocoa.PointerOnScreen() -> Vector2`, `Cocoa.MainScreenVisible() -> Rect`, `Cocoa.BackingScale() -> float`). `ImageFrom` makes an `NSBitmapImageRep` of exactly those pixels and an `NSImage` whose size is `points`, so nothing is smoothed.
- [ ] **Step 6: Write `DesktopProgram.cs`**: on `Start`, the panel of the recipe, 120 by 189 points (the figure, 40 by 48 at 3 times, and 45 points over its head for the badge), bottom right of `Cocoa.MainScreenVisible()` with a 16 point margin; the figure is `bot-stand-sw` with `badge-question` over its head when `Lit`, `bot-off-sw` and no badge when not, both through `ProgramCard.Scaled` at `3 * Cocoa.BackingScale()`. A click raises `FigureClicked` from `Update`, never from inside the AppKit callback. `OnDestroy` closes the panel. Static fields keep the callback delegates alive.
- [ ] **Step 7: In `RosterApp.Start`**, after settings are loaded: add `DesktopProgram` only when `fixedSnap == null && settings.program && Application.platform == RuntimePlatform.OSXPlayer`. For this task only, `FigureClicked` flips `Lit` and logs `[program] click`.
- [ ] **Step 8: BUILD, then ask the owner to quit their copy, launch this build, and check by hand:**
  - The figure stands bottom right, no frame, no shadow, clear around it, crisp pixels.
  - While typing in another app, click the figure: it flips between lit and dark, and the typing goes on in that app, uninterrupted.
  - It is there on another Space and over a full-screen app.
  - A click just beside the figure, inside its 120 by 189 frame but on a clear pixel: note whether it reaches the app underneath.

  **STOP here if the click does not arrive or takes the keyboard.** Report to the owner; the fallbacks are theirs to choose.
- [ ] **Step 9: Commit** `GRID-45: the bare program stands on the desktop and takes a click`.

### Task 2: The card's rows

**Files:** Modify `Assets/TheGrid/Core/ProgramCard.cs`, `Assets/TheGrid/Core/Panels.cs`, `Assets/TheGrid/Tests/ProgramCardTests.cs`.

**Interfaces:**
- Consumes: `RosterSnapshot`, `AgentRow`, `World.BadgeFor`, `World.NeedsOwner`, `Panels.What` (made `internal`), `Activity.Working`.
- Produces:
  - `sealed class ProgramRow { string key, name, word; Badge badge; int pid; }` (`key` is `account + "|" + sessionId`, as `Panels` keys a bot)
  - `sealed class ProgramView { List<ProgramRow> rows; int more; bool lit; }`
  - `ProgramCard.MaxRows = 5`, `ProgramCard.NameLetters = 24`
  - `ProgramCard.Of(RosterSnapshot snap) -> ProgramView`

- [ ] **Step 1: Write the failing tests** (helper `Agent(name, state, activity = "", pid = 0)` makes an `AgentRow` with `account = "one"`, `sessionId = name`):

```csharp
[Test]
public void TheCardIsTheFirstFiveRowsOfTheRosterInItsOrderAndSaysHowManyMore()
{
    var snap = new RosterSnapshot();
    foreach (var n in new[] { "a", "b", "c", "d", "e", "f", "g" }) snap.agents.Add(Agent(n, "Running"));
    var v = ProgramCard.Of(snap);
    CollectionAssert.AreEqual(new[] { "a", "b", "c", "d", "e" }, v.rows.Select(r => r.name).ToArray());
    Assert.AreEqual(2, v.more);
    Assert.AreEqual("one|a", v.rows[0].key);
}

[Test]
public void WithFiveOrFewerThereIsNoMoreAndWithNoneThereAreNoRows()
{
    var three = new RosterSnapshot(); foreach (var n in new[] { "a", "b", "c" }) three.agents.Add(Agent(n, "Finished"));
    Assert.AreEqual(3, ProgramCard.Of(three).rows.Count); Assert.AreEqual(0, ProgramCard.Of(three).more);
    Assert.AreEqual(0, ProgramCard.Of(new RosterSnapshot()).rows.Count);
}

[TestCase("BlockedOnQuestion", "", "Answer a question", Badge.Question)]
[TestCase("WaitingForPermission", "", "Approve a step", Badge.Approve)]
[TestCase("Errored", "", "Failed", Badge.Fault)]
[TestCase("Running", "reading", "reading", Badge.None)]
[TestCase("Running", "", "working", Badge.None)]
[TestCase("Finished", "", "Done", Badge.Done)]
[TestCase("Offline", "", "Offline", Badge.None)]
public void ARowSaysTheStateInAWordAndARunningOneWhatItIsDoing(string state, string activity, string word, Badge badge)
{
    var row = ProgramCard.Of(Only(Agent("a", state, activity, 41))).rows[0];
    Assert.AreEqual(word, row.word); Assert.AreEqual(badge, row.badge); Assert.AreEqual(41, row.pid);
}

[Test]
public void TheProgramIsLitWhenAnySessionNeedsTheOwnerAlsoOneNotOnTheCard()
{
    var snap = new RosterSnapshot();
    foreach (var n in new[] { "a", "b", "c", "d", "e", "f" }) snap.agents.Add(Agent(n, "Running"));
    Assert.IsFalse(ProgramCard.Of(snap).lit);
    snap.agents[5].state = "WaitingForPermission";   // sixth: under "+1 more"
    Assert.IsTrue(ProgramCard.Of(snap).lit);
    Assert.IsFalse(ProgramCard.Of(Only(Agent("a", "Finished"))).lit);   // done is not waiting
}

[Test]
public void ALongNameIsCutWithAnEllipsis()
{
    var row = ProgramCard.Of(Only(Agent(new string('x', 60), "Running"))).rows[0];
    Assert.AreEqual(ProgramCard.NameLetters, row.name.Length);
    Assert.IsTrue(row.name.EndsWith("…"));
    Assert.AreEqual("one|" + new string('x', 60), row.key);   // the key is never cut
}
```

- [ ] **Step 2: RUN TESTS.** Expected: `error CS` for `ProgramCard.Of`, `ProgramRow`.
- [ ] **Step 3: Implement `Of`.** The word: for a state that needs the owner, `Panels.What(a)`; `Running`: the activity, or `Activity.Working` when empty; `Finished`: `Done`; `Offline`: `Offline`.
- [ ] **Step 4: RUN TESTS.** Expected: `failed 0`.
- [ ] **Step 5: Commit** `GRID-45: the card is the first five rows of the roster`.

### Task 3: Where things sit, what a point hits, when the card shows

**Files:** Modify `Assets/TheGrid/Core/ProgramCard.cs`, `Assets/TheGrid/Tests/ProgramCardTests.cs`.

**Interfaces:** all rectangles are in points, AppKit's way: y grows upward. `frame` is on the screen; everything else is inside the frame, from its bottom left.
- Produces:
  - `sealed class ProgramLayout { Rect frame, figure, badge, card, pin, more; List<Rect> rows; }` (`card`, `pin`, `more` are `default` when not there). `badge` is always there: the place over the figure's head where the amber badge is drawn while the program is lit. The helmet leaves only 6 clear rows of the picture's 48, and the badge is 20 high, so the frame is taller than the figure by the 15 rows that do not fit: 45 points.
  - `ProgramCard.Layout(Rect screenVisible, int rows, bool more, bool cardShown) -> ProgramLayout`
  - `enum ProgramHit { None, Figure, Row, More, Pin }`
  - `ProgramCard.Hit(ProgramLayout l, Vector2 inFrame, out int row) -> ProgramHit`
  - `ProgramCard.Near(ProgramLayout l, Vector2 onScreen) -> bool`
  - `ProgramCard.Shows(bool pinned, int rows, bool near, long nowMs, long leftAtMs) -> bool`
  - Constants: `FigureSize = (120, 144)`, `BadgeSize = (48, 60)`, `BadgeLift = 45`, `Margin = 16`, `CardWidth = 280`, `RowHeight = 22`, `Pad = 24`, `HoldMs = 800`.

- [ ] **Step 1: Write the failing tests**

```csharp
static readonly Rect Screen = new Rect(0, 80, 1440, 820);   // a Dock 80 high at the bottom

[Test]
public void TheFigureStandsAtTheBottomRightOfWhateverTheScreenShowsAndWithItsBadgeItIsTheWholeFrame()
{
    foreach (var screen in new[] { Screen, new Rect(100, 0, 1820, 1050) })   // Dock on the left, another size
    {
        var l = ProgramCard.Layout(screen, 3, false, false);
        Assert.AreEqual(new Rect(screen.xMax - 16 - 120, screen.y + 16, 120, 189), l.frame);   // nothing clear beyond the figure and its badge
        Assert.AreEqual(new Rect(0, 0, 120, 144), l.figure);
        Assert.AreEqual(new Rect(36, 129, 48, 60), l.badge);   // centred over the head, its foot in the picture's clear rows
        Assert.AreEqual(0, l.rows.Count);
    }
}

[Test]
public void TheCardStandsOverTheFigureItsRowsInTheRostersOrderTopDownNoneOverlappingAllInside()
{
    var l = ProgramCard.Layout(Screen, 5, true, true);
    Assert.GreaterOrEqual(l.card.yMin, l.badge.yMax);
    Assert.AreEqual(5, l.rows.Count);
    for (int i = 1; i < 5; i++) Assert.LessOrEqual(l.rows[i].yMax, l.rows[i - 1].yMin, "row " + i + " is under row " + (i - 1));
    Assert.LessOrEqual(l.more.yMax, l.rows[4].yMin);
    foreach (var r in l.rows.Concat(new[] { l.more, l.pin, l.figure, l.badge }))
        Assert.IsTrue(r.xMin >= 0 && r.yMin >= 0 && r.xMax <= l.frame.width && r.yMax <= l.frame.height, r + " is outside the frame");
    foreach (var r in l.rows.Concat(new[] { l.more, l.pin })) Assert.IsTrue(l.card.Contains(r.center));
    Assert.IsFalse(l.rows.Any(r => r.Overlaps(l.pin)));
    Assert.LessOrEqual(l.frame.xMax, Screen.xMax); Assert.GreaterOrEqual(l.frame.xMin, Screen.xMin); Assert.LessOrEqual(l.frame.yMax, Screen.yMax);
    Assert.AreEqual(Screen.xMax - 16, l.frame.xMax);          // the figure does not move when the card comes and goes
    Assert.AreEqual(l.frame.width - 120, l.figure.xMin);
}

[Test]
public void WithNoRowsThereIsNoCardEvenWhenAskedFor()
{
    var l = ProgramCard.Layout(Screen, 0, false, true);
    Assert.AreEqual(default(Rect), l.card);
    Assert.AreEqual(new Rect(0, 0, 120, 189), new Rect(0, 0, l.frame.width, l.frame.height));
    Assert.IsFalse(ProgramCard.Shows(true, 0, true, 0, 0));   // pinned and near, and still nothing to show
}

[Test]
public void APointHitsTheRowThePinTheMoreLineOrTheFigureAndTheClearPartNothing()
{
    var l = ProgramCard.Layout(Screen, 5, true, true);
    for (int i = 0; i < 5; i++) { Assert.AreEqual(ProgramHit.Row, ProgramCard.Hit(l, l.rows[i].center, out int row)); Assert.AreEqual(i, row); }
    Assert.AreEqual(ProgramHit.Pin, ProgramCard.Hit(l, l.pin.center, out _));
    Assert.AreEqual(ProgramHit.More, ProgramCard.Hit(l, l.more.center, out _));
    Assert.AreEqual(ProgramHit.Figure, ProgramCard.Hit(l, l.figure.center, out _));
    Assert.AreEqual(ProgramHit.None, ProgramCard.Hit(l, new Vector2(2, 2), out _));   // beside the figure, under the card's left part
}

[Test]
public void ThePointerIsNearOnTheFigureAndALittleAroundItAndOnTheCardOnlyWhileTheCardIsThere()
{
    var shut = ProgramCard.Layout(Screen, 5, false, false);
    Assert.IsTrue(ProgramCard.Near(shut, shut.frame.center));
    Assert.IsTrue(ProgramCard.Near(shut, new Vector2(shut.frame.xMin - 20, shut.frame.center.y)));
    Assert.IsFalse(ProgramCard.Near(shut, new Vector2(shut.frame.xMin - 40, shut.frame.center.y)));
    var open = ProgramCard.Layout(Screen, 5, false, true);
    var onCard = new Vector2(open.frame.x + open.card.center.x, open.frame.y + open.card.center.y);
    Assert.IsTrue(ProgramCard.Near(open, onCard));
    Assert.IsFalse(ProgramCard.Near(shut, onCard));   // where the card would be is not near while it is shut
}

[TestCase(false, true, 5000, 0, true)]      // pointer near
[TestCase(false, false, 5000, 4500, true)]  // left half a second ago: still up
[TestCase(false, false, 5000, 4000, false)] // left a second ago: gone
[TestCase(true, false, 5000, 0, true)]      // pinned
public void TheCardShowsWhilePinnedOrNearAndForAShortTimeAfter(bool pinned, bool near, long now, long leftAt, bool shows) =>
    Assert.AreEqual(shows, ProgramCard.Shows(pinned, 3, near, now, leftAt));
```

- [ ] **Step 2: RUN TESTS.** Expected: `error CS` for `Layout`, `Hit`, `Near`, `Shows`.
- [ ] **Step 3: Implement them.** The card is `CardWidth` wide, right edge at the frame's right edge; a title strip at its top holds the pin at the right; rows under it, top down; the "more" line last.
- [ ] **Step 4: RUN TESTS.** Expected: `failed 0`.
- [ ] **Step 5: Commit** `GRID-45: where the program and its card sit, and when the card shows`.

### Task 4: The jump

**Files:** Modify `Assets/TheGrid/Core/Zellij.cs`, `Assets/TheGrid/Tests/ZellijTests.cs`.

**Interfaces:**
- Consumes: `Zellij.TryPane`, the private `Pane` class (gains `public int tab_id;`), `Zellij.Run`, the private `Expected(Exception)`.
- Produces:
  - `Zellij.TabIdOf(string panesJson, int pane) -> int`: the `tab_id` of the terminal pane with that number; `-1` when the pane is not in the list, the list cannot be read, or the list has no `"tab_id"` in it at all (an older Zellij: `JsonUtility` would read a missing number as 0, which is a real tab).
  - `Zellij.TerminalApp(string ps) -> string`: the value of `__CFBundleIdentifier` in a process's environment as `ps eww` prints it, when it is letters, digits, dots and hyphens only; else `""`.
  - `Zellij.Jump(int pid, string zellijPath, Func<string, string, string> run, out string app) -> bool`: true when Zellij was told to go to the tab. `app` is the terminal's bundle id, or `""`.

- [ ] **Step 1: Write the failing tests** (the existing `InAPane` and `Bare` constants; add `InAPaneOfATerminal = InAPane + " __CFBundleIdentifier=com.example.term"` and a `PanesWithIds` list in which terminal pane 2 has `"tab_id":3` and a plugin pane 2 has `"tab_id":0`):

```csharp
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
```

- [ ] **Step 2: RUN TESTS.** Expected: `error CS` for `TabIdOf`, `TerminalApp`, `Jump`.
- [ ] **Step 3: Implement the three.** `Jump` catches what `Where` catches, through the same `Expected`.
- [ ] **Step 4: RUN TESTS.** Expected: `failed 0`.
- [ ] **Step 5: Commit** `GRID-45: a jump goes to the session's tab in Zellij`.

### Task 5: The card on the desktop, and its clicks

Shell only. No rule is written here: if a decision is needed that Tasks 2 to 4 do not make, stop and add it there with a test.

**Files:** Modify `Assets/TheGrid/DesktopProgram.cs`, `Assets/TheGrid/Cocoa.cs`, `Assets/TheGrid/RosterApp.cs`.

**Interfaces:**
- Consumes: `ProgramCard.Of`, `Layout`, `Hit`, `Near`, `Shows`; `Zellij.Jump`; `Settings.programPinned`; `WorldView.Select(string botKey)`; `RosterApp`'s `nextWorldOpen`, `zellij`, `TrySave`.
- Produces on `DesktopProgram`: `void Show(ProgramView view)`, `bool Pinned { get; set; }`, `event Action<ProgramRow> RowClicked`, `event Action GridWanted`, `event Action PinClicked`. `FigureClicked` and Task 1's flip are removed.

- [ ] **Step 1: Draw.** Each frame in `Update`: read the pointer and, once a second, the main screen's visible part; work out `Near`, remember when the pointer left, work out `Shows` and `Layout`. Only when the layout, the view or the lit state changed: set the panel's frame and redraw. Everything is placed from `ProgramLayout`: the figure, the badge (drawn only while the view is lit), the card. The card is a dark rounded layer (`#0c1024` at 92%), text in The Grid's font (`Settings.font`, else the one `Fonts.Pick` gives) at 12 points in `#e4ecff`; each row is the badge's picture (through `ProgramCard.Scaled`, as the figure) or a small dot when there is none, the name, and the word at the right in `#8791c4`; the pin is a filled or hollow square; "+N more" in `#8791c4`.
- [ ] **Step 2: Clicks.** `mouseDown` stores the point; `Update` passes it to `ProgramCard.Hit` and raises `RowClicked(row)`, `GridWanted` (the figure, or "more") or `PinClicked`.
- [ ] **Step 3: In `RosterApp`:**
  - each time `drawn` changes: `program.Show(ProgramCard.Of(drawn))`.
  - `RowClicked`: `Task.Run(() => Zellij.Jump(row.pid, zellij, Zellij.Run, out app))`. When it comes back true: bring the app with that bundle id forward, if there is one. When false: bring The Grid forward, `nextWorldOpen = true`, `world.Select(row.key)`.
  - `GridWanted`: bring The Grid forward.
  - `PinClicked`: `settings.Reload(settingsPath)`, flip `programPinned`, `TrySave()`, `program.Pinned = settings.programPinned`.
  - Bringing an app forward goes through `Cocoa`: first `NSRunningApplication`'s `activateWithOptions:`. If, in the hand check below, the terminal does not come forward that way (newer macOS may refuse a request from an app that is not in front), use `/usr/bin/open -b <bundle id>` instead and say so in the README's list of programs The Grid runs.
- [ ] **Step 4: RUN TESTS.** Expected: `failed 0` (the shell has no tests; this shows nothing else broke).
- [ ] **Step 5: BUILD, ask the owner to quit their copy, launch, and check by hand, with a screenshot for the ticket (not the repo):**
  - The card's five rows are the first five of the Lists view, same order, same badges; "+N more" is right.
  - The card comes when the pointer nears the figure, goes shortly after it leaves, stays when pinned; the pin is still set after a restart.
  - The figure is lit with the amber badge within 3 seconds of a session starting to wait, dark when none waits.
  - A click on a row of a session in Zellij: the terminal comes forward on that session's tab.
  - A click on a row of a session not in Zellij: The Grid comes forward, World view, that bot selected.
  - While typing in another app, clicking the pin does not interrupt the typing.
  - A click on the clear part of the frame, left of the figure under the card: note whether it reaches the app underneath. If it does not, say so on the ticket: the design allows it only if the frame is no larger than the figure, its badge and the card, which Task 3's tests hold it to.
  - `program: false` in `settings.json`, restart: no figure, no card.
  - Activity Monitor: processor use with the card shut is no higher than before this branch.
- [ ] **Step 6: Commit** `GRID-45: the card shows on the desktop and a click goes to the session`.

### Task 6: Say it, and send it

**Files:** Modify `README.md`.

- [ ] **Step 1: README.** A section "The program on the desktop": what it is, lit and dark, the card and its pin, what a click does (the tab, not the exact pane; The Grid itself for a session not in Zellij), `program` and `programPinned` in the settings table. In the opening paragraph and wherever it says what The Grid runs: it runs `zellij` to ask where a session lives and, when you click that session on the card, to go to its tab; it never types into a terminal or a conversation. Mac only. No picture with real names.
- [ ] **Step 2: RUN TESTS**, then `python3 tools/check_meta.py`. Expected: `failed 0`; `PASS`.
- [ ] **Step 3: Commit** `GRID-45: the README says what the program is and what a click does`, push, open the pull request against `main` with the ticket's done list as its checklist, post `@codex review`, and watch for findings by comment id.
- [ ] **Step 4: On the ticket:** what was built, the screenshot, and anything that differs from the design.
