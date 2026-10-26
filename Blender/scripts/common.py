"""Shared helpers for the Facecast procedural asset pipeline.

Conventions (see ../MODELS.md):
  * 1 Blender unit = 1 m, scene unit scale 1.0, +Z up.
  * A model's front faces -Y (it becomes Unity +Z / transform.forward after export).
  * Every model = one EMPTY root named like the model at the world origin, with named mesh children
    (and optional EMPTY anchors) parented to it. Child origins sit at their pivots; rotation and scale
    are always identity.
  * Ground-standing models have their ground contact at z = 0.
  * Colour lives in a per-corner vertex colour attribute ("Col", sRGB bytes), never in textures or
    material slots: every exported mesh carries the single material "M_Vertex". Alpha encodes how Unity's
    Flat shader treats the corner:
        A = 0      plain surface
        A = 0.25   tint: the grey vertex colour is multiplied by the owner's colour (an avatar's chosen colour)
        A = 0.5    always emissive (lamp glass, hover rings)
        A = 1      "signal" glow that Unity switches per renderer (an avatar's speaking lights)
  * Faces are built from the fixed palette below (names only matter inside the Blender scripts). A model
    module may add its own colours with PALETTE.update({...}) at import time; prefix them to avoid clashes.
  * The one exception to "no UVs": an avatar's Face screen gets planar 0..1 UVs (set_planar_uv) because
    Unity draws the player's webcam on it.

All geometry is built in *world* coordinates (model root at the origin) with bmesh and only converted to
pivot-relative coordinates when the object is created, so pivots are exact by construction.
"""

import math
import os

import bmesh
import bpy
from mathutils import Matrix, Vector

# --------------------------------------------------------------------------
# Paths
# --------------------------------------------------------------------------
SCRIPTS_DIR = os.path.dirname(os.path.abspath(__file__))
BLENDER_DIR = os.path.dirname(SCRIPTS_DIR)
PROJECT_DIR = os.path.dirname(BLENDER_DIR)
SOURCE_DIR = os.path.join(BLENDER_DIR, "source")
PREVIEW_DIR = os.path.join(BLENDER_DIR, "previews")
MODELS_DIR = os.path.join(PROJECT_DIR, "Assets", "_Project", "Art", "Models")

# --------------------------------------------------------------------------
# Palette. emit: "always" (A = 0.5), "team" (A = 0.25, the avatar's tint) or "signal" (A = 1.0).
# Facecast is a sunny town square at golden hour, seen at eye level: warm stone, fresh greens, and toy-like
# avatars in the colour each player picks.
# --------------------------------------------------------------------------
PALETTE = {
    # avatar tint (greys: Unity multiplies them by the player's colour)
    "Tint":         dict(color="#FFFFFF", emit="team"),
    "TintShade":    dict(color="#C4C4C4", emit="team"),
    "TintDark":     dict(color="#8C8C8C", emit="team"),
    # avatar fittings
    "Visor":        dict(color="#16181E"),
    "Screen":       dict(color="#0C0E12"),
    "Joint":        dict(color="#3A3E48"),
    "JointLight":   dict(color="#666C78"),
    "HoverGlow":    dict(color="#A8ECFF", emit="always"),
    "Signal":       dict(color="#FFFFFF", emit="signal"),
    # paving and stone
    "Paving":       dict(color="#DDD2BC"),
    "PavingDark":   dict(color="#C6B79B"),
    "PavingAccent": dict(color="#A8967A"),
    "PavingWarm":   dict(color="#D9B98F"),
    "Kerb":         dict(color="#9D9384"),
    "Stone":        dict(color="#BDB4A4"),
    "StoneDark":    dict(color="#8F8778"),
    "StoneLight":   dict(color="#E2DACB"),
    # plants and ground
    "Grass":        dict(color="#86B85C"),
    "GrassDark":    dict(color="#6FA34B"),
    "GrassLight":   dict(color="#9CC86E"),
    "Hedge":        dict(color="#4E8B3B"),
    "HedgeDark":    dict(color="#3B6F2D"),
    "HedgeLight":   dict(color="#63A048"),
    "Leaf":         dict(color="#6EAE48"),
    "LeafLight":    dict(color="#8DC75C"),
    "LeafDark":     dict(color="#4C8C35"),
    "LeafAutumn":   dict(color="#E0A040"),
    "Bark":         dict(color="#7A5638"),
    "BarkDark":     dict(color="#5A3F29"),
    "Soil":         dict(color="#6B4E37"),
    "Flower1":      dict(color="#F2788A"),
    "Flower2":      dict(color="#FFD25A"),
    "Flower3":      dict(color="#B58CF2"),
    "Flower4":      dict(color="#FAF4EA"),
    "Hill":         dict(color="#93BD6E"),
    "HillDark":     dict(color="#77A257"),
    "HillFar":      dict(color="#A8C69C"),
    # wood, metal, water, odds and ends
    "Wood":         dict(color="#B47E4A"),
    "WoodDark":     dict(color="#86592F"),
    "Iron":         dict(color="#3B4049"),
    "IronLight":    dict(color="#656C78"),
    "LampGlow":     dict(color="#FFE7A8", emit="always"),
    "Water":        dict(color="#5DB9DA"),
    "WaterDeep":    dict(color="#3E92B8"),
    "WaterFoam":    dict(color="#E6F8FD"),
    "Canvas":       dict(color="#F3E8D2"),
    "Awning":       dict(color="#E5705B"),
    "White":        dict(color="#F7F4EE"),
    "Black":        dict(color="#2A2A2E"),
}

EMIT_ALPHA = {None: 0.0, "team": 64 / 255.0, "always": 128 / 255.0, "teamglow": 192 / 255.0, "signal": 1.0}

# Previews show tinted corners in sky blue (Unity swaps in each player's colour).
PREVIEW_TEAM_HEX = "#5AA6F2"


def _srgb_to_linear(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def hex_to_srgb(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4))


def hex_to_linear(h):
    return tuple(_srgb_to_linear(c) for c in hex_to_srgb(h))


