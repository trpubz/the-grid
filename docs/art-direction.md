# Art direction

How The Grid should look. This is the target every sprite and every screen is judged against. It was set by the project's owner on 2026-11-11 and replaces the look of the first sprite set (navy boxes, riders on bikes, stones and lakes).

Three studies made with the project's own sprite kit show the direction. They are studies, not the shipped art, and they are older than rules 6 to 8 below: the shipped set, with its depth and its stockier bots, is on `docs/art/contact-sheet.png`.

![A server tower and bots at the game's pixel density](art/direction-tower.png)
![Bots, close up](art/direction-bots.png)
![Light in long lines, on a building and on the ground](art/direction-light.png)

## The brief

The Grid is a virtual city where each repository occupies a sector and each coding-agent session appears as a small helmeted bot.

**Camera and rendering.** Drawn like a classic 16-bit isometric management game. A fixed 2:1 isometric view, looking down on a compact, playable district. Chunky pixels, stepped diagonal edges, crisp silhouettes, distinct light and shadow faces. A sector is framed close enough to follow one bot from a doorway to a workstation.

**Materials and lighting.** The city is built from smooth, poured blue-gray concrete, dark graphite panels, and smoked glass. Pavement is broad and uninterrupted, with an occasional expansion joint. The buildings illuminate the paths: cyan-white strips are recessed under entrances, overhangs and foundation edges, and their light spills a short distance onto the concrete. The city is sterile: no cobblestones, trees, flowers, planters or freestanding streetlamps. Light is primarily blue, teal and white; another colour is allowed, but the lights on any one building are all the same colour.

**Where the detail goes.** Each building has a recognizable job. The server tower has cooling fans and a service ladder, the relay a compact antenna, the test workshop a cutaway wall so a bot can be seen at its terminal. Walking surfaces stay quiet. A small violet light fountain is the plaza's landmark, and a cyan lane runs round the perimeter.

**Characters and activity.** Bots are roughly two heads tall, with oversized, fully enclosed helmets, short legs and chunky boots. Their actions are legible through pose: a courier leans over a bike, a debugger inspects a disc, a relay touches its helmet receiver. An amber question badge marks an agent waiting on the user; a brief violet pulse marks a delivered message.

**Composition check.** At normal game zoom it must be possible to spot the two waiting bots, follow the courier's route, and identify the workshop by sight. The camera stays on the playable sector. No cinematic skylines, distant horizons, heavy bloom or photorealistic rendering.

## Rules added while studying it

1. **Pixel density is fixed.** A ground tile is 64 pixels wide and 32 high, the size classic tycoon games use. Detail comes from what is built at that grain, never from more pixels per tile. The world is looked at two or three screen pixels to the art pixel.
2. **Bots are people, not robots.** A bot is a person in a dark suit under its helmet, stocky, about 32 pixels tall. At that size it is read from a few large shapes, not from fine lines: a large faceted helmet, a thick visor that wraps the face, a dark compact body, slab arms, chunky boots.
3. **On buildings, light runs in long lines** up the corners and sides, unbroken from foot to roof and on round the roof edge. Windows are secondary to those lines.
4. **The ground is lit the same way:** dark paving in large squares, with long lines of light along kerbs, lane edges and the outlines of platforms, and their glow lying on the paving beside them. Glow is flat bands in palette colours, never a gradient.
5. **State is shown on the bot, not by a building.** Buildings stand only for kinds of work, plus the relay. Waiting, faulted, done and offline are a badge and a pose on the bot, wherever it stands.
6. **Nothing that stands is flat.** Every large surface is panelled, roofs and floors as well as walls. Things cast shadows, and the shadows are soft: one tone down at their edge, two at most. A light casts no shadow and is never shaded. The walls of a room we look into cast none; what stands inside does. Each thing has a dark outline, and a light line where a face sits over a darker one. Walls are concrete, lighter than the graphite they stand on. A lit window is the colour of its building's lines of light, and no line of light runs through a window. The ground itself stays flat.
7. **A bot carries a ring of light on its back.** The bot that inspects holds that disc in its hand, and its back is bare.
8. **A bot's light comes in blue, white and orange.** In the picture it is three marker colours (the light, its highlight, its dim partner), and the game swaps them. Orange is redder than the amber of a badge, so a badge is still the only amber thing on a bot.
9. **Sectors stand apart.** Each has its own lane round it. Between one sector's lane and the next's lies open, dark ground that belongs to neither, crossed by one short link of road on each side. Seen from far out, every sector can be told from its neighbours at a glance.
10. **Sectors differ.** Every sector has the same six buildings and the fountain. Where they stand differs from sector to sector, and so does which of its buildings carry an accent light (rule 12). The fountain is violet everywhere. A sector's arrangement follows from its place on the map, so it looks the same every time, and unlike every sector round it.
11. **In every arrangement no bot is hidden.** Tall buildings stand along the back; the two low ones stand far enough forward that a bot at a back door is seen over their roofs; bots that need the owner stand in the open at the front, seen whole. `LayoutTests` proves it for every arrangement and every neighbour.

12. **A region has a light of its own.** The sectors of one workspace stand together as a region. Each region has a look: a primary light, carried by its lanes, the lines on its ground and at least half the buildings of every sector, and two accent lights, each carried by one or two buildings of a sector. All the lights on one building are still one colour. The looks are cyan, white, rose, mint, violet, blue and ember, in the order workspaces take them; the owner can give a region another. No look is amber or red: amber means a bot needs the owner, red a fault. `Lights.cs` has the colours.
13. **Only a sector gives off its region's colour.** Open ground, the dashes on it, the dark blocks beyond the map and the roads between regions look the same everywhere, in the light the ground is drawn in. Between two regions lies open ground at least one sector across, with nothing standing on it, wider than the ground between two sectors of one region.

## What stays from before

- Everything shown is true. Where a bot stands and what it is doing follows from the roster snapshot; nothing moves unless something really changed.
- One fixed palette, point filtering, no half-transparent pixels, whole-number scaling.
- All art is original and made by `tools/art/make_sprites.py`. Reference pictures are for how light and material behave only; no design or name is taken from them, and they are not kept in this repo.

## Words

In code and in the window: a repo's area is a **sector**, a workspace's group of sectors is a **region**, a session's figure is a **bot**, the road round a sector is the **lane**, the vehicle is a **bike**.
