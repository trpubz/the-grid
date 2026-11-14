"""Check that Unity's .meta files match what is tracked under Assets. Run with Python 3, without -O.

Unity keeps a .meta beside every file and folder under Assets. One that is missing gets made anew on the next machine, with
a new id, and whatever pointed at the old id is lost; one left behind makes Unity warn and delete it.
"""
import subprocess
from pathlib import Path


def problems(tracked):
    def seen(path):   # by Unity
        return not any(part.startswith(".") or part.endswith("~") for part in path.split("/")[1:])

    paths = {p for p in tracked if p.startswith("Assets/") and seen(p)}
    metas = {p for p in paths if p.endswith(".meta")}
    things = set()   # the files, and every folder above one
    for path in paths - metas:
        while path != "Assets":
            things.add(path)
            path = path.rsplit("/", 1)[0]
    return sorted(["no .meta: " + t for t in things if t + ".meta" not in metas]
                  + ["nothing beside: " + m for m in metas if m[:-len(".meta")] not in things])


# What Unity does not see has no .meta: a name that starts with a dot or ends with a tilde.
assert problems(["Assets/A.cs", "Assets/A.cs.meta"]) == []
assert problems(["Assets/A.cs"]) == ["no .meta: Assets/A.cs"]
assert problems(["Assets/A.cs.meta"]) == ["nothing beside: Assets/A.cs.meta"]
assert problems(["Assets/Dir/A.cs", "Assets/Dir/A.cs.meta"]) == ["no .meta: Assets/Dir"]
assert problems(["Assets/Dir.meta", "Assets/Dir/A.cs", "Assets/Dir/A.cs.meta"]) == []
assert problems(["Assets/.hidden/A.cs", "Assets/Old~/A.cs", "Assets/.keep"]) == []
assert problems(["README.md", "Packages/manifest.json"]) == []

if __name__ == "__main__":
    root = Path(__file__).resolve().parents[1]
    tracked = subprocess.run(["git", "ls-files", "-z", "Assets"], cwd=root, check=True, capture_output=True, text=True).stdout.split("\0")
    found = problems([p for p in tracked if p])
    for line in found:
        print(line)
    assert not found, f"{len(found)} .meta problems"
    print("PASS: every file and folder under Assets has its .meta, and every .meta its file or folder.")
