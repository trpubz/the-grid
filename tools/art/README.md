# Art

Every sprite in The Grid is made by one script, `make_sprites.py`. It builds each thing from boxes in Blender, renders it as isometric pixel art, and writes PNGs into `Assets/TheGrid/Resources/Art/`. The PNGs are committed, so building the app does not need Blender. You only need Blender to change the art.

## Render everything

From the repo root (Blender 5.1 or later):

```bash
/Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup --python tools/art/make_sprites.py
```

This rewrites every PNG and `docs/art/contact-sheet.png`, a picture of the whole set. Look at the contact sheet after every change. To see the sprites standing in a city, run **The Grid > Render World Pictures** in Unity and look at `Logs/world-near.png`.

## How it works

- **Flat colour.** Blender's Workbench engine with flat lighting gives every face one colour and no gradients. Smoothing is off, so each pixel is either fully on or fully off.
- **The camera** looks down at 30 degrees, turned 45 degrees, with no perspective. A 1 by 1 square of ground becomes a diamond 64 pixels wide and 32 high: one tile.
- **Three shades per block.** With flat lighting nothing is shaded for you, so `box(...)` takes a colour for the top, the left face and the right face. Light on top, darker to the left, darkest to the right.
- **One palette.** `PALETTE` at the top of the script lists every colour. After rendering, each pixel is moved to its nearest palette colour. `palette.png` is that list as a picture, and a test checks every sprite against it.
- **Glow** is just a thin box in a bright colour. On the ground it follows the buildings, not the tiles: `tile-kerb` and `tile-road-kerb` carry a strip along one edge for the two sides of a street, and `tile-alley` a strip along the other for the gap between two buildings. A strip on every tile makes a grid, and a grid reads as a car park.
- **Buildings are stretched** to one and a half times their modelled height (`TALL`), so they tower over the riders. Write heights as if they were not.
- **Outlands** (`o-*`, `tile-water`) are what lies beyond the plots. Keep them dark and dim: nothing out there may draw the eye from a city.
- **Riders** are drawn in grey with a pure white glow line. The game tints them per account, so the white becomes that account's colour.

## What is where in a picture

| Sprite | Size | The ground point is |
|---|---|---|
| `tile-*` | 64×32 | the centre |
| `b-*` (buildings) | 128×192 | the centre of the 2 by 2 tile footprint, 32 pixels above the bottom edge |
| `o-*` (outlands: stones, outcrop, spire) | 128×192 | as a building |
| `rider-se`, `-ne`, `-nw`, `-sw` | 64×64 | under the bike, 18 pixels above the bottom edge |
| `rider-small` | 32×32 | 9 pixels above the bottom edge |
| `marker` | 12×16 | the bottom edge |

Unity's import settings for these files are set by `Assets/TheGrid/Editor/ArtImport.cs`. Nobody sets them by hand.

## Add or change a building

1. Write a function like the others, starting with `slab()` for the ground:

   ```python
   def b_tower():
       slab()
       box(0, 0, .04, 1.0, 1.0, 1.8, *BODY)       # centre x, centre y, base height, width, depth, height, colours
       trim(0, 0, 1.84, 1.0, 1.0, CYAN)           # a glow line round the roof
       windows_front(0, -.5, .6, 3, AMBER)
   ```

2. Add it to `BUILDINGS`.
3. Render and look at the contact sheet.
4. Add its name to the list in `Assets/TheGrid/Tests/ArtTests.cs`.

Keep a building inside 2 by 2 units of ground and under about 2.2 units tall, or it will be cut off.
