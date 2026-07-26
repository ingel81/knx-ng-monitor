#!/usr/bin/env python3
"""Generate cards (3840x2160, crisp for supersampled zoom) + lower-third labels (1920x1080).

Language: set env VLANG=en to render English text into cards_en/. Default (de) → cards/.
The DE pipeline is the default and stays byte-for-byte unchanged."""
import os
from PIL import Image, ImageDraw, ImageFont, ImageFilter

HERE  = os.path.dirname(os.path.abspath(__file__))
SHOTS = os.path.normpath(os.path.join(HERE, ".."))
LANG  = os.environ.get("VLANG", "de").lower()
OUT   = os.path.join(HERE, "cards_en" if LANG == "en" else "cards")
os.makedirs(OUT, exist_ok=True)

# ---------------------------------------------------------------- i18n strings
# Every user-visible string keyed by language. DE is the original wording; EN is
# a natural (non-literal) translation. Labels are (title, subtitle[, tag]).
TXT = {
    "de": {
        "intro_tag":  "KNX-Bus in Echtzeit  ·  historisiert  ·  visualisiert",
        "outro_os":   "Open Source  ·  MIT",
        "outro_l2":   "KNX-Bus in Echtzeit - selbst gehostet",
        "plat_title": "Überall lauffähig",
        "plat_rows":  [("Docker", "  ·  ", "Single-File-Binary"),
                       ("Linux", "  ·  ", "Windows  ·  macOS"),
                       ("x64", "  +  ", "ARM64  ·  Raspberry Pi")],
        "plat_foot":  "Als Container oder portable Binary starten - bedient im Browser, komplett lokal",
        "beta_title": "Beta-Tester gesucht",
        "beta_sub":   "Egal ob KNX-Profi oder Smart-Home-Einsteiger",
        "beta_foot":  "Issues & Ideen willkommen",
        "close_title":"Beta-Tester gesucht",
        "close_sub":  "Egal ob KNX-Profi oder Smart-Home-Einsteiger",
        "close_foot": "Open Source  ·  MIT  ·  selbst gehostet",
        "mob_title":  "Voll responsive",
        "mob_sub":    "Phone-Layout  ·  Bottom-Nav  ·  Karten statt Tabellen",
        "moblive_title": "Voll responsive",
        "moblive_bul": ["Live-Feed auch unterwegs", "Für Smartphone und Tablet",
                        "Karten statt Tabellen", "Touch-optimiert"],
        "secure_title": "KNX Secure",
        "secure_bul": ["Passwortgeschützte Projekte (ETS 4 / 5 / 6)",
                       ".knxkeys-Keyring-Entschlüsselung",
                       "Data-Secure: Telegramme zur Laufzeit entschlüsselt",
                       "Optional: IP-Secure-Tunnel"],
        "labels": {
            "monitor":  ("Live-Monitor",        "Telegramme in Echtzeit vom Bus · always-on, auto-reconnect · DPT-decodiert mit Einheiten"),
            "archive":  ("Live & Archiv",        "Lückenlos historisiert · Volltextsuche · umfangreich filterbar · Historie ohne Limit · CSV-Export"),
            "detail":   ("Schreiben & Lesen",    "Werte direkt auf den Bus schreiben oder lesen - aus der Detailansicht"),
            "charts":   ("Charts",               "Zeitreihen je DPT · eigene Y-Achse pro Einheit · neue Werte live"),
            "temp":     ("Mess- & Schaltkurven", "Alle Zahlenwerte und Schaltvorgänge im Zeitverlauf"),
            "stats":    ("Statistik",            "Summen · Ø msg/s · Telegramme über Zeit"),
            "heatmap":  ("Aktivitäts-Heatmap",   "Wochentag × Stunde - Routinen auf einen Blick"),
            "import":   ("ETS 4 / 5 / 6 Import", "Zweistufiger Wizard · .knxproj · Gruppenadressen, Geräte, Hardware"),
            "topology": ("Topologie",            "Gebäude → Etage → Raum · Kommunikationsobjekte · Raumfilter"),
            "groupadr": ("Gruppenadressen",      "3-Ebenen-Baum · durchsuchbar · lesen / schreiben / charten"),
            "settings": ("Themes & Sprache",     "Light & Console (Dark) · DE / EN live umschaltbar · Dichte einstellbar"),
            "graph":    ("GA-Netzwerk-Graph",    "Building → Floor → Room → GA · Live-Telegramme lassen Knoten glühen", "EXPERIMENTELL"),
        },
    },
    "en": {
        "intro_tag":  "Your KNX bus in real time  ·  historized  ·  visualized",
        "outro_os":   "Open Source  ·  MIT",
        "outro_l2":   "Your KNX bus in real time - self-hosted",
        "plat_title": "Runs anywhere",
        "plat_rows":  [("Docker", "  ·  ", "Single-file binary"),
                       ("Linux", "  ·  ", "Windows  ·  macOS"),
                       ("x64", "  +  ", "ARM64  ·  Raspberry Pi")],
        "plat_foot":  "Run as a container or portable binary - operated in the browser, fully local",
        "beta_title": "Beta testers wanted",
        "beta_sub":   "Whether KNX pro or smart-home beginner",
        "beta_foot":  "Issues & ideas welcome",
        "close_title":"Beta testers wanted",
        "close_sub":  "Whether KNX pro or smart-home beginner",
        "close_foot": "Open Source  ·  MIT  ·  self-hosted",
        "mob_title":  "Fully responsive",
        "mob_sub":    "Phone layout  ·  bottom nav  ·  cards instead of tables",
        "moblive_title": "Fully responsive",
        "moblive_bul": ["Live feed on the go", "For smartphone and tablet",
                        "Cards instead of tables", "Touch-optimized"],
        "secure_title": "KNX Secure",
        "secure_bul": ["Password-protected projects (ETS 4 / 5 / 6)",
                       ".knxkeys keyring decryption",
                       "Data Secure: telegrams decrypted at runtime",
                       "Optional: IP Secure tunnel"],
        "labels": {
            "monitor":  ("Live Monitor",         "Telegrams in real time from the bus · always-on, auto-reconnect · DPT-decoded with units"),
            "archive":  ("Live & Archive",        "Seamlessly historized · full-text search · richly filterable · unlimited history · CSV export"),
            "detail":   ("Write & Read",          "Write or read values directly on the bus - from the detail view"),
            "charts":   ("Charts",                "Time series per DPT · own Y axis per unit · new values live"),
            "temp":     ("Measurement & switching curves", "All numeric values and switching events over time"),
            "stats":    ("Statistics",            "Totals · avg msg/s · telegrams over time"),
            "heatmap":  ("Activity heatmap",      "Weekday × hour - routines at a glance"),
            "import":   ("ETS 4 / 5 / 6 import",  "Two-step wizard · .knxproj · group addresses, devices, hardware"),
            "topology": ("Topology",              "Building → Floor → Room · communication objects · room filter"),
            "groupadr": ("Group addresses",       "3-level tree · searchable · read / write / chart"),
            "settings": ("Themes & Language",     "Light & Console (Dark) · DE / EN live switchable · adjustable density"),
            "graph":    ("GA network graph",      "Building → Floor → Room → GA · live telegrams make nodes glow", "EXPERIMENTAL"),
        },
    },
}
L = TXT[LANG]

