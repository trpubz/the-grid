# Slice 1: Local Roster Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** One narrow Mac window that lists the owner's live Claude Code sessions on this machine, sorted so the agent that needs a person most is on top.

**Architecture:** Everything runs inside the Unity app; there is no separate collector process in this slice. A plain C# library (`TheGrid.Core`) asks Claude Code for its live session list every 2 seconds, enriches each session from files Claude Code already writes, and produces an immutable `RosterSnapshot`. A single `MonoBehaviour` draws that snapshot with IMGUI, the same UI approach the starter's `GridReplay.cs` uses.

**Tech Stack:** Unity 6000.4.3f1, C#, `UnityEngine.JsonUtility` (no JSON dependency), Unity Test Framework (EditMode), IMGUI.

**Spec:** Ticket GRID-4 and slice 1 of the approved product requirements (kept privately; its slice 1 rules are restated under Global Constraints). Record shapes and signals: `docs/spike-idle-detection.md`. Sample records: `fixtures/claude-code/`.

## Global Constraints

- Unity editor 6000.4.3f1. Mac only.
- Nothing leaves the machine. No network calls of any kind in this slice.
- Agent app storage is read-only. Never write under a Claude config directory.
- State comes from `claude agents --json`, run once per config directory. Do not infer state from the end of a transcript.
- A blocked agent shows as blocked within 10 seconds. Poll interval is 2000 ms.
- Usable at one quarter of the screen width: the layout must work at 360 points wide.
- No sound and no notifications.
- A repo with no workspace assigned is treated as `Personal`.
- A session not tied to any repo is listed under `No project`.
- An unrecognised status is shown as unknown with its raw text; it is never dropped.
- The starter's recorded demo keeps working: `Assets/TheGrid/Demo.unity` plays and `python3 tools/check_replay.py` prints PASS.
- The repo is public. No real names of people, companies, accounts, repos or sessions in code, tests, fixtures or docs.
- No new package dependency except `com.unity.test-framework`.

## Decisions made in this plan

These depart from the starter's defaults and are the reviewer's to veto:

1. **Collector lives in the Unity app, in C#.** The starter proposed a separate TypeScript collector. Nothing in this slice needs a second process; one arrives with team sharing.
2. **IMGUI for the roster.** Already used by the starter, needs no assets, and the view is one file that reads only `RosterSnapshot`, so replacing it later touches nothing else.
3. **Every Claude config directory found is read**, not just one. Directories are `~/.claude` and any `~/.claude-*` that contains a `projects` folder. It is the same loop either way.
4. **The pull request comes from the session's own records**, so there is no GitHub lookup at all.
5. **The written description of the data** is `fixtures/roster/snapshot.json`, pinned by a test, plus a short `docs/roster-snapshot.md`.

Not in this plan: the isometric world, the review pile, search, Codex, a copy-to-terminal command, sending anything to an agent.

## Review Focus

1. `claude` is missing, exits non-zero, or hangs: the roster keeps its last rows and shows one error line. (Task 7)
2. A transcript's last line is half-written, or the tail read starts mid-line: bad lines are skipped, no exception. (Task 3)
3. A session's folder is gone, is not a git repo, or is a git worktree: it becomes `No project`, or groups under the main repo. (Task 4)
4. An unknown `status` such as `shell`, or a `waitingFor` value not seen before: mapped to a visible state with raw text kept. (Task 2)
5. `settings.json` is missing or corrupt: defaults are used and a corrupt file is copied to `settings.json.bad` before anything is saved over it. (Task 5)

## File Structure

