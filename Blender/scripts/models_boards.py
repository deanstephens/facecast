"""The square's noticeboards and the sticky notes people write and stick on them. Unity's Noticeboard finds the
cork, the title plate and the four notepads by name, and draws each note's handwriting on its paper (see
../MODELS.md for the contract: names, pivots and sizes)."""

import math
import random

import bmesh
from mathutils import Vector

from common import PALETTE, MeshBuilder

PALETTE.update({
    "Cork":           dict(color="#C99A63"),
    "CorkDark":       dict(color="#B5854F"),
    "CorkLight":      dict(color="#D8AE7C"),
    # Sticky note colours; Unity's Notes.Colors matches the bright ones, in this order.
    "NoteYellow":     dict(color="#FFE27A"),
    "NoteYellowEdge": dict(color="#E6C65C"),
    "NotePink":       dict(color="#FFA9C6"),
    "NotePinkEdge":   dict(color="#E68BAA"),
    "NoteBlue":       dict(color="#9DD7FF"),
    "NoteBlueEdge":   dict(color="#7DBAE3"),
    "NoteGreen":      dict(color="#B6EA98"),
    "NoteGreenEdge":  dict(color="#94CC77"),
})

# Board layout (metres, Blender coordinates: the front is -Y, the board's own left is +X).
CORK_W, CORK_H = 2.1, 1.25
CORK_Z = 1.5                 # centre height: eye level
CORK_Y = -0.03               # the cork's surface
RAIL = 0.07                  # frame rails around the cork, standing proud of it
RAIL_FRONT = CORK_Y - 0.022
POST_X = 1.17
POST_TOP = 2.66
TRAY_TOP = 0.805             # the shelf under the cork the notepads sit on
PAD_Y = -0.145
PAD_SIZE = 0.19
PAD_HEIGHT = 0.045
# Viewer's left to right (Blender -X to +X): the pads' colours, in Notes.Colors order.
PADS = (("PadYellow", -0.72, "NoteYellow"), ("PadPink", -0.24, "NotePink"),
        ("PadBlue", 0.24, "NoteBlue"), ("PadGreen", 0.72, "NoteGreen"))
HEADER_W, HEADER_H = 1.24, 0.18
HEADER_Z = 2.305
HEADER_Y = -0.032

NOTE = 0.2                   # a sticky note's side
NOTE_THICK = 0.0012
CURL_FROM = -0.07            # the bottom 3 cm curls away from the board...
CURL_LIFT = 0.007            # ...by up to 7 mm at its corner


def _cork(root):
    """The cork panel: a mottled surface of jittered triangles, its pivot at the surface's centre."""
    rnd = random.Random(17)
    mb = MeshBuilder()
    x0, x1 = -CORK_W / 2, CORK_W / 2
    z0, z1 = CORK_Z - CORK_H / 2, CORK_Z + CORK_H / 2
    mb.box((x0, CORK_Y + 0.002, z0), (x1, 0.0, z1), "CorkDark")
    nx, nz = 30, 18
    mats = ["Cork", "CorkDark", "CorkLight"]
    bm = bmesh.new()
    grid = []
    for j in range(nz + 1):
        row = []
        for i in range(nx + 1):
            x = x0 + CORK_W * i / nx
            z = z0 + CORK_H * j / nz
            if 0 < i < nx:
                x += rnd.uniform(-0.3, 0.3) * CORK_W / nx
            if 0 < j < nz:
                z += rnd.uniform(-0.3, 0.3) * CORK_H / nz
            row.append(bm.verts.new((x, CORK_Y, z)))
        grid.append(row)
    for j in range(nz):
        for i in range(nx):
            a, b, c, d = grid[j][i], grid[j][i + 1], grid[j + 1][i + 1], grid[j + 1][i]
            # Counter-clockwise seen from the front (-Y): bottom-left, top-left, top-right.
            for tri in ((a, d, c), (a, c, b)):
                f = bm.faces.new(tri)
                f.material_index = rnd.choices((0, 1, 2), weights=(6, 2, 2))[0]
    bm.normal_update()
    for f in bm.faces:
        if f.normal.y > 0:
            f.normal_flip()
    mb.merge(bm, mats, smooth=False)
    mb.to_object("Cork", root, pivot=(0.0, CORK_Y, CORK_Z))


