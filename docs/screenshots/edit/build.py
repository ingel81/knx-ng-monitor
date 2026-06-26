#!/usr/bin/env python3
"""Build the KNX-NG-Monitor intro video (long form, ~100s).
Jitter-free Ken Burns via supersampling: zoompan renders at 3840x2160, then Lanczos-downscale to 1920x1080."""
import os, subprocess

HERE  = os.path.dirname(os.path.abspath(__file__))
SHOTS = os.path.normpath(os.path.join(HERE, ".."))
CARDS = os.path.join(HERE, "cards")
SEG   = os.path.join(HERE, "segments")
os.makedirs(SEG, exist_ok=True)

FPS, T, CRF, ZOOM = 30, 0.5, "16", 0.030
SS = 4  # supersample factor for jitter-free Ken Burns (zoompan @ SS*res, then lanczos down)

def C(n): return os.path.join(CARDS, n)
def P(n): return os.path.join(SHOTS, n)
def V(n): return os.path.join(HERE, n)

# (id, image, label|None, duration, zoom_dir)
SEGMENTS = [
    ("s00_intro",    C("intro.png"),              None,                 6.0,  1),
    ("s01_monitor",  V("clips_live_desktop.mp4"), C("label_monitor.png"),  8.0,  1),  # real desktop live feed
    ("s02_archive",  P("monitor-archive.webp"),   C("label_archive.png"),  9.0, -1),
    ("s03_detail",   P("monitor-detail.webp"),    C("label_detail.png"),   8.5,  1),
    ("s04_charts",   P("charts.webp"),            C("label_charts.png"),   8.5, -1),
    ("s05_temp",     P("charts-temp.webp"),       C("label_temp.png"),     7.5,  1),
    ("s06_stats",    P("stats.webp"),             C("label_stats.png"),    6.5, -1),
    ("s07_heatmap",  P("stats-heatmap.webp"),     C("label_heatmap.png"),  6.5,  1),
    ("s08_import",   P("projects-import.webp"),   C("label_import.png"),   8.0, -1),
    ("s09_secure",   C("secure.png"),             None,                 9.0,  1),
    ("s10_topology", P("topology.webp"),          C("label_topology.png"), 7.5, -1),
    ("s11_groupadr", P("group-addresses.webp"),   C("label_groupadr.png"), 7.0,  1),
    ("s12_mobile",   V("clips_mobile_beat.mp4"),  None,                 7.0, -1),  # real mobile live feed (cropped)
    ("s13_settings", P("settings.webp"),          C("label_settings.png"), 7.0,  1),
    ("s14_platform", C("platform.png"),           None,                 9.0,  1),
    ("s15_graph",    P("graph.webp"),             C("label_graph.png"),    8.5, -1),
    ("s16_close",    C("close.png"),              None,                 9.0,  1),  # merged Beta + Outro
]

def run(cmd):
    r = subprocess.run(cmd, capture_output=True, text=True)
    if r.returncode != 0:
        print("FFMPEG ERROR:\n", r.stderr[-2500:]); raise SystemExit(1)

# per-beat focus/zoom overrides (default: centered, ZOOM)
OVR = {
    "s03_detail": dict(fx=0.82, fy=0.46, amt=0.12),  # push into the right-hand bus-actions drawer
    "s08_import": dict(fx=0.50, fy=0.46, amt=0.11),  # punch into the centered wizard modal
}

def kenburns(sid, zdir, tot):
    o = OVR.get(sid, {})
    fx, fy, amt = o.get("fx", 0.5), o.get("fy", 0.5), o.get("amt", ZOOM)
    if zdir > 0:
        z = f"1.0+{amt}*on/{tot}"
    else:
        z = f"{1.0+amt}-{amt}*on/{tot}"
    # supersample SSx: zoompan at SS*res, then lanczos downscale -> integer x/y steps shrink to sub-pixel = no visible jitter
    sw, sh = 1920 * SS, 1080 * SS
    return (f"scale={sw}:{sh}:flags=lanczos,zoompan=z='{z}':"
            f"x='(iw-iw/zoom)*{fx}':y='(ih-ih/zoom)*{fy}':"
            f"d={tot}:s={sw}x{sh}:fps={FPS},scale=1920:1080:flags=lanczos")

