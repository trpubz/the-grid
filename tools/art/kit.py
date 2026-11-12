"""The sprite kit: the camera, the palette, and what every sprite is built, rendered and saved with.

How it stays pixel art: the Workbench engine with flat lighting gives every face one flat colour, anti-aliasing is off,
and every pixel is then snapped to the palette. The camera is orthographic, 30 degrees above the ground and turned 45
degrees, which draws a 1 by 1 ground square as a 64 by 32 diamond: one tile. That grain is fixed; see docs/art-direction.md.

How it gets depth without leaving the palette: what stands (buildings, bots, blocks; not ground tiles) is rendered
three times. The flat render says what each pixel is. The same wide sun, rendered with and without shadows, says how much
light each pixel lost: half shadow moves a pixel one tone down its own ramp, full shadow two. Large faces are panelled,
a light line is drawn where a face sits over a clearly darker one, and a dark outline round the silhouette.
"""
import json
import math
import os
import tempfile

import bpy
import numpy as np

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT = os.path.join(ROOT, "Assets", "TheGrid", "Resources", "Art")
SHEET = os.path.join(ROOT, "docs", "art", "contact-sheet.png")
TMP = os.path.join(tempfile.gettempdir(), "grid-sprite.png")

TILE_PX = 64                                   # one ground unit is 64 pixels wide on screen
PX_PER_UNIT = TILE_PX / math.sqrt(2)           # across the screen
PX_PER_Z = PX_PER_UNIT * math.cos(math.radians(30))   # one unit of height

# The palette. Sprites name their colours; nothing outside this list reaches a picture.
PALETTE = {
    # the ground under everything, two blacks, then graphite from dark to light
    "ground": "#0b0e22", "k0": "#05070f", "k1": "#0c1024",
    "g0": "#141732", "g1": "#1b1f41", "g2": "#242950", "g3": "#2e3463", "g4": "#3a4178", "g5": "#474e8c",
    "c0": "#525a9b", "c1": "#5f67aa", "c2": "#6e76b7",                       # poured concrete
    "pv0": "#2c3360", "pv1": "#353d70", "pv2": "#3f4780",                    # paving: joint, face, lip
    "glass": "#101a34",                                                      # smoked glass
    "cyan": "#35f2ff", "cyanw": "#c9fbff", "teal": "#1a8f9c",
    "cg0": "#3f5a96", "cg1": "#4a7fb4", "cg2": "#62b4d6",                    # cyan light lying on paving, far to near
    "violet": "#a070ff", "violetw": "#d9c8ff", "vg0": "#4b4696", "vg1": "#6657b8",   # and violet light, far and near
    "amber": "#ffb23e", "amberd": "#b8741f", "amberk": "#6e4a1c",
    "fault": "#ff4d4d",
    "white": "#ffffff",                                                      # a bot's light, as drawn: the game swaps it
    "w0": "#8791c4", "w1": "#e4ecff",                                        # what a white bot's dim light and light become
    "orange": "#ff7a1e", "orangew": "#ffc27a", "oranged": "#a8501a",         # an orange bot's light, highlight and dim light
    # What the game turns a building's light into in other sectors: bright line, dimmer line, lit window, glow far to near.
    # No picture is drawn in these; they are here so that the palette is the whole truth. See Core/Lights.cs.
    "bluew": "#a9c8ff", "blue": "#4d8dff", "blued": "#2a55b0", "bg0": "#2f4a96", "bg1": "#3a62c0", "bg2": "#5b8be6",
    "mintw": "#b6ffe6", "mint": "#3ef2b0", "mintd": "#1a9c78", "mg0": "#2f6f7a", "mg1": "#3a9c8c", "mg2": "#5cd6b0",
}
RAMP = ["k1", "g0", "g1", "g2", "g3", "g4", "g5", "c0", "c1", "c2"]         # graphite and concrete, dark to light
PAVING = ["pv0", "pv1", "pv2"]
# Colours that are light: they keep their colour in shadow and cast none.
LIGHTS = {"cyan", "cyanw", "teal", "violet", "violetw", "amber", "white", "cg0", "cg1", "cg2", "vg0", "vg1", "fault"}
SEAM = {**{n: RAMP[max(0, i - 1)] for i, n in enumerate(RAMP)}, "pv2": "pv0", "pv1": "pv0"}   # a seam is one tone down
PANEL, FLOOR = .42, .43     # how wide a panel is, and how tall a storey
GLOW = {"cyan": (("cg0", .6), ("cg1", .36), ("cg2", .16)), "violet": (("vg0", .5), ("vg1", .24))}   # colour and reach, far to near