def palette_rgba(name):
    """sRGB colour + alpha code stored in the vertex colour attribute."""
    spec = PALETTE[name]
    return hex_to_srgb(spec["color"]) + (EMIT_ALPHA[spec.get("emit")],)


def check_palette(name):
    if name not in PALETTE:
        raise KeyError("Colour %r is not in the palette" % name)
    return name


EXPORT_MATERIAL = "M_Vertex"


def get_vertex_material():
    """The one material every exported mesh uses. In Blender it shows the vertex colours (so previews show
    what Unity will get, with team colour as Dawn gold) and lights up emissive corners."""
    mat = bpy.data.materials.get(EXPORT_MATERIAL)
    if mat is not None:
        return mat
    mat = bpy.data.materials.new(EXPORT_MATERIAL)
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = nt.nodes.get("Principled BSDF")
    bsdf.inputs["Roughness"].default_value = 0.85
    attr = nt.nodes.new("ShaderNodeVertexColor")
    attr.layer_name = "Col"

    def math(op, a, b):
        n = nt.nodes.new("ShaderNodeMath")
        n.operation = op
        for i, v in enumerate((a, b)):
            if isinstance(v, (int, float)):
                n.inputs[i].default_value = v
            else:
                nt.links.new(v, n.inputs[i])
        return n.outputs[0]

    alpha = attr.outputs["Alpha"]
    # team corners: 0.125 < a < 0.375 (team) or 0.625 < a < 0.875 (team glow)
    t1 = math("MULTIPLY", math("GREATER_THAN", alpha, 0.125), math("LESS_THAN", alpha, 0.375))
    t2 = math("MULTIPLY", math("GREATER_THAN", alpha, 0.625), math("LESS_THAN", alpha, 0.875))
    team = math("MAXIMUM", t1, t2)
    mix = nt.nodes.new("ShaderNodeMixRGB")
    mix.blend_type = "MULTIPLY"
    nt.links.new(team, mix.inputs["Fac"])
    nt.links.new(attr.outputs["Color"], mix.inputs["Color1"])
    mix.inputs["Color2"].default_value = hex_to_linear(PREVIEW_TEAM_HEX) + (1.0,)
    nt.links.new(mix.outputs["Color"], bsdf.inputs["Base Color"])
    nt.links.new(mix.outputs["Color"], bsdf.inputs["Emission Color"])
    # a > 0.375 -> emissive in previews (always, team glow and signal kinds)
    glow = math("MULTIPLY", math("GREATER_THAN", alpha, 0.375), 2.5)
    nt.links.new(glow, bsdf.inputs["Emission Strength"])
    mat.diffuse_color = (0.8, 0.8, 0.8, 1.0)
    mat.use_backface_culling = True  # Unity culls back faces; mirror that so flipped normals show up
    return mat


# The geometry helpers validate colour names through this.
get_material = check_palette
HAZARD = ("Awning", "White")

# --------------------------------------------------------------------------
# Small math helpers
# --------------------------------------------------------------------------
AXES = {
    "X": Vector((1, 0, 0)), "Y": Vector((0, 1, 0)), "Z": Vector((0, 0, 1)),
    "-X": Vector((-1, 0, 0)), "-Y": Vector((0, -1, 0)), "-Z": Vector((0, 0, -1)),
}


def V(*a):
    if len(a) == 1:
        return Vector(a[0])
    return Vector(a)


def axis_vec(axis):
    return AXES[axis].copy() if isinstance(axis, str) else Vector(axis).normalized()


def z_to_axis_matrix(axis):
    """4x4 rotation taking local +Z onto `axis`."""
    a = axis_vec(axis)
    if (a - Vector((0, 0, -1))).length < 1e-9:
        return Matrix.Rotation(math.pi, 4, "X")
    return Vector((0, 0, 1)).rotation_difference(a).to_matrix().to_4x4()


def circle_profile(r, n, phase=0.0):
    """CCW circle in the (u, v) plane."""
    return [(r * math.cos(phase + 2 * math.pi * i / n), r * math.sin(phase + 2 * math.pi * i / n))
            for i in range(n)]


def rect_profile(w, h):
    return [(-w / 2, -h / 2), (w / 2, -h / 2), (w / 2, h / 2), (-w / 2, h / 2)]


def rounded_rect_path(cx, cy, z, hx, hy, r, seg_per_corner=4):
    """Closed CCW rounded rectangle in the XY plane (list of points, no repeat)."""
    pts = []
    corners = [(hx - r, hy - r, 0.0), (-hx + r, hy - r, 90.0), (-hx + r, -hy + r, 180.0), (hx - r, -hy + r, 270.0)]
    for (ox, oy, a0) in corners:
        for i in range(seg_per_corner + 1):
            a = math.radians(a0 + 90.0 * i / seg_per_corner)
            pts.append(Vector((cx + ox + r * math.cos(a), cy + oy + r * math.sin(a), z)))
    return pts


def bezier(p0, p1, p2, p3, n):
    """n+1 points on a cubic bezier (inclusive)."""
    p0, p1, p2, p3 = (Vector(p) for p in (p0, p1, p2, p3))
    out = []
    for i in range(n + 1):
        t = i / n
        mt = 1 - t
        out.append(p0 * mt ** 3 + p1 * 3 * mt * mt * t + p2 * 3 * mt * t * t + p3 * t ** 3)
    return out


def arc_points(center, radius, a0, a1, n, plane="XZ"):
    """Arc from angle a0 to a1 (degrees) with n segments in the given plane."""
    c = Vector(center)
    out = []
    for i in range(n + 1):
        a = math.radians(a0 + (a1 - a0) * i / n)
        ca, sa = math.cos(a), math.sin(a)
        if plane == "XZ":
            out.append(c + Vector((radius * ca, 0, radius * sa)))
        elif plane == "XY":
            out.append(c + Vector((radius * ca, radius * sa, 0)))
        else:  # YZ
            out.append(c + Vector((0, radius * ca, radius * sa)))
    return out


