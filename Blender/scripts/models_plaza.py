"""The town square the avatars meet in: ring-paved plaza, fountain, benches, lamps, planters, hedges, trees and
the hills on the horizon. Unity's SceneBuilder places the props (see ../MODELS.md for sizes and pivots)."""

import math
import random

import bmesh
from mathutils import Vector

from common import MeshBuilder, bezier

PLAZA_RADIUS = 12.8          # paving ends here; the kerb runs to KERB_RADIUS
KERB_RADIUS = 13.2
LAWN_Z = -0.04               # the lawn sits a step below the paving
PATH_WIDTH = 2.4
PATH_ANGLES = (0, 90, 180, 270)


def _arc(r, a0, a1, step=0.6):
    n = max(2, int(math.ceil(abs(a1 - a0) * r / step)) + 1)
    return [(r * math.cos(a0 + (a1 - a0) * i / (n - 1)), r * math.sin(a0 + (a1 - a0) * i / (n - 1))) for i in range(n)]


def _sector(bm, r0, r1, a0, a1, z0, z1=None):
    """Flat annular sector facing up (z0 at the inner edge, z1 at the outer edge)."""
    z1 = z0 if z1 is None else z1
    outer = [bm.verts.new((x, y, z1)) for (x, y) in _arc(r1, a0, a1)]
    if r0 < 1e-6:
        inner = [bm.verts.new((0.0, 0.0, z0))]
    else:
        inner = [bm.verts.new((x, y, z0)) for (x, y) in reversed(_arc(r0, a0, a1))]
    return bm.faces.new(outer + inner)


def paved_rings(mb, rings):
    """rings: (r0, r1, segments, [colours...]) - each ring is split into `segments` tiles whose colours cycle
    through the list, offset by half a tile on every other ring so the joints stagger."""
    for k, (r0, r1, segs, mats) in enumerate(rings):
        bm = bmesh.new()
        offset = (math.pi / segs) * (k % 2)
        for j in range(segs):
            a0 = offset + 2 * math.pi * j / segs
            f = _sector(bm, r0, r1, a0, a0 + 2 * math.pi / segs, 0.0)
            f.material_index = j % len(mats)
        mb.merge(bm, mats, smooth=False)


def build_plaza_ground(root):
    rnd = random.Random(7)
    mb = MeshBuilder()
    paved_rings(mb, [
        (0.0, 2.7, 8, ["PavingDark"]),
        (2.7, 3.4, 24, ["PavingAccent", "Kerb"]),
        (3.4, 4.8, 28, ["Paving", "PavingDark"]),
        (4.8, 6.2, 34, ["PavingDark", "Paving"]),
        (6.2, 6.7, 40, ["PavingWarm"]),
        (6.7, 8.65, 44, ["Paving", "PavingDark"]),
        (8.65, 10.6, 52, ["PavingDark", "Paving"]),
        (10.6, 11.1, 64, ["PavingAccent", "Kerb"]),
        (11.1, PLAZA_RADIUS, 72, ["Paving", "PavingDark"]),
    ])
    # Kerb: a low raised ring with a rounded-off top edge, stepping down to the lawn.
    mb.lathe((0, 0, 0), [(KERB_RADIUS, LAWN_Z), (KERB_RADIUS, 0.05), (KERB_RADIUS - 0.08, 0.08),
                         (PLAZA_RADIUS + 0.06, 0.08), (PLAZA_RADIUS, 0.04), (PLAZA_RADIUS, 0.0)],
             ["Kerb", "Stone", "StoneLight", "Stone", "Kerb"], seg=96, smooth=False)
    # Lawn: patchy greens out to the hills.
    bm = bmesh.new()
    mats = ["Grass", "GrassDark", "GrassLight"]
    radii = [KERB_RADIUS, 17.0, 22.0, 28.0, 36.0, 48.0, 70.0, 100.0]
    for r0, r1 in zip(radii, radii[1:]):
        segs = max(24, int(2 * math.pi * r1 / 4.0))
        for j in range(segs):
            a0 = 2 * math.pi * j / segs
            f = _sector(bm, r0, r1, a0, a0 + 2 * math.pi / segs, LAWN_Z)
            f.material_index = rnd.choices((0, 1, 2), weights=(5, 3, 2))[0]
    mb.merge(bm, mats, smooth=False)
    # Paths out through the hedge to the lawn.
    for deg in PATH_ANGLES:
        a = math.radians(deg)
        d = Vector((math.cos(a), math.sin(a), 0))
        n = Vector((-d.y, d.x, 0))
        for i, (r0, r1) in enumerate([(KERB_RADIUS - 0.05, 18.5), (18.5, 26.0)]):
            c = d * ((r0 + r1) / 2)
            half = PATH_WIDTH / 2 * (1.0 if i == 0 else 0.9)
            corners = [c - d * (r1 - r0) / 2 - n * half, c + d * (r1 - r0) / 2 - n * half,
                       c + d * (r1 - r0) / 2 + n * half, c - d * (r1 - r0) / 2 + n * half]
            bm = bmesh.new()
            bm.faces.new([bm.verts.new((p.x, p.y, LAWN_Z + 0.015)) for p in corners])
            mb.merge(bm, "PavingWarm", smooth=False)
    mb.to_object("Ground", root)


