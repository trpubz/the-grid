"""The six buildings, each for one kind of work. See docs/art-direction.md.

Every building is graphite and smoked glass, and carries one light. That light is a recessed strip round the footing with
its glow on the paving, a line up each corner the camera sees and up each side, unbroken from footing to roof, and on
round the roof edge. What tells one building from another is its shape and one or two things only it has.
"""
import math

import bpy

import kit
from kit import PALETTE, box, disc, glow, light, mat, render

# One light a building: the bright line for corners and strips, its dimmer partner for sides, and its glow on the ground.
LIGHTS = {"cyanw": ("cyanw", "cyan", "cyan"), "cyan": ("cyan", "teal", "cyan"), "violet": ("violetw", "violet", "violet")}
WALL = ("c0", "g4", "g1")       # concrete over graphite: top, left face, right face
TRIM = ("c1", "g5", "g2")


def footing(w, d, lit):
    """The glow on the paving, a footing with the strip of light recessed in it, and a plinth overhanging the strip."""
    bright, _, family = LIGHTS[lit]
    glow(0, 0, w + .16, d + .16, family)
    box(0, 0, 0, w + .16, d + .16, .1, "g3", "g1", "g0")
    light(0, -d / 2 - .08, .1, w + .16, .03, .07, bright); light(w / 2 + .08, 0, .1, .03, d + .16, .07, bright)
    box(0, 0, .17, w + .2, d + .2, .16, *TRIM)
    return .33      # where the walls start


def shaft(cx, cy, w, d, z0, h, lit, sides=2):
    """A block of the building, with the long lines: up both ends of each face we see, and `sides` more between them."""
    bright, dim, _ = LIGHTS[lit]
    f, r = cy - d / 2, cx + w / 2
    box(cx, cy, z0, w, d, h, *WALL)
    for x in (cx - w / 2 + .035, cx + w / 2 - .035):
        light(x, f - .02, z0, .07, .04, h, bright)
    for y in (cy - d / 2 + .035, cy + d / 2 - .035):
        light(r + .02, y, z0, .04, .07, h, bright)
    for i in range(sides):
        t = (i + 1) / (sides + 1) - .5
        light(cx + t * w, f - .02, z0, .035, .03, h, dim); light(r + .02, cy + t * d, z0, .03, .035, h, dim)


def roof(cx, cy, w, d, z, lit):
    """A parapet with the line of light running on round its two near edges."""
    bright = LIGHTS[lit][0]
    box(cx, cy, z, w + .12, d + .12, .14, *TRIM); light(cx, cy, z + .14, w - .1, d - .1, .002, "g3")
    light(cx, cy - d / 2 - .07, z + .06, w + .14, .03, .05, bright); light(cx + w / 2 + .07, cy, z + .06, .03, d + .14, .05, bright)
    return z + .14


def entrance(x, f, lit, w=.6):
    """A doorway lit from within, a step, and a canopy with a strip beneath it."""
    bright = LIGHTS[lit][0]
    box(x, f - .14, 0, w + .3, .28, .06, "pv2", "g3", "g2")
    light(x, f - .008, .33, w + .12, .03, .78, "k1"); light(x, f - .012, .33, w, .02, .7, bright)
    box(x, f - .2, 1.13, w + .35, .4, .07, *TRIM); light(x, f - .39, 1.1, w + .35, .012, .03, bright)


def glazing(cx, cy, w, d, z0, floors, per_floor, lit, every=5):
    """Smoked glass in panes, a few of them lit from inside in the building's dim light. Secondary to the lines."""
    pane = "violet" if lit == "violet" else "teal"      # dimmer than any line on the building
    f, r = cy - d / 2, cx + w / 2
    for floor in range(floors):
        z = z0 + floor * .43
        for i in range(per_floor):
            t = (i + .5) / per_floor - .5
            c = pane if (floor * 7 + i * 3) % every == 0 else "glass"
            light(cx + t * (w - .5), f - .012, z, (w - .5) / per_floor * .8, .02, .26, c)
            light(r + .012, cy + t * (d - .5), z, .02, (d - .5) / per_floor * .8, .26, c)


