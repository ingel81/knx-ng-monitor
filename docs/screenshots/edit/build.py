#!/usr/bin/env python3
"""Build the KNX-NG-Monitor intro video (long form, voice-over).
Jitter-free Ken Burns via supersampling. Beat durations are driven by the per-beat
voice-over length; music is ducked under the VO via sidechaincompress."""
import os, subprocess

HERE  = os.path.dirname(os.path.abspath(__file__))
SHOTS = os.path.normpath(os.path.join(HERE, ".."))
# Language switch: VLANG=en → English cards/VO/output. Default (de) unchanged.
LANG  = os.environ.get("VLANG", "de").lower()
_SFX  = "_en" if LANG == "en" else ""
CARDS = os.path.join(HERE, "cards" + _SFX)
SEG   = os.path.join(HERE, "segments" + _SFX)
VO    = os.path.join(HERE, "vo" + _SFX)
FINAL = "final_en.mp4" if LANG == "en" else "final.mp4"
os.makedirs(SEG, exist_ok=True)

FPS, T, CRF, ZOOM = 30, 0.5, "16", 0.030
SS = 4              # supersample factor for jitter-free Ken Burns
LEAD, TRAIL = 0.4, 1.1   # VO starts LEAD after beat start; beat = VO + LEAD + TRAIL
FLOOR = 4.5        # minimum beat duration
MUSVOL = 0.10      # music base level (ducked further under VO) - track04 lauter
MUSTEMPO = 0.80    # slow the music down (pitch-preserving) - langsamer

def C(n): return os.path.join(CARDS, n)
def P(n): return os.path.join(SHOTS, n)
def V(n): return os.path.join(HERE, n)

# (id, image, label|None, zoom_dir) — duration is computed from the VO clip
SEGMENTS = [
    ("s00_intro",    C("intro.png"),              None,                     1),
    ("s01_monitor",  V("clips_live_desktop.mp4"), C("label_monitor.png"),   1),
    ("s02_archive",  P("monitor-archive.webp"),   C("label_archive.png"),  -1),
    ("s03_detail",   P("monitor-detail.webp"),    C("label_detail.png"),    1),
    ("s04_charts",   P("charts.webp"),            C("label_charts.png"),   -1),
    ("s05_temp",     P("charts-temp.webp"),       C("label_temp.png"),      1),
    ("s06_stats",    P("stats.webp"),             C("label_stats.png"),    -1),
    ("s07_heatmap",  P("stats-heatmap.webp"),     C("label_heatmap.png"),   1),
    ("s08_import",   P("projects-import.webp"),   C("label_import.png"),   -1),
    ("s09_secure",   C("secure.png"),             None,                     1),
    ("s10_topology", P("topology.webp"),          C("label_topology.png"), -1),
    ("s11_groupadr", P("group-addresses.webp"),   C("label_groupadr.png"),  1),
    # Der Text des Mobile-Beats ist in den Hintergrund eingebrannt, also gibt es je
    # Sprache ein eigenes Composite (siehe REBUILD.md §3).
    ("s12_mobile",   V("clips_mobile_beat%s.mp4" % _SFX), None,            -1),
    ("s13_settings", P("settings.webp"),          C("label_settings.png"),  1),
    ("s14_platform", C("platform.png"),           None,                     1),
    ("s15_graph",    P("graph.webp"),             C("label_graph.png"),    -1),
    ("s16_close",    C("close.png"),              None,                     1),
]

def run(cmd):
    r = subprocess.run(cmd, capture_output=True, text=True)
    if r.returncode != 0:
        print("FFMPEG ERROR:\n", r.stderr[-2500:]); raise SystemExit(1)

def probe_dur(path):
    r = subprocess.run(["ffprobe", "-v", "error", "-show_entries", "format=duration",
                        "-of", "default=noprint_wrappers=1:nokey=1", path],
                       capture_output=True, text=True)
    try: return float(r.stdout.strip())
    except ValueError: return 0.0

def vo_path(i): return os.path.join(VO, "s%02d.mp3" % i)

OVR = {
    "s03_detail": dict(fx=0.82, fy=0.46, amt=0.12),
    "s08_import": dict(fx=0.50, fy=0.46, amt=0.11),
}

