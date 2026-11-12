"""The six buildings, each for one kind of work. See docs/art-direction.md.

Every building is graphite and smoked glass, and carries one light. That light is a recessed strip round the footing with
its glow on the paving, a line up each corner the camera sees and up each side, unbroken from footing to roof, and on
round the roof edge. What tells one building from another is its shape and one or two things only it has.

Every building is drawn in the same light: white lines, cyan dimmer lines, teal windows, cyan glow. The game swaps those
six colours for another family's, so a kind of building is lit differently from sector to sector (Core/Lights.cs). Use
no other light colour on a building, or the swap will leave it behind.
"""
import math

import bpy

import kit
from kit import PALETTE, box, disc, glow, light, mat, render

# One light a building: the bright line for corners and strips, its dimmer partner for sides, and its glow on the ground.
LIGHTS = {"cyanw": ("cyanw", "cyan", "cyan")}
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
    pane = "teal"                                       # dimmer than any line on the building
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
    w, h, lit = 2.5, 3.4, "cyanw"
    z = footing(w, w, lit)
    for x in (-.8, .8):
        glazing(x, 0, .9, w, .9, 5, 2, lit, every=4)
        shaft(x, 0, .9, w, z, h - z, lit, sides=1)
        roof(x, 0, .9, w, h, lit)
    box(0, -.2, 2.5, .72, 1.5, .5, *TRIM)                                            # the bridge
    light(0, -.96, 2.5, .72, .02, .05, "cyanw"); light(0, -.96, 2.95, .72, .02, .05, "cyanw")
    for i in range(3):
        light(-.2 + i * .2, -.955, 2.66, .12, .02, .2, "teal")
    entrance(-.8, -w / 2, lit, w=.5)
    render("b-review-hall", 256, 288, 80)


def works():
    """Working, when it cannot be told on what. Told by: a plain shed with one stack."""
    w, h, lit = 2.4, 1.5, "cyanw"
    z = footing(w, w, lit)
    shaft(0, 0, w, w, z, h - z, lit, sides=1)
    top = roof(0, 0, w, w, h, lit)
    box(.45, .45, top, 1.1, 1.1, .35, *TRIM)                                          # a plant room
    box(-.6, .6, top, .34, .34, .12, "g5", "g4", "g3")                                # the stack and its collar
    disc(-.6, .6, top + .12, .13, .72, "g5", "g3"); disc(-.6, .6, top + .84, .16, .05, "c0", "g4")
    light(-.6, .6, top + .89, .1, .1, .04, "cyan")
    entrance(.5, -w / 2, lit)
    for y in (-.75, -.3, .3, .75):                                                    # windows in the building's own light,
        light(w / 2 + .012, y, .7, .02, .28, .22, lit)                                # clear of the line up the middle of the wall
    render("b-works", 256, 224, 80)


def opened(w, d, h, lit):
    """A building while a bot works in it, opened the way the test workshop always is: its footing, a lit floor, its two
    far walls standing to their full height, its two near ones cut to a kerb, and a line of light up each cut edge and
    along the top of each far wall. What it is for goes inside, along the far walls; the middle of the floor is the bots'."""
    glow(0, 0, w + .16, d + .16, "cyan")
    box(0, 0, 0, w + .16, d + .16, .1, "g3", "g1", "g0")
    light(0, -d / 2 - .08, .1, w + .16, .03, .07, lit); light(w / 2 + .08, 0, .1, .03, d + .16, .07, lit)
    box(0, 0, .17, w + .2, d + .2, .08, *TRIM)
    light(0, 0, .25, w - .1, d - .1, .003, "pv2")                                     # the floor inside, lit
    kit.SHADOWLESS = True       # we look into this room: its walls cast no shadow, only what stands inside does
    box(-w / 2 + .07, 0, .25, .14, d, h - .25, *WALL)
    kit.SHADOWLESS = False
    light(-w / 2 + .15, -d / 2 + .1, .25, .03, .04, h - .25, lit); light(-w / 2 + .15, 0, h, .03, d, .05, lit)
    box(0, -d / 2 + .06, .25, w, .12, .22, *TRIM); box(w / 2 - .06, 0, .25, .12, d, .22, *TRIM)  # the near ones are cut to a kerb
    light(0, -d / 2 + .06, .47, w, .12, .01, "c0"); light(w / 2 - .06, 0, .47, .12, d, .01, "c0")


def back_wall(cx, w, d, h, lit, right_edge=True):
    """A length of an opened building's back wall, with the line of light along its top and, where it ends the wall, up
    its cut edge."""
    kit.SHADOWLESS = True
    box(cx, d / 2 - .07, .25, w, .14, h - .25, *WALL)
    kit.SHADOWLESS = False
    light(cx, d / 2 - .15, h, w, .03, .05, lit)
    if right_edge:
        light(cx + w / 2 - .035, d / 2 - .15, .25, .07, .04, h - .25, lit)