def build_fountain(root):
    mb = MeshBuilder()
    # Basin: outer wall, rounded rim, inner wall down to the water.
    mb.lathe((0, 0, 0), [(2.62, 0.0), (2.64, 0.36), (2.7, 0.44), (2.66, 0.52), (2.32, 0.52), (2.28, 0.46),
                         (2.28, 0.28)],
             ["Stone", "StoneLight", "StoneLight", "StoneLight", "Stone", "StoneDark"], seg=48,
             flat_segments=(3,))
    mb.lathe((0, 0, 0), [(2.29, 0.32), (1.4, 0.32), (0.0, 0.32)], ["Water", "WaterDeep"], seg=48, smooth=False)
    # Pedestal, lower bowl, upper column and bowl, finial.
    mb.lathe((0, 0, 0), [(0.62, 0.3), (0.56, 0.46), (0.36, 0.62), (0.27, 0.9), (0.25, 1.16)], "Stone", seg=24)
    mb.lathe((0, 0, 0), [(0.25, 1.14), (0.7, 1.22), (1.0, 1.36), (1.07, 1.46), (1.0, 1.51), (0.92, 1.45),
                         (0.92, 1.42)], ["StoneDark", "Stone", "StoneLight", "StoneLight", "Stone", "Stone"], seg=32)
    mb.lathe((0, 0, 0), [(0.93, 1.43), (0.0, 1.43)], "Water", seg=32, smooth=False)
    mb.lathe((0, 0, 0), [(0.21, 1.42), (0.16, 1.72), (0.15, 2.0)], "Stone", seg=16)
    mb.lathe((0, 0, 0), [(0.15, 1.98), (0.42, 2.04), (0.53, 2.15), (0.48, 2.2), (0.43, 2.16), (0.43, 2.15)],
             ["StoneDark", "StoneLight", "StoneLight", "Stone", "Stone"], seg=24)
    mb.lathe((0, 0, 0), [(0.44, 2.16), (0.0, 2.16)], "Water", seg=24, smooth=False)
    mb.sphere((0, 0, 2.32), 0.11, "StoneLight", seg=12, rings=8)
    mb.cylinder((0, 0, 2.2), 0.05, 0.12, "Stone", seg=10)
    # Water spilling over the bowl rims, and foam where it lands.
    for i in range(8):
        a = 2 * math.pi * (i + 0.5) / 8
        d = Vector((math.cos(a), math.sin(a), 0))
        pts = bezier(d * 0.52 + Vector((0, 0, 2.19)), d * 0.66 + Vector((0, 0, 2.2)),
                     d * 0.8 + Vector((0, 0, 1.8)), d * 0.82 + Vector((0, 0, 1.44)), 6)
        mb.tube(pts, 0.022, "WaterFoam", seg=6)
    for i in range(12):
        a = 2 * math.pi * i / 12
        d = Vector((math.cos(a), math.sin(a), 0))
        pts = bezier(d * 1.06 + Vector((0, 0, 1.49)), d * 1.3 + Vector((0, 0, 1.5)),
                     d * 1.55 + Vector((0, 0, 0.9)), d * 1.6 + Vector((0, 0, 0.33)), 7)
        mb.tube(pts, 0.028, "WaterFoam", seg=6)
    mb.torus((0, 0, 0.33), 1.6, 0.05, "WaterFoam", axis="Z", seg=40, mseg=4)
    mb.torus((0, 0, 1.44), 0.8, 0.03, "WaterFoam", axis="Z", seg=28, mseg=4)
    mb.to_object("Basin", root)


