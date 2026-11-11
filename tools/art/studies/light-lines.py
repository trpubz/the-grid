# Study for docs/art-direction.md: a server tower, bots and a lit ground at the game's own density (64 by 32 tiles).
# Not part of the sprite set. It shows the palette, the shapes and the lighting the direction asks for, built with the
# sprite factory's camera. Writes two pictures into Logs/, which is not tracked:
#   Blender --background --factory-startup --python tools/art/studies/light-lines.py
import bpy, math, os, numpy as np
HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(os.path.dirname(HERE), "make_sprites.py")
OUTDIR = os.path.join(os.path.dirname(os.path.dirname(os.path.dirname(HERE))), "Logs")     # not tracked
os.makedirs(OUTDIR, exist_ok=True)
src = open(SRC).read(); src = src[:src.rindex("\nmain()")]
ns = {"__file__": SRC, "__name__": "hero"}; exec(compile(src, SRC, "exec"), ns)
mat, render, hex_rgb = ns["mat"], ns["render"], ns["hex_rgb"]
ns["OUT"] = OUTDIR
S = 1.0                                           # RollerCoaster Tycoon's own density: a tile is 64 by 32 pixels
P = dict(bg="#0b0e22", k0="#05070f", k1="#0c1024", g0="#141732", g1="#1b1f41", g2="#242950", g3="#2e3463", g4="#3a4178", g5="#474e8c",
         c0="#525a9b", c1="#5f67aa", c2="#6e76b7", c3="#8087c6", c4="#979ed6",
         amber="#ffb23e", amberd="#b8741f", amberk="#6e4a1c", amberw="#ffe2a8", cyan="#35f2ff", cyanw="#c9fbff", cyand="#1a8f9c",
         sp0="#3f5a96", sp1="#4a7fb4", sp2="#62b4d6", pv0="#2c3360", pv1="#353d70", pv2="#3f4780", warm0="#6c6aa6", warm1="#8478a6", warm2="#a48ea2", warm3="#c9a892", glass="#101a34", glassh="#1c2c52", white="#ffffff")
ns["PALETTE"] = list(P.values()); ns["PAL"] = np.array([hex_rgb(h) for h in P.values()])

def B(cx, cy, z0, w, d, h, top, left=None, right=None):
    left = left or top; right = right or left
    bpy.ops.mesh.primitive_cube_add(size=1, location=(cx * S, cy * S, (z0 + h / 2) * S)); o = bpy.context.object; o.scale = (w * S, d * S, h * S)
    for c in (top, left, right): o.data.materials.append(mat(P[c]))
    for p in o.data.polygons: n = p.normal; p.material_index = 0 if n.z > .5 else 1 if n.y < -.5 else 2
def L(cx, cy, z0, w, d, h, c): B(cx, cy, z0, w, d, h, c, c, c)                     # a light, or a painted line
def C(cx, cy, z0, r, h, top, side=None, axis="z"):
    bpy.ops.mesh.primitive_cylinder_add(vertices=20, radius=r * S, depth=h * S, location=(cx * S, cy * S, (z0 + (h / 2 if axis == "z" else 0)) * S))
    o = bpy.context.object
    if axis == "x": o.rotation_euler = (0, math.radians(90), 0)
    if axis == "y": o.rotation_euler = (math.radians(90), 0, 0)
    for c in (top, side or top): o.data.materials.append(mat(P[c]))
    for p in o.data.polygons: p.material_index = 0 if (p.normal.z > .5 if axis == "z" else abs(p.normal.z) > .5 and p.normal.z > 0) else 1
W = ("g4", "g3", "g2")                                                               # graphite: top, left face, right face

def lit_line(x0, y0, x1, y1, side=0):
    """A long line of light along the ground, and its glow lying on the paving to one side (side: -1, 0 or 1 across the line)."""
    along_x = y0 == y1
    n = abs(x1 - x0) if along_x else abs(y1 - y0); cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    for i, (c, reach) in enumerate((("sp0", .5), ("sp1", .28), ("sp2", .12))):
        if side:
            off = side * reach / 2
            L(cx + (0 if along_x else off), cy + (off if along_x else 0), .002 + i * .001, n if along_x else reach, reach if along_x else n, .001, c)
    L(cx, cy, .006, n + .05 if along_x else .05, .05 if along_x else n + .05, .012, "cyanw")