| Path | Responsibility |
|---|---|
| `Assets/TheGrid/Core/TheGrid.Core.asmdef` | Library assembly, no editor or scene code |
| `Assets/TheGrid/Core/Ticket.cs` | Branch name to ticket id |
| `Assets/TheGrid/Core/LiveSessions.cs` | `LiveSession`, parse `claude agents --json`, `AgentState`, state mapping |
| `Assets/TheGrid/Core/SessionFiles.cs` | Per-process session file and transcript tail readers |
| `Assets/TheGrid/Core/RepoResolver.cs` | Folder to repo root and name |
| `Assets/TheGrid/Core/Subagents.cs` | Active subagents of a session |
| `Assets/TheGrid/Core/Settings.cs` | Settings file, config directory discovery, workspace assignment |
| `Assets/TheGrid/Core/Roster.cs` | `AgentRow`, `RosterSnapshot`, `RosterBuilder` |
| `Assets/TheGrid/Core/Collector.cs` | `ClaudeCli`, polling loop |
| `Assets/TheGrid/Tests/TheGrid.Core.Tests.asmdef` and `*Tests.cs` | EditMode tests |
| `Assets/TheGrid/RosterApp.cs` | The window: starts the collector, draws the roster |
| `Assets/TheGrid/Editor/GridMenu.cs` | Menu items: run core tests, create roster scene, build Mac app |
| `Assets/TheGrid/Roster.unity` | Scene holding one `RosterApp` |
| `fixtures/roster/snapshot.json`, `docs/roster-snapshot.md` | The data description |

All Core types are in namespace `TheGrid.Core`. Tests locate fixtures at `Path.Combine(Directory.GetCurrentDirectory(), "fixtures")`; tests that need files on disk build them under a fresh `Path.GetTempPath()` subfolder and delete it afterwards.

**Running tests.** With the editor open: execute menu item `The Grid/Run Core Tests`, then read `Logs/core-tests.txt` (first line `PASS n` or `FAIL n of m`, then one line per failure). With the editor closed:

```bash
/Applications/Unity/Hub/Editor/6000.4.3f1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath "$PWD" -runTests -testPlatform EditMode -assemblyNames TheGrid.Core.Tests -testResults Logs/editmode.xml
```

Both are written "run the core tests" below.

---

### Task 0: Baseline commits

The working tree holds earlier, uncommitted work. Commit it before slice 1 so slice 1's commits contain only slice 1.

- [ ] **Step 1:** Commit `docs/spike-idle-detection.md` and `fixtures/` as `GRID-3: idle-detection spike report and fixtures`.
- [ ] **Step 2:** Commit everything else except `docs/superpowers/` as `GRID-1: bootstrap Unity project from starter` (body notes the superseded-doc notes from GRID-2).
- [ ] **Step 3:** Commit this plan as `GRID-4: slice 1 implementation plan`.
- [ ] **Step 4:** `git status --short` prints nothing.

Owner decision before Step 2: `Packages/manifest.json` now lists `com.unity.ai.assistant` and `com.unity.ai.inference`, added for editor automation, and an `Assets/XR` folder appeared with them. Committing them makes them dependencies of the public repo.

### Task 1: Test harness and ticket parsing

**Files:** Create `Core/TheGrid.Core.asmdef`, `Core/Ticket.cs`, `Tests/TheGrid.Core.Tests.asmdef`, `Tests/TicketTests.cs`, `Editor/GridMenu.cs`. Modify `Packages/manifest.json` (add `com.unity.test-framework` through the Package Manager so the editor picks the version).

**Interfaces:**
- Produces: `static string Ticket.FromBranch(string branch)` — the first `letters-digits` token, upper-cased, or `""`.
- Produces: menu item `The Grid/Run Core Tests` (`GridMenu.RunCoreTests`) that runs EditMode tests in `TheGrid.Core.Tests` and writes `Logs/core-tests.txt`.

- [ ] **Step 1: Write the failing test**

```csharp
[TestCase("someone/abc-848-custom-range", "ABC-848")]
[TestCase("feature/grid-4-slice-1-local-roster", "GRID-4")]
[TestCase("main", "")]
[TestCase("", "")]
[TestCase(null, "")]
public void FromBranch(string branch, string expected) => Assert.AreEqual(expected, Ticket.FromBranch(branch));
```

