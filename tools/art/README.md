# Art

Every sprite in The Grid is made by one command. It builds each thing from simple solids in Blender, renders it as isometric pixel art, and writes PNGs into `Assets/TheGrid/Resources/Art/`. The PNGs are committed, so building the app does not need Blender. You only need Blender to change the art.

What the art should look like is in [docs/art-direction.md](../../docs/art-direction.md). Read that first.

## Render everything

From the repo root (Blender 5.1 or later):

```bash
/Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup --python tools/art/make_sprites.py
```

This rewrites every PNG, `sprites.json` beside them, and `docs/art/contact-sheet.png`, a picture of the whole set. Look at the contact sheet after every change. Rendering the same script twice on one machine gives the same files, byte for byte. The shadow pass uses Blender's EEVEE engine, which needs a graphics card; on another machine a shadow's edge may fall a pixel differently.

## The files

| File | What is in it |
|---|---|
| `kit.py` | The camera, the palette, the building blocks (`box`, `light`, `disc`, `ball`, `limb`, `glow`), and `render`, which also gives what stands its depth |
| `ground.py` | Paving, the lane, the void, lines of light, the fountain, city blocks |
| `buildings.py` | The six buildings, the opened pictures of the three a bot works in (with the floors and stairs of the two tall ones), and the crates that stand at the depot |
| `bots.py` | Bots in every pose and facing, the courier's bike, badges, the pulse |
| `make_sprites.py` | The entry point |

## How it stays pixel art

- **Flat colour.** Blender's Workbench engine with flat lighting gives every face one colour and no gradients. Smoothing is off, so each pixel is either fully on or fully off.
- **The camera** looks down at 30 degrees, turned 45 degrees, with no perspective. A 1 by 1 square of ground becomes a diamond 64 pixels wide and 32 high: one tile. That grain is fixed. Detail comes from what you build at it, never from more pixels.
- **Three shades per block.** Nothing is shaded for you, so `box(...)` takes a colour for the top, the left face and the right face. Light on top, darker to the left, darkest to the right.
- **One palette.** `PALETTE` in `kit.py` names every colour, and sprites use the names. After rendering, each pixel is moved to its nearest palette colour. `palette.png` is that list as a picture.
- **Light** is a thin solid in a bright colour: `light(...)`. Where it lies on the ground it is flat bands, `glow(...)`, never a fade.
- **A bot's light is three marker colours.** White is the light itself, cyan-white (`cyanw`) its highlight, teal its dim partner. The game swaps the three for the bot's colourway: blue, white or orange. A bot that is off shows none of them.

## How it gets depth

Ground tiles and the lines of light laid on them are flat, edge to edge. Everything that stands (buildings, the fountain, blocks, bots, bikes) gets four more things, all inside the palette:

- **Soft shadows.** `render` takes the picture three times. The flat render says what each pixel is. The same wide sun, rendered with and without shadows, says how much light each pixel lost. In half shadow a pixel moves one tone down its own ramp (`RAMP` for graphite and concrete, `PAVING` for floors); in full shadow, two. A face the sun never reaches gets no cast shadow.
- **Lights cast no shadow and take none.** A colour listed in `LIGHTS` keeps its colour everywhere. The walls of a room we look into cast none either: set `kit.SHADOWLESS = True` while you build them, as `test_workshop` does.
- **Panels.** `box` puts a seam, one tone down, between panels and at every storey of any large face, the roof included.
- **Edges.** A light line where a face sits over a clearly darker one, and the darkest colour round the silhouette.

To build something flat, set `kit.DEPTH = False` first and back to `True` after, as `ground.make` does for tiles.

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
3. Render and look at the contact sheet. Every building is drawn in the same light: `"cyanw"` lines, `"cyan"` dimmer lines, `"teal"` windows, cyan glow. The game swaps those six colours for another family's (`Assets/TheGrid/Core/Lights.cs`): the primary light of the sector's region, or one of its accents. It does the same to the lines of light on the ground and the chevrons on a lane. So use no other light colour on a building, or the swap will leave it behind. A new family's colours go in `PALETTE` in `kit.py` as well, though no picture is drawn in them. Keep windows clear of the lines `shaft` draws up a wall. If the building is taller than before, raise its number in `Layout.Wall`; a test tells you.

Keep the building, and the glow round it, inside its picture. The tests fail if a pixel touches the edge.

The game stands bots on the floors of an opened tall building and walks them up its stairs, from numbers of its own (`Layout.StoreyPixels`, `Layout.FloorReach` and the stairs' ends in `Assets/TheGrid/Core/Layout.cs`). If you move a floor or a stair (`STOREY`, `REACH`, `upper_floor`, `stair`), change those too; a test fails if a bot on an upper floor has no floor edge in front of it.

## Add a pose

In `bots.py`, add the name to `POSES` and handle it in `bot()`, which is modelled facing +x. Every pose is rendered in four facings by turning the model, so say which arm acts with `near`, the side the camera sees, or the pose will be hidden from behind. The tests fail if two pictures of a bot come out the same.