def hex_rgb(h):
    return tuple(int(h[i:i + 2], 16) / 255 for i in (1, 3, 5))


PAL = np.array([hex_rgb(h) for h in PALETTE.values()])
BG = hex_rgb(PALETTE["ground"])


def setup():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    s = bpy.context.scene
    s.render.engine = 'BLENDER_WORKBENCH'
    sh = s.display.shading
    sh.light, sh.color_type = 'FLAT', 'MATERIAL'
    sh.show_shadows = sh.show_cavity = sh.show_object_outline = False
    s.display.render_aa = 'OFF'
    s.render.film_transparent = True
    s.view_settings.view_transform = 'Standard'
    s.render.image_settings.file_format, s.render.image_settings.color_mode = 'PNG', 'RGBA'
    s.render.resolution_percentage = 100
    cam = bpy.data.cameras.new("cam")
    cam.type = 'ORTHO'
    ob = bpy.data.objects.new("cam", cam)
    s.collection.objects.link(ob)
    s.camera = ob
    ob.rotation_euler = (math.radians(60), 0, math.radians(45))
    # For the shadow pass only: a wide sun from the upper left in front, so shadows have a broad, soft edge.
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", 'SUN'))
    s.collection.objects.link(sun)
    sun.data.energy, sun.data.angle = 4.0, math.radians(28)
    if hasattr(sun.data, "use_shadow_jitter"):
        sun.data.use_shadow_jitter = True
    sun.rotation_euler = (math.radians(52), 0, math.radians(-28))
    s.world = bpy.data.worlds.new("dark")
    s.world.use_nodes = True
    s.world.node_tree.nodes["Background"].inputs[1].default_value = 0
    return s, ob, sun


SCENE, CAM, SUN = setup()
MATS = {}
TURN = 0        # quarter turns applied to everything built, so one model gives four facings
DEPTH = True    # panels, shadows, edges and outline. Off for ground tiles and overlays, which are flat, edge to edge
SHADOWLESS = False   # while set, what is built casts no shadow: the walls of a room we look into


LIGHT_HEX = {PALETTE[n] for n in LIGHTS}


def mat(h):
    """The material for a hex colour: flat for the flat render, and for the shadow pass a surface that takes light,
    or gives it off if the colour is a light."""
    if h not in MATS:
        m = bpy.data.materials.new(h)
        lin = lambda c: c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4
        m.diffuse_color = tuple(lin(c) for c in hex_rgb(h)) + (1,)
        m.use_nodes = True
        m.node_tree.nodes.clear()
        out = m.node_tree.nodes.new("ShaderNodeOutputMaterial")
        n = m.node_tree.nodes.new("ShaderNodeEmission" if h in LIGHT_HEX else "ShaderNodeBsdfDiffuse")
        n.inputs[0].default_value = m.diffuse_color
        m.node_tree.links.new(n.outputs[0], out.inputs[0])
        MATS[h] = m
    return MATS[h]


def _m(name):
    if name not in PALETTE:
        raise KeyError(f"'{name}' is not a palette colour; the palette is in tools/art/kit.py")
    return mat(PALETTE[name])


def _casts(o, colour):
    """A light is not a thing that blocks light, and nothing built while SHADOWLESS is set casts a shadow."""
    o.visible_shadow = not SHADOWLESS and colour not in LIGHTS