def _roof(mb):
    """A small pitched canopy in the board's colour (Tint corners), shingled on the front slope."""
    eave, ridge, back = Vector((-0.34, 2.5)), Vector((0.02, 2.7)), Vector((0.26, 2.56))
    x0, x1 = -1.36, 1.36
    d = ridge - eave
    up = Vector((-d.y, d.x)).normalized()
    if up.y < 0:
        up = -up
    rows = 5
    for k in range(rows):
        a = eave + d * (k / rows) - d.normalized() * (0.02 if k else 0.0)
        b = eave + d * ((k + 1) / rows)
        lift = 0.012 * (rows - k) / rows
        poly = [tuple(a - up * 0.01), tuple(b), tuple(b + up * 0.02), tuple(a + up * (0.035 + lift))]
        mb.prism_x(poly, x0, x1, "Tint" if k % 2 == 0 else "TintShade")
    bd = back - ridge
    bup = Vector((bd.y, -bd.x)).normalized()
    if bup.y < 0:
        bup = -bup
    mb.prism_x([tuple(ridge), tuple(back), tuple(back + bup * 0.03), tuple(ridge + bup * 0.03)], x0, x1, "TintShade")
    mb.cylinder((0, ridge.x, ridge.y + 0.03), 0.035, x1 - x0 + 0.04, "TintDark", axis="X", seg=10)


def _frame(root):
    mb = MeshBuilder()
    x0, x1 = -CORK_W / 2, CORK_W / 2
    z0, z1 = CORK_Z - CORK_H / 2, CORK_Z + CORK_H / 2
    # Backboard and the rails around the cork.
    mb.box((x0 - RAIL, 0.0, z0 - RAIL), (x1 + RAIL, 0.025, z1 + RAIL), "WoodDark")
    mb.box((x0 - RAIL, RAIL_FRONT, z1), (x1 + RAIL, 0.0, z1 + RAIL), "Wood", bevel=0.01)
    mb.box((x0 - RAIL, RAIL_FRONT, z0 - RAIL), (x1 + RAIL, 0.0, z0), "Wood", bevel=0.01)
    mb.box((x0 - RAIL, RAIL_FRONT, z0), (x0, 0.0, z1), "Wood", bevel=0.01)
    mb.box((x1, RAIL_FRONT, z0), (x1 + RAIL, 0.0, z1), "Wood", bevel=0.01)
    # Posts on stone footings, up into the roof.
    for sx in (-1, 1):
        mb.cbox((sx * POST_X, 0.012, POST_TOP / 2), (0.1, 0.1, POST_TOP), "WoodDark", bevel=0.015)
        mb.cbox((sx * POST_X, 0.012, 0.06), (0.21, 0.21, 0.12), "Stone", bevel=0.02)
    # The title plate's frame, in the board's colour (the cream plate itself is the Header child).
    mb.cbox((0, 0.0, HEADER_Z), (HEADER_W + 0.08, 0.04, HEADER_H + 0.04), "Tint", bevel=0.012)
    _roof(mb)
    # The shelf for the notepads, on brackets, with a lip so nothing slides off.
    mb.box((-1.0, -0.27, TRAY_TOP - 0.03), (1.0, 0.0, TRAY_TOP), "Wood", bevel=0.008)
    mb.box((-1.0, -0.29, TRAY_TOP - 0.03), (1.0, -0.26, TRAY_TOP + 0.022), "WoodDark", bevel=0.006)
    for x in (-0.86, 0.0, 0.86):
        mb.prism_x([(0.0, TRAY_TOP - 0.03), (-0.2, TRAY_TOP - 0.03), (0.0, TRAY_TOP - 0.24)], x - 0.02, x + 0.02,
                   "WoodDark")
    mb.to_object("Frame", root)


