"""The Grid sprite factory.

Renders every sprite as isometric pixel art and writes PNGs into Assets/TheGrid/Resources/Art, with sprites.json beside
them saying what each one is. Run from the repo root:

    /Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup --python tools/art/make_sprites.py

The camera, the palette and the building blocks are in kit.py. This file is the entry point, and still holds the first
sprite set, which draws from its own palette until the world stops using it.
"""
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import kit
from kit import hex_rgb, mat

import bpy

# ---- the first sprite set ------------------------------------------------------------------------------------------

PALETTE = kit.PALETTE_1
N0, N1, N2, N3, N4, N5, N6, N7, N8 = PALETTE[0:9]
K0, K1, K2, K3, K4, WHITE = PALETTE[9:15]
CYAN, CYAN_DIM, AMBER, ORANGE, RED, PINK, GREEN, BLUE, VIOLET = PALETTE[15:24]
BODY = (N5, N4, N3)        # top, left, right faces of an ordinary wall
BODY_HI = (N6, N5, N4)
TURN = 0    # quarter turns applied to everything built, so one model gives four facings
SCALE = 1.0
TALL = 1.0   # heights are stretched by this; buildings are drawn half again as tall as modelled, towers over the riders


def render(name, w, h, ground_y):
    kit.render(name, w, h, ground_y, palette=1)


def box(cx, cy, z0, w, d, h, top, left=None, right=None):
    """A box standing on z0, centred on (cx, cy). Flat lighting has no shading, so the three faces the camera sees
    (top, left, right) each get their own colour."""
    left = left or top
    right = right or left
    for _ in range(TURN % 4):
        cx, cy, w, d = -cy, cx, d, w
    cx, cy, z0, w, d, h = (v * SCALE for v in (cx, cy, z0, w, d, h))
    z0, h = z0 * TALL, h * TALL
    bpy.ops.mesh.primitive_cube_add(size=1, location=(cx, cy, z0 + h / 2))
    o = bpy.context.object
    o.scale = (w, d, h)
    for c in (top, left, right):
        o.data.materials.append(mat(c))
    for p in o.data.polygons:
        n = p.normal
        p.material_index = 0 if n.z > .5 else 1 if n.y < -.5 else 2


def glow(cx, cy, z0, w, d, h, colour):
    box(cx, cy, z0, w, d, h, colour, colour, colour)


# ---- tiles: 64 by 32, centred -------------------------------------------------------------------------------------

def tile(name, fill, kerb=None, alley=None):
    """Ground. Light is a strip along one edge, never a grid: a kerb strip runs the way a street runs, along the fronts of a
    row of buildings; an alley strip runs between two buildings, along their sides."""
    box(0, 0, -.02, 1, 1, .02, fill)
    if kerb:
        box(0, .455, 0, 1, .09, .001, kerb)
    if alley:
        box(.455, 0, 0, .09, 1, .001, alley)
    render(name, 64, 32, 16)


# ---- buildings: 128 by 192, a 2 by 2 tile footprint whose near corner touches the bottom edge ----------------------

def slab(fill=N3, edge=N4):
    """The pad a building stands on. A little smaller than the 2 by 2 footprint, so an alley shows between neighbours."""
    box(0, 0, 0, 1.8, 1.8, .04, fill, N1, N0)
    box(0, -.88, .04, 1.8, .04, .01, edge)
    box(.88, 0, .04, .04, 1.8, .01, edge)


def trim(cx, cy, z, w, d, colour):
    """A glow line around the two visible roof edges of a w by d block centred on (cx, cy)."""
    glow(cx, cy - d / 2 - .02, z, w, .05, .06, colour)
    glow(cx + w / 2 + .02, cy, z, .05, d, .06, colour)


def windows_front(cx, y, z, count, colour, w=.16, h=.2, gap=.3):
    for i in range(count):
        glow(cx + (i - (count - 1) / 2) * gap, y - .02, z, w, .03, h, colour)