def ground():
    B(0, 0, -.3, 9, 9, .3, "pv1", "g1", "g0")
    L(0, 0, 0, 9, 9, .002, "pv1")                                                     # dark paving, in large squares
    for v in (-3, -1.5, 0, 1.5, 3):
        L(v, 1.2, .002, .02, 6.6, .001, "pv0"); L(0, v + 1.2 if abs(v + 1.2) < 4.4 else 0, .002, 9, .02, .001, "pv0")
    L(0, -3.3, .002, 9, 1.7, .001, "g0")                                               # the lane: darker still
    # its kerb is one long line of light that jogs out round a bay, and its glow lies on the paving beside it
    lit_line(-4.5, -2.4, -1.2, -2.4, side=1); lit_line(-1.2, -2.4, -1.2, -1.9, side=-1); lit_line(-1.2, -1.9, 1.0, -1.9, side=1)
    lit_line(1.0, -2.4, 1.0, -1.9, side=1); lit_line(1.0, -2.4, 4.5, -2.4, side=1)
    lit_line(-4.5, -4.2, 4.5, -4.2, side=0)
    for i in range(7): L(2.3 + i * .22, -3.3, .003, .1, 1.5, .001, "pv2")              # a crossing
    # a low platform, outlined in light
    B(3.0, 2.6, 0, 2.0, 2.0, .14, "pv2", "g2", "g1")
    for x0, y0, x1, y1 in ((2.0, 1.6, 4.0, 1.6), (4.0, 1.6, 4.0, 3.6)):
        along_x = y0 == y1; L((x0 + x1) / 2, (y0 + y1) / 2 - (.01 if along_x else 0) + (0), .14, 2.0 if along_x else .04, .04 if along_x else 2.0, .012, "cyanw")
    lit_line(1.94, 1.5, 4.1, 1.5, side=-1); lit_line(4.1, 1.5, 4.1, 3.6, side=1)
    C(3.0, 2.6, .14, .5, .06, "g3", "g1"); C(3.0, 2.6, .2, .38, .01, "cyan"); C(3.0, 2.6, .21, .26, .01, "g1"); L(3.0, 2.6, .2, .06, .06, .7, "cyanw")

def tower_lines(cx, cy):
    """The server tower again, lit the way the references are: long lines of one light up its corners and sides, round its roof and foot."""
    w, h = 2.6, 5.0; f = cy - w / 2; r = cx + w / 2
    for i, (c, reach) in enumerate((("sp0", .6), ("sp1", .36), ("sp2", .16))):        # the foot's light on the paving
        L(cx, f - reach / 2 - .1, .002 + i * .001, w + reach + .2, reach, .001, c); L(r + reach / 2 + .1, cy, .002 + i * .001, reach, w + reach + .2, .001, c)
    B(cx, cy, 0, w + .16, w + .16, .1, "g3", "g1", "g0")
    L(cx, f - .1, .1, w + .16, .03, .07, "cyanw"); L(r + .1, cy, .1, .03, w + .16, .07, "cyanw")
    B(cx, cy, .17, w + .2, w + .2, .16, "g5", "g3", "g2")
    B(cx, cy, .33, w, w, h - .33, "g4", "g2", "g1")
    for floor in range(9):                                                            # racks behind smoked glass: dim, the same light
        z = .95 + floor * .43
        for i in range(6):
            lit = (floor * 7 + i * 3) % 5 == 0
            x = cx - .86 + i * .344; y = cy - .86 + i * .344
            L(x, f - .012, z, .27, .02, .26, "cyand" if lit else "glass"); L(r + .012, y, z, .02, .27, .26, "cyand" if lit else "glass")
    # the long lines: up each corner we can see, and up the sides between the bays, unbroken from foot to roof
    for x in (cx - w / 2 + .03, cx + w / 2 - .03): L(x, f - .02, .33, .06, .04, h - .33, "cyanw")
    for y in (cy - w / 2 + .03, cy + w / 2 - .03): L(r + .02, y, .33, .04, .06, h - .33, "cyanw")
    for t in (-.35, .35):
        L(cx + t - .01, f - .02, .33, .035, .03, h - .33, "cyan"); L(r + .02, cy + t - .01, .33, .03, .035, h - .33, "cyan")
    L(cx - .78, f - .02, 1.3, .035, .03, h - 1.3, "cyan")
    # the entrance, under the same light
    B(cx - .62, f - .14, 0, .9, .28, .06, "pv2", "g3", "g2")
    L(cx - .62, f - .008, .33, .62, .03, .78, "k1"); L(cx - .62, f - .012, .33, .5, .02, .7, "cyanw")
    B(cx - .62, f - .2, 1.13, .95, .4, .07, "g5", "g3", "g2"); L(cx - .62, f - .39, 1.1, .95, .012, .03, "cyanw")
    # roof: the line runs on round the parapet; fans as before
    B(cx, cy, h, w + .12, w + .12, .14, "g5", "g4", "g3"); L(cx, cy, h + .14, w - .1, w - .1, .002, "g3")
    L(cx, f - .07, h + .06, w + .14, .03, .05, "cyanw"); L(r + .07, cy, h + .06, .03, w + .14, .05, "cyanw")
    for fx, fy in ((-.62, -.6), (.62, -.6), (-.62, .62), (.62, .62)):
        B(cx + fx, cy + fy, h + .14, 1.0, 1.0, .16, "g4", "g3", "g2")
        C(cx + fx, cy + fy, h + .3, .44, .05, "g5", "g3"); C(cx + fx, cy + fy, h + .35, .37, .005, "k0")
        for a in range(3):
            bpy.ops.mesh.primitive_cube_add(size=1, location=((cx + fx) * S, (cy + fy) * S, (h + .36) * S)); o = bpy.context.object
            o.scale = (.7 * S, .09 * S, .008 * S); o.rotation_euler = (0, 0, math.radians(a * 60 + fx * 40 + fy * 25)); o.data.materials.append(mat(P["g4"]))
        C(cx + fx, cy + fy, h + .365, .09, .02, "c1", "g4")
    lx = r + .07                                                                       # the ladder, plain graphite now: it is not a light
    for ly in (cy - 1.0, cy - .78): B(lx, ly, .33, .035, .035, h - .1, "c0", "g5", "g3")
    for i in range(22): B(lx, cy - .89, .5 + i * .21, .03, .22, .025, "c1", "g5", "g3")

