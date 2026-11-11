# Slice 3: The World Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A third view in the window that draws the roster's agents as riders in an isometric pixel-art world, one city per project, so a glance shows where attention is needed.

**Architecture:** The world is a pure function of the roster snapshot the window already has. `World.Build` (Core) turns a `RosterSnapshot` into cities, buildings and rider slots. `Iso` (Core) turns those into pixel positions. `PixelArt` (Core) draws every sprite in code from one palette. `WorldView` (a `MonoBehaviour`) keeps one `SpriteRenderer` per thing on screen and moves riders when the model changes. `RosterApp` gains a view switch and draws the text.

**Tech Stack:** Unity 6000.4.3f1, C#, `SpriteRenderer` with sprites made at run time, orthographic camera at whole-number scale, IMGUI for text, Unity Test Framework (EditMode). No new package.

**Spec:** Ticket GRID-9 and slice 3 of the approved product requirements (kept privately; its rules are restated under Global Constraints). Data shape: `docs/roster-snapshot.md`. Patterns to follow: `Assets/TheGrid/Core/Roster.cs`, `Assets/TheGrid/RosterApp.cs`.

## Global Constraints

- Everything shown is true: a rider moves only because its agent's state changed. No decorative motion except the bounce of a rider that needs the owner.
- The world and the roster never disagree. Both are drawn from the same `RosterSnapshot` object in the same frame.
- The only attention signal is a bouncing sprite. No sound, no notifications.
- 16-bit pixel art: point filtering, whole-number scaling, one fixed palette. All art is original and drawn by this project's own code.
- Names and counts are text drawn at the screen's own sharpness, not pixel art.
- The roster and the review pile keep working unchanged; the 117 existing EditMode tests pass.
- 10 frames a second stays. Idle processor use stays near 6.5% of a core.
- Nothing leaves the machine. Agent app storage is read-only. No new reads of any kind: the world uses only the snapshot.
- Usable from 360 points wide.
- The repo is public. No real names of people, companies, accounts, repos or sessions in code, tests, fixtures or docs.
- Only one copy of the app may run at a time on the owner's Mac (see Task 4, Step 6).

## Decisions made in this plan

The reviewer's to veto:

1. **A building stands for a state, not a kind of work.** The requirements proposed one building per activity (editing, testing, reviewing). The roster does not know activity yet, only state. So each city has six buildings: Booth (blocked on a question), Gate (waiting for permission or on something else, background blocked), Yard (errored or unknown), Workshop (running), Garage (done), Depot (offline). A rider rides to another building exactly when its state changes. Activity buildings can replace these once activity is collected.
2. **Art is drawn by code and is placeholder quality.** No artist has been chosen. Every sprite is generated at run time from a 16-colour palette: ground tile, road tile, six buildings, a motorcycle with rider (two shapes, one per agent app), a small motorcycle for subagents, an attention marker, a city icon. Account decides the glow colour. Replacing them with drawn art later touches one file.
3. **Plain sprites, layout in Core.** One `SpriteRenderer` per tile, building and rider, positioned by pure maths that tests can check. No Tilemap and no pixel-perfect package: fewer parts, and the same maths gives hit-testing.
4. **Two zoom levels, no free zoom or pan.** Far: every city as an icon with its name and counts. Near: one city. Click a city to go in; a Back button or Escape to come out. The scale is the largest whole number at which the level fits the window.
5. **No building interiors in this slice.** Clicking a rider shows that agent's roster row under the city instead.
6. **The world is a view switch.** Wide window: `Lists` and `World`. Narrow window: `Agents`, `Messages`, `World`.
7. **A picture mode for checking.** Started with `-snapshot <file> -shots <folder>`, the app draws the world from a snapshot file instead of live sessions, saves two PNGs and quits. It reads no agent data, so it needs no folder access. It is also how the pull request gets its pictures.

