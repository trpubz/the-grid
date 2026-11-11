# The Grid

> **Note (2026-11-10):** parts of this document are superseded by the approved product requirements. The world is now isometric 16-bit pixel art; version 1 observes the messages agents already send rather than delivering them; seeing which agents are idle is the primary feature; and the order of work has changed. Where this document and the requirements differ, the requirements win. A public summary of the requirements will be added to this repo.

An open-source Unity desktop world where people share a repo sector, watch their coding agents work, and let specific agents exchange addressed messages across providers.

## Start here

This is an implementation starter. The included Unity component plays a fixed recording. It opens no network connection, reads no real session history, and spends no provider tokens. The collector, relay, installers, and live messaging described below are implementation work.

1. From this folder run `python3 tools/check_replay.py`. Expect two actors, ten events, and two correlated deliveries.
2. Open this folder as a project in Unity 6 (pinned in `ProjectSettings/ProjectVersion.txt`). Open `Assets/TheGrid/Demo.unity` and press Play. Use a Game view at least 800 pixels wide.
3. Step with **Next event**. Alex edits, asks Sam’s agent for a review, and receives a reply. A pulse travels on each delivery. Sam goes offline. Replay resets both actors. Names and message text here are synthetic.
4. Read [the agent brief](AGENT_HANDOFF.md), [the architecture](docs/architecture.md), [the transport contract](docs/protocol.md), and [the acceptance plan](docs/delivery.md).
5. First implementation issue: prove a real supported send-and-reply path between two different agent apps. Record versions, exact capabilities, and failure behavior. Then connect observation and transport to Unity.

## The roster

The first working piece: a window that lists your live Claude Code sessions on this Mac, with the one that needs you most on top. It reads what Claude Code already publishes about its own sessions and never writes to an agent app's files. The Grid's own code makes no network calls; it does run the `claude` program to ask for the session list.

- **Run it in the editor:** open the project in Unity 6000.4.3f1, open `Assets/TheGrid/Roster.unity`, press Play.
- **Build the app:** menu **The Grid > Build Mac App**, then open `Builds/TheGrid.app`.
- **Run the tests:** menu **The Grid > Run Core Tests**; results appear in Unity's Test Runner window and in `TestResults.xml` in the app's data folder. With the editor closed:

  ```bash
  /Applications/Unity/Hub/Editor/6000.4.3f1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath "$PWD" -runTests -testPlatform EditMode -assemblyNames TheGrid.Core.Tests -testResults Logs/editmode.xml
  ```

The first time a build runs, macOS asks whether The Grid can access folders such as Documents, because your repos live there. Until you answer, the roster shows an error saying `claude agents did not answer`. It asks once: **Build Mac App** signs every build with the first Apple Development certificate in your keychain (Xcode creates one for any Apple account, free or paid), so a rebuilt app is still the same app to macOS. To sign with a different certificate, put its name or SHA-1, as `security find-identity -v -p codesigning` prints it, on one line in `UserSettings/TheGridSignIdentity.txt`; that folder is not tracked. With no certificate at all the build is left unsigned and macOS asks again after every rebuild. Run one copy at a time: two open copies with different signatures, such as an older unsigned build beside a new signed one, are one app by name but not by identity, and macOS then asks over and over until one of them is closed.

Each row shows the state (question, permission, done, running and so on), what a running agent is doing (reading, editing, testing, reviewing, or just working when it cannot be told), the session name, its account, how long it has been in that state, the repo, the ticket read from the branch name, the pull request, and the workspace. Click the pull request to open it. Click the workspace to move the repo to the next one.

Settings live in `~/Library/Application Support/DefaultCompany/The Grid/settings.json`, created on first launch. Edit the file to add workspaces; the app re-reads it the next time you click a workspace.

| Field | Meaning |
|---|---|
| `configDirs` | Claude config directories to read. Empty means `~/.claude` and every `~/.claude-*` that has a `projects` folder |
| `claudePath` | Path to the `claude` program. Empty means look in `~/.local/bin`, `/opt/homebrew/bin` and `/usr/local/bin` |
| `workspaces` | Workspace names, in the order a click cycles through them. Starts as `Personal` |
| `repoWorkspaces` | Which repo belongs to which workspace. A repo not listed is `Personal` |

## The review pile

Beside the roster, the window lists every message your Claude Code sessions sent each other on this Mac, newest first, once each. The Grid only observes these messages. It never sends, changes or deletes one.

- At 720 points or wider the roster is on the left and the pile on the right, under **Lists**. Narrower, they are two tabs: **Agents** and **Messages**. **World** is beside them either way.
- Each row shows who sent it, who received it, how long ago, and the sender's summary or the first line. Click the names to read the whole message; it is read from the session's transcript at that moment.
- Tick a message once you have reviewed it. **Clear ticked** archives the ticked rows: they leave the list and stay archived. **Tick shown** ticks every row on screen, which helps with the backlog on first launch; once they are all ticked the same button reads **Untick shown** and takes the ticks back.
- A row drawn in red, starting with two workspace names such as `Work → Personal`, is a message whose sender and recipient repos belong to different workspaces. A message is only flagged once both its sent and its received record have been found.
- A session's exchanges with its own subagents are not listed.

