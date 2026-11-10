# Spike: can The Grid tell why an agent is idle?

Ticket GRID-3. Run on 2026-11-10 on one Mac (Apple silicon, Darwin 27.2).
Versions: Claude Code 2.1.322 (sessions on disk span 2.1.310 to 2.1.322), codex-cli 0.163.0, Zellij 0.45.1, Ghostty 1.4.0.

Method: structure-only surveys of real session stores (field names and categorical values, never message text), one live test, and the official Claude Code docs. Nothing in any agent app's storage was modified.

## Answer

For Claude Code, yes, and without installing anything. Claude Code already publishes each live session's state.

| Idle kind | Signal | How sure |
|---|---|---|
| Running | `status: busy` | Observed |
| Blocked on a question | `status: waiting`, `waitingFor: input needed` | Observed live, see below |
| Waiting for permission | `status: waiting`, `waitingFor: permission prompt` | Observed live, see below |
| Finished, awaiting next prompt | `status: idle` | Observed |
| Background session blocked | `kind: background`, `state: blocked` | Observed |
| Errored | `state: failed` for background sessions (documented). For interactive sessions no separate signal was found; assistant records carry `isApiErrorMessage` | Partial |
| Offline | Transcript exists, session absent from the live list | Inferred |

**Live test.** A session asked its user a question while a sampler read that session's row every 2 seconds. The row read `waiting / input needed` from the first sample to the last one before the answer (58 seconds), and `busy` on the next sample. The matching `AskUserQuestion` record in the transcript is timestamped at the start of that window, and its result at the end.

**Second live test.** A disposable Claude Code session was started in its own Zellij pane, in a scratch folder, in the default permission mode, and asked to run a harmless shell command. Its row went `busy`, then `waiting / permission prompt` within 5 seconds, then `busy` and `idle` after the prompt was approved. One limit: while the first-run "trust this folder" dialog is open, the session is not in the live list at all.

## Where the state comes from

1. **`claude agents --json`** (documented). One row per live session: `sessionId`, `name`, `cwd`, `kind`, `pid`, `startedAt`, `status`, and `waitingFor` when waiting. Takes about 0.12 s per call here. Must be run once per config directory.
2. **`<config dir>/sessions/<pid>.json`** (not documented, may change). One file per live process with the same fields plus `statusUpdatedAt`, `entrypoint` and `version`. `statusUpdatedAt` gives "time in this state" directly, and the files can be watched instead of polled.
3. **Hooks** (documented, opt-in, not needed for the states above). Tested with a logging hook in the scratch project's `.claude/settings.json`. Order and timing seen for one turn with one approved command:

   | Seconds | Hook | Note |
   |---|---|---|
   | 0 | `SessionStart` | |
   | 15 | `UserPromptSubmit` | prompt submitted |
   | 17 | `PreToolUse`, `PermissionRequest` | same second; `tool_name` present on both |
   | 23 | `Notification` with `notification_type: permission_prompt` | 6 seconds after the request |
   | 55 | `PostToolUse` | after approval |
   | 56 | `Stop` | turn ended |
   | 116 | `Notification` with `notification_type: idle_prompt` | 60 seconds after `Stop` |
   | 148 | `SessionEnd` | pane closed |

   Every hook received `session_id`, `transcript_path` and `cwd`. `PermissionRequest` gives a blocked signal 6 seconds sooner than `Notification`. Hooks can be set per project or per config directory.

Recommended for slice 1: read source 1 for state, use source 2 only for time-in-state, and keep hooks in reserve.

**Do not infer state from the end of a transcript.** A classifier that looked for unanswered tool calls marked most finished sessions as "pending". The cause was not run down; the live list makes the heuristic unnecessary.

## Where the other roster fields come from

All from the session transcript, `<config dir>/projects/<project>/<session id>.jsonl`.