def build_bench(root):
    mb = MeshBuilder()
    # Seat slats along X (the bench faces -Y), back slats leaning back.
    for i in range(3):
        y = -0.17 + i * 0.15
        mb.cbox((0, y, 0.44), (1.8, 0.13, 0.05), "Wood" if i % 2 == 0 else "WoodDark", bevel=0.012)
    lean = math.radians(14)
    for i, z in enumerate((0.62, 0.8)):
        y = 0.2 + (z - 0.46) * math.tan(lean)
        mb.cbox((0, y, z), (1.8, 0.05, 0.13), "Wood" if i == 0 else "WoodDark", bevel=0.012)
    # Cast iron ends: legs, a seat rail, a back post and an armrest.
    for x in (-0.78, 0.78):
        mb.cbox((x, -0.18, 0.21), (0.06, 0.06, 0.42), "Iron", bevel=0.01)
        mb.cbox((x, 0.2, 0.21), (0.06, 0.06, 0.42), "Iron", bevel=0.01)
        mb.cbox((x, 0.01, 0.4), (0.06, 0.48, 0.04), "Iron", bevel=0.01)
        mb.cyl_between((x, 0.2, 0.4), (x, 0.2 + 0.5 * math.tan(lean), 0.9), 0.028, "Iron", seg=8)
        mb.cyl_between((x, -0.22, 0.42), (x, -0.2, 0.64), 0.025, "Iron", seg=8)
        mb.cyl_between((x, -0.25, 0.64), (x, 0.24, 0.66), 0.03, "IronLight", seg=8)
    mb.to_object("Seat", root)


def build_lamp(root):
    mb = MeshBuilder()
    mb.cylinder((0, 0, 0.12), 0.2, 0.24, "Iron", seg=12, radius2=0.16, bevel=0.02)
    mb.cylinder((0, 0, 0.3), 0.1, 0.14, "IronLight", seg=12)
    mb.cylinder((0, 0, 1.6), 0.055, 2.6, "Iron", seg=10)
    mb.torus((0, 0, 0.9), 0.07, 0.02, "IronLight", axis="Z", seg=12, mseg=6)
    mb.cylinder((0, 0, 2.92), 0.1, 0.06, "IronLight", seg=12)
    # Lantern: an iron frame around glowing glass, a pyramid cap and a finial.
    mb.cbox((0, 0, 3.18), (0.26, 0.26, 0.4), "LampGlow")
    for sx in (-1, 1):
        for sy in (-1, 1):
            mb.cbox((0.14 * sx, 0.14 * sy, 3.18), (0.035, 0.035, 0.44), "Iron")
    mb.cbox((0, 0, 2.97), (0.34, 0.34, 0.04), "Iron", bevel=0.008)
    mb.cylinder((0, 0, 3.5), 0.27, 0.22, "Iron", seg=4, radius2=0.04, phase=math.pi / 4, smooth=False)
    mb.sphere((0, 0, 3.64), 0.045, "IronLight", seg=8, rings=6)
    mb.to_object("Post", root)


def _crown(mb, blobs, mats, rnd):
    for (c, r) in blobs:
        mb.sphere(c, r, rnd.choice(mats), seg=8, rings=6, smooth=False, rot_z=rnd.uniform(0, 45))


def build_tree_round(root):
    rnd = random.Random(3)
    mb = MeshBuilder()
    mb.cylinder((0, 0, 1.1), 0.2, 2.2, "Bark", seg=9, radius2=0.12, smooth=False)
    mb.cyl_between((0, 0, 1.7), (0.6, 0.2, 2.5), 0.07, "BarkDark", seg=6, smooth=False)
    mb.cyl_between((0, 0, 1.9), (-0.5, -0.3, 2.6), 0.06, "BarkDark", seg=6, smooth=False)
    _crown(mb, [((0, 0, 3.1), 1.45), ((0.8, 0.3, 2.75), 1.0), ((-0.7, -0.35, 2.85), 1.05),
                ((0.15, -0.25, 3.95), 0.95), ((-0.2, 0.6, 3.4), 0.9)],
           ["Leaf", "LeafLight", "LeafDark"], rnd)
    mb.to_object("Tree", root)


def build_tree_tall(root):
    mb = MeshBuilder()
    mb.cylinder((0, 0, 0.6), 0.16, 1.2, "BarkDark", seg=8, radius2=0.12, smooth=False)
    tiers = [(1.0, 1.35, 1.6), (1.9, 1.1, 1.5), (2.75, 0.85, 1.35), (3.5, 0.6, 1.2)]
    for i, (z, r, h) in enumerate(tiers):
        mb.cylinder((0, 0, z + h / 2), r, h, "LeafDark" if i % 2 == 0 else "HedgeDark", seg=9,
                    radius2=0.02, smooth=False, phase=0.3 * i)
    mb.to_object("Tree", root)


