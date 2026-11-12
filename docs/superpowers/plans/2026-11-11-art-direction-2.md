# Art Direction 2 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the world look and read as the art direction asks: sectors of concrete and graphite lit by long lines of light, sessions as helmeted bots whose state is on the bot, a panel of who needs the owner, and couriers for delivered messages.

**Architecture:** Four parts, each its own ticket, branch and pull request, each leaving the app working. A: a new sprite set from the Blender script, described by a manifest; the world does not use it yet. B: the world model moves state from buildings onto bots and the view is rebuilt on the new set; the old set goes. C: two panels drawn over the map from the same model. D: deliveries read from the review pile become a courier and a pulse.

**Tech Stack:** Unity 6000.4.3f1, C#, IMGUI, isometric Tilemap, Unity Test Framework (EditMode), Blender 5.1 in background mode with numpy.

**Spec:** `docs/art-direction.md`. Tickets: GRID-16 (parent), GRID-17 (A), GRID-18 (B), GRID-19 (C), GRID-20 (D). Built on top of GRID-15's branch.

## Global Constraints

- A ground tile is 64 pixels wide and 32 high. No sprite is drawn at a finer grain than that.
- One palette (decision 2). Every opaque pixel is one of its colours; alpha is fully on or fully off; point filtering; whole-number scaling.
- On any one building every light is the same colour. Light is primarily blue, teal and white.
- On buildings light runs in long unbroken lines up corners and sides and round the roof edge. On the ground it runs along kerbs, lane edges and platform outlines, with glow beside it in flat bands.
- No cobblestones, trees, flowers, planters or freestanding streetlamps. No skyline, horizon, bloom or gradient.
- Bots are people in suits under oversized closed helmets, about 32 pixels tall, read from the gaps between legs, between arm and body, between helmet and shoulders.
- Everything shown is true: a bot's place, pose and badge follow from the roster snapshot; a courier and a pulse appear only for a delivery the review pile observed. Nothing moves unless something changed.
- World and roster never disagree: both come from the same `RosterSnapshot`.
- Idle processor use with the world open stays at the lists' level (about 6 to 7% of a core on the owner's machine). The map is drawn only when something in it changed.
- Nothing leaves the machine. Agent app storage is read-only. The Grid sends, changes and delivers no message.
- The repo is public: no real names of people, companies, accounts, repos or sessions. Reference pictures and the owner's mockup are never committed; no design or name is taken from them. In code and in the window the words are sector, bot, lane and bike.
- All art is made by `tools/art/make_sprites.py`. The PNGs are committed, so building the app does not need Blender.
- Only one copy of the app runs at a time on the owner's Mac. Ask before launching a build while theirs is open.
- After opening each pull request post `@codex review`; post it again after every push that answers findings.

## Decisions made in this plan

The reviewer's to veto.

1. **Words.** sector (was city), bot (was rider), lane (was path), bike. Code is renamed to match in part B.
2. **Palette 2**, 31 colours: `#0b0e22 #05070f #0c1024 #141732 #1b1f41 #242950 #2e3463 #3a4178 #474e8c #525a9b #5f67aa #6e76b7` (ground to light graphite and concrete), `#2c3360 #353d70 #3f4780` (paving), `#101a34` (smoked glass), `#35f2ff #c9fbff #1a8f9c` (cyan, cyan-white, dim teal), `#3f5a96 #4a7fb4 #62b4d6` (cyan glow on paving, far to near), `#a070ff #d9c8ff #4b4696 #6657b8` (violet, violet-white, violet glow far and near), `#ffb23e #b8741f #6e4a1c` (amber and its two shades), `#ff4d4d` (fault), `#ffffff` (the part of a bot the game tints). `tools/art/studies/light-lines.py` uses these.
3. **Six buildings, for kinds of work.** Server tower: reading and searching. Assembly bay: editing. Test workshop: running tests. Review hall: reviewing. Works: working, when it cannot be told on what. Relay: where couriers leave from and arrive.
4. **State is on the bot** (decision 5 gives where it stands):

| `state` | `activity` | Place | Pose | Badge |
|---|---|---|---|---|
| `Running` | `reading` | ServerTower | Inspect | None |
| `Running` | `editing` | AssemblyBay | Type | None |
| `Running` | `testing` | Workshop | Type | None |
| `Running` | `reviewing` | ReviewHall | Inspect | None |
| `Running` | anything else | Works | Stand | None |
| `BlockedOnQuestion` | | Forecourt | Stand | Question |
| `WaitingForPermission`, `WaitingOther`, `BackgroundBlocked` | | Forecourt | Stand | Approve |
| `Errored`, `Unknown`, any other | | Forecourt | Stand | Fault |
| `Finished` | | Plaza | Stand | Done |
| `Offline` | | Dock | Off | None |

