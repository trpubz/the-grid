"""Bots, the courier's bike, badges and the pulse. See docs/art-direction.md.

A bot is a person, not a robot: a dark suit traced with light, under an oversized closed helmet, about two heads tall.
At 32 pixels a person is read from the gaps: between the legs, between an arm and the body, between helmet and shoulders.
The visor and the suit's lines are pure white; the game tints them per account. A bot that is off shows neither.

A bot is modelled facing +x, which the camera shows as facing south-east. kit.TURN gives the other three facings.
"""
import numpy as np

import kit
from kit import PALETTE, ball, box, disc, hex_rgb, light, limb, record, render

FACINGS = [("se", 0), ("ne", 1), ("nw", 2), ("sw", 3)]
POSES = ["stand", "walk-a", "walk-b", "type", "inspect", "signal", "off"]
SUIT = ("g2", "g1", "k1")


def bot(pose="stand", s=1.0):
    """One bot at the origin, facing +x. s scales it; a subagent is drawn at three fifths."""
    lit = pose != "off"
    line = "white" if lit else "g3"
    def b(x, y, z, w, d, h, *c): box(x * s, y * s, z * s, w * s, d * s, h * s, *c)
    def l(x, y, z, w, d, h, c): light(x * s, y * s, z * s, w * s, d * s, h * s, c)
    def col(x, y, z, r, h, c, r2=None): limb(x * s, y * s, z * s, r * s, h * s, c, None if r2 is None else r2 * s)
    def orb(x, y, z, r, c): ball(x * s, y * s, z * s, r * s, c)

    stride = {"walk-a": .07, "walk-b": -.07}.get(pose, 0)
    l(0, 0, .001, .3, .28, .001, "k1")                                # its shadow, small: the gap between the legs must show
    near = -1 if kit.TURN % 4 in (0, 1) else 1                        # the arm on the side the camera sees does the acting
    for side in (-1, 1):                                              # legs apart; a stride moves one forward, one back
        y, x = side * .095, side * stride
        b(x + .02, y, 0, .15, .1, .055, "g5", "g2", "k1")            # boots, chunky, toes forward
        col(x, y, .055, .042, .19, "g2", .05)
        if lit:
            l(x, y + side * .05, .08, .02, .012, .13, line)           # a line of light down the outside of each leg
    col(0, 0, .24, .075, .21, "g1", .14)                              # torso: narrow at the waist, wide at the shoulders
    l(.11, 0, .37, .012, .12, .018, line); l(.1, 0, .29, .012, .018, .09, line)       # the mark on the chest
    l(-.11, 0, .29, .012, .018, .15, line); l(-.12, 0, .42, .012, .14, .018, line)    # the spine and the shoulder bar, behind

    for side in (-1, 1):
        y = side * .22
        orb(0, y, .44, .05, "g4")                                     # shoulder
        raised = pose in ("signal", "inspect") and side == near
        if pose == "type" or pose == "inspect" and side == near:      # arm held forward
            reach = .26 if pose == "type" else .2
            b(reach / 2, y, .36 if pose == "type" else .4, reach, .06, .06, "g3", "g2", "g1")
            orb(reach + .01, y, .39 if pose == "type" else .43, .042, "g5")
        elif pose == "signal" and side == near:                       # hand up to the helmet
            b(.02, y, .44, .06, .06, .2, "g3", "g2", "g1"); orb(.04, y * .9, .66, .042, "g5")
        else:                                                         # at the side, swinging against the stride
            sw = -side * stride
            col(sw, y, .25, .034, .19, "g3"); orb(sw, y, .24, .042, "g5")
        if lit and not raised and pose != "type":
            l(0, y + side * .037, .28, .02, .012, .13, line)
    if pose == "inspect":                                             # the disc it is looking at
        disc(.25, near * .22, .5, .08, .03, "white" if lit else "g3", "g5", axis="x")

    col(0, 0, .45, .05, .04, "k1")                                    # the neck: a dark gap under the helmet
    orb(0, 0, .65, .19, "g3")                                         # the helmet: oversized, closed
    orb(.02, -.04, .7, .15, "g5")                                     # where light catches its crown
    l(.1, 0, .59, .2, .27, .075, line if lit else "glass")            # the visor: one band across the face


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
    """A courier leaning over a bike, facing +x. The line of light along the bike and the visor are white, for tinting."""
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
    ball(.1, 0, .56, .15, "g3"); ball(.11, -.03, .6, .12, "g5")
    light(.18, 0, .52, .15, .22, .06, "white")


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