def build_planter(root):
    rnd = random.Random(11)
    mb = MeshBuilder()
    mb.cbox((0, 0, 0.26), (1.3, 1.3, 0.52), "Stone", bevel=0.03)
    mb.cbox((0, 0, 0.55), (1.4, 1.4, 0.07), "StoneLight", bevel=0.02)
    mb.cbox((0, 0, 0.57), (1.12, 1.12, 0.04), "Soil")
    for (c, r) in [((0, 0, 0.8), 0.36), ((0.18, 0.12, 1.02), 0.24), ((-0.15, -0.1, 0.98), 0.22)]:
        mb.sphere(c, r, rnd.choice(["HedgeLight", "Leaf", "Hedge"]), seg=8, rings=6, smooth=False)
    # A ring of flowers around the bush, a few colours in clumps.
    flowers = ["Flower1", "Flower2", "Flower3", "Flower4"]
    for i in range(28):
        a = 2 * math.pi * i / 28 + rnd.uniform(-0.08, 0.08)
        rr = rnd.uniform(0.38, 0.5)
        p = Vector((rr * math.cos(a), rr * math.sin(a), rnd.uniform(0.66, 0.74)))
        mb.sphere(p, rnd.uniform(0.06, 0.08), flowers[(i // 3) % len(flowers)], seg=6, rings=4, smooth=False)
        mb.sphere(p * 0.92 + Vector((0, 0, -0.06)), 0.05, "Leaf", seg=5, rings=3, smooth=False)
    mb.to_object("Box", root)


def build_hedge(root):
    """A 3 m hedge segment along X, centred on the origin."""
    rnd = random.Random(5)
    mb = MeshBuilder()
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co = Vector((v.co.x * 3.0, v.co.y * 0.85, 0.55 + v.co.z * 1.1))
    bmesh.ops.bevel(bm, geom=list(bm.verts) + list(bm.edges), offset=0.18, offset_type="OFFSET", segments=2,
                    profile=0.5, affect="EDGES", clamp_overlap=True)
    mb.merge(bm, "Hedge", smooth=False)
    mb.cbox((0, 0, 0.06), (2.9, 0.8, 0.12), "HedgeDark")
    for i in range(5):
        x = -1.2 + i * 0.6 + rnd.uniform(-0.1, 0.1)
        mb.sphere((x, rnd.uniform(-0.12, 0.12), 1.08), rnd.uniform(0.3, 0.38), rnd.choice(["Hedge", "HedgeLight"]),
                  seg=7, rings=5, smooth=False, rot_z=rnd.uniform(0, 60))
    mb.to_object("Bush", root)


def build_hills(root):
    """A ring of rolling hills on the horizon, 80 to 150 m out (far beyond where anyone can walk)."""
    rnd = random.Random(21)
    n = 72
    radii = [78.0, 92.0, 108.0, 126.0, 150.0]
    peaks = [rnd.uniform(4.0, 16.0) for _ in range(n)]
    peaks = [(peaks[i - 1] + 2 * peaks[i] + peaks[(i + 1) % n]) / 4 for i in range(n)]  # soften
    shape = [0.0, 0.55, 1.0, 0.8, 0.6]
    bm = bmesh.new()
    rows = []
    for k, r in enumerate(radii):
        row = []
        for j in range(n):
            a = 2 * math.pi * j / n
            h = peaks[j] * shape[k] * (1.0 + 0.35 * math.sin(3 * a + k))
            row.append(bm.verts.new((r * math.cos(a), r * math.sin(a), LAWN_Z - 0.3 + h)))
        rows.append(row)
    mats = ["HillDark", "Hill", "HillFar"]
    for k in range(len(radii) - 1):
        for j in range(n):
            j1 = (j + 1) % n
            f = bm.faces.new((rows[k][j], rows[k][j1], rows[k + 1][j1], rows[k + 1][j]))
            f.material_index = (0, 1, 2, 2)[k] if rnd.random() > 0.25 else (1, 0, 1, 2)[k]
    bm.normal_update()
    for f in bm.faces:
        if f.normal.z < 0:
            f.normal_flip()
    mb = MeshBuilder()
    mb.merge(bm, mats, smooth=False)
    mb.to_object("Range", root)


MODELS = {
    "PlazaGround": build_plaza_ground,
    "Fountain": build_fountain,
    "Bench": build_bench,
    "Lamp": build_lamp,
    "TreeRound": build_tree_round,
    "TreeTall": build_tree_tall,
    "Planter": build_planter,
    "Hedge": build_hedge,
    "Hills": build_hills,
}