5. **A sector's layout**, in tiles from the plot's first tile (column, row; tall things at the back so they hide nothing):

| Structure | First tile | Size |
|---|---|---|
| ServerTower | 0, 0 | 3 by 3 |
| ReviewHall | 4, 0 | 3 by 3 |
| Relay | 7, 0 | 2 by 2 |
| Works | 9, 0 | 3 by 3 |
| AssemblyBay | 0, 5 | 4 by 3 |
| Fountain | 5, 5 | 2 by 2 |
| Workshop | 8, 5 | 4 by 3 |

| Place | First spot | In a row | Step along | Step away |
|---|---|---|---|---|
| ServerTower | 0.25, 3.25 | 3 | 1 | 0.5 |
| ReviewHall | 4.25, 3.25 | 3 | 1 | 0.5 |
| Works | 9.25, 3.25 | 3 | 1 | 0.5 |
| AssemblyBay | 0.4, 8.25 | 4 | 1 | 0.5 |
| Workshop | 8.7, 8.25 | 4 | 1 | 0.5 |
| Plaza | 4.4, 7.4 | 4 | 1 | 0.5 |
| Forecourt | 3.5, 9.75 | 6 | 1 | 0.75 |
| Dock | 9.6, 9.75 | 3 | 0.8 | 0.75 |

The workshop's first two bots stand inside it, at its terminal (9.5, 6.0) and its bench (10.5, 6.6); the table starts from its third. These numbers have been checked: no two structures share a tile, 50 spots of every place are 400 different points, and the first six of each place stand on no structure.

6. **One manifest says what every sprite is.** `Assets/TheGrid/Resources/Art/sprites.json`, written by the script: name, width, height, and the ground point in pixels from the bottom left. The importer takes pivots from it, the tests take expectations from it, the stage takes the building names from it. Today that knowledge is in three places.
7. **Bots** are drawn in four facings and seven poses: `stand`, `walk-a`, `walk-b`, `type`, `inspect`, `signal`, `off`. The visor and the suit's lines are pure white in the PNG and tinted per account by the game, as riders were. `off` has neither lit.
8. **The world opens on one sector**: the first that needs the owner, else the first, at the closest zoom level at which a whole sector and its lane fit the view. One step out shows every sector, with each summed up as today.
9. **Lit lines are overlays**, on a second tilemap above the ground: a line along a tile's edge with its glow on both sides. The ground tiles themselves stay plain.
10. **Beyond a sector** is `tile-void` and dark city blocks. Stones, spires and lakes go.
11. **Panels are IMGUI**, drawn as flat plates in palette colours with a one-pixel lit edge. The bot card replaces the roster row under the map.
12. **A delivery is shown** when a pile row gains a received time later than the moment the world opened, and its sender and recipient each match exactly one bot by account and session name. Between sectors a courier rides the lanes at 8 tiles a second, and never for less than 1.5 seconds; on arrival the recipient pulses for 0.9 seconds. Within one sector there is the pulse and no courier.
13. **`WorldView` is split.** `WorldView`: the window side (camera, kept picture, input, labels). `WorldStage`: the scene's objects, built from the model. `WorldPanels`: the two panels. `WorldTraffic`: couriers and pulses.
14. **The first sprite set is deleted** in part B, with `Building`, `Iso.BuildingTile`, `Iso.RiderSpot` and the outland scenery.

Not in this plan: search, a title plate with buttons, sound, building interiors other than the workshop's, other agent apps, bots walking between sectors, an `activity` for messaging.

## Review Focus

1. Forty bots waiting in one sector: each has its own spot, the panel scrolls, the map stays clickable. (B2, C2)
2. A `state` this build has never heard of: a fault badge on the forecourt, counted as needing the owner, nothing thrown. (B1)
3. A message whose sender or recipient matches no bot, or two bots with the same name on one account: no courier, no pulse, nothing thrown. (D1)
4. A sprite named in code but absent from the manifest or the folder: one log line naming it, the world drawn without it; the art tests fail naming it. (A1, B3)
5. A window 360 points wide, or 300 tall: panels shrink or fold, never cover the whole map. (C2)

## Running things

In the worktree, with no editor open on it. "Run the core tests":

```bash
/Applications/Unity/Hub/Editor/6000.4.3f1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath "$PWD" -runTests -testPlatform EditMode -assemblyNames TheGrid.Core.Tests -testResults Logs/editmode.xml
```