def fillet_path(points, radius, seg=6):
    """Polyline with every interior corner replaced by a circular arc (pipe elbows)."""
    pts = [Vector(p) for p in points]
    out = [pts[0]]
    for i in range(1, len(pts) - 1):
        p0, p1, p2 = pts[i - 1], pts[i], pts[i + 1]
        d1 = (p1 - p0).normalized()
        d2 = (p2 - p1).normalized()
        ang = d1.angle(d2, 0.0)
        if ang < 1e-4:
            out.append(p1)
            continue
        t = min(radius * math.tan(ang / 2), (p1 - p0).length * 0.5, (p2 - p1).length * 0.5)
        r = t / math.tan(ang / 2)
        a = p1 - d1 * t
        nrm = (d2 - d1 * d1.dot(d2)).normalized()
        c = a + nrm * r
        axis = d1.cross(d2).normalized()
        for k in range(seg + 1):
            out.append(c + Matrix.Rotation(ang * k / seg, 3, axis) @ (a - c))
    out.append(pts[-1])
    return out


def clip_polygon_rect(poly, x0, y0, x1, y1):
    """Sutherland-Hodgman clip of a 2D polygon against an axis-aligned rect."""
    def clip(points, inside, intersect):
        out = []
        n = len(points)
        for i in range(n):
            cur, prev = points[i], points[i - 1]
            if inside(cur):
                if not inside(prev):
                    out.append(intersect(prev, cur))
                out.append(cur)
            elif inside(prev):
                out.append(intersect(prev, cur))
        return out

    def ix(xc):
        return lambda p, q: (xc, p[1] + (q[1] - p[1]) * (xc - p[0]) / (q[0] - p[0]))

    def iy(yc):
        return lambda p, q: (p[0] + (q[0] - p[0]) * (yc - p[1]) / (q[1] - p[1]), yc)

    pts = list(poly)
    for inside, inter in (
        (lambda p: p[0] >= x0 - 1e-12, ix(x0)),
        (lambda p: p[0] <= x1 + 1e-12, ix(x1)),
        (lambda p: p[1] >= y0 - 1e-12, iy(y0)),
        (lambda p: p[1] <= y1 + 1e-12, iy(y1)),
    ):
        if not pts:
            break
        pts = clip(pts, inside, inter)
    # drop near-duplicate consecutive points
    clean = []
    for p in pts:
        if not clean or abs(p[0] - clean[-1][0]) > 1e-7 or abs(p[1] - clean[-1][1]) > 1e-7:
            clean.append(p)
    if len(clean) > 1 and abs(clean[0][0] - clean[-1][0]) < 1e-7 and abs(clean[0][1] - clean[-1][1]) < 1e-7:
        clean.pop()
    return clean if len(clean) >= 3 else []


def polygon_area(poly):
    return 0.5 * sum(poly[i - 1][0] * poly[i][1] - poly[i][0] * poly[i - 1][1] for i in range(len(poly)))


