# Delivery plan and acceptance

> **Note (2026-11-10):** parts of this document are superseded by the approved product requirements. The world is now isometric 16-bit pixel art; version 1 observes the messages agents already send rather than delivering them; seeing which agents are idle is the primary feature; and the order of work has changed. Where this document and the requirements differ, the requirements win. A public summary of the requirements will be added to this repo.

## Milestones

| Stage | Implementation | Evidence required to exit |
|---|---|---|
| 0 Fixture | Import supplied component, play twice, reset | Same final state both times; pulse only on ACK |
| 1 Capability spike | One minimal sender and recipient per harness | Real A→B and B→A turns; stable session identity; busy/unsupported outcomes documented |
| 2 Local observation | Checkpointed adapters, reducer, persisted identity | Two apps in one repo, correct states across restart, unknown fields handled |
| 3 Shared room | Relay, invite, two collectors, snapshots, auth and revocation | Separate devices and different checkout paths show same three actors; revoked member loses access |
| 4 Live messaging | Tool binding, private mailbox, receipts, replies, budgets | One real correlated agent-to-agent round trip visible to both permitted clients |
| 5 Coworker beta | Desktop build bundles collector, self-host setup, consent and diagnostics | A second person installs from a clean account without a Unity editor or development SDK |
| 6 Public release | License, third-party notices, signed builds where applicable, issue templates and CI | Rebuild from tag; publish verified capability and platform matrix |

## Implementation tickets in dependency order

- G01. Capture controlled input fixtures and a short capability report for each supported app/version. Distinguish observe, send from agent, receive into active session, receive into managed session, resume, reply correlation and verified delivery evidence.
- G02. Define protocol types plus validation. Freeze golden fixture cases. Implement a pure projection reducer shared conceptually between server tests and client fixtures.
- G03. Implement local identity, worktree mapping and source checkpoints. Add adapters independently. Expose local snapshot and incremental cursors.
- G04. Replace the Unity fixture-specific state logic with a source-independent reducer. Introduce live/replay selection and offline banner. Keep the synthetic demo usable on a clean install.
- G05. Add private room creation, invitation redemption, installation enrollment and short presence leases. Two processes in integration tests precede the actual two-device test.
- G06. Add durable routes, sender tools, recipient capability gating, inbox, correlated replies and ambiguity recovery. Track who originated each message and who acknowledged it.
- G07. Add build packaging, start/stop collector controls, self-host deployment guide, support export containing redacted diagnostics, and two-machine test receipts.

## Acceptance matrix

| Scenario | Pass condition |
|---|---|
| Same repo, different worktrees and folder names | One room, distinct actors, local checkout details stay private |
| Same basename, unrelated remotes | Two separate bindings unless manually joined |
| Same model across apps; model switch mid-session | Actors stay distinct, existing actor survives switch |
| Unknown provider or stale log | Honest unknown/inferred status; scanner continues |
| Partial JSONL line, rename or SQLite schema change | No double activity or crash; show adapter degradation |
| Tool call fails but turn recovers | Tool failure visible without permanently marking session failed |
| Parent and child, or just concurrent edits | Relationship only for observed parentage |
| Offline receiver, busy receiver, or expired invite | Queue/busy/expiry visible; consumed invite cannot be reused |
| Sender times out after acceptance, sends identical request again | One mailbox item and one provider submission where verifiable |
| Receiver crashes in ambiguous window | Query provider receipt or mark uncertain; no automatic duplicate turn |
| Same event arrives twice or replay has a cursor gap | Deduplicate or resync; no repeated pulse or stale state |
| User pauses sharing or revokes membership mid-flight | Stop new sends/feeds; discard pending work when revoked; report any prior external acceptance |
| Observer-only session targeted | Typed unsupported response and disabled recipient option |
| Message includes command-like text | Display as attributed peer content; no shell execution at the transport layer |
| Session A delegates to B and B replies | Native agent tool invoked on both ends, reply references original, sender and receiver confirmed |
| Third room member watches exchange | At most opted-in activity metadata; private body absent from stream, logs, history and snapshots |
| Collector stops while model still runs | Overlay offline after lease expires; last known activity retained separately |
| Fresh coworker computer | Download, join, select checkout and see both users; no inbound port or vendor key sharing |

## Operating the first beta

Begin with a private relay address and a small repo owned by the test participants. Document release artifact checksums, supported OS/architecture, installation steps, how to identify a known-good relay, invite expiry and expected background process. Include commands for backup, restore, revoke, purge and uninstall once those commands exist. Keep the local fixture demo available during relay outages. Test restoring a database backup against a stale client cursor; force snapshot refresh on relay epoch change.

Instrument counts and timings for adapter health, checkpoint lag, room cursor lag, mailbox age, ACK latency, unknown outcomes, and reconnect attempts. Use opaque IDs and coarse error codes. A support export requires a preview. Proposed beta target: event visible within five seconds on a healthy connection after capture, using a heartbeat separate from turn state. Record p50 and p95 with ten simulated sessions. Measure chat delivery separately because recipient turn boundaries can dominate latency.

## Release decisions for the owner

Confirm the public repo owner, license, trademark clearance, supported coworker platforms, relay operator, retention policy and whether participant-visible history survives a device reinstall. Identify how each supported app charges for managed or resumed turns. Make those costs and consent visible before enabling automatic peer replies. Start with typed text and an optional commit reference; add attachments and shared raw transcript browsing only as separate opt-in product decisions.