Pass means the root of `Logs/editmode.xml` has `result="Passed"`, `failed="0"`, and the log has no `error CS`. "Render the sprites":

```bash
/Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup --python tools/art/make_sprites.py
```

"Render the world" (no window, no text): `-batchmode -projectPath "$PWD" -executeMethod TheGrid.EditorTools.GridMenu.WorldPictures -quit`, then look at `Logs/world-near.png`. "Shoot the app": build with `-executeMethod TheGrid.EditorTools.GridMenu.BuildMac`, copy the app and the fixture outside Documents, run it with `-snapshot <file> -shots <folder> -logFile <file>`.

---

# Part A: Art kit 2 (GRID-17)

Branch `feature/grid-17-art-kit-2-palette-ground-buildings-bots`.

### Task A1: The kit, the manifest, the importer

**Files:** Create `tools/art/kit.py`, `Assets/TheGrid/Resources/Art/sprites.json`. Modify `tools/art/make_sprites.py`, `Assets/TheGrid/Editor/ArtImport.cs`, `Assets/TheGrid/Tests/ArtTests.cs`, `tools/art/README.md`.

**Interfaces, produced:**

```python
# tools/art/kit.py: the camera, palette 2, and everything a sprite is built and saved with
PALETTE: dict[str, str]                      # name to hex, decision 2
def box(cx, cy, z0, w, d, h, top, left=None, right=None)    # colours by palette name
def light(cx, cy, z0, w, d, h, colour)                       # one colour on every face
def disc(cx, cy, z0, r, h, top, side=None, axis="z")
def ball(cx, cy, z, r, colour)
def limb(cx, cy, z0, r, h, colour, r2=None)                  # a cylinder, or a cone when r2 is given
def glow(cx, cy, w, d, family)               # three flat bands on the ground round a w by d rectangle; family "cyan" or "violet"
def render(name, w, h, ground_x, ground_y)   # renders the scene, snaps to the palette, saves, records the sprite for the manifest, clears
def write_manifest(); def contact_sheet()
```

```json
{ "sprites": [ { "name": "tile-paving", "width": 64, "height": 32, "groundX": 32, "groundY": 16 } ] }
```

The first sprite set keeps rendering exactly as before, through the kit, and is in the manifest too. Until part B removes it, its 24 colours are kept as `palette-1.png`, and `render` takes which palette to snap to. `palette.png` becomes palette 2.

- [ ] **Step 1: Write the failing tests** in `ArtTests.cs`, replacing the hand-written size lists:

```csharp
[Serializable] sealed class Manifest { public List<Entry> sprites; }
[Serializable] sealed class Entry { public string name; public int width, height, groundX, groundY; }
static Manifest Read() => JsonUtility.FromJson<Manifest>(Resources.Load<TextAsset>("Art/sprites").text);

[Test] public void EveryPictureInTheFolderIsInTheManifestAndTheOtherWayRound()   // names of *.png under Resources/Art, less the two palettes, equal the manifest's names
[TestCaseSource(nameof(Names))] public void SpriteIsAsTheManifestSays(string name)
// width, height, pivot == (groundX, groundY), Point filter, 1 pixel per unit, one mip, every pixel alpha 0 or 255;
// its opaque pixels are all in palette.png, or all in palette-1.png (the first set, until part B)
[TestCaseSource(nameof(Names))] public void NothingIsCutOffAtTheEdgeOfItsPicture(string name)
// for sprites drawn from palette.png, tiles apart (names starting "tile-" or "line-"): no opaque pixel lies in the outermost row or column
[Test] public void ThePaletteIsDecisionTwo()   // palette.png is 31 by 1 and its colours are the 31 of decision 2, in order
```

- [ ] **Step 2:** Run the core tests. Expected: failures naming `Art/sprites`.
- [ ] **Step 3:** Write `kit.py` (move the camera, `snap`, `save`, `render` and the contact sheet out of `make_sprites.py`; add the primitives above; `render` appends to the manifest). `make_sprites.py` imports it (`sys.path.insert(0, os.path.dirname(__file__))`). `ArtImport.Pivot` reads the manifest; `OnPreprocessTexture` calls `context.DependsOnSourceAsset` on it; `GetVersion()` returns 4. A name the manifest lacks gets a centred pivot and one `Debug.LogWarning` naming it (Review Focus 4).
- [ ] **Step 4:** Render the sprites, run the core tests. Expected: all pass; `git status` shows the old PNGs unchanged.
- [ ] **Step 5:** Commit `GRID-17: sprite kit, palette 2 and a manifest the importer and tests read`.

