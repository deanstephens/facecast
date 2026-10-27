"""The projection screen someone's shared screen shows on: a pull-up screen that rolls up out of a case on two feet,
in the sharer's colour. Unity's Projection unrolls it, draws the shared picture on its Screen and writes the sharer's
name on its Plate (see ../MODELS.md)."""

from common import PALETTE, MeshBuilder, set_planar_uv

PALETTE.update({
    "ScreenWhite":    dict(color="#F2F2EF"),
    "ScreenBack":     dict(color="#9AA0AA"),
})

SCREEN_W, SCREEN_H = 2.4, 1.35       # 16:9
CASE_TOP = 0.76                      # the screen rolls up from here
SCREEN_Y = -0.012                    # the screen's face, just in front of the case's centre line


def build_projection_screen(root):
    # The case on its feet (Tint: the sharer's colour), with a cream plate on the front for the name.
    case = MeshBuilder()
    case.cylinder((0, 0, CASE_TOP - 0.07), 0.07, SCREEN_W + 0.16, "Tint", axis="X", seg=14)
    for sx in (-1, 1):
        x = sx * (SCREEN_W / 2 + 0.08)
        case.cylinder((x, 0, CASE_TOP - 0.07), 0.075, 0.03, "TintDark", axis="X", seg=14)
        case.cbox((x * 0.82, 0, (CASE_TOP - 0.12) / 2), (0.05, 0.05, CASE_TOP - 0.12), "Iron", bevel=0.01)
        case.cbox((x * 0.82, 0, 0.02), (0.08, 0.6, 0.04), "Iron", bevel=0.012)
    case.box((-SCREEN_W / 2 - 0.02, -0.02, CASE_TOP - 0.01), (SCREEN_W / 2 + 0.02, 0.02, CASE_TOP), "Black")
    case.to_object("Case", root)

    plate = MeshBuilder()
    plate.cbox((0, -0.074, CASE_TOP - 0.07), (0.7, 0.012, 0.085), "Canvas", bevel=0.003)
    plate.to_object("Plate", root, pivot=(0.0, -0.08, CASE_TOP - 0.07))

    # What rolls up: two thin poles at the sides, the bar along the top and a grey back behind the screen. Its pivot
    # is the case's slot, so Unity can unroll it by scaling it up from there.
    rig = MeshBuilder()
    top = CASE_TOP + SCREEN_H
    for sx in (-1, 1):
        rig.cylinder((sx * (SCREEN_W / 2 + 0.03), 0.01, (CASE_TOP + top) / 2), 0.015, SCREEN_H, "Iron", seg=8)
    rig.cylinder((0, 0.0, top + 0.02), 0.025, SCREEN_W + 0.12, "IronLight", axis="X", seg=10)
    rig.box((-SCREEN_W / 2, 0.0, CASE_TOP), (SCREEN_W / 2, 0.006, top), "ScreenBack")
    rig.to_object("Rig", root, pivot=(0.0, 0.0, CASE_TOP))

    # The screen itself: a plain white panel with 0..1 UVs (u from the viewer's left to right, v up); its pivot is the
    # middle of its bottom edge, at the case's slot.
    screen = MeshBuilder()
    screen.box((-SCREEN_W / 2, SCREEN_Y, CASE_TOP), (SCREEN_W / 2, SCREEN_Y + 0.004, top), "ScreenWhite")
    ob = screen.to_object("Screen", root, pivot=(0.0, SCREEN_Y, CASE_TOP))
    set_planar_uv(ob, (-SCREEN_W / 2, CASE_TOP), (SCREEN_W / 2, top))


MODELS = {
    "ProjectionScreen": build_projection_screen,
}
