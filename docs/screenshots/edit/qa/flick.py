import os, glob
from PIL import Image
import numpy as np

def analyze(d):
    fs = sorted(glob.glob(os.path.join(d,"*.png")))
    L = [np.asarray(Image.open(f).convert("L"), dtype=np.int16) for f in fs]
    A = np.stack(L,0)                       # (T,H,W)
    deltas = np.diff(A,axis=0).astype(np.float32)   # (T-1,H,W)
    # shimmer = oscillation: sign of delta flips back & forth
    sg = np.sign(deltas)
    flips = np.sum(np.abs(np.diff(sg,axis=0))>0, axis=0)   # per-pixel sign-change count over sequence
    osc = flips>=3                          # oscillating >=3 times = flicker, not smooth motion
    # also restrict to pixels that actually change (ignore flat bg)
    moving = np.max(np.abs(deltas),axis=0) > 3
    osc_moving = osc & moving
    H,W = flips.shape
    tot = H*W
    return dict(
        mean_abs_delta=float(np.mean(np.abs(deltas))),
        osc_pct=100*float(np.sum(osc))/tot,
        osc_moving_pct=100*float(np.sum(osc_moving))/tot,
        moving_pct=100*float(np.sum(moving))/tot,
    )

for name in ["intro","ga","charts","import"]:
    r=analyze("qa/flick/"+name)
    print(f"{name:8s} meanDelta={r['mean_abs_delta']:.3f}  moving={r['moving_pct']:.1f}pct  osc={r['osc_pct']:.2f}pct  osc_moving={r['osc_moving_pct']:.2f}pct")