# --------------------------------------------------------------------------
# MeshBuilder: accumulates primitives (world coords) into one bmesh
# --------------------------------------------------------------------------
class MeshBuilder:
    def __init__(self):
        self.bm = bmesh.new()
        self.mat_names = []

    # -- material slots ------------------------------------------------------
    def _mi(self, name):
        get_material(name)  # validate palette name early
        if name not in self.mat_names:
            self.mat_names.append(name)
        return self.mat_names.index(name)

    # -- merge a temporary bmesh into the builder -----------------------------
    def merge(self, bm2, mats, matrix=None, smooth=None):
        """Copy bm2 into the builder. `mats` is a material name or a list
        indexed by bm2's face.material_index. bm2 is freed."""
        if isinstance(mats, str):
            mats = [mats]
        remap = [self._mi(m) for m in mats]
        vmap = {}
        for v in bm2.verts:
            co = matrix @ v.co if matrix is not None else v.co.copy()
            vmap[v] = self.bm.verts.new(co)
        for f in bm2.faces:
            try:
                nf = self.bm.faces.new([vmap[v] for v in f.verts])
            except ValueError:
                continue  # duplicate face (should not happen)
            nf.material_index = remap[min(f.material_index, len(remap) - 1)]
            nf.smooth = f.smooth if smooth is None else smooth
        bm2.free()
        return self

    # -- primitives -----------------------------------------------------------
    def box(self, lo, hi, mat, bevel=0.0, seg=1, matrix=None):
        """Axis aligned box from corner lo to corner hi, optional bevel."""
        lo, hi = Vector(lo), Vector(hi)
        size = hi - lo
        c = (lo + hi) / 2
        bm = bmesh.new()
        bmesh.ops.create_cube(bm, size=1.0)
        for v in bm.verts:
            v.co = Vector((c.x + v.co.x * size.x, c.y + v.co.y * size.y, c.z + v.co.z * size.z))
        if bevel > 0:
            bmesh.ops.bevel(bm, geom=list(bm.verts) + list(bm.edges), offset=bevel, offset_type="OFFSET",
                            segments=seg, profile=0.5, affect="EDGES", clamp_overlap=True)
        for f in bm.faces:
            f.smooth = False
        return self.merge(bm, mat, matrix=matrix)

    def cbox(self, center, size, mat, bevel=0.0, seg=1, matrix=None):
        c, s = Vector(center), Vector(size) / 2
        return self.box(c - s, c + s, mat, bevel, seg, matrix)

    def cylinder(self, center, radius, depth, mat, axis="Z", seg=16, bevel=0.0, bseg=1,
                 radius2=None, smooth=True, caps=True, phase=None):
        """Cylinder/frustum centred at `center` along `axis`. radius at -axis end,
        radius2 at +axis end."""
        bm = bmesh.new()
        r2 = radius if radius2 is None else radius2
        bmesh.ops.create_cone(bm, cap_ends=caps, cap_tris=False, segments=seg, radius1=radius,
                              radius2=r2, depth=depth, matrix=Matrix.Identity(4))
        if phase is not None:
            bmesh.ops.rotate(bm, cent=(0, 0, 0), matrix=Matrix.Rotation(phase, 3, "Z"), verts=bm.verts)
        if bevel > 0 and caps:
            bm.normal_update()
            rim = [e for e in bm.edges if any(abs(f.normal.z) > 0.99 for f in e.link_faces)]
            bmesh.ops.bevel(bm, geom=rim + list({v for e in rim for v in e.verts}), offset=bevel,
                            offset_type="OFFSET", segments=bseg, profile=0.5, affect="EDGES",
                            clamp_overlap=True)
        bm.normal_update()
        for f in bm.faces:
            f.smooth = smooth and abs(f.normal.z) < 0.95
        m = Matrix.Translation(Vector(center)) @ z_to_axis_matrix(axis)
        return self.merge(bm, mat, matrix=m)

    def cyl_between(self, p0, p1, radius, mat, seg=12, bevel=0.0, smooth=True, caps=True, radius2=None):
        p0, p1 = Vector(p0), Vector(p1)
        d = p1 - p0
        return self.cylinder((p0 + p1) / 2, radius, d.length, mat, axis=d.normalized(), seg=seg,
                             bevel=bevel, smooth=smooth, caps=caps, radius2=radius2)

    def sphere(self, center, radius, mat, seg=12, rings=8, scale=(1, 1, 1), smooth=True, rot_z=0.0):
        bm = bmesh.new()
        bmesh.ops.create_uvsphere(bm, u_segments=seg, v_segments=rings, radius=radius)
        for v in bm.verts:
            v.co = Vector((v.co.x * scale[0], v.co.y * scale[1], v.co.z * scale[2]))
        for f in bm.faces:
            f.smooth = smooth
        m = Matrix.Translation(Vector(center)) @ Matrix.Rotation(math.radians(rot_z), 4, "Z")
        return self.merge(bm, mat, matrix=m)

    def sweep(self, pts, profile, mat, closed=False, caps=True, smooth=True, normal0=None, scales=None):
        """Sweep a CCW 2D profile (u along frame normal, v along binormal) along a
        polyline using rotation-minimising frames. Outward normals."""
        pts = [Vector(p) for p in pts]
        n = len(pts)
        T = []
        for i in range(n):
            if closed:
                a = pts[(i + 1) % n] - pts[i - 1]
            else:
                a = pts[min(i + 1, n - 1)] - pts[max(i - 1, 0)]
            T.append(a.normalized())
        if normal0 is None:
            ref = Vector((0, 0, 1)) if abs(T[0].z) < 0.9 else Vector((1, 0, 0))
        else:
            ref = Vector(normal0)
        r0 = (ref - T[0] * ref.dot(T[0])).normalized()
        R = [r0]
        steps = n if closed else n - 1
        for i in range(steps):
            j = (i + 1) % n
            v1 = pts[j] - pts[i]
            c1 = v1.dot(v1)
            rL = R[i] - (2 / c1) * v1.dot(R[i]) * v1
            tL = T[i] - (2 / c1) * v1.dot(T[i]) * v1
            v2 = T[j] - tL
            c2 = v2.dot(v2)
            r = rL - (2 / c2) * v2.dot(rL) * v2 if c2 > 1e-14 else rL
            r = (r - T[j] * r.dot(T[j])).normalized()
            if closed and i == n - 1:
                # twist correction so the loop closes cleanly
                ang = r.angle(R[0], 0.0)
                if r.cross(R[0]).dot(T[0]) < 0:
                    ang = -ang
                for k in range(1, n):
                    R[k] = Matrix.Rotation(ang * k / n, 3, T[k]) @ R[k]
            else:
                R.append(r)
        bm = bmesh.new()
        rings = []
        for i in range(n):
            B = T[i].cross(R[i])
            k = 1.0 if scales is None else scales[i]
            rings.append([bm.verts.new(pts[i] + (R[i] * u + B * v) * k) for (u, v) in profile])
        m = len(profile)
        segs = n if closed else n - 1
        for i in range(segs):
            a, b = rings[i], rings[(i + 1) % n]
            for k in range(m):
                k1 = (k + 1) % m
                f = bm.faces.new((a[k], a[k1], b[k1], b[k]))
                f.smooth = smooth
        if caps and not closed:
            f = bm.faces.new(list(reversed(rings[0])))
            f.smooth = False
            f = bm.faces.new(rings[-1])
            f.smooth = False
        return self.merge(bm, mat)

    def tube(self, pts, radius, mat, seg=10, closed=False, caps=True, smooth=True):
        return self.sweep(pts, circle_profile(radius, seg), mat, closed=closed, caps=caps, smooth=smooth)

    def torus(self, center, major, minor, mat, axis="Y", seg=24, mseg=8, smooth=True):
        """Ring lying in the plane perpendicular to `axis`."""
        pts = arc_points((0, 0, 0), major, 0, 360, seg, plane="XY")[:-1]
        m = Matrix.Translation(Vector(center)) @ z_to_axis_matrix(axis)
        pts = [m @ p for p in pts]
        return self.sweep(pts, circle_profile(minor, mseg), mat, closed=True, smooth=smooth)

    def lathe(self, center, profile, mats, seg=16, smooth=True, flat_segments=(), phase=0.0):
        """Revolve a (r, z) profile (bottom→top, outer surface first) around +Z.
        `mats` is a name or a list per profile segment. r == 0 makes a pole."""
        c = Vector(center)
        if isinstance(mats, str):
            mats = [mats] * (len(profile) - 1)
        local = []
        for m in mats:
            if m not in local:
                local.append(m)
        bm = bmesh.new()
        rings = []
        for (r, z) in profile:
            if r < 1e-9:
                pole = bm.verts.new(c + Vector((0, 0, z)))
                rings.append([pole] * seg)
            else:
                rings.append([bm.verts.new(c + Vector((r * math.cos(phase + 2 * math.pi * j / seg),
                                                       r * math.sin(phase + 2 * math.pi * j / seg), z)))
                              for j in range(seg)])
        for k in range(len(profile) - 1):
            a, b = rings[k], rings[k + 1]
            for j in range(seg):
                j1 = (j + 1) % seg
                quad = [a[j], a[j1], b[j1], b[j]]
                vs = []
                for v in quad:
                    if v not in vs:
                        vs.append(v)
                if len(vs) < 3:
                    continue
                f = bm.faces.new(vs)
                f.material_index = local.index(mats[k])
                f.smooth = smooth and (k not in flat_segments)
        return self.merge(bm, local)

    def prism(self, poly, origin, u, v, depth, mat, smooth=False, bevel=0.0, seg=1):
        """Extrude a 2D polygon (in the plane origin + x*u + y*v) along u x v."""
        u, v, o = Vector(u), Vector(v), Vector(origin)
        nrm = u.cross(v).normalized()
        if polygon_area(poly) < 0:
            poly = list(reversed(poly))
        bm = bmesh.new()
        bot = [bm.verts.new(o + u * x + v * y) for (x, y) in poly]
        top = [bm.verts.new(o + u * x + v * y + nrm * depth) for (x, y) in poly]
        bm.faces.new(top)
        bm.faces.new(list(reversed(bot)))
        n = len(poly)
        for i in range(n):
            j = (i + 1) % n
            bm.faces.new((bot[i], bot[j], top[j], top[i]))
        if bevel > 0:
            bmesh.ops.bevel(bm, geom=list(bm.verts) + list(bm.edges), offset=bevel, offset_type="OFFSET",
                            segments=seg, profile=0.5, affect="EDGES", clamp_overlap=True)
        for f in bm.faces:
            f.smooth = smooth
        return self.merge(bm, mat)

    def prism_x(self, poly_yz, x0, x1, mat, bevel=0.0, seg=1):
        """Extrude a polygon given in world (y, z) coordinates between x0 and x1."""
        return self.prism(poly_yz, (min(x0, x1), 0, 0), (0, 1, 0), (0, 0, 1), abs(x1 - x0), mat,
                          bevel=bevel, seg=seg)

    def prism_y(self, poly_xz, y0, y1, mat, bevel=0.0, seg=1):
        """Extrude a polygon given in world (x, z) coordinates between y0 and y1."""
        return self.prism(poly_xz, (0, max(y0, y1), 0), (1, 0, 0), (0, 0, 1), abs(y1 - y0), mat,
                          bevel=bevel, seg=seg)

    def stripes(self, origin, u, v, w, h, depth, mats=HAZARD, stripe=0.1, slant=1.0, phase=0.0,
                back=False):
        """Diagonal-striped slab (hazard band). Occupies origin + [0,w]*u + [0,h]*v,
        extruded `depth` along u x v (the outward normal). Back face omitted
        unless back=True (it normally sits flush on a surface)."""
        u, v, o = Vector(u).normalized(), Vector(v).normalized(), Vector(origin)
        nrm = u.cross(v).normalized()
        polys = []
        span = slant * h
        kmin = int(math.floor((-abs(span) - abs(phase)) / stripe)) - 1
        kmax = int(math.ceil((w + abs(span) + abs(phase)) / stripe)) + 1
        for k in range(kmin, kmax + 1):
            a = k * stripe + phase
            para = [(a, 0.0), (a + stripe, 0.0), (a + stripe + span, h), (a + span, h)]
            clipped = clip_polygon_rect(para, 0.0, 0.0, w, h)
            if clipped and abs(polygon_area(clipped)) > 1e-9:
                if polygon_area(clipped) < 0:
                    clipped = list(reversed(clipped))
                polys.append((clipped, k % 2))
        keymap = {}
        pts2 = []

        def key(p):
            kk = (round(p[0], 6), round(p[1], 6))
            if kk not in keymap:
                keymap[kk] = len(pts2)
                pts2.append(p)
            return keymap[kk]

        ipolys = [([key(p) for p in poly], mi) for poly, mi in polys]
        bm = bmesh.new()
        bot = [bm.verts.new(o + u * p[0] + v * p[1]) for p in pts2]
        top = [bm.verts.new(o + u * p[0] + v * p[1] + nrm * depth) for p in pts2]
        directed = {}
        for idx, (ip, mi) in enumerate(ipolys):
            for i in range(len(ip)):
                directed[(ip[i], ip[(i + 1) % len(ip)])] = mi
        for ip, mi in ipolys:
            f = bm.faces.new([top[i] for i in ip])
            f.material_index = mi
            f.smooth = False
            if back:
                fb = bm.faces.new([bot[i] for i in reversed(ip)])
                fb.material_index = mi
        for (a, b), mi in directed.items():
            if (b, a) not in directed:
                f = bm.faces.new((bot[a], bot[b], top[b], top[a]))
                f.material_index = mi
                f.smooth = False
        return self.merge(bm, list(mats))

    def text(self, s, center, u, v, size, depth, mat, resolution=4):
        """Embossed text centred on `center` in the (u, v) plane, extruded along u x v."""
        cu = bpy.data.curves.new("_tmp_text", "FONT")
        cu.body = s
        cu.size = size
        cu.extrude = depth / 2
        cu.resolution_u = resolution
        cu.align_x = "CENTER"
        cu.align_y = "CENTER"
        ob = bpy.data.objects.new("_tmp_text", cu)
        bpy.context.scene.collection.objects.link(ob)
        dg = bpy.context.evaluated_depsgraph_get()
        me = bpy.data.meshes.new_from_object(ob.evaluated_get(dg))
        bm = bmesh.new()
        bm.from_mesh(me)
        bpy.data.objects.remove(ob)
        bpy.data.curves.remove(cu)
        bpy.data.meshes.remove(me)
        xs = [vv.co.x for vv in bm.verts]
        ys = [vv.co.y for vv in bm.verts]
        cx, cy = (min(xs) + max(xs)) / 2, (min(ys) + max(ys)) / 2
        u, v = Vector(u).normalized(), Vector(v).normalized()
        n = u.cross(v).normalized()
        m = Matrix.Identity(4)
        for i in range(3):
            m[i][0], m[i][1], m[i][2] = u[i], v[i], n[i]
        m.translation = Vector(center) - u * cx - v * cy + n * (depth / 2)
        bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4])
        for f in bm.faces:
            f.smooth = False
        return self.merge(bm, mat, matrix=m)

    # -- utilities -------------------------------------------------------------
    def transform(self, matrix, start_vert_index=0):
        self.bm.verts.ensure_lookup_table()
        verts = self.bm.verts[start_vert_index:]
        bmesh.ops.transform(self.bm, matrix=matrix, verts=list(verts))

    def vert_count(self):
        return len(self.bm.verts)

    def bounds(self):
        xs = [v.co.x for v in self.bm.verts]
        ys = [v.co.y for v in self.bm.verts]
        zs = [v.co.z for v in self.bm.verts]
        return Vector((min(xs), min(ys), min(zs))), Vector((max(xs), max(ys), max(zs)))

    def to_object(self, name, parent, pivot=(0, 0, 0)):
        """Create the mesh object `name` with its origin at world `pivot`."""
        bm = self.bm
        # Normals first: triangulating concave n-gons without them scrambles the fan.
        bm.normal_update()
        ngons = [f for f in bm.faces if len(f.verts) > 4]
        if ngons:
            bmesh.ops.triangulate(bm, faces=ngons, quad_method="BEAUTY", ngon_method="BEAUTY")
        bm.normal_update()
        # simple box-projected UVs (world space, 1 UV unit = 1 m)
        uv = bm.loops.layers.uv.new("UVMap")
        for f in bm.faces:
            nn = f.normal
            ax = max(range(3), key=lambda i: abs(nn[i]))
            for loop in f.loops:
                co = loop.vert.co
                if ax == 0:
                    loop[uv].uv = (co.y, co.z)
                elif ax == 1:
                    loop[uv].uv = (co.x, co.z)
                else:
                    loop[uv].uv = (co.x, co.y)
        pv = Vector(pivot)
        bmesh.ops.translate(bm, vec=-pv, verts=bm.verts)
        me = bpy.data.meshes.new(name)
        bm.to_mesh(me)
        bm.free()
        self.bm = None
        write_vertex_colors(me, self.mat_names)
        me.update()
        ob = bpy.data.objects.new(name, me)
        if ob.name != name:
            raise RuntimeError("object name %r is taken (got %r); child names must be unique" % (name, ob.name))
        bpy.context.scene.collection.objects.link(ob)
        ob.parent = parent
        ob.matrix_parent_inverse = Matrix.Identity(4)
        ob.location = pv
        return ob


