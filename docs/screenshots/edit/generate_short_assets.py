#!/usr/bin/env python3
"""Baut die 9:16-Bilder fuer den YouTube-Short (1080x1920, gerendert in 2160x3840).

Anders als beim Langvideo wird hier pro Beat ein fertiges Vollbild komponiert:
Ueberschrift, Screenshot im Rahmen und Fusszeile stecken schon im PNG. Im Hochformat
muss der Screenshot beschnitten oder eingebettet werden - das laesst sich in PIL
kontrollieren, in einer ffmpeg-Filterkette nicht mehr lesbar.

  python generate_short_assets.py            -> short/*.png        (Deutsch)
  VLANG=en python generate_short_assets.py   -> short_en/*.png     (Englisch)

Der Live-Beat bekommt statt eines Screenshots ein Loch: `live_bg.png` traegt nur
Text und Rahmen, das Phone-Video legt build_short.py an die Stelle PHONE_BOX.
"""
import os
from PIL import Image, ImageDraw, ImageFont, ImageFilter

HERE  = os.path.dirname(os.path.abspath(__file__))
SHOTS = os.path.normpath(os.path.join(HERE, ".."))
LANG  = os.environ.get("VLANG", "de").lower()
OUT   = os.path.join(HERE, "short_en" if LANG == "en" else "short")
os.makedirs(OUT, exist_ok=True)

# ---------------------------------------------------------------- Texte
TXT = {
    "de": {
        "intro_tag": "KNX-Bus in Echtzeit",
        "intro_sub": "Open Source  ·  selbst gehostet",
        "close_title": "Beta-Tester gesucht",
        "close_sub": "Egal ob KNX-Profi oder Einsteiger",
        "close_foot": "Open Source  ·  MIT  ·  selbst gehostet",
        "beats": {
            "live":     ("Live-Monitor",      "Jedes Telegramm sofort dekodiert"),
            "archive":  ("Live & Archiv",     "Lückenlos historisiert · durchsuchbar"),
            "charts":   ("Charts",            "Zeitreihen je Einheit · live ergänzt"),
            "stats":    ("Statistik",         "Aufkommen und Routinen auf einen Blick"),
            "groupadr": ("ETS-Import",        "Gruppenadressen und Geräte mit Klarnamen"),
            "desktop":  ("Auch am Desktop",   "Docker · Binary · Linux, Windows, macOS, Pi"),
        },
    },
    "en": {
        "intro_tag": "Your KNX bus in real time",
        "intro_sub": "Open source  ·  self-hosted",
        "close_title": "Beta testers wanted",
        "close_sub": "Whether KNX pro or beginner",
        "close_foot": "Open source  ·  MIT  ·  self-hosted",
        "beats": {
            "live":     ("Live Monitor",      "Every telegram decoded instantly"),
            "archive":  ("Live & Archive",    "Seamless history · searchable"),
            "charts":   ("Charts",            "Time series per unit · updated live"),
            "stats":    ("Statistics",        "Traffic and routines at a glance"),
            "groupadr": ("ETS import",        "Group addresses and devices, real names"),
            "desktop":  ("On the desktop too", "Docker · binary · Linux, Windows, macOS, Pi"),
        },
    },
}
L = TXT[LANG]

BG    = (14, 53, 49)
MINT  = (79, 224, 168)
WHITE = (240, 248, 245)
GRAY  = (150, 190, 180)
DIM   = (110, 150, 142)

FONTS = "C:/Windows/Fonts/"
def font(name, size): return ImageFont.truetype(FONTS + name, int(size))
def Fb(sz):  return font("segoeuib.ttf", sz)
def Fsb(sz): return font("seguisb.ttf", sz)
def Fr(sz):  return font("segoeui.ttf", sz)
def Fl(sz):  return font("segoeuil.ttf", sz)
def Fsl(sz): return font("segoeuisl.ttf", sz)
def tw(d, t, f): b = d.textbbox((0, 0), t, f); return b[2] - b[0]

S = 2                       # Supersampling: gerendert wird 2160x3840
W, H = 1080 * S, 1920 * S
# Platz fuer das Phone-Video im Live-Beat (Koordinaten im gerenderten 2160x3840-Bild)
PHONE_H = 1330 * S
PHONE_W = int(PHONE_H * 780 / 1688)
PHONE_X = (W - PHONE_W) // 2
PHONE_Y = 400 * S