| Field | Source |
|---|---|
| Repo | `cwd` on every conversation record, and in the live list |
| Branch | `gitBranch` on every conversation record |
| Ticket | Parsed from the branch name |
| Pull request | `pr-link` records: `prNumber`, `prUrl`, `prRepository`. Present in 48 of 107 sessions scanned. No network lookup needed |
| Account | The config directory, plus `session-owner` records (`accountUuid`, `organizationUuid`) |
| Session name | `name` in the live list; `agent-name`, `custom-title` and `ai-title` records |
| App | `entrypoint`: `cli` or `claude-desktop` |
| Time blocked on a question | Gap between an `AskUserQuestion` tool call and its result. Median 283 s over 239 questions |
| Permission outcome, after the fact | `permissionDecision` on tool results: `decision` and `source` (`config`, `user_temporary`, `user_permanent`, `user_reject`, `hook`) |

## Accounts

Two Claude accounts on this Mac are kept apart by config directory (`CLAUDE_CONFIG_DIR`). Each directory held exactly one `accountUuid`. A collector lists the config directories it should read and labels every session by the directory it came from.

## Subagents

- Files under `<project>/<session id>/subagents/`, including a `workflows/` level.
- Records inside the parent carry `isSidechain: true` and an `agentId`.
- The parent's `Agent` tool call has `description`, `prompt` and `subagent_type`, which is the "what is it doing" text.

## Messages between sessions

- **Sent:** an assistant record with a `SendMessage` tool call; input has `to`, `message` and `summary`.
- **Received:** a user record with `origin.kind: peer` and `turnOrigin: peer`. The origin holds `from`, `name`, `msg_id` and `body`, and sometimes `hopChain`.
- **Subagent reports** use the same record with `origin.handback: true`. Filter on that flag to keep them out of a review list.
- **Other machine-made turns** are marked `origin.kind: task-notification`, so human prompts can be told apart from all of them (`origin.kind: human`).
- Messaging works only between sessions of one account.

**Typed-in messages.** Another agent can reach a session by typing into its terminal pane. On this Mac that is done with Zellij: `action list-panes` to find the pane, `action dump-screen -p <pane>` twice to check nobody is typing, `action write-chars -p <pane>` for the text, and `action write -p <pane> 13` for Enter. Tested: a prompt typed into the scratch session this way was recorded exactly like one a person typed (`origin.kind: human`, `promptSource: typed`). The records cannot tell the two apart. Labelling such a message needs a marker in the text or a send path that The Grid owns.

## Other apps

| App | Finding |
|---|---|
| Claude desktop (coding sessions) | Same transcript store and same live list as the terminal app; `entrypoint: claude-desktop`. No separate reader needed |
| Codex | `~/.codex/sessions/<y>/<m>/<d>/rollout-*.jsonl`. `session_meta` has `cwd`, `parent_thread_id`, `creator_account_id`, `agent_nickname`. Turn boundaries are `task_started`, `task_complete` and `turn_aborted` events. It records its own agent-to-agent messages (`inter_agent_communication_metadata`, `agent_message`). A turn ends with a `task_complete` event. On this Mac a command that needed approval was reviewed by a second Codex thread (`source: subagent`, named guardian) and ran without asking a person, so a human approval wait was not seen and may be rare. `codex queue --thread <id> --message <text>` queues a message for an existing session, and `codex agents` browses sessions on a shared local daemon; neither was exercised |
| Grok | Used as a chat app, not a coding agent. It leaves no session records; it only appears as text typed into a pane |

## Terminal control

- **Zellij 0.45.1**, exercised against a disposable session: `action list-panes -j -a` returns each pane's id, title, command, working directory, tab and focus; `action focus-pane-id` moves focus; `action dump-screen -p` reads a pane; `action write-chars -p` and `action write -p <pane> 13` type and press Enter.
- **Session to pane:** the Claude process's environment holds `ZELLIJ_SESSION_NAME` and `ZELLIJ_PANE_ID`. The live list gives the process id, so a session maps to its exact pane with no guessing.
- **Ghostty 1.4.0** has helper actions on the command line only; it was not explored further.