def rack(x, y, along_back):
    """A rack of drives against a far wall, lit down its front: where a bot swaps a drive or plugs in a cable."""
    if along_back:
        box(x, y, .25, .52, .36, .95, "g5", "g3", "g2")
        for i in range(4):
            light(x, y - .19, .42 + i * .19, .38, .012, .07, "cyan" if (i + int(x * 2)) % 2 else "teal")
    else:
        box(x, y, .25, .36, .52, .95, "g5", "g3", "g2")
        for i in range(4):
            light(x + .19, y, .42 + i * .19, .012, .38, .07, "teal" if i % 2 else "cyan")


# A tall building has floors. The game stands bots on them (Core/Layout.cs: StoreyPixels, FloorReach, the stairs' ends), and
# a test holds the two together, so change these only with those.
STOREY = 1.5                    # from one floor to the next
REACH = 2 * kit.PANEL           # how far an upper floor reaches out from the back wall: two floor squares


def upper_floor(x0, x1, w, z, lit, depth=REACH, back=0.0):
    """A floor along the back wall from x0 to x1, its top at z, with the line of light along its open edge. `back` leaves
    it short of the wall by so much: the walk beside the well a stair comes up through."""
    y1 = w / 2 - .14 - back
    box((x0 + x1) / 2, y1 - (depth - back) / 2, z - .08, x1 - x0, depth - back, .08, "c0", "g4", "g2")
    light((x0 + x1) / 2, w / 2 - .14 - depth - .012, z - .08, x1 - x0, .02, .08, lit)


def stair(x0, y0, z0, x1, y1, steps=8, wide=.42):
    """A flight up one storey from (x0, y0) to (x1, y1), along x or along y, as solid steps with a lit edge to each."""
    along_y = abs(y1 - y0) > abs(x1 - x0)
    run = (abs(y1 - y0) if along_y else abs(x1 - x0)) / steps
    for i in range(steps):
        t = (i + .5) / steps
        x, y, top = x0 + (x1 - x0) * t, y0 + (y1 - y0) * t, z0 + STOREY * (i + 1) / steps
        if along_y:
            box(x, y, z0, wide, run, top - z0, "c0", "g4", "g2"); light(x, y - run / 2 - .006, top - .03, wide, .012, .03, "cyan")
        else:
            box(x, y, z0, run, wide, top - z0, "c0", "g4", "g2"); light(x, y - wide / 2 - .006, top - .03, run, .012, .03, "cyan")


def first_stair(w):
    """Up the left wall from the ground floor to the first, landing on it."""
    stair(-.87, -.85, .25, -.87, .4)


def server_tower_open():
    """The server tower with a bot at work in it. Told by: racks along the back wall on each of its three floors, a stair
    up the left wall and another up the back, and two of its roof fans on a ledge over the back wall."""
    w, h, lit = 2.5, 5.0, "cyanw"
    opened(w, w, h, lit)
    back_wall(0, w, w, h, lit)
    for floor in range(1, 10):                          # the inside of the far walls: a line at every storey, panes lit here and there
        z = .82 + floor * .43
        light(0, w / 2 - .15, z, w - .3, .012, .03, "g5"); light(-w / 2 + .15, 0, z, .012, w - .3, .03, "g5")
        for i in range(5):
            if (floor * 7 + i * 3) % 4 == 0:
                light(-.9 + i * .45, w / 2 - .152, z + .1, .3, .012, .22, "teal")
            if (floor * 5 + i * 2) % 4 == 1:
                light(-w / 2 + .152, -.9 + i * .45, z + .1, .012, .3, .22, "teal")
    for x in (-.75, -.05, .65):
        rack(x, w / 2 - .42, True)
    left, right, g1, g2 = -w / 2 + .14, w / 2 - .06, .25 + STOREY, .25 + 2 * STOREY
    first_stair(w)
    upper_floor(left, right, w, g1, lit)
    stair(-.87, .87, g1, .05, .87, wide=.4)             # the second stair, up along the back wall from the left
    upper_floor(.05, right, w, g2, lit)                 # the second floor: whole on the right,
    upper_floor(left, .05, w, g2, lit, back=.44)        # and a walk past the well the stair comes up through
    for z in (g1, g2):                                  # a rack on each upper floor
        box(.75, w / 2 - .34, z, .52, .3, .8, "g5", "g3", "g2")
        for i in range(3):
            light(.75, w / 2 - .5, z + .15 + i * .2, .38, .012, .07, "cyan" if i % 2 else "teal")
    box(0, w / 2 - .5, h, w + .12, 1.1, .14, *TRIM); fan(-.6, w / 2 - .55, h + .14); fan(.6, w / 2 - .55, h + .14)
    render("b-server-tower-open", 256, 352, 80)


