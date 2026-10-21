"""Small, readable README variants of the existing ownership animations."""
from __future__ import annotations

import argparse
import hashlib
import json

from PIL import Image, ImageDraw, ImageFont
from render import BG, TEXT, MUTED, COLOURS, FONT_DIR, MODES, OUT, palette, set_final_hold

SIZE, LEFT, TOP, CELL = (380, 438), 15, 42, 7
FONT = ImageFont.truetype(str(FONT_DIR / "segoeui.ttf"), 11)
BOLD = ImageFont.truetype(str(FONT_DIR / "segoeuib.ttf"), 11)
NUMBER = ImageFont.truetype(str(FONT_DIR / "segoeuib.ttf"), 9)



def point(x, z):
    return LEFT+(x+.5)*CELL, TOP+(49.5-z)*CELL


def render(mode, frame):
    image = Image.new("RGB", SIZE, BG)
    draw = ImageDraw.Draw(image)
    for i, uid in enumerate((1, 2, 3, 99)):
        if mode == 4 and uid == 99:
            continue
        x = 15+i*90
        draw.rectangle((x, 15, x+9, 24), fill=COLOURS[uid])
        draw.text((x+15, 11), "Server" if uid==99 else "Player "+str(uid), font=FONT, fill=TEXT)
    owner_map = Image.frombytes("P", (50, 50), bytes(frame["owners"]))
    colours = [0]*768
    for uid, value in COLOURS.items():
        colours[uid*3:uid*3+3] = bytes.fromhex(value[1:])
    owner_map.putpalette(colours)
    image.paste(owner_map.transpose(Image.Transpose.FLIP_TOP_BOTTOM).resize((350, 350), Image.Resampling.NEAREST).convert("RGB"), (LEFT, TOP))
    for i in range(51):
        draw.line((LEFT+i*CELL, TOP, LEFT+i*CELL, TOP+350), fill=BG)
        draw.line((LEFT, TOP+i*CELL, LEFT+350, TOP+i*CELL), fill=BG)
    for player in frame["players"]:
        cells = {tuple(c[:2]) for c in player["coverage"]}
        edges = []
        for x, z in cells:
            l, t = LEFT+x*CELL, TOP+(49-z)*CELL
            r, b = l+CELL, t+CELL
            if (x-1,z) not in cells: edges.append((l,t,l,b))
            if (x+1,z) not in cells: edges.append((r,t,r,b))
            if (x,z+1) not in cells: edges.append((l,t,r,t))
            if (x,z-1) not in cells: edges.append((l,b,r,b))
        for edge in edges: draw.line(edge, fill=BG, width=3)
        for edge in edges: draw.line(edge, fill=COLOURS[player["id"]], width=1)
    for x,z,caption in ((10,46,"Forge"),(38,46,"Storage"),(32,3,"Farm")):
        px,py = point(x,z)
        width = draw.textlength(caption,font=BOLD)+12
        draw.rounded_rectangle((px-width/2,py-9,px+width/2,py+9),radius=3,fill=BG)
        draw.text((px,py),caption,font=BOLD,fill=TEXT,anchor="mm")
    for player in frame["players"]:
        px,py = point(player["x"],player["z"])
        draw.ellipse((px-6,py-6,px+6,py+6),fill=COLOURS[player["id"]],outline=BG,width=1)
        draw.text((px,py),str(player["id"]),font=NUMBER,fill=BG,anchor="mm")
    note = "Vacated fields keep their owner." if mode in (1,2) else "Vacated fields return to the server." if mode==3 else "Shared fields prefer a better-connected player."
    draw.text((190,409),note,font=FONT,fill=MUTED,anchor="mm")
    return image