# --------------------------------------------------------------------------
# Path generators
# --------------------------------------------------------------------------
def helix_points(radius, z0, z1, turns, seg_per_turn=28, end_flat=0.0, phase=0.0, clockwise=False):
    """Helix centreline from z0 to z1. `end_flat` (in turns) blends the pitch to
    zero at both ends (closed/ground compression-spring ends)."""
    n = max(2, int(round(turns * seg_per_turn)))
    s_vals = [turns * i / n for i in range(n + 1)]

    def w(s):
        if end_flat <= 0:
            return 1.0

        def sstep(x):
            x = max(0.0, min(1.0, x))
            return x * x * (3 - 2 * x)
        return sstep(s / end_flat) * sstep((turns - s) / end_flat)

    # integrate dz/ds = w(s) (trapezoid), then normalise to [z0, z1]
    acc = [0.0]
    for i in range(1, len(s_vals)):
        ds = s_vals[i] - s_vals[i - 1]
        acc.append(acc[-1] + 0.5 * (w(s_vals[i]) + w(s_vals[i - 1])) * ds)
    total = acc[-1]
    sign = -1.0 if clockwise else 1.0
    pts = []
    for s, a in zip(s_vals, acc):
        ang = phase + sign * 2 * math.pi * s
        pts.append(Vector((radius * math.cos(ang), radius * math.sin(ang), z0 + (z1 - z0) * a / total)))
    return pts


