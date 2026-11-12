"""The Grid sprite factory.

Renders every sprite as isometric pixel art and writes PNGs into Assets/TheGrid/Resources/Art, with sprites.json beside
them saying what each one is. Run from the repo root:

    /Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup --python tools/art/make_sprites.py

The camera, the palette and the building blocks are in kit.py; what the art should look like is in docs/art-direction.md.
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import kit
import ground
import buildings
import bots

os.makedirs(kit.OUT, exist_ok=True)
kit.write_palette()
ground.make()
buildings.make()
bots.make()
kit.write_manifest()
kit.contact_sheet()
print("SPRITES:", ", ".join(f"{n} {p.shape[1]}x{p.shape[0]}" for n, p, _ in kit.SPRITES))