def windows_side(x, cy, z, count, colour, d=.16, h=.2, gap=.3):
    for i in range(count):
        glow(x + .02, cy + (i - (count - 1) / 2) * gap, z, .03, d, h, colour)


def b_garage():      # editing: wide and low, one big lit roll door
    slab()
    box(.1, .15, .04, 1.6, 1.3, .8, *BODY)
    trim(.1, .15, .84, 1.6, 1.3, CYAN)
    glow(.1, -.52, .04, 1.0, .03, .6, CYAN_DIM)
    for i in range(4):
        glow(.1, -.53, .12 + i * .14, 1.0, .02, .03, CYAN)
    box(.55, .45, .84, .4, .4, .25, *BODY_HI)
    windows_side(.9, .15, .35, 3, AMBER)


def b_library():     # reading: a tall tower with shelves of light
    slab()
    box(0, .2, .04, 1.1, 1.1, 2.1, *BODY)
    trim(0, .2, 2.14, 1.1, 1.1, AMBER)
    for row in range(5):
        glow(0, -.37, .35 + row * .34, .9, .03, .1, AMBER)
        glow(.57, .2, .35 + row * .34, .03, .9, .1, AMBER)
    glow(-.2, -.37, .04, .3, .03, .25, WHITE)


def b_lab():         # testing: a block with a green ring and a mast
    slab()
    box(0, .1, .04, 1.4, 1.4, 1.0, *BODY)
    for z in (.5, .62):
        glow(0, -.62, z, 1.4, .03, .05, GREEN)
        glow(.72, .1, z, .03, 1.4, .05, GREEN)
    box(0, .1, 1.04, .7, .7, .5, *BODY_HI)
    trim(0, .1, 1.54, .7, .7, GREEN)
    box(.2, .3, 1.54, .06, .06, .5, K1)
    glow(.2, .3, 2.04, .12, .12, .1, GREEN)
    windows_front(0, -.6, .15, 3, WHITE, w=.12, h=.14)


def b_review():      # reviewing: two towers joined by a bridge
    slab()
    box(-.55, .2, .04, .6, 1.2, 1.6, *BODY)
    box(.55, .2, .04, .6, 1.2, 1.6, *BODY)
    box(0, .2, 1.0, .5, .9, .3, *BODY_HI)
    trim(-.55, .2, 1.64, .6, 1.2, PINK)
    trim(.55, .2, 1.64, .6, 1.2, PINK)
    glow(0, -.27, 1.0, .5, .03, .08, PINK)
    for z in (.4, .8, 1.2):
        glow(-.55, -.42, z, .35, .03, .12, PINK)
        glow(.87, .2, z, .03, .8, .12, PINK)


def b_workshop():    # working: a plain shed with a stack
    slab()
    box(.1, .2, .04, 1.5, 1.3, 1.0, *BODY)
    box(.4, .45, 1.04, .9, .8, .45, *BODY_HI)
    trim(.1, .2, 1.04, 1.5, 1.3, CYAN)
    glow(-.25, -.47, .04, .4, .03, .6, CYAN)
    windows_front(.35, -.45, .5, 2, AMBER)
    windows_side(.87, .2, .5, 3, AMBER)
    box(-.35, .55, 1.04, .2, .2, .7, K2, K1, K1)
    glow(-.35, .55, 1.74, .24, .24, .06, WHITE)


def b_booth():       # blocked on a question: a small kiosk under a tall red beacon
    slab()
    box(0, .1, .04, .8, .8, .7, *BODY)
    trim(0, .1, .74, .8, .8, RED)
    glow(0, -.32, .25, .5, .03, .3, WHITE)
    box(0, .1, .74, .12, .12, 1.0, K2, K1, K1)
    glow(0, .1, 1.74, .4, .4, .3, RED)
    glow(0, .1, 2.04, .2, .2, .1, WHITE)