def _turned(cx, cy, w=0.0, d=0.0):
    for _ in range(TURN % 4):
        cx, cy, w, d = -cy, cx, d, w
    return cx, cy, w, d


def _box(cx, cy, z0, w, d, h, top, left, right):
    """A plain box standing on z0, centred on (cx, cy). Flat lighting has no shading, so the three faces the camera sees
    (top, left, right) each get their own palette colour."""
    cx, cy, w, d = _turned(cx, cy, w, d)
    bpy.ops.mesh.primitive_cube_add(size=1, location=(cx, cy, z0 + h / 2))
    o = bpy.context.object
    o.scale = (w, d, h)
    for c in (top, left, right):
        o.data.materials.append(_m(c))
    for p in o.data.polygons:
        n = p.normal
        p.material_index = 0 if n.z > .5 else 1 if n.y < -.5 else 2
    _casts(o, top)


def box(cx, cy, z0, w, d, h, top, left=None, right=None):
    """A box standing on z0, centred on (cx, cy), in three colours: top, left face, right face. Its large faces are
    panelled, the roof as well as the walls: a seam between panels and at every storey, one tone down."""
    left = left or top
    right = right or left
    _box(cx, cy, z0, w, d, h, top, left, right)
    if not DEPTH:
        return
    def cuts(span):
        n = max(2, round(span / PANEL))
        return [(i / n - .5) * span for i in range(1, n)]
    if w >= .8 and d >= .8 and top in SEAM:
        for t in cuts(w):
            _box(cx + t, cy, z0 + h, .022, d, .0015, *[SEAM[top]] * 3)
        for t in cuts(d):
            _box(cx, cy + t, z0 + h, w, .022, .0015, *[SEAM[top]] * 3)
    for span, colour, front in ((w, left, True), (d, right, False)):
        if span < .8 or h < .5 or colour not in SEAM:
            continue
        c = [SEAM[colour]] * 3
        for t in cuts(span):
            _box(cx + t, cy - d / 2 - .004, z0, .022, .008, h, *c) if front else _box(cx + w / 2 + .004, cy + t, z0, .008, .022, h, *c)
        z = z0 + FLOOR
        while z < z0 + h - .1:
            _box(cx, cy - d / 2 - .004, z, w, .008, .024, *c) if front else _box(cx + w / 2 + .004, cy, z, .008, d, .024, *c)
            z += FLOOR


def light(cx, cy, z0, w, d, h, colour):
    """A box that is one colour on every face: a light, or a painted line."""
    box(cx, cy, z0, w, d, h, colour, colour, colour)


def disc(cx, cy, z0, r, h, top, side=None, axis="z"):
    """A cylinder standing on z0, or lying along x or y with its centre line at z0."""
    cx, cy, _, _ = _turned(cx, cy)
    if axis in "xy" and TURN % 2:
        axis = "y" if axis == "x" else "x"
    bpy.ops.mesh.primitive_cylinder_add(vertices=20, radius=r, depth=h, location=(cx, cy, z0 + (h / 2 if axis == "z" else 0)))
    o = bpy.context.object
    if axis == "x":
        o.rotation_euler = (0, math.radians(90), 0)
    if axis == "y":
        o.rotation_euler = (math.radians(90), 0, 0)
    for c in (top, side or top):
        o.data.materials.append(_m(c))
    for p in o.data.polygons:
        p.material_index = 0 if axis == "z" and p.normal.z > .5 else 1 if axis == "z" else 0
    _casts(o, top)


def ball(cx, cy, z, r, colour):
    cx, cy, _, _ = _turned(cx, cy)
    bpy.ops.mesh.primitive_uv_sphere_add(segments=16, ring_count=10, radius=r, location=(cx, cy, z))
    bpy.context.object.data.materials.append(_m(colour))
    _casts(bpy.context.object, colour)