### Task A2: Ground, light, and what lies beyond

**Files:** Create `tools/art/ground.py`. Modify `tools/art/make_sprites.py`.

Sprites, all with the ground point at the footprint's centre:

| Name | Size | What |
|---|---|---|
| `tile-paving`, `tile-paving-joint-a`, `tile-paving-joint-b` | 64×32 | paving `#353d70`; a joint (`#2c3360` with a `#3f4780` lip) along the far edge one way or the other |
| `tile-lane`, `tile-lane-chevron-a`, `tile-lane-chevron-b` | 64×32 | lane `#141732`; a pair of cyan chevrons pointing along it |
| `tile-crossing-a`, `tile-crossing-b` | 64×32 | lane with bars of `#3f4780` across it |
| `tile-void` | 64×32 | ground colour with a few dim teal dashes |
| `line-a`, `line-b` | 96×48 | overlay: a cyan-white line along the tile's far edge one way or the other, three bands of cyan glow each side |
| `fountain` | 192×160, ground 96, 64 | a 2 by 2 platform outlined in light, a ring and a column of violet light, violet glow on the paving |
| `block-a`, `block-b`, `block-c` | 128×160, ground 64, 32 | dark graphite blocks of three heights, one or two dim teal lines each |

- [ ] **Step 1:** Add these names to the manifest by writing `ground.py` and calling it. The art tests from A1 are the tests: they fail for any missing, mis-sized, off-palette or cut-off sprite.
- [ ] **Step 2:** Render the sprites, run the core tests. Expected: all pass.
- [ ] **Step 3:** Look at `docs/art/contact-sheet.png` against `docs/art/direction-light.png`. Expected: the same paving, lane and line of light. Fix and repeat from step 2 until true.
- [ ] **Step 4:** Commit `GRID-17: ground, lit lines, fountain and city blocks`.

### Task A3: The six buildings

**Files:** Create `tools/art/buildings.py`. Modify `tools/art/make_sprites.py`.

| Name | Size | Ground | One light | Told by |
|---|---|---|---|---|
| `b-server-tower` | 256×352 | 128, 80 | cyan-white | four roof fans, a service ladder, racks behind smoked glass |
| `b-review-hall` | 256×288 | 128, 80 | violet | two blocks joined by a bridge at the top |
| `b-works` | 256×224 | 128, 80 | cyan | a plain shed with one stack |
| `b-relay` | 192×256 | 96, 64 | cyan-white | a kiosk under a mast with three rings |
| `b-assembly-bay` | 288×224 | 144, 88 | amber | a wide low hall, one large lit door, a gantry across the roof |
| `b-test-workshop` | 288×224 | 144, 88 | cyan-white | the two near walls cut to a kerb; inside, a terminal with a wall screen and a bench |

Every building: a footing with a recessed strip of its light and that light's glow on the paving; a line of its light up each visible corner and at least one up each visible side, unbroken from footing to roof, and on round the roof edge; an entrance under a canopy with a strip beneath it. `light-lines.py` builds the server tower this way.

- [ ] **Step 1:** Write `buildings.py`; the art tests are the tests.
- [ ] **Step 2:** Render the sprites, run the core tests. Expected: all pass.
- [ ] **Step 3:** Look at the contact sheet. Expected: each building named from its picture alone by someone shown only the "Told by" column; on each, one light colour. Fix and repeat from step 2 until true.
- [ ] **Step 4:** Commit `GRID-17: six buildings, each with a job and one light`.

### Task A4: Bots, the bike, badges, the pulse

**Files:** Create `tools/art/bots.py`. Modify `tools/art/make_sprites.py`, `tools/art/README.md`.

| Name | Size | Ground | What |
|---|---|---|---|
| `bot-<pose>-<facing>`: 7 poses × `se ne nw sw` | 40×48 | 20, 8 | decision 7. `type`: both arms forward. `inspect`: one arm raised holding a disc. `signal`: one hand to the helmet. `walk-a`, `walk-b`: one leg forward, then the other |
| `bot-small` | 24×32 | 12, 5 | a subagent: `stand`, facing `se`, at three fifths the size |
| `bike-<facing>` × 4 | 72×56 | 36, 14 | a courier leaning over a bike; the bike's line of light is white for tinting |
| `badge-question`, `badge-approve`, `badge-done` | 16×20 | 8, 0 | amber with `?`, amber with `!`, cyan-white with a tick |
| `badge-fault` | 16×20 | 8, 0 | `#ff4d4d` with a cross |
| `pulse-0`, `pulse-1`, `pulse-2` | 64×32 | 32, 16 | a violet ring on the ground, growing |