def b_gate():        # waiting for permission: an arch with a striped beam
    slab()
    box(-.7, 0, .04, .35, .5, 1.2, *BODY)
    box(.7, 0, .04, .35, .5, 1.2, *BODY)
    box(0, 0, 1.0, 1.75, .5, .25, *BODY_HI)
    for i in range(5):
        glow(-.6 + i * .3, -.27, 1.05, .16, .03, .14, ORANGE)
    glow(0, -.27, .55, 1.05, .03, .06, ORANGE)       # the barrier
    glow(-.7, -.27, .3, .2, .03, .2, ORANGE)
    trim(0, 0, 1.25, 1.75, .5, ORANGE)


def b_yard():        # errored or unknown: a fenced yard with a warning post
    slab(N2, N3)
    box(0, -.85, .04, 1.8, .08, .3, K3, K2, K1)
    box(.85, 0, .04, .08, 1.8, .3, K3, K2, K1)
    box(-.3, .3, .04, .7, .6, .35, *BODY)
    box(.4, .4, .04, .1, .1, 1.3, K2, K1, K1)
    glow(.4, .4, 1.34, .3, .3, .25, VIOLET)
    glow(0, -.9, .2, 1.8, .02, .04, VIOLET)
    glow(.9, 0, .2, .02, 1.8, .04, VIOLET)


def b_lot():         # done: a parking slab with painted bays and two lamps
    slab(N4, N5)
    for i in range(5):
        glow(-.8 + i * .4, -.3, .04, .04, .9, .005, BLUE)
    glow(0, .15, .04, 1.64, .04, .005, BLUE)
    for x in (-.8, .8):
        box(x, .75, .04, .06, .06, .9, K2, K1, K1)
        glow(x, .75, .94, .16, .16, .08, WHITE)
    box(0, .75, .04, 1.0, .25, .25, *BODY)
    trim(0, .75, .29, 1.0, .25, BLUE)


def b_depot():       # offline: a dark shed with the lights off
    slab(N1, N2)
    box(0, .1, .04, 1.6, 1.4, .7, N3, N2, N1)
    box(0, .1, .74, 1.6, .5, .2, N4, N3, N2)
    box(0, -.61, .04, .9, .02, .5, K1, K0, K0)
    box(.81, .1, .3, .02, 1.0, .2, K1, K0, K0)
    glow(-.7, -.61, .5, .06, .02, .06, K4)             # one dim lamp


BUILDINGS = [("b-garage", b_garage), ("b-library", b_library), ("b-lab", b_lab), ("b-review", b_review),
             ("b-workshop", b_workshop), ("b-booth", b_booth), ("b-gate", b_gate), ("b-yard", b_yard),
             ("b-lot", b_lot), ("b-depot", b_depot)]


# ---- outlands: what lies beyond the plots, out to the edge of the view. Dark and dim, so it never competes with a city. --

ROCK = (N2, N1, N0)
ROCK_HI = (N3, N2, N1)


def column(cx, cy, w, h, faces=ROCK, seam=None):
    """One square column of stone. A seam is a crack of light down its front."""
    box(cx, cy, 0, w, w, h, *faces)
    if seam:
        glow(cx - w * .15, cy - w / 2 - .01, h * .2, .05, .02, h * .55, seam)


def o_stones():      # a scatter of low stones
    column(-.5, .2, .5, .25)
    column(.1, -.3, .7, .4)
    column(.55, .35, .4, .2)
    column(-.15, .5, .35, .55, ROCK_HI)


def o_outcrop():     # a stand of columns, one of them cracked
    column(-.3, .3, .7, 1.1)
    column(.6, .55, .4, 1.4, ROCK_HI)
    column(.3, .1, .6, .7, seam=CYAN_DIM)
    column(-.1, -.35, .5, .4)
    column(-.7, -.1, .35, .3)


def o_spire():       # a tower of stone with one lit seam, seen from far off
    column(0, .2, .8, 1.2)
    column(.1, .25, .55, 2.6, ROCK_HI, seam=CYAN_DIM)
    column(-.35, -.2, .45, .6)
    column(.5, -.1, .35, .9)


