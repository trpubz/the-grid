# Slice 2: Review Pile Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Beside the roster, list every message exchanged between Claude Code sessions on this Mac, once each, so the owner can tick each one off and clear the ticked ones.

**Architecture:** A second reader inside the same Unity app. `SessionFiles.ReadNewLines` reads only the bytes appended to a transcript since the last pass. `Messages.Parse` turns a transcript line into message events. `Pile` applies the events to a list of messages, pairs the sent and received sides, keeps the list and the read positions in `pile.json` in The Grid's own data folder, and publishes an immutable `PileSnapshot` from its own background thread. `RosterApp` gains a second tab that draws that snapshot.

**Tech Stack:** Unity 6000.4.3f1, C#, `UnityEngine.JsonUtility`, Unity Test Framework (EditMode), IMGUI. No new dependency.

**Spec:** Ticket GRID-8 and slice 2 of the approved product requirements (kept privately; its rules are restated under Global Constraints). Record shapes: `docs/spike-idle-detection.md`, section "Messages between sessions", extended by Task 2. Patterns to follow: `Assets/TheGrid/Core/SessionFiles.cs`, `Collector.cs`, `Settings.cs`.

## Global Constraints

- Version 1 only observes messages. Nothing here delivers, sends, edits or deletes a message.
- Every message between sessions is listed. Each is ticked individually. "Clear" archives the ticked rows of the review list and nothing else.
- Agent app storage is read-only. Never write under a Claude config directory. Review state lives in `Path.Combine(Application.persistentDataPath, "pile.json")`, beside `settings.json`.
- A session's exchanges with its own subagents are not listed.
- Nothing leaves the machine. No network calls.
- Transcripts reach 1 GB on this Mac (3.4 GB in total). After the first pass a file is read only from where the last pass stopped. A whole file is never held in memory.
- Usable at 360 points wide. No sound and no notifications. 10 frames a second stays.
- A repo with no workspace is `Personal`. Workspace comes from `Settings.WorkspaceFor(repoRoot)`.
- The repo is public. No real names of people, companies, accounts, repos or sessions in code, tests, fixtures or docs.
- Slice 1 keeps working: the 80 existing EditMode tests pass and the roster tab is unchanged.

## What the records look like

Checked against the real stores on this Mac on 2026-11-10 (595 sends, 356 receives; values not copied):

| Side | Record | Fields used |
|---|---|---|
| Sent | `type: assistant`, a `message.content[]` block with `type: tool_use`, `name: SendMessage` | block `id`; `input.to`, `input.message`, `input.summary`, `input.recipient_kind`; record `cwd`, `timestamp` |
| Send result | `type: user`, a `message.content[]` block with `type: tool_result` and that `tool_use_id` | `toolUseResult.msg_id`, or `toolUseResult.pin` when the recipient is the sender's own subagent |
| Received, between turns | `type: user` with `origin.kind: peer` | `origin.from`, `name`, `msg_id`, `body`; record `uuid`, `cwd`, `timestamp` |
| Received, mid-turn | `type: attachment`, `attachment.type: queued_command`, same object at `attachment.origin` | same |
| Received, desktop app | `type: user` with `origin.kind: peer`, `origin.hostInjected: true`, no `msg_id` or `body` | `message.content` is a string holding `<cross-session-message from="…" name="…">\nBODY\n</cross-session-message>` |
| Subagent report | `origin.handback: true` | skipped |
| Session name | `type: agent-name` (`agentName`), `custom-title` (`customTitle`), `ai-title` (`aiTitle`) | last `agent-name` or `custom-title` wins; `ai-title` only when there is neither |

The sender's `toolUseResult.msg_id` equals the recipient's `origin.msg_id`: all 340 received ids matched a sent one. Sends to a desktop-app session get no id, so those pair on the message text.

## Decisions made in this plan

The reviewer's to veto:

1. **`pile.json` holds an index, not just ticks.** It stores each message's sender, recipient, repos, time, summary and first line (no full body), plus a read position per transcript. Without it every launch would re-read 3.4 GB. Archived rows stay in the file with `archived: true`.
2. **Tabs, not side by side.** The window must work at 360 points, so the roster and the pile are two tabs: `Agents (n)` and `Messages (n unticked)`.
3. **First launch lists the whole history** (about 600 messages here), newest first, read once in the background. To make that backlog clearable there is a `Tick shown` button beside `Clear ticked`; it is a toggle, reading `Untick shown` once every drawn row is ticked (asked for by the owner, 2026-11-10). The list draws the newest 200 rows and says how many more there are.
4. **Only the first line and the sender's summary are shown.** Reading a whole message stays in the terminal for now.
5. **Desktop-app messages pair by exact text** within one account, because their records carry no shared id. Only a send addressed to a desktop session id pairs this way, and each pairs with the first unpaired receipt of the same text that came after it, oldest send first (tightened in review).
6. **The flag needs both sides.** A message is flagged only when both its sent and received records were found and the two repos' workspaces differ. A message nobody has received yet is not flagged.
7. **The pile runs on its own thread**, so the first long read never delays the roster.

8. **Side by side when there is room** (approved by the owner, 2026-11-10). At 720 points or wider the roster is on the left and the pile on the right, each with its own scroll; below 720, the two tabs of decision 2.
9. **The whole message on demand** (approved by the owner, 2026-11-10; replaces decision 4). Clicking a row opens its full text under it, read from the transcript at that moment. `pile.json` gains only the transcript path and the byte offset of the record (`srcPath`, `srcOffset`); no body is stored. If the transcript is gone, the row says so.

Not in this plan: delivering or sending messages, Codex and Grok, the isometric world, search, browsing or restoring archived rows.

## Review Focus

