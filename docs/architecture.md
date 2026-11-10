# Architecture and user experience

> **Note (2026-11-10):** parts of this document are superseded by the approved product requirements. The world is now isometric 16-bit pixel art; version 1 observes the messages agents already send rather than delivering them; seeing which agents are idle is the primary feature; and the order of work has changed. Where this document and the requirements differ, the requirements win. A public summary of the requirements will be added to this repo.

## Process boundaries

    Agent apps on Alex’s machine          Agent apps on Sam’s machine
                ↕                                      ↕
       Alex’s local collector                 Sam’s local collector
                ↕                                      ↕
       Unity client / local IPC               Unity client / local IPC
              Collector A ← outbound TLS → relay ← outbound TLS → Collector B

Each Unity client subscribes to its own collector. Each collector owns its own outbound relay connection. The relay validates membership, sequences public room events and routes private messages. Long-lived vendor credentials and local session handles live on their owner’s device. A trusted self-hosted relay can read routed content in v1. Document that trust model to room members.

## Identity

Room creation generates a random room ID. On joining, each person explicitly maps that room to a checkout. Use Git’s common directory to relate local worktrees; store that association on the device. A sanitized Git remote can suggest a match. Forks, renamed remotes, identical folder basenames, differing SSH/HTTPS spellings and multiple tenants make automatic global merging unsafe. Let the person confirm the mapping. A workspace without Git can join through manual binding.

Use different IDs for room, member, installation, actor, harness session, provider and model. Issue a random room-scoped actor ID; privately map it to (installation, harness, native session ID). Persist mapping across restart. Same native ID on two devices must produce separate actors. Resuming the same verified native session preserves the actor. A fork/new session gets a new actor and optional parent link. A model switch changes turn metadata. Allow unknown provider/model. Avoid guessing a provider from the executable name. Tie each publishing actor to a registered installation and authenticated member at the relay.

## Collector and adapter contract

Give each adapter discovery, observation, optional delivery, and optional outbound agent-tool support. Discovery returns native session handle, directory, last activity, model/provider evidence and advertised capabilities. Observation yields source IDs or offsets, event times, kinds and confidence. Delivery takes an opaque local handle and a stable message ID and reports acceptance, refusal, indeterminate outcome or unsupported. The local tool binds its caller to an enrolled actor. Refuse ambiguous session identity.

Read harness-owned storage read-only. For JSONL, checkpoint file identity and last complete newline; tolerate a partial final record. Resume from that boundary, deduplicate on stable source identity, detect replacement/truncation and reconcile. For SQLite sources, use supported read-only access and bounded queries; handle locks and schema drift by degrading the affected adapter. Combine file watching with periodic reconciliation. Use recorded lifecycle markers when possible. Surface inferred state explicitly; retain “unknown” for missing signals.

Map observable tool use into a small public vocabulary: reading, editing, testing, reviewing, waiting, idle, error. A command classifier with low confidence produces “working.” A failed tool and a failed session represent distinct events. A read/unread flag alone cannot establish a question. Keep per-session activity separate from per-installation presence. Do not infer delegation or collaboration from proximity or simultaneous edits.

## Same-machine and team flows

First launch opens the fixture, then offers Connect local repo. Show a preview of detected apps, sessions, shared fields, and capabilities before joining. Ask for access only to selected locations. Let the user deselect a session and stop publishing. Install collector with the desktop release. Make keep-sharing-after-close an explicit visible setting, with a tray/menu stop control and clear process ownership.

Room creator obtains a short-lived single-use invite. Coworker installs the client, enters the invite, verifies relay identity, selects a checkout, sees their outgoing preview and joins. After redemption, rotate to a scoped member credential held in the OS credential store. Both sides connect outbound so a coworker needs no inbound port on their laptop. Show whose actors are local and whose are remote. Provide leave, pause, revoke and device removal. Relays enforce each on the next operation and disconnect revoked streams.

## Unity world

Start with one orthographic sector, one persistent spawn slot per actor and an owner nameplate. Show app and current action as text, with glyphs as redundant cues. Use idle, active, waiting, error, offline and unknown states. Keep the last observed action as history after going offline. Only animate a message crossing on confirmed delivery. A queued or expired message gets its own textual status. A pulse records a real exchange; arbitrary ambient movement has no operational meaning.

Selecting a bot shows owner, harness, optional provider/model, capability flags, last observed activity and permitted history. Inbox includes target, sender, time, status, reply linkage and optional commit. A room observer sees the exchange marker only if both peers’ sharing policy allows it. Direct-message text is visible to the two enrolled endpoints and their owners. A received history replay may update positions immediately and suppress old pulses. Keep deterministic positions across reconnects and update Unity GameObjects on the main thread.

## Proposed repository layout after bootstrap

Keep Assets/TheGrid in the Unity project. Add collector/, relay/, protocol/, fixtures/, and packaging/ beside the Unity project or use clients/unity/ at migration. Commit Unity .meta files and pin the editor and package lock once the actual project is initialized. Keep per-user logs, session databases, local credentials, builds and generated Library directories outside version control.