# Palette — card bg = app nav-bar dark teal (bridges dark hero + light screenshots)
BG    = (14, 53, 49)
MINT  = (79, 224, 168)
WHITE = (240, 248, 245)
GRAY  = (150, 190, 180)
DIM   = (110, 150, 142)

FONTS = "C:/Windows/Fonts/"
def font(name, size): return ImageFont.truetype(FONTS + name, int(size))
def tw(d, t, f): b = d.textbbox((0, 0), t, f); return b[2] - b[0]
def th(d, t, f): b = d.textbbox((0, 0), t, f); return b[3] - b[1]

bold  = lambda s: f"segoeuib.ttf"
def Fb(sz): return font("segoeuib.ttf", sz)
def Fsb(sz): return font("seguisb.ttf", sz)
def Fr(sz): return font("segoeui.ttf", sz)
def Fl(sz): return font("segoeuil.ttf", sz)
def Fsl(sz): return font("segoeuisl.ttf", sz)

def grad_bg(s):
    W, H = 1920 * s, 1080 * s
    img = Image.new("RGB", (W, H), BG)
    d = ImageDraw.Draw(img)
    top, bot = (17, 60, 55), (8, 33, 30)
    for y in range(H):
        t = y / H
        d.line([(0, y), (W, y)], fill=tuple(int(top[i] + (bot[i] - top[i]) * t) for i in range(3)))
    return img, d