def grad_bg():
    img = Image.new("RGB", (W, H), BG)
    d = ImageDraw.Draw(img)
    top, bot = (18, 63, 58), (7, 30, 27)
    for y in range(H):
        t = y / H
        d.line([(0, y), (W, y)], fill=tuple(int(top[i] + (bot[i] - top[i]) * t) for i in range(3)))
    return img, d

def wordmark(d, cx, cy, mult=1.0, dim=True):
    fs = 52 * mult * S
    fb, fl = Fb(fs), Fsl(fs * 0.62)
    parts = [("KNX", WHITE if not dim else (200, 220, 214), fb), ("·NG", MINT, fb)]
    wmark = sum(tw(d, t, fo) for t, _, fo in parts)
    gap = 20 * mult * S
    mon = " ".join("MONITOR")
    total = wmark + gap + tw(d, mon, fl)
    x = cx - total / 2
    ab = d.textbbox((0, 0), "KNX", fb)
    y = cy - (ab[3] - ab[1]) / 2 - ab[1]
    for t, col, fo in parts:
        d.text((x, y), t, font=fo, fill=col); x += tw(d, t, fo)
    x += gap
    al = d.textbbox((0, 0), mon, fl)
    d.text((x, cy - (al[3] - al[1]) / 2 - al[1]), mon, font=fl, fill=GRAY if not dim else DIM)

def headline(d, title, sub):
    ft, fs = Fb(66 * S), Fl(36 * S)
    d.text(((W - tw(d, title, ft)) / 2, 132 * S), title, font=ft, fill=WHITE)
    d.rectangle([W / 2 - 60 * S, 236 * S, W / 2 + 60 * S, 242 * S], fill=MINT)
    d.text(((W - tw(d, sub, fs)) / 2, 272 * S), sub, font=fs, fill=GRAY)

def framed(img, shot, box, radius=30):
    """Screenshot mit weichem Schatten und duennem Rahmen einsetzen."""
    x, y, w, h = box
    im = shot.convert("RGB").resize((w, h), Image.LANCZOS)
    mask = Image.new("L", (w, h), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, w, h], radius=radius * S, fill=255)
    sh = Image.new("RGBA", img.size, (0, 0, 0, 0))
    ImageDraw.Draw(sh).rounded_rectangle([x + 6 * S, y + 14 * S, x + w + 6 * S, y + h + 14 * S],
                                         radius=radius * S, fill=(0, 0, 0, 165))
    sh = sh.filter(ImageFilter.GaussianBlur(16 * S))
    img.paste(Image.new("RGB", img.size, (0, 0, 0)), (0, 0), sh.split()[3].point(lambda v: int(v * 0.55)))
    img.paste(im, (x, y), mask)
    ImageDraw.Draw(img).rounded_rectangle([x, y, x + w, y + h], radius=radius * S,
                                          outline=(46, 104, 96), width=int(3 * S))

def save(img, name):
    img.save(os.path.join(OUT, name))
    print("  ", name)

# ---------------------------------------------------------------- Beats
def intro_card():
    img, d = grad_bg()
    wordmark(d, W / 2, H * 0.42, mult=1.9, dim=False)
    ft = Fsb(46 * S)
    d.text(((W - tw(d, L["intro_tag"], ft)) / 2, H * 0.50), L["intro_tag"], font=ft, fill=WHITE)
    fs = Fl(36 * S)
    d.text(((W - tw(d, L["intro_sub"], fs)) / 2, H * 0.555), L["intro_sub"], font=fs, fill=GRAY)
    d.rectangle([W / 2 - 60 * S, H * 0.61, W / 2 + 60 * S, H * 0.61 + 6 * S], fill=MINT)
    save(img, "intro.png")

def close_card():
    img, d = grad_bg()
    wordmark(d, W / 2, H * 0.30, mult=1.5, dim=False)
    ft = Fb(72 * S)
    d.text(((W - tw(d, L["close_title"], ft)) / 2, H * 0.40), L["close_title"], font=ft, fill=MINT)
    d.rectangle([W / 2 - 60 * S, H * 0.475, W / 2 + 60 * S, H * 0.475 + 6 * S], fill=MINT)
    fs = Fr(40 * S)
    d.text(((W - tw(d, L["close_sub"], fs)) / 2, H * 0.51), L["close_sub"], font=fs, fill=WHITE)
    url = "github.com/ingel81/knx-ng-monitor"
    fu = Fsb(40 * S)
    d.text(((W - tw(d, url, fu)) / 2, H * 0.58), url, font=fu, fill=MINT)
    ff = Fl(32 * S)
    d.text(((W - tw(d, L["close_foot"], ff)) / 2, H * 0.64), L["close_foot"], font=ff, fill=GRAY)
    save(img, "close.png")

