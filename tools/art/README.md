# Art

Every sprite in The Grid is made by one command. It builds each thing from simple solids in Blender, renders it as isometric pixel art, and writes PNGs into `Assets/TheGrid/Resources/Art/`. The PNGs are committed, so building the app does not need Blender. You only need Blender to change the art.

What the art should look like is in [docs/art-direction.md](../../docs/art-direction.md). Read that first.

## Render everything

From the repo root (Blender 5.1 or later):

```bash
/Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup --python tools/art/make_sprites.py
```

This rewrites every PNG, `sprites.json` beside them, and `docs/art/contact-sheet.png`, a picture of the whole set. Look at the contact sheet after every change. Rendering the same script twice gives the same files, byte for byte.

## The files

| File | What is in it |
|---|---|
| `kit.py` | The camera, the palette, and the building blocks: `box`, `light`, `disc`, `ball`, `limb`, `glow`, and `render` |
| `ground.py` | Paving, the lane, the void, lines of light, the fountain, city blocks |
| `buildings.py` | The six buildings |
| `bots.py` | Bots in every pose and facing, the courier's bike, badges, the pulse |
| `make_sprites.py` | The entry point. It also still holds the first sprite set, which goes when the world stops using it |
| `studies/` | Renders made while settling the art direction. Not part of the set |

## How it stays pixel art

- **Flat colour.** Blender's Workbench engine with flat lighting gives every face one colour and no gradients. Smoothing is off, so each pixel is either fully on or fully off.
- **The camera** looks down at 30 degrees, turned 45 degrees, with no perspective. A 1 by 1 square of ground becomes a diamond 64 pixels wide and 32 high: one tile. That grain is fixed. Detail comes from what you build at it, never from more pixels.
- **Three shades per block.** Nothing is shaded for you, so `box(...)` takes a colour for the top, the left face and the right face. Light on top, darker to the left, darkest to the right.
- **One palette.** `PALETTE` in `kit.py` names every colour, and sprites use the names. After rendering, each pixel is moved to its nearest palette colour. `palette.png` is that list as a picture.
- **Light** is a thin solid in a bright colour: `light(...)`. Where it lies on the ground it is flat bands, `glow(...)`, never a fade.
- **White is for tinting.** A bot's visor and suit lines, and the line along a bike, are pure white. The game multiplies each bot by its account's colour, so the white becomes that colour.

## The manifest

`sprites.json` says what every sprite is: its size, and its ground point, the pixel that sits on the spot the sprite stands at, counted from the bottom left. `render(name, width, height, ground_y)` writes the entry; the ground point is always at the horizontal centre.

Unity's import settings are set by `Assets/TheGrid/Editor/ArtImport.cs`, which takes each ground point from the manifest. The tests in `Assets/TheGrid/Tests/ArtTests.cs` take what they expect from it too: every picture is in the manifest, is the size it says, uses only palette colours, has no half-transparent pixel, and is not cut off at the edge of its picture. So adding a sprite needs no change in either file.

## Add or change a building

1. In `buildings.py`, write a function like the others. Start from `footing`, which gives the strip of light and its glow, then `shaft` for each block, `roof` for the top:

   ```python
   def depot():
       """Told by: ..."""
       w, h, lit = 2.4, 2.0, "cyan"
       z = footing(w, w, lit)                 # returns where the walls start
       shaft(0, 0, w, w, z, h - z, lit)       # centre x, centre y, width, depth, base, height, light
       roof(0, 0, w, w, h, lit)
       entrance(.4, -w / 2, lit)
       render("b-depot", 256, 256, 80)
   ```

2. Call it from `make()`.
3. Render and look at the contact sheet. A building carries one light: pick `"cyanw"`, `"cyan"` or `"violet"` and use nothing else on it.

Keep the building, and the glow round it, inside its picture. The tests fail if a pixel touches the edge.

## Add a pose

In `bots.py`, add the name to `POSES` and handle it in `bot()`, which is modelled facing +x. Every pose is rendered in four facings by turning the model, so say which arm acts with `near`, the side the camera sees, or the pose will be hidden from behind. The tests fail if two pictures of a bot come out the same.