def draw_logo(d, cx, cy, s, mult=1.0):
    fs = 64 * mult * s
    fb, fl = Fb(fs), Fsl(fs * 0.62)
    parts = [("KNX", WHITE, fb), ("·NG", MINT, fb)]
    wmark = sum(tw(d, t, fo) for t, _, fo in parts)
    gap = 24 * mult * s
    mon = " ".join("MONITOR")
    monw = tw(d, mon, fl)
    total = wmark + gap + monw
    x = cx - total / 2
    ab = d.textbbox((0, 0), "KNX", fb)
    y = cy - (ab[3] - ab[1]) / 2 - ab[1]
    for t, col, fo in parts:
        d.text((x, y), t, font=fo, fill=col); x += tw(d, t, fo)
    x += gap
    al = d.textbbox((0, 0), mon, fl)
    yl = cy - (al[3] - al[1]) / 2 - al[1]
    d.text((x, yl), mon, font=fl, fill=GRAY)

def rrect(d, box, r, fill=None, outline=None, width=1):
    d.rounded_rectangle(box, radius=r, fill=fill, outline=outline, width=width)

def save(img, name): img.save(os.path.join(OUT, name))

# ---------------------------------------------------------------- cards (s=2)
S = 2
W, H = 1920 * S, 1080 * S

def intro_card():
    img, d = grad_bg(S)
    draw_logo(d, W / 2, H / 2 - 40 * S, S, mult=1.30)
    tagline = L["intro_tag"]
    ft = Fl(36 * S)
    d.text(((W - tw(d, tagline, ft)) / 2, H / 2 + 60 * S), tagline, font=ft, fill=GRAY)
    d.rectangle([W / 2 - 60 * S, H / 2 + 130 * S, W / 2 + 60 * S, H / 2 + 134 * S], fill=MINT)
    save(img, "intro.png")

def outro_card():
    img, d = grad_bg(S)
    draw_logo(d, W / 2, H / 2 - 110 * S, S, mult=1.05)
    l1 = L["outro_os"]
    f1 = Fsb(40 * S)
    d.text(((W - tw(d, l1, f1)) / 2, H / 2 - 5 * S), l1, font=f1, fill=WHITE)
    l2 = L["outro_l2"]
    f2 = Fl(32 * S)
    d.text(((W - tw(d, l2, f2)) / 2, H / 2 + 60 * S), l2, font=f2, fill=GRAY)
    url = "github.com/ingel81/knx-ng-monitor"
    f3 = Fr(34 * S)
    d.text(((W - tw(d, url, f3)) / 2, H / 2 + 130 * S), url, font=f3, fill=MINT)
    save(img, "outro.png")

def bullet_card(name, title, bullets, accent_title=False):
    img, d = grad_bg(S)
    ft = Fb(72 * S)
    title_w = tw(d, title, ft)
    tx = W / 2 - title_w / 2
    ty = H * 0.20
    d.text((tx, ty), title, font=ft, fill=(MINT if accent_title else WHITE))
    # accent bar under title
    d.rectangle([W / 2 - 70 * S, ty + 100 * S, W / 2 + 70 * S, ty + 106 * S], fill=MINT)
    fb = Fr(40 * S)
    line_h = 86 * S
    block_h = line_h * len(bullets)
    y = ty + 200 * S
    # left-align the block, centered horizontally as a group
    maxw = max(tw(d, b, fb) for b in bullets)
    x0 = W / 2 - (maxw + 60 * S) / 2
    for b in bullets:
        d.ellipse([x0, y + 16 * S, x0 + 18 * S, y + 34 * S], fill=MINT)
        d.text((x0 + 44 * S, y), b, font=fb, fill=WHITE)
        y += line_h
    save(img, name)