- [ ] **Step 2:** Run the core tests. Expected: FAIL, `Ticket` not defined (a compile error in the console counts).
- [ ] **Step 3:** Implement `Ticket.FromBranch` with the pattern `(?<![A-Za-z0-9])([A-Za-z][A-Za-z0-9]*-\d+)`. Add the comment `// shortcut: any letters-digits token counts as a ticket (e.g. "utf-8"); add an allowed-prefix setting if false matches show up.`
- [ ] **Step 4:** Run the core tests. Expected: `PASS 5`.
- [ ] **Step 5:** Commit `GRID-4: test harness and ticket parsing`.

### Task 2: Live session list and state mapping

**Files:** Create `Core/LiveSessions.cs`, `Tests/LiveSessionsTests.cs`.

**Interfaces:**
- Produces:

```csharp
public enum AgentState { BlockedOnQuestion, WaitingForPermission, WaitingOther, BackgroundBlocked, Errored, Finished, Unknown, Running, Offline } // declared in urgency order
public sealed class LiveSession { public string SessionId, Name, Cwd, Kind, Status, WaitingFor, State, ConfigDir; public int Pid; public long StartedAt; }
public static class LiveSessions {
    public static bool TryParse(string json, string configDir, out List<LiveSession> sessions, out string error);
    public static AgentState Map(LiveSession s);
    public static string RawStatus(LiveSession s); // "waiting / input needed", "blocked", "shell", ...
}
```

Mapping: `busy` → Running; `idle` → Finished; `waiting` + `input needed` → BlockedOnQuestion; `waiting` + `permission prompt` or `sandbox request` → WaitingForPermission; `waiting` + anything else → WaitingOther. Background rows use `State`: `blocked` → BackgroundBlocked, `working` → Running, `done` or `stopped` → Finished, `failed` → Errored. Everything else → Unknown.

- [ ] **Step 1: Write the failing tests**

```csharp
// fixtures/claude-code/agents.json has 5 rows: busy, waiting/input needed, waiting/permission prompt, idle, background blocked
Assert.IsTrue(LiveSessions.TryParse(File.ReadAllText(fixture), "/cfg", out var s, out var err));
Assert.AreEqual(5, s.Count);
CollectionAssert.AreEqual(new[] { AgentState.Running, AgentState.BlockedOnQuestion, AgentState.WaitingForPermission, AgentState.Finished, AgentState.BackgroundBlocked }, s.Select(LiveSessions.Map).ToArray());
Assert.AreEqual("/cfg", s[0].ConfigDir);
Assert.AreEqual(0, s[4].Pid); // background rows have no pid

[TestCase("shell", null, AgentState.Unknown)]
[TestCase("waiting", "something new", AgentState.WaitingOther)]
[TestCase(null, null, AgentState.Unknown)]
// MapUnknown(status, waitingFor, expected): build a LiveSession with Kind "interactive"

Assert.IsFalse(LiveSessions.TryParse("not json", "/cfg", out _, out var e)); Assert.IsNotEmpty(e);
Assert.IsTrue(LiveSessions.TryParse("[]", "/cfg", out var none, out _)); Assert.AreEqual(0, none.Count);
Assert.AreEqual("shell", LiveSessions.RawStatus(new LiveSession { Status = "shell" }));
```

- [ ] **Step 2:** Run the core tests. Expected: FAIL.
- [ ] **Step 3:** Implement. `JsonUtility` cannot read a top-level array, so wrap the text as `{"items":<json>}`. It also matches fields by exact name, so parse into a private class whose fields are spelled like the JSON keys (`sessionId`, `waitingFor`, ...) and copy into `LiveSession`.
- [ ] **Step 4:** Run the core tests. Expected: all pass.
- [ ] **Step 5:** Commit `GRID-4: parse live session list and map states`.

### Task 3: Session file and transcript tail

**Files:** Create `Core/SessionFiles.cs`, `Tests/SessionFilesTests.cs`.