- [ ] **Step 1:** Write `bots.py`: one `bot(pose)` that every facing renders through the kit's quarter turns; the art tests are the tests. Add to `ArtTests`:

```csharp
[Test] public void ABotIsReadFromItsGaps()
// in bot-stand-se: some column between the two legs is empty for at least 4 rows; some row has empty pixels between an arm and the body
[Test] public void OnlyTheVisorAndSuitLinesAreWhite()   // in every bot-* but bot-off-*: at least 12 pure white pixels; in bot-off-*: none
```

- [ ] **Step 2:** Render the sprites, run the core tests. Expected: all pass.
- [ ] **Step 3:** Look at the contact sheet against `docs/art/direction-bots.png`. Expected: a person, not a robot; the three poses told apart at a glance. Fix and repeat from step 2 until true.
- [ ] **Step 4:** `tools/art/README.md`: the kit's files, the manifest, the table of sprites, how to add a building or a pose. Commit `GRID-17: bots, courier, badges and pulse`. Push, open the pull request with the contact sheet, post `@codex review`.

---

# Part B: The sector (GRID-18)

Branch `feature/grid-18-the-sector-state-on-the-bot-buildings-for`, cut from part A's.

### Task B1: Sectors and bots in the model

**Files:** Modify `Assets/TheGrid/Core/World.cs`, `Assets/TheGrid/Tests/WorldTests.cs`, `docs/roster-snapshot.md`; in `Assets/TheGrid/WorldView.cs` and `Assets/TheGrid/RosterApp.cs` only the renames below.

The old view still draws the first sprite set until B3, so `WorldBot` also keeps `public Building building; public int buildingSlot;` (the slot among bots at the same building), filled as today and removed in B3. Renames: `WorldRider` to `WorldBot`, `WorldCity` to `WorldSector`, `cities` to `sectors`, `riders` to `bots`.

**Interfaces, produced:**

```csharp
public enum Place { ServerTower, ReviewHall, Works, AssemblyBay, Workshop, Plaza, Forecourt, Dock }
public enum Pose { Stand, Type, Inspect, Off }
public enum Badge { None, Question, Approve, Fault, Done }
[Serializable] public sealed class WorldBot { public string key, sessionId, name, account, app, state, activity; public Place place; public Pose pose; public Badge badge; public int slot, subagents; public bool needsOwner; }
[Serializable] public sealed class WorldSector { public string repoRoot, name, workspace; public int plot; public Vector2Int cell; public int needOwner, done, running; public List<WorldBot> bots; }
public sealed class WorldModel { public long generatedAt; public List<WorldSector> sectors; }
public static class World {
    public static Place PlaceFor(string state, string activity);
    public static Pose PoseFor(string state, string activity);
    public static Badge BadgeFor(string state);
    public static bool NeedsOwner(string state);                 // Question, Approve and Fault
    public static string Summary(WorldSector sector);            // unchanged wording
    public static WorldModel Build(RosterSnapshot snap, Plots plots);
}
```

`slot` is the bot's index among the bots of its sector at the same `place`, ordered by `key`.

- [ ] **Step 1: Write the failing tests.** One `TestCase` per row of decision 4's table for `PlaceFor`, `PoseFor`, `BadgeFor`, plus:

```csharp
[TestCase("something new", "", Place.Forecourt, Pose.Stand, Badge.Fault)]      // Review Focus 2
[TestCase("BlockedOnQuestion", "editing", Place.Forecourt, Pose.Stand, Badge.Question)]   // a stale activity changes nothing
// TheWorldAndTheRosterNeverDisagree, on fixtures/world/snapshot.json: every agent is exactly one bot, in its repo's sector,
// with PlaceFor, PoseFor, BadgeFor and NeedsOwner of its state; needOwner, done and running match; slots are 0..n-1 in key order within a place
// Assert.IsTrue(World.NeedsOwner("something new"));
```

- [ ] **Step 2:** Run the core tests. Expected: `error CS` naming `Place`.
- [ ] **Step 3:** Implement; rename in the view and the window; the old view reads `buildingSlot` where it read `slot`.
- [ ] **Step 4:** Run the core tests. Expected: all pass.
- [ ] **Step 5:** `docs/roster-snapshot.md`: replace the buildings table with decision 4's. Commit `GRID-18: sectors and bots; state is on the bot`.

### Task B2: Where things stand

**Files:** Modify `Assets/TheGrid/Core/Iso.cs`, `Assets/TheGrid/Tests/IsoTests.cs`.