def kenburns(sid, zdir, tot):
    o = OVR.get(sid, {})
    fx, fy, amt = o.get("fx", 0.5), o.get("fy", 0.5), o.get("amt", ZOOM)
    z = f"1.0+{amt}*on/{tot}" if zdir > 0 else f"{1.0+amt}-{amt}*on/{tot}"
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
        # loop the clip so it keeps moving across a longer (VO-driven) beat instead of freezing
        base = f"scale=1920:1080:flags=lanczos,fps={FPS},setsar=1"
    elif is_card:
        base = kenburns(sid, zdir, tot)
    else:
        base = f"scale=1920:1080:flags=lanczos,setsar=1,fps={FPS}"   # screenshots static = zero shimmer
    loop = ["-stream_loop", "-1"] if video else ["-loop", "1"]
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
    print("build", sid, "%.2fs" % dur); run(cmd)
    return out, dur

def main():
    # VO-driven durations
    durs, vos = [], []
    for i, _ in enumerate(SEGMENTS):
        vp = vo_path(i)
        vd = probe_dur(vp) if os.path.exists(vp) else 0.0
        vos.append((vp, vd))
        durs.append(max(FLOOR, round(vd + LEAD + TRAIL, 2)) if vd > 0 else FLOOR)

    segs = [build_segment(sid, img, lbl, durs[i], zdir)
            for i, (sid, img, lbl, zdir) in enumerate(SEGMENTS)]

    inputs = []
    for out, _ in segs:
        inputs += ["-i", out]

    # video xfade chain
    fc, prev, cum = [], "0:v", segs[0][1]
    for i in range(1, len(segs)):
        off = cum - T
        lbl = f"x{i}"
        fc.append(f"[{prev}][{i}:v]xfade=transition=fade:duration={T}:offset={off:.3f}[{lbl}]")
        prev, cum = lbl, cum + segs[i][1] - T
    fc.append(f"[{prev}]fade=t=in:st=0:d=0.5,fade=t=out:st={cum-0.7:.3f}:d=0.7,format=yuv420p[out]")

    # real beat start times on the final timeline (each xfade overlaps by T)
    starts = [0.0]
    for i in range(1, len(segs)):
        starts.append(starts[i-1] + segs[i-1][1] - T)

    n = len(segs)
    music = V("track04.mp3")
    has_music = os.path.exists(music)
    has_vo = all(os.path.exists(vp) for vp, _ in vos)

    final = os.path.join(HERE, FINAL)
    cmd = ["ffmpeg", "-y"] + inputs
    # input indices: segments 0..n-1, then music (n) if present, then VO clips
    next_idx = n
    music_in = None
    if has_music:
        cmd += ["-stream_loop", "-1", "-i", music]; music_in = next_idx; next_idx += 1
    vo_in0 = next_idx
    if has_vo:
        for vp, _ in vos:
            cmd += ["-i", vp]; next_idx += 1

    if has_vo:
        for i in range(n):
            ms = int(max(0.0, starts[i] + LEAD) * 1000)
            fc.append(f"[{vo_in0+i}:a]adelay={ms}|{ms},aresample=48000[vo{i}]")
        fc.append("".join(f"[vo{i}]" for i in range(n)) + f"amix=inputs={n}:normalize=0[vomix]")
        fc.append("[vomix]asplit=2[vsc][vmix]")
        if has_music:
            fc.append(f"[{music_in}:a]aresample=48000,atempo={MUSTEMPO},volume={MUSVOL}[mus]")
            fc.append("[mus][vsc]sidechaincompress=threshold=0.04:ratio=14:attack=5:release=400[duck]")
            fc.append(f"[duck][vmix]amix=inputs=2:normalize=0,"
                      f"afade=t=in:st=0:d=1.0,afade=t=out:st={cum-1.5:.3f}:d=1.5,atrim=0:{cum:.3f}[aud]")
        else:
            fc.append(f"[vmix]afade=t=out:st={cum-1.0:.3f}:d=1.0,atrim=0:{cum:.3f}[aud]")
    elif has_music:
        fc.append(f"[{music_in}:a]volume=0.12,afade=t=in:st=0:d=2.0,"
                  f"afade=t=out:st={cum-2.5:.3f}:d=2.5,atrim=0:{cum:.3f},aresample=48000[aud]")

    cmd += ["-filter_complex", ";".join(fc), "-map", "[out]"]
    if has_vo or has_music:
        cmd += ["-map", "[aud]", "-c:a", "aac", "-b:a", "192k"]
    cmd += ["-r", str(FPS), "-c:v", "libx264", "-crf", CRF,
            "-preset", "medium", "-pix_fmt", "yuv420p", "-movflags", "+faststart", final]
    print("xfade chain, total ~%.1fs  vo=%s music=%s" % (cum, has_vo, has_music)); run(cmd)
    print("DONE ->", final)

if __name__ == "__main__":
    main()