def fan(cx, cy, z):
    box(cx, cy, z, 1.0, 1.0, .16, "g4", "g3", "g2")
    disc(cx, cy, z + .16, .44, .05, "g5", "g3"); disc(cx, cy, z + .21, .37, .005, "k0")
    for a in range(3):          # blades: boxes turned about the hub
        bpy.ops.mesh.primitive_cube_add(size=1, location=(cx, cy, z + .22))
        o = bpy.context.object
        o.scale = (.7, .09, .008); o.rotation_euler = (0, 0, math.radians(a * 60 + cx * 40 + cy * 25))
        o.data.materials.append(mat(PALETTE["g4"]))
    disc(cx, cy, z + .225, .09, .02, "c1", "g4")


def server_tower():
    """Reading and searching. Told by: four roof fans, a service ladder, racks behind smoked glass."""
    w, h, lit = 2.5, 5.0, "cyanw"
    z = footing(w, w, lit)
    glazing(0, 0, w, w, .95, 9, 6, lit)
    shaft(0, 0, w, w, z, h - z, lit)
    entrance(-.62, -w / 2, lit)
    top = roof(0, 0, w, w, h, lit)
    for fx, fy in ((-.6, -.58), (.6, -.58), (-.6, .6), (.6, .6)):
        fan(fx, fy, top)
    lx = w / 2 + .07            # the ladder: graphite, it is not a light
    for ly in (-1.0, -.78):
        box(lx, ly, z, .035, .035, h - .1, "c0", "g5", "g3")
    for i in range(22):
        box(lx, -.89, .5 + i * .21, .03, .22, .025, "c1", "g5", "g3")
    render("b-server-tower", 256, 352, 80)


def review_hall():
    """Reviewing. Told by: two blocks joined by a bridge at the top."""
    w, h, lit = 2.5, 3.4, "violet"
    z = footing(w, w, lit)
    for x in (-.8, .8):
        glazing(x, 0, .9, w, .9, 5, 2, lit, every=4)
        shaft(x, 0, .9, w, z, h - z, lit, sides=1)
        roof(x, 0, .9, w, h, lit)
    box(0, -.2, 2.5, .72, 1.5, .5, *TRIM)                                            # the bridge
    light(0, -.96, 2.5, .72, .02, .05, "violetw"); light(0, -.96, 2.95, .72, .02, .05, "violetw")
    for i in range(3):
        light(-.2 + i * .2, -.955, 2.66, .12, .02, .2, "violet")
    entrance(-.8, -w / 2, lit, w=.5)
    render("b-review-hall", 256, 288, 80)


def works():
    """Working, when it cannot be told on what. Told by: a plain shed with one stack."""
    w, h, lit = 2.4, 1.5, "cyan"
    z = footing(w, w, lit)
    shaft(0, 0, w, w, z, h - z, lit, sides=1)
    top = roof(0, 0, w, w, h, lit)
    box(.45, .45, top, 1.1, 1.1, .35, *TRIM)                                          # a plant room
    box(-.6, .6, top, .34, .34, .12, "g5", "g4", "g3")                                # the stack and its collar
    disc(-.6, .6, top + .12, .13, .72, "g5", "g3"); disc(-.6, .6, top + .84, .16, .05, "c0", "g4")
    light(-.6, .6, top + .89, .1, .1, .04, "cyan")
    entrance(.5, -w / 2, lit)
    for i in range(3):
        light(w / 2 + .012, -.5 + i * .5, .7, .02, .28, .22, "amber")                 # someone is in: warm windows
    render("b-works", 256, 224, 80)


def relay():
    """Where couriers leave from and arrive. Told by: a kiosk under a mast with three rings."""
    w, h, lit = 1.5, 1.0, "cyanw"
    z = footing(w, w, lit)
    shaft(0, 0, w, w, z, h - z, lit, sides=0)
    box(0, 0, h, w + .26, w + .26, .12, *TRIM)                                        # an overhang, lit from beneath
    light(0, -w / 2 - .14, h - .05, w + .26, .02, .05, "cyanw"); light(w / 2 + .14, 0, h - .05, .02, w + .26, .05, "cyanw")
    light(.2, -w / 2 - .012, z, .4, .02, .5, "cyanw")                                 # a hatch
    box(-.2, .2, h + .12, .6, .6, .3, "g4", "g3", "g2")
    box(-.2, .2, h + .42, .07, .07, 1.7, "g5", "g3", "g2")                            # the mast
    for dz, r in ((.55, .27), (1.0, .2), (1.4, .13)):
        disc(-.2, .2, h + .42 + dz, r, .045, "c0", "g4")
    light(-.2, .2, h + 2.12, .12, .12, .12, "cyanw")
    render("b-relay", 192, 256, 64)