## Verdict for slice 1

| Idle kind | Can slice 1 show it? |
|---|---|
| Blocked on a question | Yes |
| Waiting for permission | Yes |
| Finished | Yes |
| Running | Yes |
| Errored | Background sessions only; interactive needs more work |
| Offline | Yes |

## Still open

- Codex: a wait for a person's approval was not seen; how it is recorded is unknown.
- Interactive sessions that hit an error have no separate live signal.
- A session sitting at the "trust this folder" dialog is invisible to the live list.
- `codex queue` and `codex agents` were read in the help text, not run.
- Ghostty was not explored; Zellij covers jumping and typing.
- The per-process session files are undocumented; decide whether depending on them is acceptable.
- Fixtures are hand-written in the observed shapes, not copies of real records.

Side effects of the live tests: the two apps saved a "trusted folder" entry for the scratch folder, and the test sessions' transcripts remain in their stores.

## The two reference repositories

Both were read at the commits pinned in `docs/references.md`, which are still their latest. Both are MIT licensed, so code can be reused here if the copyright notice and licence text are kept. Nothing from them was run.

**Neither one sends or observes messages between sessions.** bot-crossing has no messaging at all. claude-devtools only displays Claude Code's own team messages inside a single session tree.

**Neither one can tell a question, a permission prompt and a finished turn apart.** Both work from the end of the transcript plus time windows, which is the approach this spike found unreliable. The live session list above is the improvement over both.

| | bot-crossing | claude-devtools |
|---|---|---|
| Apps read | Claude (terminal and desktop), Codex | Claude Code only |
| How it decides "running" | Process still alive, file changed in the last 30 minutes, last record not a handed-back turn | Activity after the last "ending" event, forced off after 5 minutes of silence |
| Codex | Reads `~/.codex/state_<n>.sqlite` (table `threads`) read-only, merged with the rollout files; parent and child threads via `thread_spawn_edges` | Not supported |
| Subagents | `<project>/<session id>/subagents/agent-<id>.jsonl`, live parents only | Three-step linking: agent id to file name, then by description, then by position |
| Pull request | Only from the Claude desktop record (`prState`) | None |
| Opening a session | Deep links (`claude://resume?session=<id>`, `codex://threads/<id>`) or a terminal running `claude --resume <id>` / `codex resume <id>` | None |
| Hooks | None, by rule | None |

Worth taking from them:

- Merging the desktop app's session records with terminal transcripts, and honouring its "deleted" markers.
- Reading every `~/.claude-*` directory as a possible config directory.
- Probing each SQLite column before use, so a schema change degrades one field instead of the reader.
- Matching a subagent file to its parent by agent id, and accepting both `Task` and `Agent` as the tool name.
- The resume commands and deep links, which give "jump to this session" as a copyable command today.

## Search across conversations

claude-devtools can search every project. How it does it, and what to change:

- It keeps no index on disk. Every transcript is parsed on demand; extracted text sits in an in-memory cache that is lost on restart, so the first search after launch reads everything.
- It searches the user's prompts and only the last text block of each assistant turn. Tool inputs and outputs, thinking, and subagent files are not searched.
- Matching is case-insensitive substring. It stops at 50 hits and sorts afterwards, so results are not guaranteed to be the newest.
- Each hit records the session and the message it came from, and the view scrolls to it.

