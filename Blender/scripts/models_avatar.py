"""The player avatar: a small hovering figure with a helmet head whose front is a screen showing the player's
webcam. See ../MODELS.md for the contract Unity relies on (child names, pivots, the Face UVs)."""

import math

import bmesh
import bpy
from mathutils import Vector

from common import MeshBuilder, add_anchor, set_planar_uv

# Head and screen layout (metres, Blender coordinates: front is -Y, the avatar's left is +X).
HEAD_CENTER = Vector((0.0, 0.0, 1.5))
HEAD_SIZE = Vector((0.6, 0.48, 0.5))
NECK_Z = 1.25                       # the Head pivot: it pitches about the neck
SCREEN_W, SCREEN_H = 0.42, 0.315    # 4:3, the shape of most webcam pictures
SCREEN_RADIUS = 0.06
BEZEL_FRONT = -0.2525               # the dark bezel set into the helmet's front (the helmet's face is at -0.24)
SCREEN_Y = -0.254                   # the screen's rim, just proud of the bezel
SCREEN_BULGE = 0.01


def rounded_box(mb, center, size, mat, bevel, seg=4):
    """Box with rounded edges, smooth shaded (MeshBuilder.box is flat shaded)."""
    c, s = Vector(center), Vector(size)
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co = Vector((c.x + v.co.x * s.x, c.y + v.co.y * s.y, c.z + v.co.z * s.z))
    bmesh.ops.bevel(bm, geom=list(bm.verts) + list(bm.edges), offset=bevel, offset_type="OFFSET",
                    segments=seg, profile=0.5, affect="EDGES", clamp_overlap=True)
    return mb.merge(bm, mat, smooth=True)


def rounded_rect_xz(cx, cz, hw, hh, r, seg_per_corner=6):
    """Closed rounded rectangle in the XZ plane, counter-clockwise seen from the front (-Y)."""
    pts = []
    corners = [(hw - r, hh - r, 0.0), (-hw + r, hh - r, 90.0), (-hw + r, -hh + r, 180.0), (hw - r, -hh + r, 270.0)]
    for ox, oz, a0 in corners:
        for i in range(seg_per_corner + 1):
            a = math.radians(a0 + 90.0 * i / seg_per_corner)
            pts.append((cx + ox + r * math.cos(a), cz + oz + r * math.sin(a)))
    return pts


def screen_mesh(mb, mat):
    """The webcam screen: a rounded 4:3 panel bulging gently forward, as concentric rings around its centre so
    the bulge is smooth. Its UVs are set after the object exists (set_planar_uv)."""
    outline = rounded_rect_xz(0.0, 0.0, SCREEN_W / 2, SCREEN_H / 2, SCREEN_RADIUS)
    rings = [0.25, 0.5, 0.75, 0.9, 1.0]
    bm = bmesh.new()
    cz = HEAD_CENTER.z
    centre = bm.verts.new((0.0, SCREEN_Y - SCREEN_BULGE, cz))
    loops = []
    for s in rings:
        y = SCREEN_Y - SCREEN_BULGE * (1.0 - s * s)
        loops.append([bm.verts.new((x * s, y, cz + z * s)) for (x, z) in outline])
    n = len(outline)
    for j in range(n):
        bm.faces.new((centre, loops[0][(j + 1) % n], loops[0][j]))
    for a, b in zip(loops, loops[1:]):
        for j in range(n):
            j1 = (j + 1) % n
            bm.faces.new((a[j], a[j1], b[j1], b[j]))
    bm.normal_update()
    if sum(f.normal.y for f in bm.faces) > 0:  # must face the front (-Y)
        bmesh.ops.reverse_faces(bm, faces=bm.faces)
    return mb.merge(bm, mat, smooth=True)