**Interfaces:**
- Produces:

```csharp
public sealed class SessionFileInfo { public long StatusUpdatedAt; public string Entrypoint; }
public sealed class TranscriptMeta { public string Branch = ""; public int PrNumber; public string PrUrl = ""; }
public static class SessionFiles {
    public static bool TryReadSessionFile(string configDir, int pid, out SessionFileInfo info); // <configDir>/sessions/<pid>.json
    public static string FindTranscript(string configDir, string sessionId);                    // <configDir>/projects/*/<sessionId>.jsonl, or null
    public static TranscriptMeta ReadTranscriptTail(string path, int maxBytes = 262144);
}
```

`ReadTranscriptTail` reads at most the last `maxBytes`, drops the first line when it did not start at byte 0, parses only lines containing `"gitBranch"` or `"pr-link"`, and keeps the last branch and the last pull request seen.

- [ ] **Step 1: Write the failing tests** (temp folder laid out as a config directory)

```csharp
// sessions/1002.json = {"pid":1002,"status":"waiting","statusUpdatedAt":1794000123000,"entrypoint":"cli"}
Assert.IsTrue(SessionFiles.TryReadSessionFile(cfg, 1002, out var f)); Assert.AreEqual(1794000123000, f.StatusUpdatedAt); Assert.AreEqual("cli", f.Entrypoint);
Assert.IsFalse(SessionFiles.TryReadSessionFile(cfg, 9999, out _));

// projects/p1/<S2>.jsonl = fixtures/claude-code/transcript-records.jsonl
Assert.AreEqual(Path.Combine(cfg, "projects", "p1", S2 + ".jsonl"), SessionFiles.FindTranscript(cfg, S2));
Assert.IsNull(SessionFiles.FindTranscript(cfg, "missing"));
var m = SessionFiles.ReadTranscriptTail(path);
Assert.AreEqual("feature/demo-12-example-change", m.Branch); Assert.AreEqual(7, m.PrNumber); Assert.AreEqual("https://example.invalid/demo/alpha/pull/7", m.PrUrl);

// Review Focus 2: append the text {"type":"user","gitBranch":"half   (no newline, cut mid-record)
Assert.AreEqual("feature/demo-12-example-change", SessionFiles.ReadTranscriptTail(path).Branch);
// tail starts mid-line and the pull request record is outside the window: no exception, PrNumber 0
Assert.AreEqual(0, SessionFiles.ReadTranscriptTail(path, 300).PrNumber);
Assert.AreEqual("", SessionFiles.ReadTranscriptTail("/no/such/file").Branch);
```

- [ ] **Step 2:** Run the core tests. Expected: FAIL.
- [ ] **Step 3:** Implement. Open files with `FileShare.ReadWrite`, since Claude Code is writing them. Add the comment `// shortcut: only the last 256 KB is read, so a pull request recorded earlier is missed until it is written again; scan the whole file once if the PR column is often empty.`
- [ ] **Step 4:** Run the core tests. Expected: all pass.
- [ ] **Step 5:** Commit `GRID-4: read session file and transcript tail`.

### Task 4: Repo resolver

**Files:** Create `Core/RepoResolver.cs`, `Tests/RepoResolverTests.cs`.

**Interfaces:**
- Produces: `public sealed class RepoInfo { public string Root = ""; public string Name = RepoResolver.NoProject; }` and `public static class RepoResolver { public const string NoProject = "No project"; public static RepoInfo Resolve(string cwd); }`

Walk up from `cwd` to the first folder containing `.git`. If `.git` is a folder, that folder is the root. If `.git` is a file (`gitdir: <path>/.git/worktrees/<name>`), the root is the folder that owns that `.git`. `Name` is the root's folder name.

- [ ] **Step 1: Write the failing tests** (temp folders; no `git` binary is called)