**Interfaces, produced:**

```csharp
public enum Structure { ServerTower, ReviewHall, Relay, Works, AssemblyBay, Fountain, Workshop }
public static RectInt Footprint(Structure s);                              // decision 5, in tiles from the plot's first tile
public static Vector2 StructureSpot(Vector2Int cell, Structure s);         // the centre of its footprint, in art pixels
public static Vector2 BotSpot(Vector2Int cell, Place place, int slot);     // decision 5
public static Vector2 Point(float col, float row);                         // made public
```

- [ ] **Step 1: Write the failing tests.**

```csharp
[Test] public void StructuresFitThePlotAndShareNoTile()        // 7 footprints, 59 tiles, all different, all within 0..11
[Test] public void FiftyBotsAtEveryPlaceEachHaveTheirOwnSpot() // 8 places by 50 slots: 400 different points (Review Focus 1)
[Test] public void TheFirstSixOfAPlaceStandOnNoStructure()     // the tile under each spot is in no footprint
[Test] public void TheWorkshopsFirstTwoStandInsideIt()
// BotSpot(cell, Workshop, 0) == Point(o.x + 9.5f, o.y + 6.0f); slot 1 == Point(o.x + 10.5f, o.y + 6.6f); slot 2 == Point(o.x + 8.7f, o.y + 8.25f)
[Test] public void ASpotMovesWithItsSector()                   // the same place and slot in two cells differ by the cells' origins
```

- [ ] **Step 2:** Run the core tests. Expected: `error CS` naming `Structure`.
- [ ] **Step 3:** Implement from decision 5's two tables.
- [ ] **Step 4:** Run the core tests. Expected: all pass.
- [ ] **Step 5:** Commit `GRID-18: where structures and bots stand in a sector`.

### Task B3: The stage

**Files:** Create `Assets/TheGrid/WorldStage.cs`. Modify `Assets/TheGrid/WorldView.cs`, `Assets/TheGrid/RosterApp.cs`, `Assets/TheGrid/Editor/GridMenu.cs`, `Assets/TheGrid/Core/World.cs`, `Assets/TheGrid/Core/Iso.cs`, their tests, `tools/art/make_sprites.py`, `fixtures/world/snapshot.json`, `README.md`. Delete the first sprite set's PNGs and the code of decision 14.

**Interfaces, produced:**

```csharp
public sealed class WorldStage {                       // the scene's objects; no camera, no input, no text
    public WorldStage(Transform root);
    public bool Sync(WorldModel model, bool far);      // lays out what changed; true when the picture must be drawn again
    public bool Tick(float now);                       // walks, badge bounce; true when the picture must be drawn again
    public bool TryGetBot(string key, out Vector2 at); // where a bot is now, in art pixels
    public string Pick(Vector2 world);                 // the bot under a point, or ""
}
// WorldView keeps: Show, Hide, Gui, Picture, Selected, ZoomBy, LookAtAll; and gains
public void LookAtSector(Vector2Int cell);             // decision 8's level, centred on that sector
public void LookAt(string botKey);                     // centre on a bot, without changing level
```

Behaviour:
- Ground tilemap: a sector is `tile-paving` with a joint every third tile each way; the lane round it is `tile-lane`, with a chevron tile every fourth tile and a crossing opposite the forecourt. Overlay tilemap: `line-a` and `line-b` along both edges of the lane and round the fountain's platform.
- The seven structures stand at `Iso.StructureSpot`. Beyond a sector: `tile-void` and blocks, laid out per cell as the scenery is today.
- A bot is `bot-<pose>-<facing>` at `Iso.BotSpot`, tinted by account, facing the structure it works at, or the viewer on the plaza, forecourt and dock. Its badge floats over its helmet and bounces as the marker does today, for the three badges that need the owner; `badge-done` does not bounce. Up to three `bot-small` stand beside a bot with subagents.
- First sight: placed. Place or slot changed: it walks there in a straight line at 4 tiles a second, `walk-a` and `walk-b` alternating every 0.2 seconds, then takes its pose.
- The world opens as decision 8 says. Far out, each sector's name and `World.Summary`, as today.
- A sprite that fails to load is logged once by name and left out (Review Focus 4).
- `GridMenu.WorldPictures` and picture mode work as before; `near.png` is the sector the world opens on.