def assembly_bay():
    """Editing. Told by: a wide low hall, one large lit door, a gantry across the roof."""
    w, d, h, lit = 3.4, 2.4, 1.4, "cyan"
    z = footing(w, d, lit)
    shaft(0, 0, w, d, z, h - z, lit, sides=0)
    f = -d / 2
    light(-.3, f - .012, z, 1.9, .02, .82, "cyan")                                    # the door: one sheet of light, slatted
    for i in range(5):
        light(-.3, f - .016, z + .12 + i * .15, 1.9, .012, .03, "teal")
    box(-.3, f - .05, z + .82, 2.1, .1, .08, *TRIM)
    for i in range(4):
        light(w / 2 + .012, -.75 + i * .5, .7, .02, .3, .24, "amber")
    top = roof(0, 0, w, d, h, lit)
    for x in (-1.5, 1.5):                                                             # the gantry
        box(x, 0, top, .12, .12, .55, "g5", "g4", "g3")
    box(0, 0, top + .55, 3.12, .16, .1, "c0", "g5", "g3")
    box(.5, 0, top + .33, .26, .26, .22, "g5", "g4", "g3"); box(.5, 0, top + .2, .04, .04, .13, "c1")
    render("b-assembly-bay", 288, 224, 88)


def test_workshop():
    """Running tests. Told by: the two near walls cut to a kerb, and inside a terminal with a wall screen, and a bench.
    A bot stands at the middle of the floor, facing the screen; a second by the bench."""
    w, d, h, lit = 3.4, 2.4, 1.0, "cyanw"      # low walls: bots stand at the works right behind it, and must be seen whole
    glow(0, 0, w + .16, d + .16, "cyan")
    box(0, 0, 0, w + .16, d + .16, .1, "g3", "g1", "g0")
    light(0, -d / 2 - .08, .1, w + .16, .03, .07, "cyanw"); light(w / 2 + .08, 0, .1, .03, d + .16, .07, "cyanw")
    box(0, 0, .17, w + .2, d + .2, .08, *TRIM)
    light(0, 0, .25, w - .1, d - .1, .003, "pv2")                                     # the floor inside, lit
    kit.SHADOWLESS = True       # we look into this room: its walls cast no shadow, only what stands inside does
    box(0, d / 2 - .07, .25, w, .14, h, *WALL); box(-w / 2 + .07, 0, .25, .14, d, h, *WALL)     # the two far walls stand
    kit.SHADOWLESS = False
    light(-w / 2 + .15, -d / 2 + .1, .25, .03, .04, h, "cyanw"); light(w / 2 - .035, d / 2 - .15, .25, .07, .04, h, "cyanw")
    light(0, d / 2 - .15, .25 + h, w, .03, .05, "cyanw"); light(-w / 2 + .15, 0, .25 + h, .03, d, .05, "cyanw")
    box(0, -d / 2 + .06, .25, w, .12, .22, *TRIM); box(w / 2 - .06, 0, .25, .12, d, .22, *TRIM)  # the near ones are cut to a kerb
    light(0, -d / 2 + .06, .47, w, .12, .01, "c0"); light(w / 2 - .06, 0, .47, .12, d, .01, "c0")
    box(0, d / 2 - .18, .6, 1.5, .05, .56, "k1"); light(0, d / 2 - .21, .65, 1.38, .02, .46, "teal")   # the wall screen
    light(.42, d / 2 - .225, .72, .34, .012, .3, "cyanw"); light(-.3, d / 2 - .225, .95, .7, .012, .05, "cyan")
    light(-.3, d / 2 - .225, .83, .5, .012, .05, "cyan")
    box(0, d / 2 - .5, .25, .9, .3, .4, "g5", "g3", "g2"); light(0, d / 2 - .52, .65, .7, .2, .02, "cyan")   # the terminal
    box(1.15, .45, .25, .7, .5, .4, "g5", "g3", "g2"); light(1.15, .43, .65, .5, .3, .02, "teal")            # the bench
    box(-1.2, .5, .25, .4, .4, .55, "g4", "g3", "g2"); box(-1.2, -.5, .25, .35, .35, .3, "g5", "g3", "g2")   # crates
    render("b-test-workshop", 288, 224, 88)


def make():
    server_tower(); review_hall(); works(); relay(); assembly_bay(); test_workshop()