def build_avatar(root):
    # Body: hover ring, egg-shaped torso, belt, collar and neck. Pivot at the ground; Unity bobs it.
    body = MeshBuilder()
    body.torus((0, 0, 0.09), 0.15, 0.026, "HoverGlow", axis="Z", seg=24, mseg=8)
    body.cylinder((0, 0, 0.15), 0.11, 0.06, "Joint", seg=20, radius2=0.14)
    profile = [(0.0, 0.16), (0.12, 0.17), (0.2, 0.22), (0.26, 0.31), (0.295, 0.43), (0.305, 0.56),
               (0.3, 0.69), (0.285, 0.82), (0.255, 0.94), (0.21, 1.05), (0.15, 1.13), (0.0, 1.17)]
    mats = ["TintShade"] * 5 + ["Tint"] * 6
    body.lathe((0, 0, 0), profile, mats, seg=28)
    body.torus((0, 0, 0.56), 0.302, 0.03, "Joint", axis="Z", seg=32, mseg=8)
    body.cylinder((0, -0.296, 0.56), 0.05, 0.03, "JointLight", axis="-Y", seg=16, bevel=0.008)
    body.cylinder((0, -0.281, 0.84), 0.045, 0.03, "HoverGlow", axis="-Y", seg=16)
    body.torus((0, 0, 1.14), 0.14, 0.04, "Joint", axis="Z", seg=24, mseg=8)
    body.cylinder((0, 0, 1.21), 0.075, 0.14, "Joint", seg=16)
    body.to_object("Body", root, pivot=(0, 0, 0))

    # Hands float beside the body: mittens with a thumb. Pivots at the hand centres; Unity sways them.
    for name, side in (("HandL", 1.0), ("HandR", -1.0)):
        hand = MeshBuilder()
        c = Vector((0.41 * side, -0.05, 0.6))
        hand.sphere(c, 0.085, "Tint", seg=16, rings=10, scale=(0.85, 0.75, 1.1))
        hand.sphere(c + Vector((-0.045 * side, -0.05, 0.03)), 0.035, "Tint", seg=10, rings=6)
        hand.torus(c + Vector((0, 0, -0.07)), 0.055, 0.016, "Joint", axis="Z", seg=16, mseg=6)
        hand.to_object(name, root, pivot=c)

    # Head: a rounded helmet in the player's colour with a dark visor, ear pods and an antenna whose lights
    # glow while the player speaks (Signal). Pivot at the neck.
    head = MeshBuilder()
    rounded_box(head, HEAD_CENTER, HEAD_SIZE, "Tint", bevel=0.09, seg=4)
    bezel_depth = 0.06
    rounded_box(head, (0, BEZEL_FRONT + bezel_depth / 2, HEAD_CENTER.z), (SCREEN_W + 0.04, bezel_depth, SCREEN_H + 0.04),
                "Visor", bevel=0.025, seg=3)
    for side in (1.0, -1.0):
        pod = Vector((0.302 * side, 0.0, HEAD_CENTER.z))
        head.cylinder(pod, 0.085, 0.06, "TintDark", axis="X", seg=20, bevel=0.012, bseg=2)
        head.cylinder(pod + Vector((0.032 * side, 0, 0)), 0.052, 0.02, "Signal", axis="X", seg=20)
    tip = Vector((0.15, 0.06, 1.9))
    head.cyl_between((0.12, 0.05, 1.72), tip, 0.012, "Joint", seg=8)
    head.sphere(tip, 0.032, "Signal", seg=12, rings=8)
    head.to_object("Head", root, pivot=(0, 0, NECK_Z))

    # Face: the webcam screen; pivot at the screen centre. Exported beside Head like every other part (the FBX
    # axis conversion mangles nested meshes); Unity's prefab builder parents it to Head so it pitches with it.
    face = MeshBuilder()
    screen_mesh(face, "Screen")
    face_ob = face.to_object("Face", root, pivot=(0, SCREEN_Y, HEAD_CENTER.z))
    bpy.context.view_layer.update()
    set_planar_uv(face_ob, (-SCREEN_W / 2, HEAD_CENTER.z - SCREEN_H / 2), (SCREEN_W / 2, HEAD_CENTER.z + SCREEN_H / 2))

    # Eye: where the first-person camera sits (inside the head, behind the screen).
    add_anchor("Eye", root, (0, -0.08, HEAD_CENTER.z))
    # Nameplate: where the player's name floats.
    add_anchor("Nameplate", root, (0, 0, 2.08))


MODELS = {"Avatar": build_avatar}