- [ ] **Step 1:** Extend `fixtures/world/snapshot.json` so the first sector has a bot at every place, two of them waiting (one question, one approve).
- [ ] **Step 2:** Implement `WorldStage`; move object building out of `WorldView`; delete decision 14's code and sprites and the tests that covered them; rename what is left of city, rider and path.
- [ ] **Step 3:** Run the core tests. Expected: all pass.
- [ ] **Step 4:** Render the world; look at `Logs/world-near.png`. Expected: the composition check of the spec, less the courier: the two waiting bots spotted at once, the workshop told by sight with a bot at its terminal, tall buildings hiding no bot. Fix and repeat from step 3 until true.
- [ ] **Step 5:** Shoot the app at 2000 by 1250 and at 720 by 1400. Expected: both pictures exist, no exception in the log, names and badges legible.
- [ ] **Step 6:** With only this build running, measure processor use for 60 seconds on the lists and on the world. Expected: the world at or below the lists.
- [ ] **Step 7:** `README.md`: the world section rewritten for sectors, bots, the six buildings and the badges, with the new pictures. Commit `GRID-18: the sector, on the new art`. Push, open the pull request with both pictures, post `@codex review`.

---

# Part C: Panels (GRID-19)

Branch `feature/grid-19-panels-who-needs-you-and-the-bot-card`, cut from part B's.

### Task C1: What the panels say

**Files:** Create `Assets/TheGrid/Core/Panels.cs`, `Assets/TheGrid/Tests/PanelsTests.cs`.

**Interfaces, produced:**

```csharp
[Serializable] public sealed class NeedsYouRow { public string key, sector, name; public Badge badge; public string what; }
[Serializable] public sealed class BotCard { public string key, title, who, doing, since, ticket, prUrl; public int prNumber; }
public static class Panels {
    public static List<NeedsYouRow> NeedsYou(RosterSnapshot snap);          // in the snapshot's own order, which is most urgent first
    public static BotCard Card(RosterSnapshot snap, string key, long nowMs); // null when no agent has that key
}
```

`what`: `Answer a question`, `Approve a step`, `Waiting`, `Blocked in the background`, `Failed`, or `Unknown: ` and the agent app's own words. `title`: `Reading`, `Editing`, `Testing`, `Reviewing`, `Working`, or for a bot that is not running the same words as `what`, `Done`, `Offline`. `who`: name, a middle dot, account. `doing`: the repo's name, then the ticket when there is one. `since`: `RosterFormat.Elapsed`.

- [ ] **Step 1: Write the failing tests.**

```csharp
// on fixtures/world/snapshot.json: NeedsYou has exactly the agents for which World.NeedsOwner(state), in snapshot order
// each state's `what`, one TestCase per state; an Unknown row carries rawStatus
// a Running agent with activity "editing": Card.title == "Editing"; a Finished one: "Done"
// Card(snap, "no|such", now) == null; NeedsYou(new RosterSnapshot()).Count == 0
// a prUrl that is not https: Card.prUrl == ""   (RosterFormat.IsWebLink)
```

- [ ] **Step 2:** Run the core tests. Expected: `error CS` naming `Panels`.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run the core tests. Expected: all pass.
- [ ] **Step 5:** Commit `GRID-19: what the panels say`.

### Task C2: The panels on the map

**Files:** Create `Assets/TheGrid/WorldPanels.cs`. Modify `Assets/TheGrid/RosterApp.cs`, `README.md`.

**Interfaces, produced:** `public sealed class WorldPanels { public string Draw(Rect map, List<NeedsYouRow> rows, BotCard card, GUIStyle text, GUIStyle strong); }` returning the key of a row that was clicked, or `""`.

Behaviour:
- Needs you: top right of the map, 220 points wide, a title `Needs you` with the count and a fold button; one row per entry with its badge and `what`, the bot's name beneath in the dim style; at most six rows showing, the rest scrolled. Folded, only the title. With no rows, not drawn.
- Clicking a row: `WorldView.LookAt(key)` and select that bot.
- Card: bottom left, 260 points wide: `title`, `who`, `doing` and `since`, and a `PR #n` button when there is one. Drawn only while a bot is selected. The roster row under the map goes.
- Plates: `#0c1024` at full strength with a one-pixel `#3a4178` edge and a `#35f2ff` top line.
- In a map narrower than 520 points the panel starts folded and the card spans the map's width; in a map shorter than 300 points the panel shows two rows (Review Focus 5). A press on a panel never reaches the map beneath.

- [ ] **Step 1:** Implement; `RosterApp.DrawWorld` builds the rows and the card on the Layout pass from the same snapshot as the world.
- [ ] **Step 2:** Run the core tests. Expected: all pass.
- [ ] **Step 3:** Shoot the app at 2000 by 1250, 720 by 1400 and 2000 by 600 with a bot selected (`-select <key>`, a new picture-mode argument). Expected: both panels legible; in the narrow one the panel folded and the card full width; in the short one two rows; the map visible around them in all three (Review Focus 5).
- [ ] **Step 4:** `README.md`: the two panels. Commit `GRID-19: who needs you, and the bot card`. Push, open the pull request with the pictures, post `@codex review`.

