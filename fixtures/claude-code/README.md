# Claude Code fixtures

Hand-written records in the shapes found by the idle-detection spike (`docs/spike-idle-detection.md`). No real session data.

- `agents.json`: the shape of `claude agents --json`, one row per state seen or documented.
- `transcript-records.jsonl`: one session ending on an unanswered question, with an outgoing message, a peer reply, and a subagent report (`origin.handback`).
- `messages/sender.jsonl`, `messages/recipient.jsonl`, `messages/desktop.jsonl`: three sessions that message each other. Between them: a message received between turns, one received mid-turn (an attachment), a reply, a message to a desktop-app session that has no shared id, two sends to the sender's own subagents and a subagent report. The review pile must list four messages from these.