def platform_card():
    img, d = grad_bg(S)
    ft = Fb(72 * S)
    title = L["plat_title"]
    d.text(((W - tw(d, title, ft)) / 2, H * 0.18), title, font=ft, fill=WHITE)
    d.rectangle([W / 2 - 70 * S, H * 0.18 + 100 * S, W / 2 + 70 * S, H * 0.18 + 106 * S], fill=MINT)
    rows = L["plat_rows"]
    fbld = Fsb(46 * S); freg = Fr(46 * S)
    y = H * 0.36
    for a, sep, b in rows:
        full = a + sep + b
        fw = tw(d, a, fbld) + tw(d, sep, freg) + tw(d, b, fbld)
        x = W / 2 - fw / 2
        d.text((x, y), a, font=fbld, fill=MINT); x += tw(d, a, fbld)
        d.text((x, y), sep, font=freg, fill=DIM); x += tw(d, sep, freg)
        d.text((x, y), b, font=fbld, fill=WHITE)
        y += 92 * S
    foot = L["plat_foot"]
    ff = Fl(36 * S)
    d.text(((W - tw(d, foot, ff)) / 2, y + 30 * S), foot, font=ff, fill=GRAY)
    save(img, "platform.png")

def beta_card():
    img, d = grad_bg(S)
    ft = Fb(88 * S)
    title = L["beta_title"]
    d.text(((W - tw(d, title, ft)) / 2, H * 0.26), title, font=ft, fill=MINT)
    sub = L["beta_sub"]
    fs = Fr(42 * S)
    d.text(((W - tw(d, sub, fs)) / 2, H * 0.46), sub, font=fs, fill=WHITE)
    url = "github.com/ingel81/knx-ng-monitor"
    fu = Fsb(44 * S)
    d.text(((W - tw(d, url, fu)) / 2, H * 0.57), url, font=fu, fill=MINT)
    foot = L["beta_foot"]
    ff = Fl(34 * S)
    d.text(((W - tw(d, foot, ff)) / 2, H * 0.66), foot, font=ff, fill=GRAY)
    save(img, "beta.png")

def close_card():
    """Merged closing slide: Logo + Beta-CTA + URL + Open Source/MIT."""
    img, d = grad_bg(S)
    draw_logo(d, W / 2, H * 0.22, S, mult=1.05)
    title = L["close_title"]
    ft = Fb(78 * S)
    d.text(((W - tw(d, title, ft)) / 2, H * 0.36), title, font=ft, fill=MINT)
    d.rectangle([W / 2 - 70 * S, H * 0.36 + 108 * S, W / 2 + 70 * S, H * 0.36 + 114 * S], fill=MINT)
    sub = L["close_sub"]
    fs = Fr(40 * S)
    d.text(((W - tw(d, sub, fs)) / 2, H * 0.52), sub, font=fs, fill=WHITE)
    url = "github.com/ingel81/knx-ng-monitor"
    fu = Fsb(44 * S)
    d.text(((W - tw(d, url, fu)) / 2, H * 0.62), url, font=fu, fill=MINT)
    foot = L["close_foot"]
    ff = Fl(32 * S)
    d.text(((W - tw(d, foot, ff)) / 2, H * 0.71), foot, font=ff, fill=GRAY)
    save(img, "close.png")