Not in this plan: activity detection, building interiors, free zoom and pan, final art, art packs, sound, other agent apps, animation frames beyond a two-step bounce.

## Review Focus

1. No agents at all, or a snapshot with errors only: the world says so in text; nothing throws. (Tasks 1 and 4)
2. Fifty riders in one building: every rider gets its own spot; none share a pixel position. (Task 2)
3. Two accounts with the same session id or the same name: both riders exist. (Task 1)
4. A session whose repo changes, or the city being viewed losing its last rider: the rider appears once, in the new city; the view falls back to the map. (Tasks 1 and 4)
5. A window 360 points wide, and one far larger than the level: the scale is a whole number of at least 1 and nothing throws. (Task 2)

## File Structure

| Path | Responsibility |
|---|---|
| `Assets/TheGrid/Core/World.cs` | `Building`, `WorldRider`, `WorldCity`, `WorldModel`, `World.Build` |
| `Assets/TheGrid/Core/Iso.cs` | Tile-to-pixel maths, building plots, rider spots, map grid, scale |
| `Assets/TheGrid/Core/PixelArt.cs` | Palette and every generated texture |
| `Assets/TheGrid/WorldView.cs` | Sprites on screen, movement, bounce, hit-testing |
| `Assets/TheGrid/RosterApp.cs` | View switch, world text, picture mode |
| `Assets/TheGrid/Tests/WorldTests.cs`, `IsoTests.cs`, `PixelArtTests.cs` | EditMode tests |
| `fixtures/world/snapshot.json` | A hand-written roster snapshot with three cities, used by tests and picture mode |

**Running tests.** In this worktree, with no editor open on it:

```bash
/Applications/Unity/Hub/Editor/6000.4.3f1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath "$PWD" -runTests -testPlatform EditMode -assemblyNames TheGrid.Core.Tests -testResults Logs/editmode.xml
```

Pass means the root of `Logs/editmode.xml` has `result="Passed"` and `failed="0"`, and the log has no `error CS`. Written "run the core tests" below.

---

### Task 1: The world model

**Files:** Create `Core/World.cs`, `Tests/WorldTests.cs`, `fixtures/world/snapshot.json`.

**Interfaces:**
- Consumes: `RosterSnapshot`, `AgentRow` (`Core/Roster.cs`).
- Produces:

```csharp
public enum Building { Booth, Gate, Yard, Workshop, Garage, Depot }
[Serializable] public sealed class WorldRider { public string key, sessionId, name, account, app, state; public Building building; public int slot, subagents; public bool needsOwner; }
[Serializable] public sealed class WorldCity { public string repoRoot, name, workspace; public int needOwner, done, running; public List<WorldRider> riders = new List<WorldRider>(); }
[Serializable] public sealed class WorldModel { public long generatedAt; public List<WorldCity> cities = new List<WorldCity>(); }
public static class World {
    public static Building BuildingFor(string state);
    public static bool NeedsOwner(string state);       // Booth, Gate and Yard states
    public static WorldModel Build(RosterSnapshot snap);
}
```

Rules: one city per distinct `repoRoot` (agents with none share one `No project` city). Cities are ordered by name, then `repoRoot`, ordinal, so they do not jump about. `key` is `account + "|" + sessionId`. Within a building, riders are ordered by `key`, ordinal, and `slot` is that index. `subagents` is the agent's active subagent count. `needOwner`, `done` and `running` count riders in Booth, Gate or Yard; in Garage; and in Workshop.

- [ ] **Step 1: Write the failing tests**