def tower(cx, cy):
    w, h = 2.6, 5.0; f = cy - w / 2; r = cx + w / 2                                  # f: the front face, r: the right face
    # light from the foundation lies on the concrete in three steps
    for i, (c, reach) in enumerate((("warm0", .62), ("warm1", .40), ("warm2", .2))):
        L(cx, f - reach / 2, .001 + i * .001, w + reach, reach, .001, c); L(r + reach / 2, cy, .001 + i * .001, reach, w + reach, .001, c)
    B(cx, cy, 0, w + .16, w + .16, .1, "g3", "g1", "g0")                             # footing
    L(cx, f - .1, .1, w + .16, .03, .07, "amberw"); L(r + .1, cy, .1, .03, w + .16, .07, "amberw")   # the recessed strip
    B(cx, cy, .17, w + .2, w + .2, .16, "g5", "g3", "g2")                            # plinth, overhanging the strip
    L(cx, f - .1, .33, w + .2, .012, .012, "c1"); L(r + .1, cy, .33, .012, w + .2, .012, "c0")       # its lit top edge
    B(cx, cy, .33, w, w, h - .33, *W)                                                # the shaft
    for x in (cx - w / 2 + .09, cx + w / 2 - .09): B(x, f - .03, .33, .18, .06, h - .33, "g5", "g4", "g3")   # corner pilasters
    for y in (cy - w / 2 + .09, cy + w / 2 - .09): B(r + .03, y, .33, .06, .18, h - .33, "g5", "g4", "g3")
    L(cx, f - .005, .34, w, .01, .22, "g1"); L(r + .005, cy, .34, .01, w, .22, "g0")  # contact shadow at the foot of the walls
    for floor in range(9):                                                            # nine floors of racks behind smoked glass
        z = .95 + floor * .43
        B(cx - .0, f - .02, z - .05, 2.06, .04, .035, "g5", "g4", "g3"); B(r + .02, cy, z - .05, .04, 2.06, .035, "g5", "g4", "g3")   # sill
        for i in range(6):
            lit = (floor * 7 + i * 3) % 11 != 0                                       # a rack is dark here and there
            c = "amberw" if lit and floor > 5 else "amber" if lit else "glass"
            x = cx - .86 + i * .344; y = cy - .86 + i * .344
            L(x, f - .012, z, .27, .02, .26, c); L(r + .012, y, z, .02, .27, .26, c)
            if lit:
                L(x, f - .016, z + .12, .27, .012, .03, "amberd"); L(r + .016, y, z + .12, .012, .27, .03, "amberd")    # the shelf across each rack
                L(x - .08, f - .016, z + .03, .03, .012, .06, "amberk"); L(r + .016, y - .08, z + .03, .012, .03, .06, "amberk")
    # the entrance: recessed, under a canopy that lights the step
    B(cx - .62, f - .14, 0, .9, .28, .06, "c2", "g3", "g2"); B(cx - .62, f - .07, .06, .8, .14, .06, "c2", "g3", "g2")   # two steps
    L(cx - .62, f - .008, .33, .62, .03, .78, "k1"); L(cx - .62, f - .012, .33, .5, .02, .7, "amberw")
    L(cx - .62, f - .016, .33, .02, .012, .7, "amberk"); L(cx - .62, f - .016, .8, .5, .012, .02, "amberk")              # door leaves and transom
    B(cx - .62, f - .2, 1.13, .95, .4, .07, "g5", "g3", "g2"); L(cx - .62, f - .39, 1.1, .95, .012, .03, "amberw")       # canopy and its strip
    L(cx - .62, f - .32, .122, .8, .22, .001, "warm3")
    # services up the right face: a ladder in a guard, and a pipe run
    lx = r + .07
    for ly in (cy - 1.0, cy - .78): B(lx, ly, .33, .035, .035, h - .1, "amberd", "amberd", "amberk")
    for i in range(22): B(lx, cy - .89, .5 + i * .21, .03, .22, .025, "amber", "amberd", "amberk")
    for z in (1.6, 2.9, 4.2): B(lx + .08, cy - .89, z, .14, .34, .03, "amberd", "amberk", "amberk")                      # guard hoops
    C(r + .09, cy + .95, .33, .06, h - 1.1, "g5", "g3"); C(r + .09, cy + .72, .33, .045, h - 1.9, "g5", "g3")            # risers
    for z in (1.2, 2.4, 3.6): B(r + .07, cy + .84, z, .1, .42, .05, "g5", "g4", "g2")                                     # brackets
    for i in range(5): L(r + .012, cy + .2 + i * .0, .42 + i * .07, .012, .6, .03, "k1")                                  # a louvre at the foot
    # the roof: parapet, four fans in their housings, a conduit, a rail on the two near sides
    B(cx, cy, h, w + .12, w + .12, .14, "g5", "g4", "g3"); L(cx, cy, h + .14, w - .1, w - .1, .002, "g3")
    L(cx, f - .06, h + .14, w + .12, .012, .012, "c2"); L(r + .06, cy, h + .14, .012, w + .12, .012, "c1")
    for fx, fy in ((-.62, -.6), (.62, -.6), (-.62, .62), (.62, .62)):
        B(cx + fx, cy + fy, h + .14, 1.0, 1.0, .16, "g4", "g3", "g2")
        C(cx + fx, cy + fy, h + .3, .44, .05, "g5", "g3"); C(cx + fx, cy + fy, h + .35, .37, .005, "k0")
        for a in range(3):
            bpy.ops.mesh.primitive_cube_add(size=1, location=((cx + fx) * S, (cy + fy) * S, (h + .36) * S)); o = bpy.context.object
            o.scale = (.7 * S, .09 * S, .008 * S); o.rotation_euler = (0, 0, math.radians(a * 60 + fx * 40 + fy * 25)); o.data.materials.append(mat(P["g4"]))
        C(cx + fx, cy + fy, h + .365, .09, .02, "c1", "g4")
    C(cx, cy, h + .19, .05, 2.3, "g5", "g3", axis="x"); B(cx - 1.05, cy, h + .14, .2, .3, .3, "g5", "g4", "g3")           # conduit and its junction box
    for i in range(7):
        t = -1.3 + i * .433
        B(cx + t, f - .05, h + .14, .03, .03, .36, "amberd", "amberd", "amberk"); B(r + .05, cy + t, h + .14, .03, .03, .36, "amberd", "amberd", "amberk")
    for z in (h + .32, h + .48): L(cx, f - .05, z, w + .1, .022, .022, "amber"); 
    for z in (h + .32, h + .48): L(r + .05, cy, z, .022, w + .1, .022, "amber")

