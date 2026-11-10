# Assignment for the implementing agent

> **Note (2026-11-10):** parts of this document are superseded by the approved product requirements. The world is now isometric 16-bit pixel art; version 1 observes the messages agents already send rather than delivering them; seeing which agents are idle is the primary feature; and the order of work has changed. Where this document and the requirements differ, the requirements win. A public summary of the requirements will be added to this repo.

Build The Grid using this directory as the seed. Preserve portability across coding agent apps and model providers. A single-person dashboard or a Unity animation driven only by simulated data does not satisfy the live milestone.

## Product decisions supplied by the owner

- The client is Unity. Use a dark, geometric digital world, luminous routing lines, and visually distinct repo sectors. Use original assets.
- A repo is a shared place. Multiple agent apps can be active in it simultaneously. People can install the project, join coworkers and see their bots.
- An agent using one provider can send an addressed message to an agent using another and receive a reply. Make the human compose box an additional client of this mechanism.
- Prepare the codebase for open-source distribution. The owner chose “The Grid” as the working title.

## Proposed engineering defaults

Desktop first. Start with Codex and Claude Code. Keep the viewer in C#, the local collector and relay in TypeScript, and use SQLite per process for durable state. A single relay process with persistent storage is enough for the first team. Use outbound encrypted connections for remote sharing. Start with a macOS development build and document the installer work for other platforms. These choices can change after a capability spike. Confirm teammate OS before declaring the downloadable beta ready.

## Order of operations

1. Execute the offline quick start. Capture the initial package state and create a feature branch in the destination repo. Keep the synthetic recording as a regression fixture.
2. Spike delivery before investing in art. Use disposable, explicitly selected sessions and a harmless test question. Test sending **from an agent tool** in both directions. Distinguish an already-running interactive session, a resumable idle session, and an app process launched and owned by the Grid. Record what the installed versions actually support.
3. Build the local normalized event store. Connect one real transcript adapter, add the second with the same contract, then bind three sessions to one repo. Write scrubbed fixtures from controlled test sessions with consenting participants.
4. Implement team room identity and permissions, relay catch-up, a durable mailbox and the two-machine flow. Add a live Unity data source alongside the recording source. The same scene reducer must handle both.
5. Ship a downloadable development build and self-host instructions with a recorded real two-machine acceptance run. Preserve a fixture-only mode for contributors who have no provider credentials.

## Decisions that require evidence

Record the OS, Unity version, harness and CLI versions, upstream docs or source, sender invocation, recipient session before and after delivery, and a redacted receipt. A CLI exit code, a copied prompt, or a fresh generic provider API conversation cannot prove a turn reached the requested existing session. Mark unsupported capabilities explicitly. If only managed sessions work, label them and keep imported sessions viewable. Report that limitation before claiming general delivery.

## Handoff evidence

Deliver the exact commands to build, test, run a relay, join on a second machine, revoke a member, and reproduce one request and reply. List verified capabilities per OS and adapter, changes to protocol v1, privacy defaults, open defects, and the next smallest unblocked issue. Publish releases or select a final license only after the project owner supplies the destination and release details.