```csharp
[TestCase("BlockedOnQuestion", Building.Booth, true)]
[TestCase("WaitingForPermission", Building.Gate, true)]
[TestCase("WaitingOther", Building.Gate, true)]
[TestCase("BackgroundBlocked", Building.Gate, true)]
[TestCase("Errored", Building.Yard, true)]
[TestCase("Unknown", Building.Yard, true)]
[TestCase("something new", Building.Yard, true)]
[TestCase("Running", Building.Workshop, false)]
[TestCase("Finished", Building.Garage, false)]
[TestCase("Offline", Building.Depot, false)]
public void StateDecidesTheBuilding(string state, Building b, bool needs) { Assert.AreEqual(b, World.BuildingFor(state)); Assert.AreEqual(needs, World.NeedsOwner(state)); }

// fixtures/world/snapshot.json: 3 repos (/work/alpha 4 agents, /work/beta 2, none 1), every state at least once, one agent with 2 subagents
var w = World.Build(snap);
CollectionAssert.AreEqual(new[] { "No project", "alpha", "beta" }, w.cities.Select(c => c.name).ToArray());
// never disagree: every agent is exactly one rider, in its repo's city, in its state's building
Assert.AreEqual(snap.agents.Count, w.cities.Sum(c => c.riders.Count));
foreach (var a in snap.agents) {
    var found = w.cities.SelectMany(c => c.riders.Select(r => (c, r))).Where(x => x.r.key == a.account + "|" + a.sessionId).ToList();
    Assert.AreEqual(1, found.Count); Assert.AreEqual(a.repoRoot, found[0].c.repoRoot);
    Assert.AreEqual(World.BuildingFor(a.state), found[0].r.building); Assert.AreEqual(a.subagents.Count, found[0].r.subagents);
}
var alpha = w.cities[1];
Assert.AreEqual(alpha.riders.Count(r => r.needsOwner), alpha.needOwner);
// slots inside one building are 0..n-1 in key order
foreach (var g in alpha.riders.GroupBy(r => r.building)) CollectionAssert.AreEqual(Enumerable.Range(0, g.Count()).ToArray(), g.OrderBy(r => r.key, StringComparer.Ordinal).Select(r => r.slot).ToArray());

// Review Focus 1 and 3
Assert.AreEqual(0, World.Build(new RosterSnapshot()).cities.Count);
// two agents, same sessionId and name, accounts "one" and "two": two riders
// the same snapshot built twice gives equal JSON (JsonUtility.ToJson)
```

- [ ] **Step 2:** Run the core tests. Expected: `error CS` naming `World`.
- [ ] **Step 3:** Implement `World`.
- [ ] **Step 4:** Run the core tests. Expected: all pass.
- [ ] **Step 5:** Commit `GRID-9: world model built from the roster snapshot`.

### Task 2: Isometric layout

**Files:** Create `Core/Iso.cs`, `Tests/IsoTests.cs`.