```csharp
// tmp/alpha/.git/ (folder), tmp/alpha/src/deep/
Assert.AreEqual("alpha", RepoResolver.Resolve(Path.Combine(tmp, "alpha", "src", "deep")).Name);
Assert.AreEqual(Path.Combine(tmp, "alpha"), RepoResolver.Resolve(Path.Combine(tmp, "alpha")).Root);
// tmp/alpha-wt/.git (file) = "gitdir: <tmp>/alpha/.git/worktrees/alpha-wt"
Assert.AreEqual(Path.Combine(tmp, "alpha"), RepoResolver.Resolve(Path.Combine(tmp, "alpha-wt")).Root);
// Review Focus 3
Assert.AreEqual(RepoResolver.NoProject, RepoResolver.Resolve(Path.Combine(tmp, "plain")).Name);
Assert.AreEqual("", RepoResolver.Resolve("/no/such/folder").Root);
Assert.AreEqual(RepoResolver.NoProject, RepoResolver.Resolve(null).Name);
```

- [ ] **Step 2:** Run the core tests. Expected: FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run the core tests. Expected: all pass.
- [ ] **Step 5:** Commit `GRID-4: resolve a folder to its repo`.

### Task 5: Settings and workspaces

**Files:** Create `Core/Settings.cs`, `Tests/SettingsTests.cs`.

**Interfaces:**
- Produces:

```csharp
[Serializable] public sealed class RepoWorkspace { public string repo; public string workspace; }
[Serializable] public sealed class Settings {
    public const string DefaultWorkspace = "Personal";
    public List<string> configDirs = new List<string>();      // empty means: discover
    public string claudePath = "";                             // empty means: look in the usual places
    public List<string> workspaces = new List<string> { DefaultWorkspace };
    public List<RepoWorkspace> repoWorkspaces = new List<RepoWorkspace>();
    public static Settings Load(string path);
    public void Save(string path);
    public string WorkspaceFor(string repoRoot);
    public string CycleWorkspace(string repoRoot);             // assigns the next entry of `workspaces`, returns it
    public static List<string> DiscoverConfigDirs(string home);
    public List<string> EffectiveConfigDirs(string home);      // configDirs if any, else discovered
}
```

- [ ] **Step 1: Write the failing tests**

```csharp
var s = Settings.Load(Path.Combine(tmp, "missing.json"));
Assert.AreEqual("Personal", s.WorkspaceFor("/work/alpha")); Assert.AreEqual("Personal", s.WorkspaceFor(""));
s.workspaces = new List<string> { "Personal", "Work" };
Assert.AreEqual("Work", s.CycleWorkspace("/work/alpha")); Assert.AreEqual("Personal", s.CycleWorkspace("/work/alpha"));
s.CycleWorkspace("/work/alpha"); s.Save(file);
Assert.AreEqual("Work", Settings.Load(file).WorkspaceFor("/work/alpha"));

// Review Focus 5
File.WriteAllText(file, "{ broken");
var d = Settings.Load(file); Assert.AreEqual(1, d.workspaces.Count);
Assert.AreEqual("{ broken", File.ReadAllText(file + ".bad"));

// home/.claude/projects/, home/.claude-two/projects/, home/.claude-router/ (no projects folder), home/.clauded/projects/
CollectionAssert.AreEqual(new[] { Path.Combine(home, ".claude"), Path.Combine(home, ".claude-two") }, Settings.DiscoverConfigDirs(home));
s.configDirs = new List<string> { "/only/this" }; CollectionAssert.AreEqual(new[] { "/only/this" }, s.EffectiveConfigDirs(home));
```

- [ ] **Step 2:** Run the core tests. Expected: FAIL.
- [ ] **Step 3:** Implement. `Save` writes to a temp file beside the target and then replaces it.
- [ ] **Step 4:** Run the core tests. Expected: all pass.
- [ ] **Step 5:** Commit `GRID-4: settings, config directory discovery, workspace assignment`.

### Task 6: Subagents and the roster builder

