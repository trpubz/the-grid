"""Ground, the light that lies on it, and what stands beyond a sector. See docs/art-direction.md.

The ground itself is plain: dark paving in large squares, a darker lane. Light is never part of a ground tile. It is a
line along an edge, laid over the ground as its own sprite, with its glow in flat bands either side.
"""
import math

import bmesh
import bpy

import kit
from kit import PALETTE, box, disc, glow, light, mat, render


def _tile(fill):
    box(0, 0, -.02, 1, 1, .02, fill)


def stroke(cx, cy, length, width, degrees, colour):
    """A flat painted stroke at any angle on the ground. Boxes only run along the grid; an arrowhead needs diagonals."""
    bpy.ops.mesh.primitive_cube_add(size=1, location=(cx, cy, .001))
    o = bpy.context.object
    o.scale = (length, width, .001)
    o.rotation_euler = (0, 0, math.radians(degrees))
    o.data.materials.append(mat(PALETTE[colour]))


def paving(name, joint=None):
    """Paving. A joint is a dark seam with a lit lip, along the far edge one way ("a") or the other ("b")."""
    _tile("pv1")
    if joint == "a":
        light(0, .47, 0, 1, .06, .001, "pv0"); light(0, .43, 0, 1, .02, .001, "pv2")
    if joint == "b":
        light(.47, 0, 0, .06, 1, .001, "pv0"); light(.43, 0, 0, .02, 1, .001, "pv2")
    render(name, 64, 32, 16)


def lane(name, mark=None):
    """The lane round a sector. "chevron" is a pair of arrowheads pointing along it; "crossing" is bars across it.
    The suffix says which way the lane runs: "a" along columns, "b" along rows."""
    _tile("g0")
    kind, way = (mark.rsplit("-", 1) + [""])[:2] if mark else ("", "")
    a = way == "a"
    if kind == "chevron":
        # Two arrowheads pointing along the lane. Each is two strokes meeting at its point, a shallow 28 degrees off the way
        # of travel: at 45 the camera turns them into a plain corner.
        for k in (-.08, .26):
            for side in (-1, 1):
                ca, sa = math.cos(math.radians(28)), math.sin(math.radians(28))
                x, y, turn = k - ca * .16, -side * sa * .16, side * 28
                if not a:
                    x, y, turn = -y, x, turn + 90
                stroke(x, y, .32, .07, turn, "cyan")
    if kind == "crossing":
        for k in (-.36, -.12, .12, .36):
            light(k, 0, 0, .12, .8, .001, "pv2") if a else light(0, k, 0, .8, .12, .001, "pv2")
    render(name, 64, 32, 16)


def void():
    """Beyond every sector: the ground colour, with a few dim dashes."""
    _tile("ground")
    for x, y, w in ((-.25, .25, .3), (.2, -.05, .22), (-.1, -.32, .26)):
        light(x, y, 0, w, .035, .001, "teal")
    render("tile-void", 64, 32, 16)


def line(name, way):
    """An overlay, not a tile: a line of light along a tile's far edge, and its glow on the ground either side."""
    for i, (colour, reach) in enumerate((("cg0", .5), ("cg1", .28), ("cg2", .12))):
        light(0, .5, i * .001, 1, 2 * reach, .001, colour) if way == "a" else light(.5, 0, i * .001, 2 * reach, 1, .001, colour)
    light(0, .5, .004, 1, .06, .012, "cyanw") if way == "a" else light(.5, 0, .004, .06, 1, .012, "cyanw")
    render(name, 96, 48, 24)


def arc(r0, r1, a0, a1, z, colour, cx=0.0, cy=0.0, steps=32):
    """A flat piece of a ring on the ground: between two radii, from one angle to another, in degrees anticlockwise from +x."""
    bm = bmesh.new()
    inner, outer = [], []
    for i in range(steps + 1):
        a = math.radians(a0 + (a1 - a0) * i / steps)
        inner.append(bm.verts.new((cx + max(r0, 0) * math.cos(a), cy + max(r0, 0) * math.sin(a), z)))
        outer.append(bm.verts.new((cx + r1 * math.cos(a), cy + r1 * math.sin(a), z)))
    for i in range(steps):
        bm.faces.new((inner[i], outer[i], outer[i + 1], inner[i + 1]))
    mesh = bpy.data.meshes.new("arc")
    bm.to_mesh(mesh)
    bm.free()
    o = bpy.data.objects.new("arc", mesh)
    bpy.context.collection.objects.link(o)
    o.data.materials.append(mat(PALETTE[colour]))