def remap_z(mb, z0, z1):
    """Affinely remap the builder's vertices so its Z bounds are exactly z0..z1."""
    lo, hi = mb.bounds()
    k = (z1 - z0) / (hi.z - lo.z)
    for v in mb.bm.verts:
        v.co.z = z0 + (v.co.z - lo.z) * k


def write_vertex_colors(me, mat_names):
    """Per-corner sRGB colour attribute "Col" from each face's palette index; one material slot."""
    import numpy as np
    table = np.array([palette_rgba(n) for n in mat_names] or [(1, 0, 1, 0)], dtype=np.float32)
    n_poly = len(me.polygons)
    mi = np.empty(n_poly, dtype=np.int32)
    me.polygons.foreach_get("material_index", mi)
    loop_total = np.empty(n_poly, dtype=np.int32)
    me.polygons.foreach_get("loop_total", loop_total)
    per_loop = np.repeat(mi, loop_total)
    attr = me.color_attributes.new("Col", "BYTE_COLOR", "CORNER")
    attr.data.foreach_set("color_srgb", table[per_loop].ravel())
    me.color_attributes.active_color = attr
    me.color_attributes.render_color_index = 0
    me.polygons.foreach_set("material_index", np.zeros(n_poly, dtype=np.int32))
    me.materials.clear()
    me.materials.append(get_vertex_material())


def set_planar_uv(ob, lo, hi):
    """Replace a front-facing (-Y) mesh's UVs with a 0..1 planar map of the world rectangle lo..hi (x, z):
    u runs along +X (the viewer's left to right when facing the model), v along +Z. Used for the avatar's Face
    screen, where Unity draws the webcam."""
    mw = ob.matrix_world
    me = ob.data
    uv = me.uv_layers.active.data
    for loop in me.loops:
        p = mw @ me.vertices[loop.vertex_index].co
        uv[loop.index].uv = ((p.x - lo[0]) / (hi[0] - lo[0]), (p.z - lo[1]) / (hi[1] - lo[1]))


def add_anchor(name, parent, location):
    """EMPTY child marking a point Unity code attaches things to (flame, smoke, rope ends...)."""
    ob = bpy.data.objects.new(name, None)
    ob.empty_display_type = "SPHERE"
    ob.empty_display_size = 0.15
    bpy.context.scene.collection.objects.link(ob)
    if ob.name != name:
        raise RuntimeError("anchor name %r is taken" % name)
    ob.parent = parent
    ob.matrix_parent_inverse = Matrix.Identity(4)
    ob.location = Vector(location)
    return ob