**Files:** Create `Core/Subagents.cs`, `Core/Roster.cs`, `Tests/RosterTests.cs`, `fixtures/roster/snapshot.json`, `docs/roster-snapshot.md`.

**Interfaces:**
- Consumes: everything produced by Tasks 1 to 5.
- Produces:

```csharp
[Serializable] public sealed class SubagentRow { public string agentId, description, agentType; }
public static class Subagents { public static List<SubagentRow> ReadActive(string transcriptPath, long nowMs, long maxAgeMs = 600000); }

[Serializable] public sealed class AgentRow {
    public string sessionId, name, account, app, repoRoot, repoName, branch, ticket, prUrl, state, rawStatus, workspace;
    public int prNumber; public long stateSince; public List<SubagentRow> subagents;
}
[Serializable] public sealed class RosterSnapshot { public long generatedAt; public List<AgentRow> agents; public List<string> errors; }
public sealed class RosterBuilder {
    public const long OfflineKeepMs = 43200000; // 12 hours
    public RosterBuilder(Settings settings);
    public RosterSnapshot Build(IReadOnlyList<LiveSession> live, IReadOnlyList<string> errors, long nowMs);
}
```

Subagents live beside the transcript: `<transcript without .jsonl>/subagents/agent-<id>.meta.json` (fields `description`, `agentType`, `finished`) and `agent-<id>.jsonl`. A subagent is active when `finished` is not true and its `.jsonl` changed within `maxAgeMs`.

`Build` rules: `state` is the `AgentState` name. `account` is the config directory's folder name with a leading `.claude-` or `.` removed (`.claude` → `claude`). `app` is the session file's entrypoint, or `""`. `stateSince` is the session file's `statusUpdatedAt`; without one, the time this builder first saw the session in its current state. A pull request once seen is kept for the session even if a later tail read misses it. A session that was live and is now absent becomes `Offline`, keeps its other fields, and is dropped after `OfflineKeepMs`. Order: `AgentState` declaration order, then `stateSince` ascending, then `name`.

- [ ] **Step 1: Write the failing tests** (temp config directory: `agents.json` fixture rows, a session file for pid 1002, the transcript fixture for `S2` with a `subagents` folder holding one unfinished, one finished and one stale meta file)

```csharp
var snap = builder.Build(live, new string[0], now);
CollectionAssert.AreEqual(new[] { "BlockedOnQuestion", "WaitingForPermission", "BackgroundBlocked", "Finished", "Running" }, snap.agents.Select(a => a.state).ToArray());
var a = snap.agents[0];
Assert.AreEqual("DEMO-12", a.ticket); Assert.AreEqual(7, a.prNumber); Assert.AreEqual("Personal", a.workspace);
Assert.AreEqual(1794000123000, a.stateSince); Assert.AreEqual("cli", a.app);
Assert.AreEqual(1, a.subagents.Count); Assert.AreEqual("Synthetic task", a.subagents[0].description);
Assert.AreEqual("No project", snap.agents[4].repoName);

// without a session file, stateSince is the first time seen in that state, and it does not move on the next build
var first = builder.Build(live, none, now).agents.First(x => x.name == "beta-01").stateSince;
Assert.AreEqual(first, builder.Build(live, none, now + 4000).agents.First(x => x.name == "beta-01").stateSince);

// a session that disappears goes Offline, sorts last, and is dropped after 12 hours
var gone = builder.Build(live.Skip(1).ToList(), none, now + 2000);
Assert.AreEqual("Offline", gone.agents.Last().state); Assert.AreEqual("alpha-01", gone.agents.Last().name);
Assert.IsFalse(builder.Build(live.Skip(1).ToList(), none, now + RosterBuilder.OfflineKeepMs + 3000).agents.Any(x => x.name == "alpha-01"));

// the data description: a snapshot serialises to exactly the golden file
Assert.AreEqual(File.ReadAllText(golden).Trim(), JsonUtility.ToJson(snapWithFixedPaths, true).Trim());
```