def mobile_card():
    img, d = grad_bg(S)
    ft = Fb(72 * S)
    title = L["mob_title"]
    d.text(((W - tw(d, title, ft)) / 2, H * 0.08), title, font=ft, fill=WHITE)
    d.rectangle([W / 2 - 70 * S, H * 0.08 + 100 * S, W / 2 + 70 * S, H * 0.08 + 106 * S], fill=MINT)
    sub = L["mob_sub"]
    fs = Fl(34 * S)
    d.text(((W - tw(d, sub, fs)) / 2, H * 0.18), sub, font=fs, fill=GRAY)
    # 3 phones
    files = ["monitor-live-mobile.webp", "charts-mobile.webp", "stats-mobile.webp"]
    ph = int(H * 0.56)
    pw = int(ph * 780 / 1688)
    gap = int(70 * S)
    total = 3 * pw + 2 * gap
    x = int(W / 2 - total / 2)
    ytop = int(H * 0.30)
    rad = int(36 * S)
    for fn in files:
        xi = int(x)
        im = Image.open(os.path.join(SHOTS, fn)).convert("RGB").resize((pw, ph), Image.LANCZOS)
        # rounded mask
        mask = Image.new("L", (pw, ph), 0)
        ImageDraw.Draw(mask).rounded_rectangle([0, 0, pw, ph], radius=rad, fill=255)
        # shadow
        sh = Image.new("RGBA", img.size, (0, 0, 0, 0))
        ImageDraw.Draw(sh).rounded_rectangle([xi + 8 * S, ytop + 12 * S, xi + pw + 8 * S, ytop + ph + 12 * S],
                                             radius=rad, fill=(0, 0, 0, 150))
        sh = sh.filter(ImageFilter.GaussianBlur(18 * S))
        img.paste(Image.new("RGB", img.size, (0, 0, 0)), (0, 0), sh.split()[3].point(lambda v: int(v * 0.5)))
        img.paste(im, (xi, ytop), mask)
        # border
        ImageDraw.Draw(img).rounded_rectangle([xi, ytop, xi + pw, ytop + ph], radius=rad,
                                              outline=(40, 90, 84), width=int(3 * S))
        x += pw + gap
    save(img, "mobile.png")

# ---------------------------------------------------------------- labels (s=1)
def label(name, title, subtitle, tag=None):
    LW, LH = 1920, 1080
    img = Image.new("RGBA", (LW, LH), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    grad_h = 460
    for i in range(grad_h):
        a = int(242 * (i / grad_h) ** 1.25)
        y = LH - grad_h + i
        d.line([(0, y), (LW, y)], fill=(3, 8, 7, a))
    x0, base = 96, LH - 158
    ft = Fsb(52); fs = Fr(31); ftag = Fsb(24)
    tb = d.textbbox((0, 0), title, font=ft); titleh = tb[3] - tb[1]
    d.rectangle([x0, base - 6, x0 + 6, base + titleh + 54], fill=MINT)
    tx = x0 + 30
    d.text((tx, base), title, font=ft, fill=WHITE)
    if tag:
        tagw = tw(d, tag, ftag)
        tagx = tx + tw(d, title, ft) + 24
        d.rounded_rectangle([tagx, base + 10, tagx + tagw + 36, base + 52], radius=21,
                            fill=(60, 40, 20, 230), outline=(240, 160, 60), width=2)
        d.text((tagx + 18, base + 16), tag, font=ftag, fill=(245, 180, 90))
    d.text((tx, base + titleh + 24), subtitle, font=fs, fill=(185, 205, 198))
    img.save(os.path.join(OUT, f"label_{name}.png"))

def mobile_live_bg():
    """1920x1080 bg for the mobile-live beat — text left, phone video overlaid right by ffmpeg."""
    LW, LH = 1920, 1080
    img = Image.new("RGB", (LW, LH), BG); d = ImageDraw.Draw(img)
    top, bot = (17, 60, 55), (8, 33, 30)
    for y in range(LH):
        t = y / LH
        d.line([(0, y), (LW, y)], fill=tuple(int(top[i] + (bot[i] - top[i]) * t) for i in range(3)))
    x, cy = 150, LH // 2
    d.text((x, cy - 230), L["moblive_title"], font=Fb(74), fill=WHITE)
    d.rectangle([x, cy - 118, x + 90, cy - 112], fill=MINT)
    bullets = L["moblive_bul"]
    fb = Fr(42); yy = cy - 60
    for b in bullets:
        d.ellipse([x, yy + 17, x + 20, yy + 37], fill=MINT)
        d.text((x + 46, yy), b, font=fb, fill=WHITE); yy += 84
    img.save(os.path.join(OUT, "mobile_live_bg.png"))

# ----- build all -----
intro_card(); outro_card(); platform_card(); beta_card(); close_card(); mobile_card(); mobile_live_bg()
bullet_card("secure.png", L["secure_title"], L["secure_bul"])

for _name, _spec in L["labels"].items():
    label(_name, *_spec)
print("assets ok (lang=%s, out=%s)" % (LANG, OUT))