# --------------------------------------------------------------------------
# Scene / model management
# --------------------------------------------------------------------------
def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    prefs = bpy.context.preferences
    prefs.filepaths.save_version = 0  # no .blend1 backups
    scn = bpy.context.scene
    scn.unit_settings.system = "METRIC"
    scn.unit_settings.scale_length = 1.0
    scn.unit_settings.length_unit = "METERS"


def make_root(name):
    root = bpy.data.objects.new(name, None)
    root.empty_display_type = "PLAIN_AXES"
    root.empty_display_size = 0.5
    bpy.context.scene.collection.objects.link(root)
    root.location = (0, 0, 0)
    return root


def model_objects(root):
    return [root] + list(root.children_recursive)


def world_bounds(objs):
    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    for ob in objs:
        if ob.type != "MESH":
            continue
        mw = ob.matrix_world
        for v in ob.data.vertices:
            p = mw @ v.co
            lo = Vector((min(lo.x, p.x), min(lo.y, p.y), min(lo.z, p.z)))
            hi = Vector((max(hi.x, p.x), max(hi.y, p.y), max(hi.z, p.z)))
    return lo, hi


def tri_count(ob):
    return sum(len(p.vertices) - 2 for p in ob.data.polygons)


def fmt_v(v):
    return "(%.3f, %.3f, %.3f)" % tuple(v)


def report(root):
    lines = []
    total = 0
    for ob in model_objects(root):
        if ob.type == "MESH":
            lo, hi = world_bounds([ob])
            t = tri_count(ob)
            total += t
            lines.append("  %-14s origin=%s  bounds=%s..%s  tris=%d" % (
                ob.name, fmt_v(ob.matrix_world.translation), fmt_v(lo), fmt_v(hi), t))
        elif ob is not root:
            lines.append("  %-14s anchor at %s" % (ob.name, fmt_v(ob.matrix_world.translation)))
    lo, hi = world_bounds(model_objects(root))
    lines.insert(0, "%s: total tris=%d  bounds=%s..%s  size=%s" % (
        root.name, total, fmt_v(lo), fmt_v(hi), fmt_v(hi - lo)))
    return "\n".join(lines), total


def check_model(root):
    for ob in model_objects(root):
        if ob.rotation_euler.to_quaternion().angle > 1e-6 or (ob.scale - Vector((1, 1, 1))).length > 1e-6:
            raise RuntimeError("%s has unapplied rotation/scale" % ob.name)
        if ob.type == "MESH":
            if [m.name for m in ob.data.materials] != [EXPORT_MATERIAL]:
                raise RuntimeError("%s must use only %s" % (ob.name, EXPORT_MATERIAL))
            if "Col" not in ob.data.color_attributes:
                raise RuntimeError("%s has no vertex colours" % ob.name)
    if root.location.length > 1e-9:
        raise RuntimeError("root not at origin")


def export_fbx(root, path):
    bpy.ops.object.select_all(action="DESELECT")
    for ob in model_objects(root):
        ob.select_set(True)
    bpy.context.view_layer.objects.active = root
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=True,
        object_types={"EMPTY", "MESH"},
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_UNITS",
        axis_forward="-Z",
        axis_up="Y",
        bake_space_transform=True,
        use_mesh_modifiers=True,
        mesh_smooth_type="FACE",
        colors_type="SRGB",
        add_leaf_bones=False,
        bake_anim=False,
        use_custom_props=False,
    )


def save_blend(path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=path, check_existing=False, compress=True)


# --------------------------------------------------------------------------
# Preview rendering (Eevee, falls back to Workbench)
# --------------------------------------------------------------------------
BG_TOP = (0.58, 0.72, 0.86)
BG_BOTTOM = (0.86, 0.84, 0.70)
GROUND_COLOR = (0.30, 0.42, 0.20)  # lawn


def _eevee_id():
    ids = [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items]
    for cand in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE"):
        if cand in ids:
            return cand
    return "BLENDER_WORKBENCH"


def setup_render(res_x, res_y, engine=None):
    scn = bpy.context.scene
    scn.render.engine = engine or _eevee_id()
    scn.render.resolution_x = res_x
    scn.render.resolution_y = res_y
    scn.render.resolution_percentage = 100
    scn.render.film_transparent = True
    scn.render.image_settings.file_format = "PNG"
    scn.render.image_settings.color_mode = "RGBA"
    scn.view_settings.view_transform = "Standard"
    scn.view_settings.look = "None"
    scn.view_settings.exposure = 0.0
    if scn.render.engine.startswith("BLENDER_EEVEE"):
        ee = scn.eevee
        ee.taa_render_samples = 24
        for attr, val in (("use_shadows", True), ("use_raytracing", False), ("use_gtao", True),
                          ("shadow_ray_count", 2), ("shadow_step_count", 8)):
            if hasattr(ee, attr):
                try:
                    setattr(ee, attr, val)
                except Exception:
                    pass
    world = bpy.data.worlds.new("_PreviewWorld")
    scn.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    bg.inputs["Color"].default_value = (0.62, 0.70, 0.80, 1)
    bg.inputs["Strength"].default_value = 0.9


def _add_sun(name, rot_deg, strength, angle_deg=6.0, shadow=True, color=(1, 1, 1)):
    ld = bpy.data.lights.new(name, "SUN")
    ld.energy = strength
    ld.angle = math.radians(angle_deg)
    ld.use_shadow = shadow
    ld.color = color
    ob = bpy.data.objects.new(name, ld)
    ob.rotation_euler = tuple(math.radians(a) for a in rot_deg)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def setup_lights():
    _add_sun("_Key", (50, 0, 35), 3.2, angle_deg=6, color=(1.0, 0.93, 0.82))   # warm afternoon key
    _add_sun("_Fill", (65, 0, -60), 0.8, shadow=False, color=(0.8, 0.88, 1.0))
    _add_sun("_Rim", (60, 0, 170), 1.0, shadow=False)