- [ ] **Step 2:** Run the core tests. Expected: FAIL.
- [ ] **Step 3:** Implement `Subagents` and `RosterBuilder`. Write `fixtures/roster/snapshot.json` from the first passing build, with temp paths replaced by `/work/...`, and review it by eye before committing.
- [ ] **Step 4:** Write `docs/roster-snapshot.md`: one table of `AgentRow` fields with meaning and source, the list of `state` values in order, and a pointer to the golden file.
- [ ] **Step 5:** Run the core tests. Expected: all pass.
- [ ] **Step 6:** Commit `GRID-4: subagents, roster builder and snapshot description`.

### Task 7: Collector

**Files:** Create `Core/Collector.cs`, `Tests/CollectorTests.cs`.

**Interfaces:**
- Consumes: `Settings`, `LiveSessions`, `RosterBuilder`.
- Produces:

```csharp
public static class ClaudeCli {
    public static string ResolvePath(string configured, string home);   // configured if it exists, else first existing of ~/.local/bin/claude, /opt/homebrew/bin/claude, /usr/local/bin/claude, else ""
    public static string AgentsJson(string claudePath, string configDir, string home, int timeoutMs = 5000); // throws on non-zero exit or timeout
}
public sealed class Collector : IDisposable {
    public Collector(Settings settings, string home, Func<string, string> agentsJsonForConfigDir);
    public RosterSnapshot Latest { get; }                // never null
    public RosterSnapshot Poll(long nowMs);              // one pass over every config directory; sets Latest
    public void Start(int intervalMs = 2000);            // background thread; never throws out of the loop
    public void Dispose();
}
```

`AgentsJson` sets `CLAUDE_CONFIG_DIR` for every directory except `<home>/.claude`, for which it leaves the variable unset.

- [ ] **Step 1: Write the failing tests**

```csharp
var c = new Collector(settings, home, dir => File.ReadAllText(agentsFixture));
Assert.AreEqual(0, c.Latest.agents.Count);
Assert.AreEqual(5, c.Poll(now).agents.Count);

// Review Focus 1: the command fails on the second pass
var calls = 0;
var flaky = new Collector(settings, home, dir => ++calls == 1 ? File.ReadAllText(agentsFixture) : throw new InvalidOperationException("claude not found"));
flaky.Poll(now); var second = flaky.Poll(now + 2000);
Assert.AreEqual(5, second.agents.Count);                       // last rows kept, not marked Offline
Assert.IsTrue(second.errors.Single().Contains("claude not found"));
Assert.IsTrue(second.agents.All(a => a.state != "Offline"));

Assert.AreEqual("", ClaudeCli.ResolvePath("", emptyHome));
Assert.AreEqual(fake, ClaudeCli.ResolvePath(fake, emptyHome)); // fake is an existing temp file
```

- [ ] **Step 2:** Run the core tests. Expected: FAIL.
- [ ] **Step 3:** Implement. When a directory's command fails, reuse that directory's sessions from the previous pass so a failure never looks like every agent going offline.
- [ ] **Step 4:** Run the core tests. Expected: all pass.
- [ ] **Step 5:** Commit `GRID-4: collector polling loop`.

### Task 8: Roster window

**Files:** Create `Assets/TheGrid/RosterApp.cs`, `Assets/TheGrid/Roster.unity` (through the menu item), `Tests/RosterFormatTests.cs`. Modify `Editor/GridMenu.cs`, `Core/Roster.cs`.

**Interfaces:**
- Consumes: `Collector`, `RosterSnapshot`, `Settings`.
- Produces: `public sealed class RosterApp : MonoBehaviour`; `public static string RosterFormat.Elapsed(long sinceMs, long nowMs)` in `Core/Roster.cs` (it lives in Core because the test assembly cannot reference scripts outside an assembly definition); menu item `The Grid/Create Roster Scene` (`GridMenu.CreateRosterScene`) that creates `Assets/TheGrid/Roster.unity` with a camera and one `RosterApp`, and puts it first in the build scene list with `Demo.unity` second.