def review_hall_open():
    """The review hall with a bot at work in it. Told by: its back wall in two with the bridge across the top, an upper
    floor with two wall screens side by side to compare, and a console of buttons on the ground under it."""
    w, h, lit = 2.5, 3.4, "cyanw"
    opened(w, w, h, lit)
    back_wall(-.8, .9, w, h, lit, right_edge=True); back_wall(.8, .9, w, h, lit, right_edge=True)
    box(0, w / 2 - .2, 2.5, .72, .4, .5, *TRIM)                                       # the bridge
    light(0, w / 2 - .41, 2.5, .72, .02, .05, lit); light(0, w / 2 - .41, 2.95, .72, .02, .05, lit)
    g1 = .25 + STOREY
    first_stair(w)
    upper_floor(-w / 2 + .14, w / 2 - .06, w, g1, lit)
    for x in (-.8, .8):                                                               # a screen on each half of the wall, upstairs
        box(x, w / 2 - .18, g1 + .4, .78, .05, .62, "k1"); light(x, w / 2 - .21, g1 + .45, .68, .02, .52, "teal")
        for i in range(3):
            light(x - .12 + (i % 2) * .16, w / 2 - .225, g1 + .53 + i * .15, .34, .012, .05, "cyanw" if i == 1 else "cyan")
    box(0, w / 2 - .52, .25, 1.9, .32, .42, "g5", "g3", "g2")                         # the console, its buttons along the front
    for i in range(7):
        light(-.78 + i * .26, w / 2 - .56, .67, .12, .1, .02, "cyanw" if i % 3 == 0 else "teal")
    box(w / 2 - .3, 0, .25, .3, .9, .5, "g5", "g3", "g2"); light(w / 2 - .3, 0, .75, .2, .7, .02, "teal")   # a reading desk
    render("b-review-hall-open", 256, 288, 80)


def works_open():
    """The works with a bot at work in it. Told by: two reactors with a lit core each, a panel of levers on the wall, and
    its stack rising from the floor."""
    w, h, lit = 2.4, 1.5, "cyanw"
    opened(w, w, h, lit)
    back_wall(0, w, w, h, lit)
    for x in (-.15, .7):                                                              # the reactors, along the back wall
        disc(x, w / 2 - .42, .25, .24, .8, "g5", "g3"); disc(x, w / 2 - .42, 1.05, .18, .06, "c0", "g4")
        light(x, w / 2 - .665, .5, .22, .012, .3, "cyanw" if x < 0 else "cyan")       # the core, seen through its window
    box(-w / 2 + .3, -.35, .25, .22, .9, .85, "g5", "g3", "g2")                       # the panel of levers, on the left wall
    for i in range(3):
        light(-w / 2 + .415, -.65 + i * .3, .78, .012, .08, .2, "c1"); light(-w / 2 + .42, -.65 + i * .3, .98, .03, .1, .06, "cyanw" if i == 1 else "teal")
    disc(-w / 2 + .42, .62, .25, .13, 1.9, "g5", "g3"); disc(-w / 2 + .42, .62, 2.15, .16, .05, "c0", "g4")   # the stack
    light(-w / 2 + .42, .62, 2.2, .1, .1, .04, "cyan")
    render("b-works-open", 256, 224, 80)


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


def depot():
    """Where the repo's open pull requests are queued. Told by: a wide low hall, one large lit door, a gantry across the
    roof, and the crates the game stands in front of it."""
    w, d, h, lit = 3.4, 2.4, 1.4, "cyanw"
    z = footing(w, d, lit)
    shaft(0, 0, w, d, z, h - z, lit, sides=0)
    f = -d / 2
    light(-.3, f - .012, z, 1.9, .02, .82, "cyan")                                    # the door: one sheet of light, slatted
    for i in range(5):
        light(-.3, f - .016, z + .12 + i * .15, 1.9, .012, .03, "teal")
    box(-.3, f - .05, z + .82, 2.1, .1, .08, *TRIM)
    for i in range(4):
        light(w / 2 + .012, -.75 + i * .5, .7, .02, .3, .24, lit)
    top = roof(0, 0, w, d, h, lit)
    for x in (-1.5, 1.5):                                                             # the gantry
        box(x, 0, top, .12, .12, .55, "g5", "g4", "g3")
    box(0, 0, top + .55, 3.12, .16, .1, "c0", "g5", "g3")
    box(.5, 0, top + .33, .26, .26, .22, "g5", "g4", "g3"); box(.5, 0, top + .2, .04, .04, .13, "c1")
    render("b-depot", 288, 224, 88)


def crate(name, band=None, lid=None):
    """One open pull request, queued at the depot. A graphite box; what it is waiting for is its light. None: a draft.
    A band in the building's light, which the game swaps for the depot's: in review. Amber, band and lid: for the owner."""
    s, h = .56, .4
    box(0, 0, 0, s, s, h, "c0", "g5", "g3")
    box(0, 0, h, s * .78, s * .78, .03, "c1", "g5", "g4")                               # the lid
    if band:
        light(0, -s / 2 - .012, h * .38, s, .02, .1, band); light(s / 2 + .012, 0, h * .38, .02, s, .1, band)
    if lid:
        light(0, 0, h + .03, s * .4, s * .4, .012, lid)
    render(name, 48, 48, 14)


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
    server_tower(); review_hall(); works(); relay(); depot(); test_workshop()
    server_tower_open(); review_hall_open(); works_open()
    crate("crate-draft"); crate("crate-review", band="cyanw"); crate("crate-ready", band="amber", lid="amber")