1. A `SendMessage` call whose result line is not written yet, or never is: listed once; when the result arrives the same row gains its id. (Task 3)
2. The same records in two transcripts (a forked or resumed session copies them): one row. (Task 3)
3. A half-written last line, a line longer than the read buffer, and a transcript that shrank or was replaced: no exception, no duplicate, no lost message. (Tasks 1 and 4)
4. `pile.json` missing or corrupt: the corrupt file is copied to `pile.json.bad`, the list is rebuilt from the transcripts, ticks are lost. (Task 3)
5. A message found on one side only (recipient never read it, or the sender's transcript is gone): listed once with what is known, not flagged. (Task 3)

## File Structure

| Path | Responsibility |
|---|---|
| `Assets/TheGrid/Core/SessionFiles.cs` (modify) | Add `Transcripts`, `ReadNewLines` |
| `Assets/TheGrid/Core/Messages.cs` (create) | `MessageEvent`, `Messages.Parse`: one transcript line to events |
| `Assets/TheGrid/Core/Pile.cs` (create) | `PileMessage`, `PileRow`, `PileSnapshot`, `PileFormat`, `Pile`: apply, pair, tick, clear, persist, poll |
| `Assets/TheGrid/Tests/MessagesTests.cs`, `PileTests.cs` (create), `SessionFilesTests.cs` (modify) | EditMode tests |
| `Assets/TheGrid/RosterApp.cs` (modify) | Tabs and the pile view |
| `fixtures/claude-code/messages/sender.jsonl`, `recipient.jsonl`, `desktop.jsonl` (create) | Hand-written transcripts of three sessions |
| `README.md`, `docs/spike-idle-detection.md`, `fixtures/claude-code/README.md` (modify) | What the pile is, where its file lives, the record shapes above |

**Running tests.** The editor is open on the main checkout, so tests run in batch mode in this worktree:

```bash
/Applications/Unity/Hub/Editor/6000.4.3f1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath "$PWD" -runTests -testPlatform EditMode -assemblyNames TheGrid.Core.Tests -testResults Logs/editmode.xml
```

Written "run the core tests" below. Pass means `Logs/editmode.xml` has `result="Passed"` on its root and `failed="0"`.

---

### Task 1: Read only what was appended

**Files:** Modify `Core/SessionFiles.cs`, `Tests/SessionFilesTests.cs`.

**Interfaces:**
- Produces:

```csharp
public static List<string> Transcripts(string configDir);   // <configDir>/projects/*/*.jsonl, top level only; empty when the folder is missing
// Calls onLine for every complete line after `offset` that contains one of `markers`, and returns the offset just past the last complete line.
// A last line with no newline is left for the next call. A file shorter than `offset` is read from 0. An unreadable file returns -1, so the caller tries again (changed in review).
public static long ReadNewLines(string path, long offset, string[] markers, Action<string, long> onLine, int bufferBytes = 1 << 20);   // onLine(line, byte offset of the line)
public static string ReadLineAt(string path, long offset, int maxBytes = 4 << 20);   // the one line starting at `offset`, or null when it cannot be read
```

Markers are matched on the raw bytes before a line is decoded, so the bulk of a transcript is never turned into strings. The buffer grows when one line is longer than it.

- [ ] **Step 1: Write the failing tests** in `SessionFilesTests`

```csharp
var p = Path.Combine(cfg, "projects", "p1", "t.jsonl");
File.WriteAllText(p, "{\"a\":1}\n{\"k\":\"hit\"}\n{\"k\":\"hit\",\"half");
var seen = new List<string>();
long at = SessionFiles.ReadNewLines(p, 0, new[] { "\"hit\"" }, (l, o) => seen.Add(l));
CollectionAssert.AreEqual(new[] { "{\"k\":\"hit\"}" }, seen);            // unmarked line skipped, half line left
Assert.AreEqual(20, at);
File.AppendAllText(p, "\"}\n");
var markers = new[] { "\"hit\"" };
long at2 = SessionFiles.ReadNewLines(p, at, markers, (l, o) => seen.Add(l));
Assert.AreEqual(new FileInfo(p).Length, at2);
Assert.AreEqual(2, seen.Count);                                           // only the appended line, now complete
Assert.AreEqual(at2, SessionFiles.ReadNewLines(p, at2, markers, (l, o) => seen.Add(l))); Assert.AreEqual(2, seen.Count);   // nothing new: nothing read

// Review Focus 3
File.WriteAllText(p, "{\"k\":\"hit\"}\n");                                // file replaced by a shorter one
seen.Clear(); SessionFiles.ReadNewLines(p, at2, markers, (l, o) => seen.Add(l)); Assert.AreEqual(1, seen.Count);
File.WriteAllText(p, "{\"pad\":\"" + new string('x', 5000) + "\",\"k\":\"hit\"}\n");
seen.Clear(); SessionFiles.ReadNewLines(p, 0, markers, (l, o) => seen.Add(l), bufferBytes: 64); Assert.AreEqual(1, seen.Count);   // line longer than the buffer
Assert.AreEqual(7, SessionFiles.ReadNewLines("/no/such/file", 7, markers, (l, o) => seen.Add(l)));
Assert.AreEqual("{\"k\":\"hit\"}", SessionFiles.ReadLineAt(twoLines, 8)); Assert.IsNull(SessionFiles.ReadLineAt("/no/such/file", 0));   // twoLines: the first file above

CollectionAssert.AreEqual(new[] { transcript, p }, SessionFiles.Transcripts(cfg));   // sorted; a .jsonl one folder deeper (a subagent's) is not listed
Assert.AreEqual(0, SessionFiles.Transcripts(Path.Combine(cfg, "nowhere")).Count);
```

- [ ] **Step 2:** Run the core tests. Expected: FAIL (methods missing).
- [ ] **Step 3:** Implement. Open with `FileShare.ReadWrite | FileShare.Delete` as `ReadTranscriptTail` does.
- [ ] **Step 4:** Run the core tests. Expected: all pass.
- [ ] **Step 5:** Commit `GRID-8: read only the appended part of a transcript`.

### Task 2: Message records

**Files:** Create `Core/Messages.cs`, `Tests/MessagesTests.cs`, `fixtures/claude-code/messages/sender.jsonl`, `recipient.jsonl`, `desktop.jsonl`. Modify `fixtures/claude-code/README.md`.

**Interfaces:**
- Produces:

```csharp
public enum MessageEventKind { Sent, SendResult, Received, SessionName }
public sealed class MessageEvent {
    public MessageEventKind Kind;
    public string Ref = "";      // Sent and SendResult: the tool call id. Received: the record uuid
    public string MsgId = "";    // SendResult and Received; empty when the records carry none
    public string Peer = "";     // Sent: `to` as written. Received: `from`
    public string PeerName = ""; // Received: the sender's name
    public string Body = "", Summary = "", Cwd = "", Name = "";   // Name: SessionName only
    public long At;              // record timestamp, ms since 1970 UTC; 0 when unreadable
    public bool OwnSubagent;     // Sent with recipient_kind "agent", or SendResult with a pin
    public bool IsTitle;         // SessionName from ai-title: used only when no other name is known
    public long Offset;          // byte offset of the record's line; set by the reader, not by Parse
}
public static class Messages {
    public static readonly string[] Markers = { "\"SendMessage\"", "\"peer\"", "\"msg_id\"", "\"pin\"", "\"agent-name\"", "\"custom-title\"", "\"ai-title\"" };
    public static void Parse(string line, List<MessageEvent> into);   // adds nothing for a line that is not valid JSON or not a message record
}
```

Fixtures (all synthetic; session ids `…0011`, `…0012`, `…0013`; folders `/work/alpha`, `/work/beta`, `/work/gamma`):

- `sender.jsonl`, session `alpha-02`: `agent-name`; a human prompt whose text mentions SendMessage in quotes; send `toolu_s1` to `beta-01` with result `msg_id: m-1001`; send `toolu_s2` to `beta-01`, result `m-1002`; send `toolu_s3` with `recipient_kind: agent`, result with `pin`; send `toolu_s4` to the name `helper` with `recipient_kind: name`, result with `pin`; received reply `m-2001` from `beta-01`; a subagent report (`handback: true`); send `toolu_s5` to `local_0001`, result with no `msg_id`.
- `recipient.jsonl`, session `beta-01`: `agent-name`; received `m-1001` as a user record; received `m-1002` as a `queued_command` attachment; send `toolu_r1` to `alpha-02`, result `m-2001`.
- `desktop.jsonl`: `custom-title` `Gamma review`; a `hostInjected` record from `local_0002`, wrapper name `Alpha desk`, whose wrapped text is exactly the message of `toolu_s5`.

- [ ] **Step 1: Write the fixtures and the failing tests** (`Events(file)` parses every line of a fixture)

```csharp
var s = Events("sender.jsonl");
Assert.AreEqual(5, s.Count(e => e.Kind == MessageEventKind.Sent));
Assert.AreEqual(4, s.Count(e => e.Kind == MessageEventKind.SendResult));         // a result with neither msg_id nor pin is not an event
Assert.AreEqual(1, s.Count(e => e.Kind == MessageEventKind.Received));     // the handback is not an event
var s1 = s.First(e => e.Kind == MessageEventKind.Sent);
Assert.AreEqual("toolu_s1", s1.Ref); Assert.AreEqual("beta-01", s1.Peer); Assert.AreEqual("/work/alpha", s1.Cwd);
Assert.AreEqual("Synthetic handoff", s1.Summary); Assert.AreEqual(1767268805000L, s1.At);   // 2026-01-01T12:00:05Z
Assert.AreEqual("m-1001", s.First(e => e.Kind == MessageEventKind.SendResult).MsgId);
Assert.IsTrue(s.Single(e => e.Kind == MessageEventKind.Sent && e.Ref == "toolu_s3").OwnSubagent);
Assert.IsTrue(s.Single(e => e.Kind == MessageEventKind.SendResult && e.Ref == "toolu_s4").OwnSubagent);
Assert.AreEqual("alpha-02", s.First(e => e.Kind == MessageEventKind.SessionName).Name);

var r = Events("recipient.jsonl").Where(e => e.Kind == MessageEventKind.Received).ToList();
CollectionAssert.AreEqual(new[] { "m-1001", "m-1002" }, r.Select(e => e.MsgId));           // user record, then attachment
Assert.AreEqual("alpha-02", r[1].PeerName); Assert.AreEqual("/work/beta", r[1].Cwd);

var d = Events("desktop.jsonl").Single(e => e.Kind == MessageEventKind.Received);
Assert.AreEqual("", d.MsgId); Assert.AreEqual("local_0002", d.Peer); Assert.AreEqual("Alpha desk", d.PeerName);
Assert.AreEqual(Events("sender.jsonl").Single(e => e.Ref == "toolu_s5" && e.Kind == MessageEventKind.Sent).Body, d.Body);

var none = new List<MessageEvent>();
Messages.Parse("{ half", none); Messages.Parse("{\"type\":\"user\",\"message\":{\"content\":\"plain\"}}", none); Messages.Parse("", none);
Assert.AreEqual(0, none.Count);
```

- [ ] **Step 2:** Run the core tests. Expected: FAIL.
- [ ] **Step 3:** Implement with `JsonUtility` and private `[Serializable]` classes, as `SessionFiles` does. `message.content` is a list on most records and a string on the desktop one: read the desktop text with a second class whose `content` is a string.
- [ ] **Step 4:** Run the core tests. Expected: all pass.
- [ ] **Step 5:** Commit `GRID-8: read message records from a transcript line`.

### Task 3: The pile

**Files:** Create `Core/Pile.cs`, `Tests/PileTests.cs`.

**Interfaces:**
- Consumes: `MessageEvent`, `Settings.WorkspaceFor`, `RepoResolver.Resolve`, `RosterBuilder.AccountLabel`.
- Produces:

```csharp
[Serializable] public sealed class PileMessage {      // field names are the JSON keys of pile.json
    public string key = "", id = "", sendRef = "", recvRef = "", bodyKey = "", account = "";
    public string from = "", fromRepo = "", to = "", toRepo = "", summary = "", firstLine = "";
    public string srcPath = ""; public long srcOffset;   // where the full text can be read: the sent record, else the received one
    public long sentAt, receivedAt;
    public bool ticked, archived;
}
[Serializable] public sealed class PileRow {
    public string key = "", from = "", fromRepoName = "", to = "", toRepoName = "", account = "", summary = "", firstLine = "", fromWorkspace = "", toWorkspace = "";
    public long at;                                    // sentAt, or receivedAt when the sent record was not found
    public bool ticked, crossWorkspace;
}
[Serializable] public sealed class PileSnapshot {     // never changed after it is published
    public long generatedAt; public List<PileRow> rows = new List<PileRow>();   // unarchived, newest first
    public int archived; public bool reading;          // reading: the first pass over the history has not finished
    public List<string> errors = new List<string>();
}
public static class PileFormat {
    public static string Age(long sinceMs, long nowMs);            // "0m".."59m", "1h".."47h", "2d"…; never negative
    public static string FirstLine(string body, int max = 140);    // first non-blank line, trimmed, cut to max with "…"
}
public sealed class Pile : IDisposable {
    public Pile(Settings settings, string home, string statePath);   // loads statePath if present
    public PileSnapshot Latest { get; }
    public void Apply(MessageEvent e, string transcriptPath, string configDir);   // used by Poll; public for tests
    public void Refresh();                                           // republishes, e.g. after a workspace changed
    public void SetTicked(IEnumerable<string> keys, bool ticked);    // one save, republishes
    public void SetTicked(string key, bool ticked);
    public string ReadBody(string key);                              // the full text from the transcript, or null when it cannot be read
    public int Clear();                                              // archives ticked rows, saves, republishes; returns how many
    public PileSnapshot Poll(long nowMs);                            // Task 4
    public void Start(int intervalMs = 2000);                        // Task 4
    public void Dispose();
}
```

Rules for `Apply` (`file` is the per-transcript entry `{ path, offset, name }` kept in `pile.json`):

| Event | Effect |
|---|---|
| `SessionName` | `file.name = Name`, unless `IsTitle` and a name is already known |
| `Sent`, `OwnSubagent` | nothing |
| `Sent` | if a message has `sendRef == Ref`: nothing. Else a new message: `sendRef`, `from = file.name`, `fromRepo`, `to = Peer`, `summary`, `firstLine`, `sentAt`, `bodyKey = account + ":" + hash of Body` |
| `SendResult`, `OwnSubagent` | remove the message with that `sendRef` |
| `SendResult` with `MsgId` | set `id` on the message with that `sendRef`; if another message already has that `id`, merge the two |
| `Received` with `MsgId` | find or create by `id`. If it has no `recvRef`: set `recvRef`, `toRepo`, `receivedAt`; `from = PeerName` when the receipt gives one; `to = file.name` only when the sender wrote an address (`uds:…`, `local_…`) or the sent record is unknown; fill `firstLine` when empty. Names used in the message win because a transcript's own label is often just a generated title (found on real data, 2026-11-10) |
| `Received` without `MsgId` | if a message has `recvRef == Ref`: nothing. Else a new message with `recvRef`, `from = PeerName`, the recipient fields and `bodyKey` |

After a pass, every message with `sendRef`, no `id` and no `recvRef` is merged with the oldest message of the same `bodyKey` that has `recvRef`, no `id` and no `sendRef`. A merge keeps the first message's `key`, fills its empty fields from the second, and ors `ticked` and `archived`. `key` is assigned once from a counter stored in `pile.json` and never changes.

`crossWorkspace` is true when the message has both `sendRef` and `recvRef` and `WorkspaceFor(fromRepo) != WorkspaceFor(toRepo)`. Saving follows `Settings.Save` (write `.tmp`, then replace); loading a corrupt file copies it to `pile.json.bad` first.

- [ ] **Step 1: Write the failing tests.** `SetUp` builds a temp home with `.claude/projects/p1/` holding the three fixtures, with `/work/alpha`, `/work/beta`, `/work/gamma` replaced by temp folders that each contain a `.git` folder. `Feed(pile)` parses every fixture line and calls `Apply` then the end-of-pass pairing (through `Poll` once Task 4 lands; until then through an `internal` pairing method exposed to the test assembly).

```csharp
[Test] public void EveryMessageAppearsOnce() {
    Feed(pile);
    var rows = pile.Latest.rows;
    Assert.AreEqual(4, rows.Count);                                    // m-1001, m-1002, m-2001, the desktop one; none for the two subagent sends
    var m1 = rows.Single(r => r.firstLine == "Synthetic message to a peer session.");
    Assert.AreEqual("alpha-02", m1.from); Assert.AreEqual("beta-01", m1.to);
    Assert.AreEqual("alpha", m1.fromRepoName); Assert.AreEqual("beta", m1.toRepoName);
    var desk = rows.Single(r => r.to == "Gamma review");               // paired by text
    Assert.AreEqual("alpha-02", desk.from);
    Assert.IsTrue(rows.Zip(rows.Skip(1), (a, b) => a.at >= b.at).All(x => x));   // newest first
}
[Test] public void FeedingTheSameRecordsAgainAddsNothing() { Feed(pile); Feed(pile); Assert.AreEqual(4, pile.Latest.rows.Count); }   // Review Focus 2
[Test] public void ASendWithNoResultYetIsOneRowBeforeAndAfter() {       // Review Focus 1
    // Apply the Sent event of toolu_s1 alone: 1 row. Then its SendResult, then the recipient's Received m-1001: still 1 row, same key.
}
[Test] public void OneSidedMessagesAreListedAndNotFlagged() {           // Review Focus 5
    // only recipient.jsonl fed: 3 rows; the two received ones have from == "alpha-02", crossWorkspace false
}
[Test] public void DifferentWorkspacesAreFlagged() {
    settings.workspaces = new List<string> { "Personal", "Work" };
    settings.repoWorkspaces.Add(new RepoWorkspace { repo = betaRepo, workspace = "Work" });
    Feed(pile);
    Assert.IsTrue(pile.Latest.rows.Where(r => r.fromRepoName == "alpha" && r.toRepoName == "beta").All(r => r.crossWorkspace && r.toWorkspace == "Work"));
    Assert.IsFalse(pile.Latest.rows.Single(r => r.to == "Gamma review").crossWorkspace);
}
[Test] public void TickAndClearArchiveOnlyTickedRowsAndTouchNothingOfClaude() {
    Feed(pile);
    var before = HashTree(Path.Combine(home, ".claude"));              // every file's path, length and bytes
    var keys = pile.Latest.rows.Take(2).Select(r => r.key).ToList();
    foreach (var k in keys) pile.SetTicked(k, true);
    Assert.AreEqual(2, pile.Latest.rows.Count(r => r.ticked));
    Assert.AreEqual(2, pile.Clear());
    Assert.AreEqual(2, pile.Latest.rows.Count); Assert.AreEqual(2, pile.Latest.archived);
    Assert.AreEqual(before, HashTree(Path.Combine(home, ".claude")));
    using (var again = new Pile(settings, home, statePath)) {          // state survives a restart
        Assert.AreEqual(2, again.Latest.rows.Count); Assert.AreEqual(2, again.Latest.archived);
        Feed(again); Assert.AreEqual(2, again.Latest.rows.Count);      // archived rows do not come back
    }
}
[Test] public void CorruptStateIsKeptAsideAndRebuilt() {               // Review Focus 4
    File.WriteAllText(statePath, "{ half");
    using (var p = new Pile(settings, home, statePath)) { Assert.AreEqual(0, p.Latest.rows.Count); Assert.IsTrue(File.Exists(statePath + ".bad")); }
}
[TestCase(0L, 59000L, "0m")] [TestCase(0L, 3600000L, "1h")] [TestCase(0L, 172800000L, "2d")] [TestCase(5000L, 1000L, "0m")]
public void Age(long since, long now, string expected) => Assert.AreEqual(expected, PileFormat.Age(since, now));
[Test] public void FirstLine() { Assert.AreEqual("two", PileFormat.FirstLine("\n  \ntwo\nthree")); Assert.AreEqual("aaaa…", PileFormat.FirstLine("aaaaaaaa", 4)); Assert.AreEqual("", PileFormat.FirstLine(null)); }
```

- [ ] **Step 2:** Run the core tests. Expected: FAIL.
- [ ] **Step 3:** Implement. All state is guarded by one lock: `Apply` runs on the pile's thread, `SetTicked` and `Clear` on the main thread.
- [ ] **Step 4:** Run the core tests. Expected: all pass.
- [ ] **Step 5:** Commit `GRID-8: the review pile, pairing, tick and clear`.

### Task 4: Polling

**Files:** Modify `Core/Pile.cs`, `Tests/PileTests.cs`.

**Interfaces:**
- Consumes: `SessionFiles.Transcripts`, `SessionFiles.ReadNewLines`, `Messages.Parse`, `Settings.EffectiveConfigDirs`.
- Produces: `Pile.Poll(long nowMs)` and `Pile.Start(int intervalMs = 2000)` as declared in Task 3.

`Poll` lists the transcripts of every config directory, most recently written first, and skips any whose length equals its stored offset. For the others it reads the new lines, applies their events and stores the new offset. Then it pairs, publishes a snapshot, and saves `pile.json` if a message changed, or if only offsets changed and the last save was over 60 seconds ago. `Dispose` saves. `Start` mirrors `Collector.Start`: a background thread that never lets an exception end the loop and reports it as one line in `errors`. During the first pass a snapshot is published after each file, with `reading: true`.

- [ ] **Step 1: Write the failing tests** (`Feed` now calls `pile.Poll`)

```csharp
[Test] public void PollReadsEachFileOnceThenOnlyWhatIsAppended() {
    Assert.AreEqual(4, pile.Poll(Now).rows.Count); Assert.IsFalse(pile.Latest.reading);
    var same = pile.Latest; Assert.AreSame(same, pile.Poll(Now + 2000));                      // nothing changed: nothing published
    File.AppendAllText(senderPath, SentLine("toolu_s9", "beta-01", "Later message.") + ResultLine("toolu_s9", "m-1009"));
    Assert.AreEqual(5, pile.Poll(Now + 4000).rows.Count);
    Assert.AreEqual("Later message.", pile.Latest.rows[0].firstLine);
}
[Test] public void AHalfWrittenSendIsPickedUpWhenItsLineCompletes() {                         // Review Focus 3
    var line = SentLine("toolu_s9", "beta-01", "Later message.");
    File.AppendAllText(senderPath, line.Substring(0, 40)); Assert.AreEqual(4, pile.Poll(Now).rows.Count);
    File.AppendAllText(senderPath, line.Substring(40)); Assert.AreEqual(5, pile.Poll(Now + 2000).rows.Count);
}
[Test] public void ATranscriptThatShrankIsReadAgainWithNoDuplicates() {                       // Review Focus 3
    pile.Poll(Now); var all = File.ReadAllText(recipientPath);
    File.WriteAllText(recipientPath, all.Substring(0, all.IndexOf('\n') + 1)); Assert.AreEqual(4, pile.Poll(Now + 2000).rows.Count);   // nothing lost
    File.WriteAllText(recipientPath, all); Assert.AreEqual(4, pile.Poll(Now + 4000).rows.Count);                                      // nothing doubled
}
[Test] public void OffsetsSurviveARestartSoNothingIsReadTwice() {
    pile.Poll(Now); pile.Dispose();
    foreach (var f in new[] { senderPath, recipientPath, desktopPath }) File.SetLastWriteTimeUtc(f, DateTime.UtcNow);
    using (var again = new Pile(settings, home, statePath)) { var before = again.Latest; Assert.AreSame(before, again.Poll(Now + 2000)); }
}
[Test] public void PollNeverWritesUnderAConfigDirectory() {
    var before = HashTree(Path.Combine(home, ".claude")); pile.Poll(Now); pile.Dispose();
    Assert.AreEqual(before, HashTree(Path.Combine(home, ".claude")));
}
```

- [ ] **Step 2:** Run the core tests. Expected: FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run the core tests. Expected: all pass, 80 old tests included.
- [ ] **Step 5: Check against the real history.** Run a throwaway script that counts distinct sends in the real stores (tool call ids, less those with `pin` or `recipient_kind: agent`); run one `Poll` against the real config directories from a throwaway EditMode test with `statePath` in a temp folder. Expected: `rows.Count` equals the script's count, and the first pass takes under 60 seconds. Record both numbers on the ticket. Delete the throwaway test.
- [ ] **Step 6:** Commit `GRID-8: poll transcripts for new messages`.

### Task 5: The window

**Files:** Modify `Assets/TheGrid/RosterApp.cs`.

**Interfaces:**
- Consumes: `Pile`, `PileSnapshot`, `PileFormat`.

Behaviour:
- On start, after the collector: `pile = new Pile(settings, home, Path.Combine(Application.persistentDataPath, "pile.json")); pile.Start();`. `OnDestroy` disposes it.
- At 720 points or wider: roster in the left half, pile in the right half. Narrower: a two-button toolbar above the list, `Agents (n)` and `Messages (n)`, where the second `n` counts unticked rows. The roster draws exactly what it draws today.
- Pile, first line: `Messages: n to review · m archived` (plus `· reading history…` while `reading`). Second line: the buttons `Tick shown` and `Clear ticked (k)`; `Clear ticked` is disabled when `k` is 0.
- Each message is two lines. Line 1: a tick box, `from → to` in the title style, then the age (`PileFormat.Age`) at the right. Line 2, indented 24 points: the summary if there is one, else the first line; clipped, never wrapped. When `crossWorkspace` the line is drawn in the question red and starts with `fromWorkspace → toWorkspace · `. Clicking line 1 opens the full text under the row (`Pile.ReadBody`, wrapped); clicking again closes it. One row is open at a time. An empty `from` or `to` is drawn as its repo name, or `unknown`.
- The newest 200 rows are drawn; below them `… and n older` when there are more. `Tick shown` ticks only drawn rows.
- The snapshot is taken on the `Layout` event, as the roster does, so both IMGUI passes see the same rows.
- Cycling a workspace on the roster calls `pile.Refresh()` so the flags follow.
- Log one line when the first full pass ends: `[pile] n messages, m archived`.

- [ ] **Step 1:** Implement.
- [ ] **Step 2:** Run the core tests. Expected: all pass.
- [ ] **Step 3: Build.** In batch mode in this worktree: `-executeMethod TheGrid.EditorTools.GridMenu.BuildMac -quit`. Expected: `Builds/TheGrid.app` exists, exit code 0.
- [ ] **Step 4: Check it live.** Launch the build. Expected: the roster tab matches `claude agents --json`; the messages tab fills; `Player.log` has `[pile] n messages` and no exception or `layout overflow` line at 360 points wide.
- [ ] **Step 5: Check the definition of done with two live sessions.** Start two disposable Claude Code sessions in scratch folders and have each send the other one message. Expected: exactly two new rows within 10 seconds, each naming both sessions; tick both, `Clear ticked`, both leave; `find ~/.claude* -newer <marker> -type f` lists only files the two sessions themselves wrote, and `pile.json` is the only file The Grid changed.
- [ ] **Step 6:** Commit `GRID-8: messages tab in the window`.

### Task 6: Docs and pull request

**Files:** Modify `README.md`, `docs/spike-idle-detection.md`, `fixtures/claude-code/README.md`.

- [ ] **Step 1:** README: a "Review pile" section (what it lists, tick and clear, that Clear only archives The Grid's own list, where `pile.json` lives, that deleting it rebuilds the list unticked). Spike doc: replace the "Messages between sessions" bullets with the table from this plan. Fixtures README: the three new files.
- [ ] **Step 2:** Run the core tests one last time. Expected: all pass.
- [ ] **Step 3:** Commit `GRID-8: document the review pile`, push, open a pull request against `main`, comment `@codex review`, and answer every finding with a reaction and a reply.
- [ ] **Step 4:** Comment on GRID-8 with what was verified, the two numbers from Task 4, and anything that departed from this plan.