def _ground(z, size=400.0):
    mat = bpy.data.materials.get("_PreviewGround") or bpy.data.materials.new("_PreviewGround")
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = GROUND_COLOR + (1,)
    bsdf.inputs["Roughness"].default_value = 0.95
    me = bpy.data.meshes.new("_PreviewGround")
    h = size / 2
    me.from_pydata([(-h, -h, z), (h, -h, z), (h, h, z), (-h, h, z)], [], [(0, 1, 2, 3)])
    me.materials.append(mat)
    ob = bpy.data.objects.new("_PreviewGround", me)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def _camera(name, ortho=False, lens=70.0):
    cd = bpy.data.cameras.new(name)
    cd.lens = lens
    cd.clip_start = 0.05
    cd.clip_end = 2000
    if ortho:
        cd.type = "ORTHO"
    ob = bpy.data.objects.new(name, cd)
    bpy.context.scene.collection.objects.link(ob)
    bpy.context.scene.camera = ob
    return ob


def _bbox_corners(lo, hi):
    return [Vector((x, y, z)) for x in (lo.x, hi.x) for y in (lo.y, hi.y) for z in (lo.z, hi.z)]


def _fit_perspective(cam, lo, hi, az_deg, el_deg, margin=1.12):
    az, el = math.radians(az_deg), math.radians(el_deg)
    d = Vector((math.sin(az) * math.cos(el), -math.cos(az) * math.cos(el), math.sin(el)))
    cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
    bpy.context.view_layer.update()
    dg = bpy.context.evaluated_depsgraph_get()
    flat = []
    for c in _bbox_corners(lo, hi):
        flat.extend(c)
    loc, _scale = cam.camera_fit_coords(dg, flat)
    center = (lo + hi) / 2
    loc = Vector(loc)
    dist = (loc - center).dot(d)
    cam.location = loc + d * dist * (margin - 1.0)


def _fit_ortho_front(cam, lo, hi, aspect=1.0, margin=1.12):
    cam.rotation_euler = (math.radians(90), 0, 0)  # looks along +Y at the model's front (-Y) face
    center = (lo + hi) / 2
    size = hi - lo
    cam.data.ortho_scale = max(size.x, size.z * aspect) * margin
    cam.location = (center.x, lo.y - 50.0, center.z)


def _render_to(path):
    scn = bpy.context.scene
    scn.render.filepath = path
    bpy.ops.render.render(write_still=True)


def _load_rgba(path):
    import numpy as np
    img = bpy.data.images.load(path, check_existing=False)
    w, h = img.size
    arr = np.empty(w * h * 4, dtype=np.float32)
    img.pixels.foreach_get(arr)
    bpy.data.images.remove(img)
    return arr.reshape(h, w, 4)


def _bg_gradient(h, w):
    import numpy as np
    t = np.linspace(0.0, 1.0, h, dtype=np.float32)[:, None, None]  # 0 = bottom row
    top = np.array(BG_TOP, dtype=np.float32)[None, None, :]
    bot = np.array(BG_BOTTOM, dtype=np.float32)[None, None, :]
    return np.broadcast_to(bot * (1 - t) + top * t, (h, w, 3)).copy()


def _save_rgba(arr, path):
    import numpy as np
    h, w, _ = arr.shape
    img = bpy.data.images.new("_composite", w, h, alpha=True)
    img.pixels.foreach_set(np.ascontiguousarray(arr, dtype=np.float32).ravel())
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)


def _composite(panels, path):
    """Alpha-over each panel on a gradient and place them side by side."""
    import numpy as np
    outs = []
    for arr in panels:
        h, w, _ = arr.shape
        bg = _bg_gradient(h, w)
        a = arr[:, :, 3:4]
        rgb = arr[:, :, :3] * a + bg * (1 - a)
        outs.append(np.concatenate([rgb, np.ones((h, w, 1), np.float32)], axis=2))
        outs.append(np.concatenate([np.full((h, 2, 3), 0.55, np.float32), np.ones((h, 2, 1), np.float32)], axis=2))
    outs = outs[:-1]
    os.makedirs(os.path.dirname(path), exist_ok=True)
    _save_rgba(np.concatenate(outs, axis=1), path)


def render_preview(root, path, size=640, az=35.0, el=20.0, ground=True):
    """Three panels side by side: a 3/4 front-right view, the in-game view (from the front, nearly level, the
    way a player standing in the square sees things) and an orthographic straight front (-Y) view."""
    import tempfile
    objs = model_objects(root)
    lo, hi = world_bounds(objs)
    setup_render(size, size)
    setup_lights()
    gnd = _ground(lo.z - 0.001, size=max(40.0, (hi - lo).length * 8)) if ground else None
    tmpdir = tempfile.mkdtemp(prefix="fc_prev_")
    cam = _camera("_PreviewCam", lens=70)
    _fit_perspective(cam, lo, hi, az, el)
    p1 = os.path.join(tmpdir, "a.png")
    _render_to(p1)
    cam_game = _camera("_PreviewCamGame", lens=70)
    _fit_perspective(cam_game, lo, hi, 0.0, 8.0)
    p2 = os.path.join(tmpdir, "b.png")
    _render_to(p2)
    if gnd:
        gnd.hide_render = True
    cam2 = _camera("_PreviewCamFront", ortho=True)
    _fit_ortho_front(cam2, lo, hi)
    p3 = os.path.join(tmpdir, "c.png")
    _render_to(p3)
    _composite([_load_rgba(p1), _load_rgba(p2), _load_rgba(p3)], path)


def build_model(name, builder, preview=True, verbose=True):
    """Fresh scene -> build -> checks -> FBX + .blend -> preview."""
    reset_scene()
    root = make_root(name)
    builder(root)
    bpy.context.view_layer.update()
    check_model(root)
    text, total = report(root)
    if verbose:
        print(text)
    export_fbx(root, os.path.join(MODELS_DIR, name + ".fbx"))
    save_blend(os.path.join(SOURCE_DIR, name + ".blend"))
    if preview:
        render_preview(root, os.path.join(PREVIEW_DIR, name + ".png"))
    return total
