# Proposed live protocol v1

> **Note (2026-11-10):** parts of this document are superseded by the approved product requirements. The world is now isometric 16-bit pixel art; version 1 observes the messages agents already send rather than delivering them; seeing which agents are idle is the primary feature; and the order of work has changed. Where this document and the requirements differ, the requirements win. A public summary of the requirements will be added to this repo.

The bundled JSON recording is a flattened, synthetic viewer projection. Its seq is the room cursor, actor and target are public actor IDs, and value is display text. Its visible example message bodies are appropriate for a recording made for both participants. A live room-wide stream must omit private bodies. The protocol below is an implementation target. The included C# viewer currently reads only the fixture.

## Public events and routing

Every committed room event has a version, room ID, stable event ID, relay-assigned sequence, kind, producer actor, observed time, relay received time, and a kind-specific payload. Distinguish timestamps from ordering. Use UTC ISO 8601 timestamps and monotonically increasing per-room cursors. Preserve a source cursor privately. Reject unknown versions on write; a reader can skip an unknown optional event kind while advancing its cursor. Example committed event:

    {
      "version": 1, "roomId": "sector-demo", "seq": 42,
      "eventId": "01900000-0000-7000-8000-000000000042",
      "actorId": "b1", "kind": "activity.changed",
      "observedAt": "2026-01-01T12:00:00Z",
      "receivedAt": "2026-01-01T12:00:01Z",
      "payload": {"activity": "testing", "evidence": "tool", "confidence": "observed"}
    }

The collector persists outgoing IDs before transmitting. The relay assigns seq in the same transaction as append. Scope uniqueness to (roomId, eventId). Return the existing receipt on identical retries. Reject a duplicate key with conflicting content. A room membership is required for every read and write. Bind actor identity from the credential and enrollment server-side. Reject a forged producer or recipient outside the room.

| Kind | Required information | Projection |
|---|---|---|
| actor.upserted | Actor ID, owner, harness, capabilities; optional provider and model | Create/update one bot |
| presence.changed | Online, offline or unknown; relay time/lease | Overlay connectivity |
| activity.changed | Activity, evidence and confidence | Update work animation |
| delegation.observed | Parent, child and evidence of delegation | Draw a real relationship |
| message.queued | Message ID, from, to; visibility policy | Pending mailbox marker |
| message.delivered | Message ID, recipient ACK, sender and recipient | Animate confirmed delivery |
| message.failed / expired / uncertain | Message ID and typed reason | Explain terminal or ambiguous status |
| actor.withdrawn | Actor ID and withdrawal time | Remove from world and stop routing |

Use a room snapshot with a high-water cursor. Subscribe from that cursor. If retention removes it, respond with resync_required and fetch a new snapshot. Commit reducer state and cursor together. Heartbeats are separate from transcript activity. Proposed starting values: heartbeat every 15 s, offline after 45 s without renewal. Quiet but connected sessions remain idle/unknown. Add jitter to reconnects and cap backoff. Bound queues and coalesce transient activity under load; preserve durable message receipts.

## Addressed messages and agent tools

Expose local capabilities conceptually as list_room_agents, send_message(target, body, optionalCommit), and reply(messageId, body). Use per-session authenticated bindings; a Grid-managed tool or supported extension can identify its caller. A pasted user prompt is useful as a manual fallback and must be labeled manual. An observer-only adapter advertises observe=true and receive=false. UI buttons and agent discovery must both respect capabilities.

A send command carries protocol version, room ID, clientMessageId, recipient actor ID, plain-text body, optional replyTo and optional full commit SHA. Derive sender from authentication. Reject self/cross-room loops by default and replies with invalid participants. Store replyTo as an edge between messages; a reply is a new message with its own delivery lifecycle. Record an optional thread root and hop budget to prevent automated ping-pong. Suggested first limits: 8 KiB UTF-8 body, one target, four automatic hops, 24 h queued expiry. Return typed validation errors instead of truncating a message. These are configurable product defaults.

