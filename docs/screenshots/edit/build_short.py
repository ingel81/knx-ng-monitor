#!/usr/bin/env python3
"""Baut den YouTube-Short (1080x1920, ~45 s) aus den 9:16-Assets.

Gleiche Bauweise wie build.py, nur kuerzer getaktet: acht Beats, Dauer aus der
Laenge des jeweiligen Voice-Over-Clips, Musik unter der Stimme weggedueckt.

  python build_short.py            -> short.mp4      (Deutsch)
  VLANG=en python build_short.py   -> short_en.mp4   (Englisch)

Voraussetzungen: `python generate_short_assets.py`, `node tts_short.mjs`
und ein aufgenommenes `clips_mobile_live.mp4` (siehe rec_clips.mjs).
"""
import os, subprocess

HERE  = os.path.dirname(os.path.abspath(__file__))
LANG  = os.environ.get("VLANG", "de").lower()
_SFX  = "_en" if LANG == "en" else ""
ASSETS = os.path.join(HERE, "short" + _SFX)
SEG    = os.path.join(HERE, "segments_short" + _SFX)
VO     = os.path.join(HERE, "vo_short" + _SFX)
FINAL  = os.path.join(HERE, "short_en.mp4" if LANG == "en" else "short.mp4")
os.makedirs(SEG, exist_ok=True)

WIDTH, HEIGHT = 1080, 1920
FPS, T, CRF, ZOOM = 30, 0.35, "18", 0.035
SS = 2                      # Supersampling gegen Ken-Burns-Jitter
LEAD, TRAIL = 0.30, 0.65    # Short-Tempo: knapper als im Langvideo
FLOOR = 3.2
MUSVOL = 0.12
MUSTEMPO = 0.85

def A(n): return os.path.join(ASSETS, n)
def V(n): return os.path.join(HERE, n)

# (id, bild, kind) - kind: card = Ken Burns, still = statisch, live = Phone-Video im Rahmen
SEGMENTS = [
    ("s0_intro",    A("intro.png"),    "card"),
    ("s1_live",     A("live_bg.png"),  "live"),
    ("s2_archive",  A("archive.png"),  "still"),
    ("s3_charts",   A("charts.png"),   "still"),
    ("s4_stats",    A("stats.png"),    "still"),
    ("s5_groupadr", A("groupadr.png"), "still"),
    ("s6_desktop",  A("desktop.png"),  "still"),
    ("s7_close",    A("close.png"),    "card"),
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

def kenburns(zdir, tot):
    z = f"1.0+{ZOOM}*on/{tot}" if zdir > 0 else f"{1.0+ZOOM}-{ZOOM}*on/{tot}"
    sw, sh = WIDTH * SS, HEIGHT * SS
    return (f"scale={sw}:{sh}:flags=lanczos,zoompan=z='{z}':x='(iw-iw/zoom)*0.5':y='(ih-ih/zoom)*0.5':"
            f"d={tot}:s={sw}x{sh}:fps={FPS},scale={WIDTH}:{HEIGHT}:flags=lanczos")

def live_box():
    with open(A("live_box.txt")) as f:
        return [int(v) for v in f.read().split()]

def build_segment(sid, img, kind, dur, zdir):
    tot = int(round(dur * FPS))
    out = os.path.join(SEG, sid + ".mp4")
    if kind == "live":
        px, py, pw, ph = live_box()
        clip = V("clips_mobile_live.mp4")
        fc = (f"[1:v]scale={pw}:{ph}:flags=lanczos,fps={FPS},setsar=1[pv];"
              f"[2:v]format=gray,scale={pw}:{ph}[pm];"
              f"[pv][pm]alphamerge[pa];"
              f"[0:v]scale={WIDTH}:{HEIGHT}:flags=lanczos,fps={FPS},setsar=1[bg];"
              f"[bg][pa]overlay={px}:{py},format=yuv420p[v]")
        cmd = ["ffmpeg", "-y", "-loop", "1", "-i", img,
               "-stream_loop", "-1", "-i", clip,
               "-loop", "1", "-i", A("live_mask.png"),
               "-filter_complex", fc, "-map", "[v]"]
    else:
        base = kenburns(zdir, tot) if kind == "card" else \
               f"scale={WIDTH}:{HEIGHT}:flags=lanczos,setsar=1,fps={FPS}"
        cmd = ["ffmpeg", "-y", "-loop", "1", "-i", img,
               "-filter_complex", f"[0:v]{base},format=yuv420p[v]", "-map", "[v]"]
    cmd += ["-t", str(dur), "-r", str(FPS), "-c:v", "libx264", "-crf", CRF,
            "-preset", "medium", "-pix_fmt", "yuv420p", out]
    print("build", sid, "%.2fs" % dur); run(cmd)
    return out, dur

def main():
    durs, vos = [], []
    for i in range(len(SEGMENTS)):
        vp = os.path.join(VO, "s%d.mp3" % i)
        vd = probe_dur(vp) if os.path.exists(vp) else 0.0
        vos.append((vp, vd))
        durs.append(max(FLOOR, round(vd + LEAD + TRAIL, 2)) if vd > 0 else FLOOR)

    segs = [build_segment(sid, img, kind, durs[i], 1 if i % 2 == 0 else -1)
            for i, (sid, img, kind) in enumerate(SEGMENTS)]

    inputs = []
    for out, _ in segs:
        inputs += ["-i", out]

    fc, prev, cum = [], "0:v", segs[0][1]
    for i in range(1, len(segs)):
        off = cum - T
        lbl = f"x{i}"
        fc.append(f"[{prev}][{i}:v]xfade=transition=fade:duration={T}:offset={off:.3f}[{lbl}]")
        prev, cum = lbl, cum + segs[i][1] - T
    fc.append(f"[{prev}]fade=t=in:st=0:d=0.4,fade=t=out:st={cum-0.6:.3f}:d=0.6,format=yuv420p[out]")

    starts = [0.0]
    for i in range(1, len(segs)):
        starts.append(starts[i-1] + segs[i-1][1] - T)

    n = len(segs)
    music = V("track04.mp3")
    has_music = os.path.exists(music)
    has_vo = all(os.path.exists(vp) for vp, _ in vos)

    cmd = ["ffmpeg", "-y"] + inputs
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
                      f"afade=t=in:st=0:d=0.8,afade=t=out:st={cum-1.2:.3f}:d=1.2,atrim=0:{cum:.3f}[aud]")
        else:
            fc.append(f"[vmix]afade=t=out:st={cum-0.8:.3f}:d=0.8,atrim=0:{cum:.3f}[aud]")
    elif has_music:
        fc.append(f"[{music_in}:a]volume=0.14,afade=t=in:st=0:d=1.5,"
                  f"afade=t=out:st={cum-2.0:.3f}:d=2.0,atrim=0:{cum:.3f},aresample=48000[aud]")

    cmd += ["-filter_complex", ";".join(fc), "-map", "[out]"]
    if has_vo or has_music:
        cmd += ["-map", "[aud]", "-c:a", "aac", "-b:a", "192k"]
    cmd += ["-r", str(FPS), "-c:v", "libx264", "-crf", CRF,
            "-preset", "medium", "-pix_fmt", "yuv420p", "-movflags", "+faststart", FINAL]
    print("xfade chain, total ~%.1fs  vo=%s music=%s" % (cum, has_vo, has_music)); run(cmd)
    print("DONE ->", FINAL)

if __name__ == "__main__":
    main()