def ball(cx, cy, z, r, c):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=16, ring_count=10, radius=r * S, location=(cx * S, cy * S, z * S)); bpy.context.object.data.materials.append(mat(P[c]))
def limb(cx, cy, z0, r, h, c, r2=None):
    bpy.ops.mesh.primitive_cone_add(vertices=12, radius1=r * S, radius2=(r2 if r2 is not None else r) * S, depth=h * S, location=(cx * S, cy * S, (z0 + h / 2) * S))
    bpy.context.object.data.materials.append(mat(P[c]))

def program(cx, cy, badge=False, k=1.25):
    """A program, not a robot: a person in a dark suit traced with light, under an oversized closed helmet. About two heads tall.
    At this size a person is read from the gaps: between the legs, between arm and body, between helmet and shoulders."""
    L(cx, cy, .001, .36 * k, .3 * k, .001, "g1")                                      # its shadow on the concrete
    for sx in (-.075, .075):
        x, y = cx + sx * k, cy
        B(x, y - .015 * k, 0, .08 * k, .13 * k, .045 * k, "g5", "g2", "k1")            # boots
        limb(x, y, .045 * k, .034 * k, .15 * k, "g2", r2=.04 * k)                      # legs, apart
        L(x + .034 * k, y - .02 * k, .07 * k, .01 * k, .012 * k, .1 * k, "cyan")       # a line of light down each
    limb(cx, cy, .19 * k, .06 * k, .17 * k, "g1", r2=.115 * k)                        # torso: narrow at the waist, wide at the shoulders
    L(cx + .07 * k, cy - .05 * k, .3 * k, .012 * k, .09 * k, .014 * k, "cyan"); L(cx + .08 * k, cy - .04 * k, .22 * k, .012 * k, .014 * k, .08 * k, "cyan")
    for sx in (-.175, .175):
        x, y = cx + sx * k, cy
        limb(x, y, .2 * k, .028 * k, .15 * k, "g3"); ball(x, y, .19 * k, .034 * k, "g5")                    # arms held a little away, gloved hands
        ball(x, y, .35 * k, .04 * k, "g4")                                                                   # shoulder
    limb(cx, cy, .35 * k, .04 * k, .04 * k, "k1")                                     # the neck: a dark gap under the helmet
    ball(cx, cy, .52 * k, .15 * k, "g3")                                              # the helmet, oversized and closed
    ball(cx - .035 * k, cy - .035 * k, .56 * k, .12 * k, "g5")                        # where light catches its crown
    B(cx + .07 * k, cy - .07 * k, .485 * k, .18 * k, .18 * k, .06 * k, "cyan", "cyan", "cyan")   # the visor: one band of light
    L(cx + .072 * k, cy - .072 * k, .53 * k, .18 * k, .18 * k, .015 * k, "cyanw")
    if badge:                                                                          # waiting on the user: an amber question badge
        z = .76 * k
        B(cx, cy, z + .04 * k, .24 * k, .04 * k, .24 * k, "amber", "amberd", "amberk"); B(cx, cy, z, .07 * k, .04 * k, .04 * k, "amber", "amberd", "amberk")
        for dx, dz, w, h in ((0, .215, .12, .03), (.045, .165, .03, .07), (0, .14, .09, .03), (0, .105, .03, .045), (0, .06, .03, .03)):
            L(cx + dx * k, cy - .025 * k, z + dz * k, w * k, .012 * k, h * k, "k0")

ground()
tower_lines(-1.6, 1.6)
program(-3.4, -.9); program(-.6, -1.2, badge=True); program(.2, -1.4, badge=True); program(1.9, -.9); program(3.3, -1.3); program(2.2, .6)
render("study-light-lines", 680, 620, 190)
im = bpy.data.images.load(os.path.join(OUTDIR, "study-light-lines.png")); w, h = im.size
px = np.array(im.pixels[:]).reshape(h, w, 4); bpy.data.images.remove(im)
bg = np.array(hex_rgb(P["bg"])); out = np.ones((h, w, 4)); out[..., :3] = px[..., :3] * px[..., 3:4] + bg * (1 - px[..., 3:4])
ns["save"](np.repeat(np.repeat(out, 3, axis=0), 3, axis=1), os.path.join(OUTDIR, "study-light-lines-3x.png"))
# how tall a figure came out, in art pixels: the rows that hold the cyan visor mark the figures
