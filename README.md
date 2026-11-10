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

The first working piece: a narrow window that lists your live Claude Code sessions on this Mac, with the one that needs you most on top. It reads what Claude Code already publishes about its own sessions, makes no network calls, and never writes to an agent app's files.

- **Run it in the editor:** open the project in Unity 6000.4.3f1, open `Assets/TheGrid/Roster.unity`, press Play.
- **Build the app:** menu **The Grid > Build Mac App**, then open `Builds/TheGrid.app`.
- **Run the tests:** menu **The Grid > Run Core Tests**; results appear in Unity's Test Runner window and in `TestResults.xml` in the app's data folder. With the editor closed:

  ```bash
  /Applications/Unity/Hub/Editor/6000.4.3f1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath "$PWD" -runTests -testPlatform EditMode -assemblyNames TheGrid.Core.Tests -testResults Logs/editmode.xml
  ```

Each row shows the state (question, permission, done, running and so on), the session name, its account, how long it has been in that state, the repo, the ticket read from the branch name, the pull request, and the workspace. Click the pull request to open it. Click the workspace to move the repo to the next one.

Settings live in `~/Library/Application Support/DefaultCompany/The Grid/settings.json` and are created the first time you change a workspace:

| Field | Meaning |
|---|---|
| `configDirs` | Claude config directories to read. Empty means `~/.claude` and every `~/.claude-*` that has a `projects` folder |
| `claudePath` | Path to the `claude` program. Empty means look in `~/.local/bin`, `/opt/homebrew/bin` and `/usr/local/bin` |
| `workspaces` | Workspace names, in the order a click cycles through them. Starts as `Personal` |
| `repoWorkspaces` | Which repo belongs to which workspace. A repo not listed is `Personal` |

The shape of the data the window draws is described in [docs/roster-snapshot.md](docs/roster-snapshot.md).

## Package map

| Path | Purpose |
|---|---|
| Assets/TheGrid/GridReplay.cs | Small interactive 3D fixture viewer. No provider SDK dependency. |
| Assets/TheGrid/Demo.unity | Scene with GridReplay attached. Open it and press Play. |
| Assets/TheGrid/Core/ | Reads live sessions and builds the roster. No scene or editor code. |
| Assets/TheGrid/RosterApp.cs, Roster.unity | The roster window and its scene. |
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