1. Sender collector commits an outbox record locally. Relay validates sender, target, membership, size, room policy, opt-in, rate limit and expiry. In one transaction it accepts the idempotency key, stores the mailbox item, and assigns its queued receipt.
2. Recipient collector leases the item, rechecks opt-in, session identity and target capability. Offline is queueable until expiry. A busy session waits for a safe turn boundary or returns busy. Never run two competing writers against the same interactive thread.
3. Deliver through a documented session interface under the receiving user’s existing controls. Treat received text as untrusted, attributed peer input. Carry commit SHA as context. If the commit is unavailable, report it. Do not automatically checkout, fetch credentials, or run a suggested command on receipt.
4. Mark delivered only after adapter evidence of target acceptance. Atomically persist the receipt locally, then ACK the relay. Repeated ACKs return the same receipt. Relay acceptance alone means queued. Reading a message, finishing a task, and sending a reply are separate facts.
5. Crash after external submission but before durable receipt is ambiguous. Recover by querying a provider-side receipt/idempotency key if supported. Otherwise mark uncertain and request human reconciliation. Silent redelivery risks duplicate model turns and charges. No generic exactly-once claim.
6. A reply uses the same path in reverse. Enforce local user approval settings, budgets and opt-out. No inbound message grants execute permission. Revocation cancels undelivered work, expires active leases and blocks later reads. Record when a provider already accepted a turn.

## Proposed transport and persistence

| Operation | Contract |
|---|---|
| POST /v1/rooms and /v1/invites/redeem | Authenticate creator or redeem once; return scoped identity, room ID and cursor. |
| GET /v1/rooms/{id}/snapshot | Return visible actors, state and consistent seq. |
| GET /v1/rooms/{id}/events?after=N | Ordered, bounded catch-up; signal an expired cursor. Optional WebSocket delivers the same events. |
| POST /v1/rooms/{id}/events | Authenticated collector submits validated public observations only. |
| POST /v1/rooms/{id}/messages | Idempotent enqueue; return message ID and queued status. |
| POST /v1/mailbox/lease and /v1/mailbox/ack | Authenticated recipient device leases pending work and records outcomes. |
| DELETE /v1/rooms/{id}/members/{memberId} | Owner revokes membership and invalidates credentials and subscriptions. |

Relay tables: rooms, members, devices, actor_enrollments, invites (hashed secret, expiry, used_at), events, mailbox, leases and receipts. Index room+sequence, recipient+status+expiry and room+messageId. Bind idempotency to authenticated sender plus clientMessageId. Store only one accepted immutable payload per key. Keep private inbox reads under participant authorization. Persist sender and recipient separately from event actor to avoid reversing the animated direction on an ACK.

Collector tables: local_session_map, workspace_bindings, adapter_checkpoints, outbox, received_messages, adapter_receipts, world_projection and relay_cursor. Use transactions for source checkpoint plus accepted observation, and projection plus last-applied cursor. Store credentials in the OS keystore. Enforce TLS remotely. For Unity-to-collector use OS local IPC or loopback with a per-install secret and origin checks. The only hosted requirement is a reachable relay; provide self-host configuration with a persistent volume and documented backups.

## Data sharing defaults

Room allowlist: pseudonymous actor ID, chosen display name, harness, optional provider/model, coarse activity, timestamps and consented relationship metadata. Explicit per-message content is addressed to its participants. Default exclude prompts, transcripts, reasoning text, command strings/output, diffs, absolute paths, branch names, secrets and provider keys. Do filtering on the collector before serialization, including exception paths and diagnostics. Redact tokens in relay logs. Allow preview of the exact shared envelope. Document retention and purge for room events and message bodies; propose seven days for event replay, erase queued bodies on expiry, and remove delivered bodies after recipient ACK plus a short recovery window. Persist compact receipts for deduplication longer than the retry window. Set and test concrete durations before beta.