def build_segment(sid, img, label, dur, zdir):
    tot = int(round(dur * FPS))
    out = os.path.join(SEG, sid + ".mp4")
    video = img.lower().endswith(".mp4")
    is_card = os.path.dirname(os.path.abspath(img)) == os.path.abspath(CARDS)
    if video:
        base = f"scale=1920:1080:flags=lanczos,fps={FPS},setsar=1"
    elif is_card:
        # cards have no fine detail -> gentle Ken Burns, no measurable shimmer
        base = kenburns(sid, zdir, tot)
    else:
        # screenshots (1px lines, small text) shimmer under any motion -> render static = zero shimmer
        base = f"scale=1920:1080:flags=lanczos,setsar=1,fps={FPS}"
    loop = [] if video else ["-loop", "1"]
    if label:
        fc = (f"[0:v]{base}[bg];"
              f"[1:v]fade=t=in:st=0.4:d=0.6:alpha=1[lbl];"
              f"[bg][lbl]overlay=0:0,format=yuv420p[v]")
        cmd = ["ffmpeg", "-y"] + loop + ["-i", img, "-loop", "1", "-i", label,
               "-filter_complex", fc, "-map", "[v]"]
    else:
        fc = f"[0:v]{base},format=yuv420p[v]"
        cmd = ["ffmpeg", "-y"] + loop + ["-i", img, "-filter_complex", fc, "-map", "[v]"]
    cmd += ["-t", str(dur), "-r", str(FPS), "-c:v", "libx264", "-crf", CRF,
            "-preset", "medium", "-pix_fmt", "yuv420p", out]
    print("build", sid); run(cmd)
    return out, dur

def main():
    segs = [build_segment(*s) for s in SEGMENTS]
    inputs = []
    for out, _ in segs:
        inputs += ["-i", out]
    fc, prev, cum = [], "0:v", segs[0][1]
    for i in range(1, len(segs)):
        off = cum - T
        lbl = f"x{i}"
        fc.append(f"[{prev}][{i}:v]xfade=transition=fade:duration={T}:offset={off:.3f}[{lbl}]")
        prev, cum = lbl, cum + segs[i][1] - T
    fc.append(f"[{prev}]fade=t=in:st=0:d=0.5,fade=t=out:st={cum-0.7:.3f}:d=0.7,format=yuv420p[out]")

    # background music — sehr dezent, fade in/out, geloopt + getrimmt auf Videolänge
    music = V("track02.mp3")
    has_music = os.path.exists(music)
    if has_music:
        VOL = 0.09
        a_in = len(segs)  # music is the input after all segment inputs
        fc.append(
            f"[{a_in}:a]volume={VOL},afade=t=in:st=0:d=2.0,"
            f"afade=t=out:st={cum-2.5:.3f}:d=2.5,atrim=0:{cum:.3f},aresample=48000[aud]")

    final = os.path.join(HERE, "final.mp4")
    cmd = ["ffmpeg", "-y"] + inputs
    if has_music:
        cmd += ["-stream_loop", "-1", "-i", music]
    cmd += ["-filter_complex", ";".join(fc), "-map", "[out]"]
    if has_music:
        cmd += ["-map", "[aud]", "-c:a", "aac", "-b:a", "160k"]
    cmd += ["-r", str(FPS), "-c:v", "libx264", "-crf", CRF,
            "-preset", "medium", "-pix_fmt", "yuv420p", "-movflags", "+faststart", final]
    print("xfade chain, total ~%.1fs%s" % (cum, " +music" if has_music else "")); run(cmd)
    print("DONE ->", final)

if __name__ == "__main__":
    main()