def limb(cx, cy, z0, r, h, colour, r2=None):
    """An upright cylinder, or a cone when r2 (its radius at the top) is given."""
    cx, cy, _, _ = _turned(cx, cy)
    bpy.ops.mesh.primitive_cone_add(vertices=12, radius1=r, radius2=r if r2 is None else r2, depth=h, location=(cx, cy, z0 + h / 2))
    bpy.context.object.data.materials.append(_m(colour))
    _casts(bpy.context.object, colour)


def glow(cx, cy, w, d, family="cyan"):
    """Light lying on the ground round a w by d rectangle: flat bands, far to near, never a gradient."""
    for i, (colour, reach) in enumerate(GLOW[family]):
        light(cx, cy, .001 + i * .001, w + 2 * reach, d + 2 * reach, .001, colour)


def clear():
    for o in [o for o in SCENE.objects if o.type == 'MESH']:
        bpy.data.objects.remove(o, do_unlink=True)


def snap(px):
    """Every visible pixel becomes its nearest palette colour; alpha becomes fully on or fully off."""
    pal = PAL
    out = np.zeros_like(px)
    on = px[..., 3] > 0.5
    rgb = px[..., :3][on]
    if len(rgb):
        nearest = np.argmin(((rgb[:, None, :] - pal[None, :, :]) ** 2).sum(axis=2), axis=1)
        out[..., :3][on] = pal[nearest]
        out[..., 3][on] = 1
    return out


def save(px, path):
    h, w, _ = px.shape
    img = bpy.data.images.new("out", w, h, alpha=True)
    img.pixels = px.astype(np.float32).ravel()
    img.filepath_raw, img.file_format = path, 'PNG'
    img.save()
    bpy.data.images.remove(img)


SPRITES = []    # every sprite made, in order: (name, pixels, ground_y)


def record(name, px, ground_y):
    """Saves a finished picture and notes it for the manifest and the contact sheet."""
    save(px, os.path.join(OUT, name + ".png"))
    SPRITES.append((name, px, ground_y))


def _shot(w, h):
    """What the camera sees now, as pixels."""
    SCENE.render.filepath = TMP
    bpy.ops.render.render(write_still=True)
    img = bpy.data.images.load(TMP, check_existing=False)
    px = np.array(img.pixels[:]).reshape(h, w, 4)
    bpy.data.images.remove(img)
    return px


def _shadow(w, h):
    """How many tones each pixel drops: 0 in the open, 1 in half shadow, 2 in full shadow."""
    lum = []
    SCENE.render.engine = 'BLENDER_EEVEE'
    SCENE.eevee.taa_render_samples, SCENE.render.filter_size = 64, .6
    for shadows in (True, False):
        SUN.data.use_shadow = shadows
        lum.append(_shot(w, h)[..., :3] @ np.array([.2126, .7152, .0722]))
    SCENE.render.engine = 'BLENDER_WORKBENCH'
    # A face the sun never reaches has no cast shadow on it: there the two renders are both dark, and their ratio is noise.
    kept = np.where(lum[1] > .04, lum[0] / np.maximum(lum[1], 1e-4), 1)
    return (kept < .9).astype(int) + (kept < .5).astype(int)


def _is(px, name):
    return (np.abs(px[..., :3] - np.array(hex_rgb(PALETTE[name]))).sum(axis=2) < .004) & (px[..., 3] > .5)


