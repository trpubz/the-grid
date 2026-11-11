# Roster snapshot

The roster snapshot is the one description of the data that the viewer draws. `TheGrid.Core.RosterBuilder` produces it; anything that later feeds or consumes the roster (a relay, another client) follows the same shape.

The exact JSON is pinned by a test against [`fixtures/roster/snapshot.json`](../fixtures/roster/snapshot.json). If that test fails, the shape changed.

## Snapshot

| Field | Meaning |
|---|---|
| `generatedAt` | When the snapshot was built, in milliseconds since 1970 UTC |
| `agents` | Agent rows, most urgent first |
| `errors` | Problems reading a source, one line each. Rows from a failing source are kept from the last good read |

## Agent row

| Field | Meaning | Source |
|---|---|---|
| `sessionId` | The agent app's id for the session | Live session list |
| `name` | Session name | Live session list |
| `account` | Which account the session runs under | Name of the config directory it was read from |
| `app` | `cli`, `claude-desktop`, or empty when unknown | Per-process session file |
| `repoRoot` | Folder of the repo the session works in; empty when none | The session's working folder, walked up to `.git`. Worktrees resolve to the main repo |
| `repoName` | Folder name of `repoRoot`, or `No project` | Same |
| `branch` | Current git branch | Last branch recorded in the session's transcript |
| `ticket` | Ticket id, such as `DEMO-12`; empty when none | First `letters-digits` token in `branch` |
| `prNumber`, `prUrl` | Pull request; `0` and empty when none | Pull request record in the transcript. Once seen it is kept |
| `state` | One of the states below | Mapped from the live session list |
| `activity` | What a running agent is doing: `reading`, `editing`, `testing`, `reviewing`, or `working` when it cannot be told. Empty in every other state | The newest record of the agent's own in its transcript: the last tool it used, or keywords in what it said |
| `rawStatus` | The agent app's own words, such as `waiting / input needed` | Live session list |
| `workspace` | Workspace the repo is assigned to; `Personal` when unassigned | Settings |
| `stateSince` | When the agent entered `state`, in milliseconds since 1970 UTC | Per-process session file, else the first time the roster saw that state |
| `subagents` | Helpers the agent started that are still working | Files beside the transcript |

A subagent has `agentId`, `description` (what it was asked to do) and `agentType`.

## States, in order of urgency

| `state` | Meaning |
|---|---|
| `BlockedOnQuestion` | The agent asked a person something and cannot continue |
| `WaitingForPermission` | The agent needs an approval |
| `WaitingOther` | Waiting on something else, such as an open dialog |
| `BackgroundBlocked` | A background session that is blocked |
| `Errored` | A background session that failed |
| `Finished` | The turn ended; the agent has nothing to do |
| `Unknown` | The status was not recognised; `rawStatus` says what it was |
| `Running` | Working |
| `Offline` | The session was live and is gone. Kept for 12 hours |

Within one state, the agent that has been in it longest comes first.

## The world

The world view is built from the same snapshot (`World.Build`), so the two never disagree. Each repo is a sector on its own plot; sessions with no repo share one. Each agent is one bot. State is on the bot: a building stands only for a kind of work, and everything else is where the bot stands, how it holds itself, and the badge it carries.

| `state` | `activity` | Stands at | Stance | Badge | Needs the owner |
|---|---|---|---|---|---|
| `Running` | `reading` | the server tower | inspecting | | |
| `Running` | `editing` | the assembly bay | typing | | |
| `Running` | `testing` | the test workshop | typing | | |
| `Running` | `reviewing` | the review hall | inspecting | | |
| `Running` | anything else | the works | standing | | |
| `BlockedOnQuestion` | | the forecourt | standing | question | yes |
| `WaitingForPermission`, `WaitingOther`, `BackgroundBlocked` | | the forecourt | standing | approve | yes |
| `Errored`, `Unknown`, or a state not in the list above | | the forecourt | standing | fault | yes |
| `Finished` | | the plaza | standing | done | |
| `Offline` | | the dock | off, unlit | | |

A bot is one agent, told apart by `account` and `sessionId` together. Its place among the other bots at the same spot follows that pair, not the roster's order, so bots do not shuffle when the roster re-sorts. `fixtures/world/snapshot.json` is a synthetic snapshot with every state and activity in it.