**Interfaces:**
- Consumes: `Building`.
- Produces (all values in art pixels, y growing downward, origin at the city's top corner):

```csharp
public static class Iso {
    public const int TileW = 32, TileH = 16, CityTiles = 8;
    public const int CityW = 256, CityH = 176;              // ground diamond plus room for buildings above it
    public static Vector2Int Tile(int col, int row);        // top corner of a tile: ((col - row) * 16, (col + row) * 8)
    public static Vector2Int Plot(Building b);              // the tile a building stands on
    public static Vector2Int RiderSpot(Building b, int slot); // where a rider waits beside that building
    public static Vector2Int MapCell(int index, int columns);  // top-left of a city icon cell on the far map; cells are 96 by 72
    public static int MapColumns(int cityCount, float viewW, float viewH);
    public static int Scale(float viewW, float viewH, int contentW, int contentH); // largest whole number that fits, never below 1
}
```

Riders line up along the road in front of their building, six to a row, rows stepping away from it.

- [ ] **Step 1: Write the failing tests**

```csharp
Assert.AreEqual(new Vector2Int(0, 0), Iso.Tile(0, 0)); Assert.AreEqual(new Vector2Int(16, 8), Iso.Tile(1, 0)); Assert.AreEqual(new Vector2Int(-16, 8), Iso.Tile(0, 1));
// six buildings on six different tiles inside the 8 by 8 city
// Review Focus 2: for every building, RiderSpot(b, 0..49) are 50 distinct points, and no spot is shared between two buildings
Assert.AreEqual(4, Iso.Scale(1100, 720, 256, 176)); Assert.AreEqual(1, Iso.Scale(360, 300, 256, 176)); Assert.AreEqual(1, Iso.Scale(100, 50, 256, 176)); // Review Focus 5
Assert.AreEqual(new Vector2Int(0, 0), Iso.MapCell(0, 3)); Assert.AreEqual(new Vector2Int(96, 0), Iso.MapCell(1, 3)); Assert.AreEqual(new Vector2Int(0, 72), Iso.MapCell(3, 3));
Assert.GreaterOrEqual(Iso.MapColumns(7, 360, 600), 1); Assert.AreEqual(1, Iso.MapColumns(0, 360, 600));
```

- [ ] **Step 2:** Run the core tests. Expected: `error CS` naming `Iso`.
- [ ] **Step 3:** Implement `Iso`.
- [ ] **Step 4:** Run the core tests. Expected: all pass.
- [ ] **Step 5:** Commit `GRID-9: isometric layout maths`.

### Task 3: Pixel art drawn by code

**Files:** Create `Core/PixelArt.cs`, `Tests/PixelArtTests.cs`.

**Interfaces:**
- Consumes: `Building`.
- Produces:

```csharp
public static class PixelArt {
    public static readonly Color32[] Palette;               // 16 colours; index 0 is fully transparent
    public static int AccountColour(string account);        // a stable palette index from the bright colours
    public static Texture2D Ground(); public static Texture2D Road();           // 32 by 16
    public static Texture2D BuildingArt(Building b);                             // 48 by 56, anchored at its bottom centre
    public static Texture2D Bike(string app, int glow, bool dimmed);             // 24 by 16; dimmed for Depot riders
    public static Texture2D SmallBike(int glow);                                 // 12 by 8
    public static Texture2D Marker();                                            // 8 by 10
    public static Texture2D CityIcon();                                          // 64 by 40
}
```

Every texture uses `FilterMode.Point`, no mipmaps, and only palette colours. The look: dark navy shapes with one bright glow line, in the spirit of the owner's reference sprite, without copying any existing design.

- [ ] **Step 1: Write the failing tests**

```csharp
Assert.AreEqual(16, PixelArt.Palette.Length); Assert.AreEqual(0, PixelArt.Palette[0].a);
// for each texture above (both bike shapes, all six buildings): expected size, FilterMode.Point, every pixel is a palette colour, at least 20 non-transparent pixels
// drawing the same thing twice gives identical pixels
// the six buildings are not all the same picture; Bike("cli", ...) differs from Bike("claude-desktop", ...); a dimmed bike differs from a bright one
Assert.AreEqual(PixelArt.AccountColour("one"), PixelArt.AccountColour("one")); Assert.AreNotEqual(0, PixelArt.AccountColour(""));
```

- [ ] **Step 2:** Run the core tests. Expected: `error CS` naming `PixelArt`.
- [ ] **Step 3:** Implement `PixelArt` with one small private canvas helper (set pixel, line, filled diamond, box).
- [ ] **Step 4:** Run the core tests. Expected: all pass.
- [ ] **Step 5:** Commit `GRID-9: pixel art generated in code`.

### Task 4: The world view

**Files:** Create `Assets/TheGrid/WorldView.cs`. Modify `Assets/TheGrid/RosterApp.cs`.

**Interfaces:**
- Consumes: `World.Build`, `Iso`, `PixelArt`, `RosterSnapshot`.
- Produces:

```csharp
public sealed class WorldView : MonoBehaviour {
    public void Show(WorldModel model, Rect viewPoints, float pointScale); // call on every Layout pass while the world view is open
    public void Hide();
    public string OpenCity { get; }                         // repoRoot of the city being viewed, or null on the far map
    public void DrawText(GUIStyle name, GUIStyle count);   // city names and counts, building names, rider names
    public string Click(Vector2 guiPoint);                  // enters a city or selects a rider; returns the selected rider's key or ""
    public void Back();
}
```

Behaviour:
- `RosterApp` creates one `WorldView` on a child object at start and builds the `WorldModel` from the same snapshot it draws the roster from, once per Layout pass.
- View switch as in decision 6. With the world open, the lists are not drawn; with the lists open, `Hide()` deactivates every world object.
- The camera is orthographic, one unit per art pixel, sized so one art pixel covers `scale` whole screen pixels, where `scale` comes from `Iso.Scale`.
- Far map: one `CityIcon` per city with its name and `n need you · n done`. A city with `needOwner > 0` shows a bouncing `Marker`.
- City: ground and road tiles, six buildings with their names, one `Bike` per rider at `Iso.RiderSpot`, `SmallBike`s trailing a rider with subagents, a bouncing `Marker` over each rider that needs the owner. Depot riders are dimmed.
- A rider seen for the first time is placed directly. A rider whose building or slot changed rides there in a straight line over 1.2 seconds. Nothing else moves.
- The bounce is two positions, 2 art pixels apart, switching every 0.4 seconds.
- Clicking a rider selects it; the selected agent's roster row is drawn under the city with the existing `DrawAgent`. Escape or `← All cities` returns to the map. If the open city is no longer in the model, the view returns to the map.
- No agents: the text `No live agent sessions.` and no sprites.
- Picture mode (decision 7): with `-snapshot <file>` the collector and the pile are not started and the snapshot is read from the file; with `-shots <folder>` the app saves `world.png` (far map) and `city.png` (first city with a rider that needs the owner, else the first city) using `ScreenCapture.CaptureScreenshot`, then quits.

- [ ] **Step 1:** Implement `WorldView` and the `RosterApp` changes.
- [ ] **Step 2:** Run the core tests. Expected: all pass (no test of this task; it has no logic the earlier tasks do not already pin).
- [ ] **Step 3:** Build with `-executeMethod TheGrid.EditorTools.GridMenu.BuildMac` in this worktree. Expected: the log reports a successful build and a signed app.
- [ ] **Step 4:** Copy the built app and `fixtures/world/snapshot.json` to a scratch folder outside Documents.
- [ ] **Step 5:** Ask the owner for a go-ahead to run that copy once in picture mode while their own copy stays open, or to quit theirs first. Picture mode reads no agent data and opens no protected folder.
- [ ] **Step 6:** Run it with `-snapshot`, `-shots` and `-logFile` pointing into the scratch folder. Expected: `world.png` and `city.png` exist; the log has no exception.
- [ ] **Step 7:** Look at both pictures. Expected: three cities on the map with the right counts; in the city, every rider beside the building its state maps to, markers only over riders that need the owner, crisp pixels, readable names. Fix what is wrong and repeat from Step 3.
- [ ] **Step 8:** Run picture mode once more at 720 by 1400 pixels (`-screen-width 720 -screen-height 1400`). Expected: both pictures still show everything.
- [ ] **Step 9:** Commit `GRID-9: world view, view switch and picture mode`.

### Task 5: Wrap-up

**Files:** Modify `README.md`, `docs/roster-snapshot.md` (a short "The world" section: which state maps to which building).

- [ ] **Step 1:** Update the README: the world view, how to switch to it, what each building means, and picture mode.
- [ ] **Step 2:** With the owner's copy closed or replaced by this build, sample the app's processor use for 60 seconds on the world view and on the lists. Expected: lists near 6.5% of a core; record the world figure.
- [ ] **Step 3:** Commit `GRID-9: README and docs for the world view`.
- [ ] **Step 4:** Push, open the pull request with both pictures attached, post `@codex review`, and answer its findings.
- [ ] **Step 5:** Comment on GRID-9 with what was verified, the processor figures, and anything that departed from this plan.