def _header(root):
    mb = MeshBuilder()
    mb.cbox((0, HEADER_Y + 0.006, HEADER_Z), (HEADER_W, 0.012, HEADER_H), "Canvas", bevel=0.004)
    mb.to_object("Header", root, pivot=(0.0, HEADER_Y, HEADER_Z))


def _pad(root, name, x, colour):
    """A stack of sticky notes on the shelf, its pivot at the middle of the top sheet."""
    mb = MeshBuilder()
    layers = 6
    h = PAD_HEIGHT / layers
    for k in range(layers):
        z = TRAY_TOP + k * h
        top = k == layers - 1
        inset = 0.0 if k % 2 == 0 else 0.002
        mb.box((x - PAD_SIZE / 2 + inset, PAD_Y - PAD_SIZE / 2 + inset, z),
               (x + PAD_SIZE / 2 - inset, PAD_Y + PAD_SIZE / 2 - inset, z + h),
               colour if top or k % 2 == 0 else colour + "Edge")
    mb.to_object(name, root, pivot=(x, PAD_Y, TRAY_TOP + PAD_HEIGHT))


def build_noticeboard(root):
    _frame(root)
    _cork(root)
    _header(root)
    for name, x, colour in PADS:
        _pad(root, name, x, colour)


def _curl(x, z):
    """How far the paper lifts off the board (along -Y) at (x, z): only the bottom strip, most at one corner."""
    s = max(0.0, (CURL_FROM - z) / (CURL_FROM + NOTE / 2))
    k = 0.35 + 0.65 * (x + NOTE / 2) / NOTE
    return -CURL_LIFT * s * s * k


def build_sticky_note(root):
    """A sticky note: flat where its glue holds it to the board, curling forward at the bottom."""
    half = NOTE / 2
    xs = [-half, -0.05, 0.0, 0.05, half]
    zs = [half, 0.02, CURL_FROM, -0.077, -0.084, -0.09, -0.095, -half]
    bm = bmesh.new()
    front = [[bm.verts.new((x, _curl(x, z) - NOTE_THICK, z)) for x in xs] for z in zs]
    back = [[bm.verts.new((x, _curl(x, z), z)) for x in xs] for z in zs]
    faces = []  # (face, material index, wanted normal sign along y or None)
    for j in range(len(zs) - 1):
        for i in range(len(xs) - 1):
            faces.append((bm.faces.new((front[j][i], front[j][i + 1], front[j + 1][i + 1], front[j + 1][i])), 0, -1))
            faces.append((bm.faces.new((back[j][i], back[j + 1][i], back[j + 1][i + 1], back[j][i + 1])), 1, 1))
    # The paper's edges.
    ring = [(0, i) for i in range(len(xs))] + [(j, len(xs) - 1) for j in range(1, len(zs))] \
        + [(len(zs) - 1, i) for i in range(len(xs) - 2, -1, -1)] + [(j, 0) for j in range(len(zs) - 2, 0, -1)]
    for k in range(len(ring)):
        (j0, i0), (j1, i1) = ring[k], ring[(k + 1) % len(ring)]
        faces.append((bm.faces.new((front[j0][i0], back[j0][i0], back[j1][i1], front[j1][i1])), 1, None))
    bm.normal_update()
    for f, mi, sign in faces:
        f.material_index = mi
        f.smooth = False
        if sign is not None and f.normal.y * sign < 0:
            f.normal_flip()
    # Edge faces point outwards from the paper's centre.
    for f, mi, sign in faces:
        if sign is None:
            c = f.calc_center_median()
            if f.normal.dot(Vector((c.x, 0.0, c.z))) < 0:
                f.normal_flip()
    mb = MeshBuilder()
    mb.merge(bm, ["Tint", "TintShade"])
    mb.to_object("Paper", root)


MODELS = {
    "NoticeBoard": build_noticeboard,
    "StickyNote": build_sticky_note,
}