def phone_beat(name, shot_file, key):
    """Phone-Screenshot formatfuellend - das Hochformat ist hier der Normalfall."""
    img, d = grad_bg()
    title, sub = L["beats"][key]
    headline(d, title, sub)
    framed(img, Image.open(os.path.join(SHOTS, shot_file)), (PHONE_X, PHONE_Y, PHONE_W, PHONE_H))
    wordmark(d := ImageDraw.Draw(img), W / 2, H - 92 * S, mult=0.62)
    save(img, name)

def desktop_beat(name, shot_file, phone_file, key, crop=(0, 100, 1980, 1500)):
    """Desktop und Phone in einem Bild.

    Der volle 16:9-Screenshot wuerde im Hochformat als schmaler Streifen landen und
    seine Schrift waere am Handy unlesbar - deshalb ein Ausschnitt der Tabelle statt
    der ganzen Seite. Das Phone daneben fuellt die untere Haelfte und zeigt nebenbei
    das dunkle Thema."""
    img, d = grad_bg()
    title, sub = L["beats"][key]
    headline(d, title, sub)

    shot = Image.open(os.path.join(SHOTS, shot_file)).crop(crop)
    bw = int(W - 70 * S)
    bh = int(bw * shot.height / shot.width)
    framed(img, shot, (int((W - bw) / 2), int(H * 0.235), bw, bh), radius=22)

    ph_h = int(H * 0.36)
    phone = Image.open(os.path.join(SHOTS, phone_file))
    ph_w = int(ph_h * phone.width / phone.height)
    # Unterkante ueber der Fusszeile halten, sonst laeuft das Phone aus dem Bild.
    framed(img, phone, (int(W - ph_w - 60 * S), int(H - 168 * S - ph_h), ph_w, ph_h), radius=26)

    wordmark(ImageDraw.Draw(img), W / 2, H - 92 * S, mult=0.62)
    save(img, name)

def live_bg():
    """Hintergrund fuer den bewegten Beat - die Flaeche von PHONE_BOX bleibt frei."""
    img, d = grad_bg()
    title, sub = L["beats"]["live"]
    headline(d, title, sub)
    # Rahmen andeuten, damit das spaeter ueberlagerte Video sauber sitzt
    d.rounded_rectangle([PHONE_X - 3 * S, PHONE_Y - 3 * S, PHONE_X + PHONE_W + 3 * S, PHONE_Y + PHONE_H + 3 * S],
                        radius=30 * S, outline=(46, 104, 96), width=int(3 * S))
    wordmark(d, W / 2, H - 92 * S, mult=0.62)
    save(img, "live_bg.png")
    # Maske fuer die gleiche Eckenrundung wie bei den Standbildern - ffmpeg legt sie
    # per alphamerge auf das Phone-Video, sonst stossen eckige Videoecken an den
    # abgerundeten Rahmen.
    mask = Image.new("L", (PHONE_W, PHONE_H), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, PHONE_W, PHONE_H], radius=30 * S, fill=255)
    mask.save(os.path.join(OUT, "live_mask.png"))
    with open(os.path.join(OUT, "live_box.txt"), "w") as f:
        f.write("%d %d %d %d\n" % (PHONE_X // S, PHONE_Y // S, PHONE_W // S, PHONE_H // S))

print("short assets (lang=%s) -> %s" % (LANG, OUT))
intro_card()
close_card()
live_bg()
phone_beat("archive.png",  "monitor-archive-mobile.webp",  "archive")
phone_beat("charts.png",   "charts-mobile.webp",           "charts")
phone_beat("stats.png",    "stats-mobile.webp",            "stats")
phone_beat("groupadr.png", "group-addresses-mobile.webp",  "groupadr")
desktop_beat("desktop.png", "monitor-live.webp", "hero-dark-mobile.webp", "desktop")