---

# Part D: Messages in the world (GRID-20)

Branch `feature/grid-20-messages-in-the-world-a-courier-and-a-pulse`, cut from part C's.

### Task D1: Deliveries and routes

**Files:** Create `Assets/TheGrid/Core/Deliveries.cs`, `Assets/TheGrid/Tests/DeliveriesTests.cs`, `fixtures/world/messages.json`. Modify `Assets/TheGrid/Core/Pile.cs` (`PileRow` gains `public long receivedAt;`, 0 until the received record is found), `Assets/TheGrid/Core/Iso.cs`, `docs/roster-snapshot.md`.

**Interfaces, produced:**

```csharp
[Serializable] public sealed class Delivery { public string key, fromBot, toBot; public Vector2Int fromCell, toCell; public long receivedAt; }
public sealed class Deliveries {
    public Deliveries(long openedAtMs);
    public List<Delivery> New(PileSnapshot pile, WorldModel world);   // each pile row at most once, ever; decision 12
}
// Iso
public static List<Vector2> Route(Vector2Int fromCell, Vector2Int toCell);   // corner points along lane centre lines, in tiles (column, row); the view turns them into pixels with Iso.Point
```

A route leaves along the lane behind the sender's relay, runs along the lanes between the cells (columns first, then rows), and ends behind the recipient's relay. Every point lies on a lane tile.

- [ ] **Step 1: Write the failing tests.**

```csharp
// a row received after openedAt whose from and to each match one bot: one Delivery, with those bots' keys and their sectors' cells
// the same snapshot given again: none
// received before openedAt: none, and none later either
// receivedAt == 0: none now; one when a later snapshot carries its received time
// from matches no bot; to matches two bots of one account; from and to on different accounts: none, nothing thrown   (Review Focus 3)
// Route(a, a).Count == 0
// Route between cells (0,0) and (2,1): the first point is on the lane behind the first cell's relay, the last behind the other's;
//   each consecutive pair differs in column or in row, never both; every tile along every leg satisfies Iso.OnPath for some cell
```

- [ ] **Step 2:** Run the core tests. Expected: `error CS` naming `Deliveries`.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run the core tests. Expected: all pass.
- [ ] **Step 5:** Commit `GRID-20: deliveries from the pile, and routes along the lanes`.

### Task D2: The courier and the pulse

**Files:** Create `Assets/TheGrid/WorldTraffic.cs`. Modify `Assets/TheGrid/WorldView.cs`, `Assets/TheGrid/RosterApp.cs`, `README.md`.

**Interfaces, produced:** `public sealed class WorldTraffic { public WorldTraffic(Transform root, WorldStage stage); public void Add(Delivery d, float now); public bool Tick(float now); }`, `Tick` true while anything is moving.

Behaviour:
- Between sectors: a `bike-<facing>` appears beside the sender's relay, rides `Iso.Route` at 8 tiles a second and for at least 1.5 seconds, facing the way it travels, and goes when it arrives. Then `pulse-0`, `-1`, `-2` under the recipient, 0.3 seconds each.
- Within one sector: the pulse only.
- While a courier rides or a pulse shows, the recipient and, for the first 0.9 seconds, the sender take the `signal` pose.
- `RosterApp` owns one `Deliveries`, made when the world is first opened, and feeds `New` to the traffic on each Layout pass. In picture mode `-messages <file>` loads a `PileSnapshot` whose rows count as received now.
- Nothing moving: `Tick` is false and the map is not redrawn.

- [ ] **Step 1:** Implement.
- [ ] **Step 2:** Run the core tests. Expected: all pass.
- [ ] **Step 3:** Shoot the app with `-snapshot fixtures/world/snapshot.json -messages fixtures/world/messages.json -shots <folder>`; picture mode saves `courier.png` one second in. Expected: a courier on a lane between two sectors, on the route; in a later picture a violet ring under the recipient; no exception in the log.
- [ ] **Step 4:** Measure processor use for 60 seconds on the world with no message moving. Expected: at or below the lists.
- [ ] **Step 5:** `README.md`: couriers and the pulse, and that The Grid only shows deliveries. Commit `GRID-20: a courier and a pulse for each delivery`. Push, open the pull request with the pictures, post `@codex review`. Comment the outcome on GRID-16.
