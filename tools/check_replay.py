"""Check the canonical synthetic exchange. Run with Python 3, without -O."""
import json
from pathlib import Path

root = Path(__file__).resolve().parents[1]
data = json.loads((root / "Assets/TheGrid/Resources/grid-demo.json").read_text())
actors = {a["id"] for a in data["actors"]}
assert data["version"] == 1 and len(actors) == len(data["actors"]) == 2
seen, queued, delivered, status = set(), {}, set(), {}
for seq, event in enumerate(data["events"], start=1):
    assert event["seq"] == seq and event["id"] not in seen
    seen.add(event["id"])
    actor = event["actor"]
    assert actor in actors
    kind = event["type"]
    if kind == "message.queued":
        mid, target = event["messageId"], event["target"]
        assert mid not in queued and target in actors and actor != target
        if "replyTo" in event:
            parent = event["replyTo"]
            assert parent in delivered and queued[parent] == (target, actor)
        queued[mid] = actor, target
    elif kind == "message.delivered":
        mid = event["messageId"]
        assert mid not in delivered and queued[mid] == (event["target"], actor)
        delivered.add(mid)
    elif kind in ("activity.changed", "presence.changed"):
        status[actor, kind] = event["value"]
    else:
        raise ValueError(kind)
assert delivered == {"m1", "m2"}
assert status["a1", "activity.changed"] == "reviewing"
assert status["b1", "presence.changed"] == "offline"
print(f"PASS: {len(actors)} actors, {len(seen)} events, two correlated deliveries.")