def _depth(px, drop):
    """Shadow, then edges. Graphite, concrete and paving move down their own ramp in shadow; lights and glow never change.
    Then a light line where a face sits over a clearly darker one, and the darkest colour round the silhouette."""
    out = px.copy()
    for ramp in (RAMP, PAVING):
        for i, name in enumerate(ramp):
            m = _is(px, name)
            for step in (1, 2):
                out[m & (drop == step), :3] = hex_rgb(PALETTE[ramp[max(0, i - step)]])
    tone = np.full(px.shape[:2], -1)
    for i, name in enumerate(RAMP):
        tone[_is(out, name)] = i
    below = np.vstack([tone[:1], tone[:-1]])                      # row 0 is the bottom row
    edge = (tone >= 2) & (below >= 0) & (tone - below >= 2)
    for i in range(len(RAMP)):
        out[edge & (tone == i), :3] = hex_rgb(PALETTE[RAMP[min(i + 2, len(RAMP) - 1)]])
    solid = px[..., 3] > .5
    bare = np.zeros_like(solid)                                    # pixels with nothing beside them
    bare[1:] |= ~solid[:-1]; bare[:-1] |= ~solid[1:]; bare[:, 1:] |= ~solid[:, :-1]; bare[:, :-1] |= ~solid[:, 1:]
    bare[0] = bare[-1] = True; bare[:, 0] = bare[:, -1] = True
    out[(tone >= 0) & bare, :3] = hex_rgb(PALETTE["k0"])
    return out


def render(name, w, h, ground_y):
    """Renders what is in the scene to name.png, w by h, and clears the scene. The ground point (0, 0, 0) lands at the
    horizontal centre, ground_y pixels above the bottom edge."""
    SCENE.render.resolution_x, SCENE.render.resolution_y = w, h
    CAM.data.ortho_scale = max(w, h) / PX_PER_UNIT
    tz = (h / 2 - ground_y) / PX_PER_Z      # aim above the ground point so it sits low in the frame
    d = 40
    CAM.location = (0.612372 * d, -0.612372 * d, tz + 0.5 * d)
    px = snap(_shot(w, h))
    if DEPTH:
        px = _depth(px, _shadow(w, h))
    record(name, px, ground_y)
    clear()


def write_palette():
    """palette.png is the palette as a picture, one pixel a colour, in order."""
    px = np.ones((1, len(PAL), 4))
    px[0, :, :3] = PAL
    save(px, os.path.join(OUT, "palette.png"))


def write_manifest():
    """sprites.json: what every sprite is. The importer takes pivots from it, the tests their expectations."""
    sprites = [{"name": n, "width": int(p.shape[1]), "height": int(p.shape[0]), "groundX": int(p.shape[1]) // 2, "groundY": int(g)}
               for n, p, g in SPRITES]
    with open(os.path.join(OUT, "sprites.json"), "w") as f:
        json.dump({"sprites": sprites}, f, indent=1)
        f.write("\n")


def contact_sheet(max_width=2200, pad=12):
    """One picture of the whole set, on the ground colour: sprites of a kind side by side, small ones enlarged."""
    kinds = []
    for name, *_ in SPRITES:
        kind = name.split("-")[0]
        if kind not in kinds:
            kinds.append(kind)
    rows = []
    for kind in kinds:
        row, width = [], pad
        for name, px, _ in SPRITES:
            if name.split("-")[0] != kind:
                continue
            k = 2 if px.shape[0] > 64 else 3
            big = np.repeat(np.repeat(px, k, axis=0), k, axis=1)
            if row and width + big.shape[1] + pad > max_width:
                rows.append(row)
                row, width = [], pad
            row.append(big)
            width += big.shape[1] + pad
        rows.append(row)
    width = max(sum(p.shape[1] + pad for p in r) for r in rows) + pad
    height = sum(max(p.shape[0] for p in r) + pad for r in rows) + pad
    sheet = np.zeros((height, width, 4))
    sheet[..., :3], sheet[..., 3] = BG, 1
    y = height - pad
    for r in rows:
        x, rh = pad, max(p.shape[0] for p in r)
        for big in r:
            bh, bw, _ = big.shape
            region = sheet[y - rh:y - rh + bh, x:x + bw]
            a = big[..., 3:4]
            region[..., :3] = big[..., :3] * a + region[..., :3] * (1 - a)
            x += bw + pad
        y -= rh + pad
    os.makedirs(os.path.dirname(SHEET), exist_ok=True)
    save(sheet, SHEET)