OUTLANDS = [("o-stones", o_stones), ("o-outcrop", o_outcrop), ("o-spire", o_spire)]


def water():
    """A tile of still water: darker than the land, with two glints."""
    box(0, 0, -.02, 1, 1, .02, K0)
    glow(-.15, .2, 0, .4, .04, .001, CYAN_DIM)
    glow(.2, -.2, 0, .25, .04, .001, N6)
    render("tile-water", 64, 32, 16)


# ---- riders: the glow is pure white so the game can tint it per account -------------------------------------------

def rider():
    """A motorcycle and rider facing +x. Drawn a little larger than true scale so it reads on a 64 pixel tile."""
    for x in (-.34, .34):
        box(x, 0, 0, .26, .1, .26, K1, K0, K0)                  # wheel
        for y in (-.055, .055):
            glow(x, y, .1, .08, .01, .08, WHITE)                # hub light
    box(0, 0, .16, .7, .1, .1, K3, K2, K1)                      # frame
    for y in (-.055, .055):
        glow(.02, y, .19, .56, .01, .03, WHITE)                 # the glow line
    box(.27, 0, .26, .16, .1, .14, K4, K3, K2)                  # fairing
    glow(.36, 0, .3, .02, .08, .06, WHITE)                      # headlight
    box(-.3, 0, .26, .12, .08, .05, K3, K2, K1)                 # tail
    box(-.08, 0, .26, .2, .13, .16, K4, K3, K2)                 # rider, leaning forward
    box(.04, 0, .38, .18, .13, .1, K4, K3, K2)
    box(.1, 0, .48, .16, .15, .15, K2, K1, K0)                  # helmet
    glow(.185, 0, .52, .02, .11, .06, WHITE)                    # visor


FACINGS = [("se", 0), ("ne", 1), ("nw", 2), ("sw", 3)]   # which way the bike points on screen, by quarter turns


def draw_marker():
    """12 by 16, drawn directly: an amber exclamation mark with a dark outline."""
    px = np.zeros((16, 12, 4))
    amber, dark = hex_rgb(AMBER), hex_rgb(K0)
    def put(x, y, c):
        px[15 - y, x, :3], px[15 - y, x, 3] = c, 1
    for y in range(0, 16):
        for x in range(3, 9):
            if y <= 9 or y >= 12:
                put(x, y, dark)
    for y in range(1, 9):
        for x in range(4, 8):
            put(x, y, amber)
    for y in range(13, 15):
        for x in range(4, 8):
            put(x, y, amber)
    return px


def main():
    os.makedirs(kit.OUT, exist_ok=True)
    global TURN, SCALE, TALL
    kit.write_palettes()

    tile("tile-ground", N0)
    tile("tile-plot", N1)
    tile("tile-kerb", N1, kerb=CYAN)
    tile("tile-alley", N1, alley=CYAN)
    tile("tile-road", N3)
    tile("tile-road-kerb", N3, kerb=CYAN)
    tile("tile-road-alley", N3, alley=CYAN)     # for the path round a plot, where it runs the other way
    water()

    TALL = 1.5
    for name, build in BUILDINGS:
        build()
        render(name, 128, 192, 32)       # footprint is 2 tiles: its centre sits 32 above the bottom edge, its near corner on it

    TALL = 1.0
    for name, build in OUTLANDS:
        build()
        render(name, 128, 192, 32)

    SCALE = 1.5
    for facing, turns in FACINGS:
        TURN = turns
        rider()
        render("rider-" + facing, 64, 64, 18)
    TURN, SCALE = 0, .75
    rider()
    render("rider-small", 32, 32, 9)
    TURN, SCALE = 0, 1.0

    kit.record("marker", kit.snap(draw_marker(), 1), 0, palette=1)

    kit.write_manifest()
    kit.contact_sheet()
    print("SPRITES:", ", ".join(f"{n} {p.shape[1]}x{p.shape[0]}" for n, p, _, _ in kit.SPRITES))


main()
