# Claude Code fixtures

Hand-written records in the shapes found by the idle-detection spike (`docs/spike-idle-detection.md`). No real session data.

- `agents.json`: the shape of `claude agents --json`, one row per state seen or documented.
- `transcript-records.jsonl`: one session ending on an unanswered question, with an outgoing message, a peer reply, and a subagent report (`origin.handback`).
