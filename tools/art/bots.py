"""Bots, the courier's bike, badges and the pulse. See docs/art-direction.md.

A bot is a person, not a robot: stocky, about two heads tall, under an oversized closed helmet. The helmet is faceted and
catches the light on its crown; a thick visor wraps round the face. The body is dark and compact, the arms are slabs, the
boots are chunky with lit toes, and on its back it carries a ring of light. The bot that inspects holds that disc in its
hand, and its back is bare.

A bot's light is drawn in three marker colours, which the game swaps for the bot's colourway: white is the light itself,
cyan-white its highlight, teal its dim partner. A bot that is off shows none of them.

A bot is modelled facing +x, which the camera shows as facing south-east. kit.TURN gives the other three facings.
"""
import bpy
import numpy as np

import kit
from kit import PALETTE, box, disc, hex_rgb, light, record, render

FACINGS = [("se", 0), ("ne", 1), ("nw", 2), ("sw", 3)]
POSES = ["stand", "walk-a", "walk-b", "type", "inspect", "signal", "off", "press-a", "press-b", "pull-a", "pull-b", "carry-a", "carry-b"]
SUIT, SLAB, BOOT = ("g2", "g0", "k1"), ("c0", "g5", "g3"), ("g5", "g3", "g1")
HELMET = ["c1", "c0", "g5", "g5", "g3", "g4", "g2"]     # the crown; then upper, middle and lower, each on the lit side and the far side


def helmet(cx, cy, z, r):
    """A faceted helmet: few faces, each one flat. Its tone falls from the crown down, and from the lit side to the other."""
    cx, cy, _, _ = kit._turned(cx, cy)
    bpy.ops.mesh.primitive_uv_sphere_add(segments=10, ring_count=6, radius=r, location=(cx, cy, z))
    o = bpy.context.object
    o.scale = (1, 1, .9)
    for c in HELMET:
        o.data.materials.append(kit.mat(PALETTE[c]))
    for p in o.data.polygons:
        n = p.normal
        band = 0 if n.z > .8 else 1 if n.z > .3 else 2 if n.z > -.3 else 3
        p.material_index = 0 if band == 0 else 2 * band - 1 + (0 if -n.y > n.x else 1)


def bot(pose="stand", s=1.0):
    """One bot at the origin, facing +x. s scales it; a subagent is drawn at three fifths."""
    lit = pose != "off"
    main, hi, dim = ("white", "cyanw", "teal") if lit else ("g4", "g5", "g3")
    def b(x, y, z, w, d, h, *c): box(x * s, y * s, z * s, w * s, d * s, h * s, *c)
    def l(x, y, z, w, d, h, c): light(x * s, y * s, z * s, w * s, d * s, h * s, c)
    def ring(x, y, z, r, h, c, axis="z"): disc(x * s, y * s, z * s, r * s, h * s, c, axis=axis)
    near = -1 if kit.TURN % 4 in (0, 1) else 1                        # the arm on the side the camera sees does the acting
    stride = {"walk-a": .08, "walk-b": -.08, "carry-a": .08, "carry-b": -.08}.get(pose, 0)

    l(0, 0, .001, .26, .28, .001, "k1")                               # its shadow
    for side in (-1, 1):                                              # a stride moves one leg forward, one back
        y, x = side * .1, side * stride
        b(x + .035, y, 0, .2, .13, .075, *BOOT)                       # boots, chunky
        l(x + .125, y, .03, .025, .1, .045, main)                     # a lit toe
        b(x, y, .075, .11, .11, .16, *SUIT)
    b(0, 0, .235, .2, .3, .2, *SUIT)                                  # the body: dark, compact
    l(.103, 0, .32, .012, .12, .06, main)                             # the mark on the chest

    if pose == "inspect":                                             # its disc is in its hand
        ring(.23, near * .17, .43, .085, .035, main); ring(.23, near * .17, .43, .045, .04, "k1")
    else:                                                             # or on its back: a ring of light
        ring(-.115, 0, .345, .115, .03, main, "x"); ring(-.125, 0, .345, .065, .03, "k1", "x")

    if pose.startswith("carry"):                                      # a core or a drive, held before it in both hands
        b(.3, 0, .3, .16, .26, .16, "g5", "g3", "g2"); l(.3, 0, .46, .1, .2, .012, main); l(.383, 0, .33, .012, .2, .1, hi)

    for side in (-1, 1):
        y = side * .205
        if pose == "type":                                            # both held forward, to a keyboard
            b(.12, y, .33, .26, .09, .09, *SLAB); l(.25, y, .335, .03, .09, .08, main)
        elif pose.startswith("carry"):                                # both held forward, under what it carries
            b(.13, y * .8, .29, .28, .09, .09, *SLAB)
        elif pose == "press-a" and side == near:                      # out to a button or a socket, the hand lit
            b(.16, y, .37, .34, .09, .09, *SLAB); l(.33, y, .37, .04, .1, .1, main)
        elif pose == "press-b" and side == near:                      # and drawn back
            b(.08, y, .34, .2, .09, .09, *SLAB); l(.18, y, .34, .03, .1, .1, dim)
        elif pose == "pull-a" and side == near:                       # up to a lever
            b(.1, y, .4, .12, .09, .26, *SLAB); l(.11, y, .64, .12, .1, .05, main)
        elif pose == "pull-b" and side == near:                       # and down with it
            b(.14, y, .22, .22, .09, .1, *SLAB); l(.26, y, .22, .04, .1, .1, main)
        elif pose == "inspect" and side == near:                      # one held forward, under the disc
            b(.09, y, .33, .2, .09, .09, *SLAB)
        elif pose == "signal" and side == near:                       # a hand up to the helmet
            b(.03, y, .36, .1, .09, .2, *SLAB); l(.05, y, .53, .09, .09, .04, main)
        else:                                                         # a slab at its side, swinging against the stride
            sw = -side * stride
            b(sw, y, .2, .11, .09, .22, *SLAB); l(sw, y + side * .046, .22, .07, .012, .14, dim)

    helmet(0, 0, .64 * s, .225 * s)                                   # oversized, closed
    # The visor is a band set a little forward of the helmet's middle: it stands proud of the helmet across the face and
    # round to each side, and is inside it everywhere else.
    ring(.035, 0, .6, .21, .085, main if lit else "glass")
    if lit:
        ring(.035, 0, .655, .213, .03, hi)


