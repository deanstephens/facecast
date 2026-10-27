"""The question corner: a box people post written questions into, with a tray of blank question cards on its side,
and a table beside it where questions go once they've been pulled out and read. Unity's QuestionBox and
QuestionTable find the parts by name and draw each card's writing (see ../MODELS.md)."""

from common import PALETTE, MeshBuilder

PALETTE.update({
    "CardPaper":      dict(color="#FBF6E8"),
    "CardEdge":       dict(color="#E4DCC8"),
    "CardRule":       dict(color="#E0675F"),
})

# A question card: an index card, 0.2 × 0.14 m, its front facing -Y like everything.
CARD_W, CARD_H = 0.2, 0.14
CARD_THICK = 0.0015
CARD_RULE_Z = 0.046          # the red rule across the top; Unity writes below it

# The box on its stand (front -Y, its own left +X).
BOX_W, BOX_D, BOX_H = 0.52, 0.4, 0.36
BOX_Z = 0.72                 # bottom of the box: the stand's height
LID_Z = BOX_Z + BOX_H
LABEL_W, LABEL_H = 0.42, 0.2
LABEL_Z = BOX_Z + 0.18
LABEL_Y = -BOX_D / 2 - 0.01   # the label's face, proud of the box front
TRAY_X = -BOX_W / 2 - 0.1    # the tray of blank cards hangs on the box's left as you face it (Blender -X)
TRAY_Z = LID_Z - 0.06

# The table (long side along X).
TABLE_W, TABLE_D = 1.5, 0.85
TABLE_TOP = 0.76


def _card_stack(mb, cx, cy, z0, layers=8):
    """Blank cards lying in a stack, long side along Y, from z0 up."""
    h = 0.004
    for k in range(layers):
        inset = 0.0 if k % 2 == 0 else 0.0015
        mb.box((cx - CARD_H / 2 + inset, cy - CARD_W / 2 + inset, z0 + k * h),
               (cx + CARD_H / 2 - inset, cy + CARD_W / 2 - inset, z0 + (k + 1) * h),
               "CardPaper" if k % 2 == 0 or k == layers - 1 else "CardEdge")
    return z0 + layers * h


def build_question_box(root):
    # Stand: a stone base and a square wooden post.
    stand = MeshBuilder()
    stand.cbox((0, 0, 0.03), (0.42, 0.42, 0.06), "Stone", bevel=0.015)
    stand.cbox((0, 0, BOX_Z / 2), (0.09, 0.09, BOX_Z), "WoodDark", bevel=0.012)
    stand.cbox((0, 0, BOX_Z - 0.015), (0.3, 0.3, 0.03), "WoodDark", bevel=0.008)
    stand.to_object("Stand", root)

    # The box: wooden sides, a lid in the corner's colour (Tint) with a dark slot to post questions through.
    box = MeshBuilder()
    box.box((-BOX_W / 2, -BOX_D / 2, BOX_Z), (BOX_W / 2, BOX_D / 2, LID_Z), "Wood", bevel=0.01)
    for z in (BOX_Z + 0.02, LID_Z - 0.02):
        box.box((-BOX_W / 2 - 0.006, -BOX_D / 2 - 0.006, z - 0.012), (BOX_W / 2 + 0.006, BOX_D / 2 + 0.006, z + 0.012),
                "WoodDark", bevel=0.004)
    box.box((-BOX_W / 2 - 0.02, -BOX_D / 2 - 0.02, LID_Z), (BOX_W / 2 + 0.02, BOX_D / 2 + 0.02, LID_Z + 0.035), "Tint",
            bevel=0.01)
    # The slot: dark, framed by a raised rim.
    top = LID_Z + 0.035
    box.box((-0.15, -0.02, top), (0.15, 0.02, top + 0.001), "Black")
    for (x0, y0, x1, y1) in ((-0.17, -0.04, 0.17, -0.02), (-0.17, 0.02, 0.17, 0.04), (-0.17, -0.02, -0.15, 0.02),
                             (0.15, -0.02, 0.17, 0.02)):
        box.box((x0, y0, top), (x1, y1, top + 0.008), "TintDark", bevel=0.002)
    # The tray of blank cards on the side, on a bracket.
    tx = TRAY_X
    box.box((tx - 0.095, -0.13, TRAY_Z - 0.012), (tx + 0.095, 0.13, TRAY_Z), "WoodDark", bevel=0.004)
    for sx in (-1, 1):
        box.box((tx + sx * 0.095 - 0.006, -0.13, TRAY_Z), (tx + sx * 0.095 + 0.006, 0.13, TRAY_Z + 0.03), "Wood")
    box.box((tx - 0.095, -0.136, TRAY_Z), (tx + 0.095, -0.124, TRAY_Z + 0.022), "Wood")
    box.prism_y([(-BOX_W / 2, TRAY_Z - 0.012), (tx - 0.02, TRAY_Z - 0.012), (-BOX_W / 2, TRAY_Z - 0.14)], -0.02, 0.02,
                "WoodDark")
    box.to_object("Box", root)

    # The cream label on the front: Unity writes "Questions" and how many are inside.
    label = MeshBuilder()
    label.cbox((0, LABEL_Y + 0.0025, LABEL_Z), (LABEL_W, 0.005, LABEL_H), "Canvas", bevel=0.002)
    label.cbox((0, LABEL_Y + 0.0075, LABEL_Z), (LABEL_W + 0.03, 0.005, LABEL_H + 0.03), "Tint", bevel=0.002)
    label.to_object("Label", root, pivot=(0.0, LABEL_Y, LABEL_Z))

    # The blank cards in the tray (their own child: Unity points at it to hand you a card).
    cards = MeshBuilder()
    top = _card_stack(cards, tx, 0.0, TRAY_Z)
    cards.to_object("Cards", root, pivot=(tx, 0.0, top))


