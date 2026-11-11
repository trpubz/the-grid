"""The sprite kit: the camera, the palette, and what every sprite is built, rendered and saved with.

How it stays pixel art: the Workbench engine with flat lighting gives every face one flat colour, anti-aliasing is off,
and every pixel is then snapped to the palette. The camera is orthographic, 30 degrees above the ground and turned 45
degrees, which draws a 1 by 1 ground square as a 64 by 32 diamond: one tile. That grain is fixed; see docs/art-direction.md.
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
    "white": "#ffffff",                                                      # the part of a bot the game tints
}
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
    return s, ob


SCENE, CAM = setup()
MATS = {}
TURN = 0    # quarter turns applied to everything built, so one model gives four facings


def mat(h):
    """The flat material for a hex colour."""
    if h not in MATS:
        m = bpy.data.materials.new(h)
        lin = lambda c: c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4
        m.diffuse_color = tuple(lin(c) for c in hex_rgb(h)) + (1,)
        MATS[h] = m
    return MATS[h]


def _m(name):
    if name not in PALETTE:
        raise KeyError(f"'{name}' is not a palette colour; the palette is in tools/art/kit.py")
    return mat(PALETTE[name])


def _turned(cx, cy, w=0.0, d=0.0):
    for _ in range(TURN % 4):
        cx, cy, w, d = -cy, cx, d, w
    return cx, cy, w, d


def box(cx, cy, z0, w, d, h, top, left=None, right=None):
    """A box standing on z0, centred on (cx, cy). Flat lighting has no shading, so the three faces the camera sees
    (top, left, right) each get their own palette colour."""
    left = left or top
    right = right or left
    cx, cy, w, d = _turned(cx, cy, w, d)
    bpy.ops.mesh.primitive_cube_add(size=1, location=(cx, cy, z0 + h / 2))
    o = bpy.context.object
    o.scale = (w, d, h)
    for c in (top, left, right):
        o.data.materials.append(_m(c))
    for p in o.data.polygons:
        n = p.normal
        p.material_index = 0 if n.z > .5 else 1 if n.y < -.5 else 2


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


def ball(cx, cy, z, r, colour):
    cx, cy, _, _ = _turned(cx, cy)
    bpy.ops.mesh.primitive_uv_sphere_add(segments=16, ring_count=10, radius=r, location=(cx, cy, z))
    bpy.context.object.data.materials.append(_m(colour))


def limb(cx, cy, z0, r, h, colour, r2=None):
    """An upright cylinder, or a cone when r2 (its radius at the top) is given."""
    cx, cy, _, _ = _turned(cx, cy)
    bpy.ops.mesh.primitive_cone_add(vertices=12, radius1=r, radius2=r if r2 is None else r2, depth=h, location=(cx, cy, z0 + h / 2))
    bpy.context.object.data.materials.append(_m(colour))


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


def render(name, w, h, ground_y):
    """Renders what is in the scene to name.png, w by h, and clears the scene. The ground point (0, 0, 0) lands at the
    horizontal centre, ground_y pixels above the bottom edge."""
    SCENE.render.resolution_x, SCENE.render.resolution_y = w, h
    CAM.data.ortho_scale = max(w, h) / PX_PER_UNIT
    tz = (h / 2 - ground_y) / PX_PER_Z      # aim above the ground point so it sits low in the frame
    d = 40
    CAM.location = (0.612372 * d, -0.612372 * d, tz + 0.5 * d)
    SCENE.render.filepath = TMP
    bpy.ops.render.render(write_still=True)
    img = bpy.data.images.load(TMP, check_existing=False)
    px = snap(np.array(img.pixels[:]).reshape(h, w, 4))
    bpy.data.images.remove(img)
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