def all_bots():
    for pose in POSES:
        for facing, turns in FACINGS:
            kit.TURN = turns
            bot(pose)
            render(f"bot-{pose}-{facing}", 40, 48, 8)
    kit.TURN = 0
    bot("stand", s=.6)
    render("bot-small", 24, 32, 5)


def bike():
    """A courier leaning over a bike, facing +x. The line of light along the bike, its lamp and the visor are the bot's light."""
    light(0, 0, .001, 1.05, .3, .001, "k1")
    for x in (-.36, .36):
        disc(x, 0, .17, .17, .1, "k1", "k0", axis="y"); disc(x, 0, .17, .07, .12, "g5", "g4", axis="y")
    box(0, 0, .16, .78, .12, .13, "g4", "g3", "g2")                  # frame
    for y in (-.066, .066):
        light(.02, y, .2, .62, .012, .035, "white")                   # the line of light along it
    box(.3, 0, .29, .2, .12, .16, "g5", "g4", "g3"); light(.405, 0, .33, .012, .09, .07, "white")   # fairing and lamp
    box(-.3, 0, .29, .16, .1, .06, "g4", "g3", "g2")                 # tail
    box(-.06, 0, .29, .3, .16, .15, "g2", "g1", "k1")                # the rider, leaning forward over the tank
    box(.12, 0, .4, .16, .2, .06, "g3", "g2", "g1")                  # arms to the bars
    helmet(.1, 0, .56, .16)
    disc(.125, 0, .53, .15, .06, "white"); disc(.125, 0, .575, .15, .015, "cyanw")


def all_bikes():
    for facing, turns in FACINGS:
        kit.TURN = turns
        bike()
        render(f"bike-{facing}", 72, 56, 14)
    kit.TURN = 0


# ---- drawn directly, pixel by pixel -------------------------------------------------------------------------------

PLATE = [   # 16 by 20, top row first. # outline, o fill, s shade
    "................",
    "...##########...",
    "..#oooooooooo#..",
    ".#ooooooooooos#.",
    ".#ooooooooooos#.",
    ".#ooooooooooos#.",
    ".#ooooooooooos#.",
    ".#ooooooooooos#.",
    ".#ooooooooooos#.",
    ".#ooooooooooos#.",
    ".#ooooooooooos#.",
    ".#ooooooooooos#.",
    ".#ooooooooooos#.",
    "..#osssssssss#..",
    "...###oos####...",
    ".....#oos#......",
    "......#os#......",
    ".......##.......",
    "................",
    "................",
]
GLYPHS = {   # 6 by 9, drawn on the plate from its fourth row
    "question": [".####.", "##..##", "....##", "...##.", "..##..", "..##..", "......", "..##..", "..##.."],
    "approve": ["..##..", "..##..", "..##..", "..##..", "..##..", "..##..", "......", "..##..", "..##.."],
    "done": ["......", ".....#", "....##", "#..##.", "##.##.", ".###..", "..#...", "......", "......"],
    "fault": ["......", "##..##", "##..##", ".####.", "..##..", ".####.", "##..##", "##..##", "......"],
}
BADGES = {"question": ("amber", "amberd"), "approve": ("amber", "amberd"), "done": ("cyanw", "cg2"), "fault": ("fault", "amberk")}


def badge(kind):
    fill, shade = BADGES[kind]
    px = np.zeros((20, 16, 4))
    colours = {"#": "k0", "o": fill, "s": shade}
    for row, text in enumerate(PLATE):
        for x, ch in enumerate(text):
            if ch in colours:
                px[19 - row, x, :3], px[19 - row, x, 3] = hex_rgb(PALETTE[colours[ch]]), 1
    for row, text in enumerate(GLYPHS[kind]):
        for x, ch in enumerate(text):
            if ch == "#":
                px[19 - (3 + row), 5 + x, :3] = hex_rgb(PALETTE["k0"])
    record("badge-" + kind, px, 2)


def pulse(index, a, b, colour):
    """A ring of light on the ground, a wide and b high, two pixels thick."""
    px = np.zeros((32, 64, 4))
    for y in range(32):
        for x in range(64):
            d = ((x - 31.5) / a) ** 2 + ((y - 15.5) / b) ** 2
            if d <= 1 and ((x - 31.5) / (a - 2)) ** 2 + ((y - 15.5) / (b - 1.5)) ** 2 > 1:
                px[y, x, :3], px[y, x, 3] = hex_rgb(PALETTE[colour]), 1
    record(f"pulse-{index}", px, 16)


def make():
    all_bots()
    all_bikes()
    for kind in ("question", "approve", "done", "fault"):
        badge(kind)
    pulse(0, 11, 5.5, "violetw"); pulse(1, 20, 10, "violet"); pulse(2, 30, 15, "vg1")