def build_question_table(root):
    legs = MeshBuilder()
    for sx in (-1, 1):
        for sy in (-1, 1):
            legs.cbox((sx * (TABLE_W / 2 - 0.1), sy * (TABLE_D / 2 - 0.09), (TABLE_TOP - 0.04) / 2),
                      (0.06, 0.06, TABLE_TOP - 0.04), "WoodDark", bevel=0.01)
    for sy in (-1, 1):
        legs.cbox((0, sy * (TABLE_D / 2 - 0.09), TABLE_TOP - 0.1), (TABLE_W - 0.2, 0.03, 0.08), "WoodDark", bevel=0.006)
    for sx in (-1, 1):
        legs.cbox((sx * (TABLE_W / 2 - 0.1), 0, TABLE_TOP - 0.1), (0.03, TABLE_D - 0.18, 0.08), "WoodDark", bevel=0.006)
    legs.to_object("Legs", root)

    top = MeshBuilder()
    top.box((-TABLE_W / 2, -TABLE_D / 2, TABLE_TOP - 0.04), (TABLE_W / 2, TABLE_D / 2, TABLE_TOP), "Wood", bevel=0.012)
    top.box((-TABLE_W / 2 - 0.004, -TABLE_D / 2 - 0.004, TABLE_TOP - 0.04),
            (TABLE_W / 2 + 0.004, TABLE_D / 2 + 0.004, TABLE_TOP - 0.025), "WoodDark", bevel=0.004)
    top.to_object("Top", root, pivot=(0.0, 0.0, TABLE_TOP))


def build_question_card(root):
    """An index card: cream, with a red rule across the top; its pivot is the centre of its back."""
    mb = MeshBuilder()
    mb.box((-CARD_W / 2, -CARD_THICK, -CARD_H / 2), (CARD_W / 2, 0.0, CARD_H / 2), "CardPaper")
    mb.box((-CARD_W / 2 + 0.008, -CARD_THICK - 0.0002, CARD_RULE_Z - 0.0012),
           (CARD_W / 2 - 0.008, -CARD_THICK, CARD_RULE_Z + 0.0012), "CardRule")
    mb.to_object("Card", root)


MODELS = {
    "QuestionBox": build_question_box,
    "QuestionTable": build_question_table,
    "QuestionCard": build_question_card,
}