For The Grid: keep a persistent full-text index (SQLite's is enough), include tool inputs and outputs and subagents, index each config directory separately so the workspace wall holds, and keep the link from a hit to its place in the session.

## Similar open-source projects

Surveyed on GitHub on 2026-11-10. Each repository was opened, but the descriptions below come from READMEs and docs; none of the code was run, and nothing outside GitHub was searched. Check a project again before relying on any detail.

| Project | Licence | What it is | How a message reaches an agent | Tells why an agent is idle? |
|---|---|---|---|---|
| [repowire](https://github.com/prassanna-ravishankar/repowire) | MIT (per README) | Lets agents in different repos ask each other; Claude Code, Codex, OpenCode, Pi | Local daemon; hooks and the app's own inbox for Claude, an app-server bridge for Codex. Reaches sessions it did not start | Not stated |
| [hcom](https://github.com/aannoo/hcom) | MIT | Message bus across many terminal agents | Hooks and SQLite; idle agents are woken by typing into the terminal. Sessions it launched or that joined | From hooks: idle and blocked events |
| [open-vibe-island](https://github.com/Octane0411/open-vibe-island) | GPL-3.0 | macOS control centre for about 15 agents, including both desktop apps | No messaging | Yes: permission and question cards, from hooks and transcripts |
| [c9watch](https://github.com/minchenlee/c9watch) | MIT | Mac app that watches Claude Code and Codex with no setup | No messaging | Yes: approval, question, or next prompt, from a process scan plus transcript tail; hooks optional. Publishes a tested accuracy grid |
| [claude-control](https://github.com/sverrirsig/claude-control) | MIT | macOS dashboard for Claude Code with pull request and CI badges | Approves prompts by sending keys through tmux | Working, idle, waiting, errored, finished, from hooks |
| [cli-agent-orchestrator](https://github.com/awslabs/cli-agent-orchestrator) | Apache-2.0 | Supervisor and worker agents in tmux | Queue, then paste into the terminal when the target is idle. Only sessions it launched | By reading terminal output |
| [mcp_agent_mail_rust](https://github.com/Dicklesworthstone/mcp_agent_mail_rust) | MIT with a rider | Mail for agents with a web view for a human overseer | Agents fetch their inbox with a tool; nothing is pushed | No |
| [agentchattr](https://github.com/bcurts/agentchattr) | MIT | Chat room shared by people and agents | A mention makes a wrapper type "read the channel" into the agent's terminal | No; pauses after a set number of hops |
| [agent-message-queue](https://github.com/avivsinai/agent-message-queue) | MIT | File-based queue between Claude Code and Codex | Files, plus a typed "doorbell" to wake the agent | No |
| [claude-peers-mcp](https://github.com/louislva/claude-peers-mcp) | MIT | Claude-only peer messaging | Pushes into running sessions through Claude's channel feature | No |
| [gastown](https://github.com/gastownhall/gastown) | MIT | Workspace manager with mailboxes and escalation | Git-backed mailboxes; hooks for Claude, terminal nudges elsewhere | Detects stuck agents |
| [agent-deck](https://github.com/asheshgoplani/agent-deck) | MIT | Terminal session manager | A send command per session | Running, waiting, done. The only one found with per-group config directories for several accounts |

Grouped by how messages travel:

- **Hooks and a local store:** hcom, repowire, gastown.
- **A feature of the agent app itself:** claude-peers-mcp, repowire.
- **A tool the agent calls to fetch mail:** mcp_agent_mail_rust.
- **Typing into the terminal:** cli-agent-orchestrator, agentchattr, agent-message-queue, hcom.
- **Watching only:** open-vibe-island, c9watch, claude-control.

Read first: repowire (closest to routing between repos and providers without typing into terminals), open-vibe-island (closest to the Mac oversight goal; read it, do not copy it, because of its licence), and c9watch together with cli-agent-orchestrator's `docs/inbox-delivery.md` (detecting why an agent is idle, and what goes wrong when text is typed into a terminal).

What none of them appear to do:

- Combine oversight and messaging in one place.
- Show the ticket an agent is working on.
- Review or route messages across several accounts.
- Hand work to another repo's agent when a ticket is written.
- Deliver into the desktop apps' sessions.

## Fixtures

`fixtures/claude-code/` holds hand-written records in the shapes described above. They contain no real session data.