# The four corners of a tile, by the steps along columns and rows that lead to them, as the game names them.
CORNERS = {"back": (-1, -1), "right": (1, -1), "front": (1, 1), "left": (-1, 1)}


def rounded(name, corner, filled):
    """An overlay for one tile: a corner of lane or road rounded by one tile. The line of light is a quarter of a circle
    about the tile's opposite corner, with its glow either side, in place of the two straight lines that would meet in
    the corner. Cut (not filled): the tile is paved, and outside the arc is open ground: the outer corner of a lane, the
    outside of a bend. Filled: the tile is open ground, and outside the arc is paved: where a road meets a lane, the
    inside of a bend."""
    columns, rows = CORNERS[corner]
    x, y = columns, -rows                                   # columns run along +x, rows along -y
    cx, cy = -x / 2, -y / 2                                 # the opposite corner: the arc's centre
    a0 = {(1, 1): 0, (-1, 1): 90, (-1, -1): 180, (1, -1): 270}[(x, y)]
    light(0, 0, 0, 1, 1, .001, "g0" if filled else "ground")
    arc(0, 1, a0, a0 + 90, .002, "ground" if filled else "g0", cx, cy)
    for i, (colour, reach) in enumerate((("cg0", .5), ("cg1", .28), ("cg2", .12), ("cyanw", .03))):
        arc(1 - reach, 1 + reach, a0, a0 + 90, .003 + i * .001, colour, cx, cy)
    render(name, 96, 48, 24)


def fountain():
    """The plaza's landmark: a low platform outlined in light, a ring and a column of violet light."""
    glow(0, 0, 2, 2, "violet")
    box(0, 0, 0, 2, 2, .14, "pv2", "g2", "g1")
    light(0, -.97, .14, 2, .05, .012, "violetw"); light(.97, 0, .14, .05, 2, .012, "violetw")     # its two near edges
    light(0, .97, .14, 2, .05, .012, "violet"); light(-.97, 0, .14, .05, 2, .012, "violet")
    disc(0, 0, .14, .56, .07, "g3", "g1"); disc(0, 0, .21, .44, .012, "violet"); disc(0, 0, .222, .3, .012, "g1")
    light(0, 0, .22, .09, .09, .62, "violetw"); light(0, 0, .84, .05, .05, .2, "violet")
    render("fountain", 192, 160, 64)


def block(name, w, d, h, lines):
    """A block of the city beyond: dark graphite, a dim line or two, nothing to look at."""
    box(0, 0, 0, w, d, h, "g2", "g1", "g0")
    box(.15, .15, h, w * .45, d * .45, .22, "g3", "g2", "g1")
    for i in range(lines):
        light(-w * .15 + i * .5, -d / 2 - .01, h * .25, .05, .02, h * .6, "teal")
    light(w / 2 + .01, 0, h * .55, .02, d * .6, .05, "teal")
    render(name, 128, 160, 32)


def make():
    kit.DEPTH = False       # ground and the light on it are flat, edge to edge: no panels, shadows or outline
    paving("tile-paving"); paving("tile-paving-joint-a", "a"); paving("tile-paving-joint-b", "b")
    lane("tile-lane"); lane("tile-lane-chevron-a", "chevron-a"); lane("tile-lane-chevron-b", "chevron-b")
    lane("tile-crossing-a", "crossing-a"); lane("tile-crossing-b", "crossing-b")
    void()
    line("line-a", "a"); line("line-b", "b")
    for corner in CORNERS:
        rounded("line-round-out-" + corner, corner, False); rounded("line-round-in-" + corner, corner, True)
    kit.DEPTH = True
    fountain()
    block("block-a", 1.7, 1.7, .6, 1); block("block-b", 1.6, 1.8, 1.2, 2); block("block-c", 1.5, 1.5, 2.0, 2)