Behaviour:
- On start: `Application.targetFrameRate = 10`, `Application.runInBackground = true`; load settings from `Path.Combine(Application.persistentDataPath, "settings.json")`; start the collector with `ClaudeCli.AgentsJson`.
- Each agent is two lines. Line 1: state label in its colour, session name, time in state. Line 2: repo, ticket, `PR #n`, workspace, account. Empty parts are omitted. Active subagents follow, indented, as `↳ description`.
- State labels and colours: `QUESTION` red, `PERMISSION` orange, `WAITING` yellow, `BG BLOCKED` orange, `ERROR` magenta, `DONE` cyan, `UNKNOWN (raw status)` grey, `RUNNING` green, `OFFLINE` dark grey.
- Errors from the snapshot are drawn in one line at the top.
- Clicking `PR #n` opens `prUrl`. Clicking the workspace calls `CycleWorkspace` for that repo and saves settings.
- The list scrolls. Scale all sizes by `Screen.dpi / 96` (use 1 when dpi is 0).
- Log one line with `Debug.Log` each time an agent's state changes: `[roster] <name> <old> -> <new>`.

- [ ] **Step 1: Write the failing test**

```csharp
[TestCase(0L, 59000L, "0m")] [TestCase(0L, 240000L, "4m")] [TestCase(0L, 4320000L, "1h 12m")] [TestCase(5000L, 1000L, "0m")]
public void Elapsed(long since, long now, string expected) => Assert.AreEqual(expected, RosterFormat.Elapsed(since, now));
```

- [ ] **Step 2:** Run the core tests. Expected: FAIL.
- [ ] **Step 3:** Implement `RosterApp` and the menu item; run `The Grid/Create Roster Scene`.
- [ ] **Step 4:** Run the core tests. Expected: all pass.
- [ ] **Step 5: Check it live.** Open `Roster.unity`, set the Game view to 360×800, enter Play mode. Expected: the console has no errors; every session from `claude agents --json` appears; sessions from each config directory carry their account label.
- [ ] **Step 6: Check the 10-second rule.** Start a disposable Claude Code session in a scratch folder and have it ask a question. Expected: a console line `[roster] <name> Running -> BlockedOnQuestion` within 10 seconds of the question appearing, and that row is first in the list.
- [ ] **Step 7: Check the demo.** Open `Demo.unity`, enter Play mode, step ten events, press Replay. Run `python3 tools/check_replay.py`. Expected: `PASS: 2 actors, 10 events, two correlated deliveries.`
- [ ] **Step 8:** Commit `GRID-4: roster window`.

### Task 9: Mac build and wrap-up

**Files:** Modify `Editor/GridMenu.cs`, `README.md`.

**Interfaces:**
- Produces: menu item `The Grid/Build Mac App` (`GridMenu.BuildMac`) that sets the player to a resizable window of 480×900 that keeps running in the background, product name `The Grid`, and builds `Builds/TheGrid.app`.

- [ ] **Step 1:** Implement `BuildMac`; run it. Expected: `Builds/TheGrid.app` exists and the console reports a successful build.
- [ ] **Step 2:** Launch the app. Expected: the roster shows the same sessions as `claude agents --json`; the window can be narrowed to 360 points with nothing cut off; `~/Library/Logs/<company>/The Grid/Player.log` has no exceptions.
- [ ] **Step 3:** With no agent changing state for 60 seconds, read the app's processor use in Activity Monitor and record the number in the ticket.
- [ ] **Step 4:** Update `README.md`: how to open the roster scene, run the tests and build the app; where `settings.json` lives and its four fields.
- [ ] **Step 5:** Commit `GRID-4: Mac build and README`.
- [ ] **Step 6:** Comment on GRID-4 with what was verified, the processor number, and anything that departed from this plan. The day-long run is the owner's to do.