Clearing changes The Grid's own list and nothing else. The pile lives in `pile.json` beside `settings.json`. It holds, for each message, the two session names and repos, the time, the summary, the first line and where the full text can be read, plus how far each transcript has been read, so a transcript is not read twice. It holds no full message text. Nothing is written under a Claude config directory. Deleting `pile.json` rebuilds the list from the transcripts with every message unticked; on this Mac that first read took 9 seconds for 3.4 GB of transcripts.

The shape of the data the window draws is described in [docs/roster-snapshot.md](docs/roster-snapshot.md).

## The world

**World** shows the same agents as the roster, as riders in a city. Every repo gets a plot on one isometric map, and the map grows as new repos turn up. A rider stands at the building that matches what its agent is doing, and rides to another when that changes. Nothing else moves, except a marker that bounces over every rider that needs you.

![The world, zoomed in on one city](docs/art/world-near.png)

| Building | A rider is here when its agent is |
|---|---|
| Garage | editing files |
| Library | reading or searching |
| Lab | running tests |
| Review hall | looking at diffs, commits or a pull request |
| Workshop | working, when it cannot be told on what |
| Booth | blocked on a question to you |
| Gate | waiting for a permission, or on something else |
| Yard | errored, or in a state The Grid does not know |
| Lot | done with its turn |
| Depot | offline |

Past the last plot the land carries on to the edge of the window: stones, outcrops, a spire or a lake here and there. No city stands stranded in it: each has another city or a thoroughfare on at least two sides, and where the map has a notch between two cities the road runs through the notch. All of that is scenery, worked out from where the cities are; it says nothing about any agent.

The Booth, the Gate and the Yard are where you are needed. The tall work buildings stand along the back street of a plot and the low ones along the front, so no tower hides a rider.

- **Move:** drag, or scroll with two fingers.
- **Zoom:** the **−** and **+** buttons, the `-` and `+` keys, or scroll while holding Command. There are five levels, each double the last. The view opens at the closest one that shows every plot.
- **Far out,** each city shows its name, how many riders need you and how many are done. **Closer in,** buildings and riders are named. A rider's glow colour is its account. Small bikes beside a rider are its subagents.
- **Click a rider** to see that agent's roster row under the map.

Which repo has which plot is kept in `world.json` beside `settings.json`. A repo keeps its plot for good, also after its sessions are gone; delete the file to start the map again. If its contents are damaged it is kept as `world.json.bad` and the map starts empty.

To look at the world without any agent sessions, start the app on a roster snapshot file. It then reads no agent data and writes nothing:

```bash
Builds/TheGrid.app/Contents/MacOS/"The Grid" -snapshot "$PWD/fixtures/world/snapshot.json"
```

Add `-shots <folder>` and it saves `far.png` and `near.png` there and quits. With the editor closed, **The Grid > Render World Pictures** does much the same without a window, into `Logs/`, though without the names:

```bash
/Applications/Unity/Hub/Editor/6000.4.3f1/Unity.app/Contents/MacOS/Unity -batchmode -projectPath "$PWD" -executeMethod TheGrid.EditorTools.GridMenu.WorldPictures -quit
```

The sprites are made by a script; see [tools/art/README.md](tools/art/README.md).

## Package map

| Path | Purpose |
|---|---|
| Assets/TheGrid/GridReplay.cs | Small interactive 3D fixture viewer. No provider SDK dependency. |
| Assets/TheGrid/Demo.unity | Scene with GridReplay attached. Open it and press Play. |
| Assets/TheGrid/Core/ | Reads live sessions and builds the roster. No scene or editor code. |
| Assets/TheGrid/RosterApp.cs, Roster.unity | The roster window and its scene. |
| Assets/TheGrid/WorldView.cs | The world: the map, the riders, moving and zooming. |
| Assets/TheGrid/Resources/Art/, tools/art/ | The sprites, and the Blender script that makes them. |
| Assets/TheGrid/Tests/ | EditMode tests for Core. |
| fixtures/ | Hand-written sample records and the pinned roster snapshot. |
| Assets/TheGrid/Resources/grid-demo.json | Ordered offline projection for two synthetic users, sessions, and messages. |
| tools/check_replay.py | Dependency-free contract check for the fixture. |
| docs/protocol.md | Live envelope, identifiers, transactions, failure handling, and proposed routes. |
| docs/architecture.md | Boundaries, data ownership, ingestion, scene behavior, coworker onboarding. |
| docs/delivery.md | Implementation sequence, acceptance cases, verification evidence and release checklist. |
| docs/references.md | Pinned upstream reference implementations and licensing notes. |

## First live demo

Two installations bind different local paths to one invite-only room. Alex runs Codex and Claude Code in the same clone. Sam runs Claude Code in another clone. Both clients see three distinct bots. Alex’s Codex addresses Sam’s Claude agent with a review request for a commit available to both. Sam’s agent returns a correlated reply. Both clients show the delivery, Sam can inspect the message, and either user can revoke access. Disconnect Sam while his agent is active and verify it becomes offline. Reconnect without duplicating the reply.

The Grid is a working name. Use original art, sound, terminology and logos. The starter includes no upstream code or brand assets.

## License

[GNU AGPL v3.0](LICENSE).
