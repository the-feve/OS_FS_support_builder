# profile_check.py: Python port of the pure 2D profile math in src/SupportWall.fs
# (filterTrace, toothCenters, contactEdge, singleProfile, bridgeProfile). Keep the two in sync.
#
# Run:  python tools/profile_check.py   (needs shapely + matplotlib)
# Checks every profile is a valid, non-self-intersecting polygon and writes docs/profile_preview.png.
import math, random

D = dict(gap=1.0, overlap=0.1, neckHeight=1.0, neckWidth=1.0, attachMode="COUNT", toothCount=12,
         toothSpacing=8.0, footReach=20.0, topWidth=3.0, thickness=1.2)
NECK_EMBED = 0.5
TOL = 1e-6


def median(v):
    v = sorted(v)
    return v[len(v) // 2]


def interp(hs, ss, h):
    if h <= hs[0]:
        return ss[0]
    for j in range(1, len(hs)):
        if h <= hs[j]:
            f = (h - hs[j - 1]) / (hs[j] - hs[j - 1])
            return ss[j - 1] + (ss[j] - ss[j - 1]) * f
    return ss[-1]


def filter_trace(rawH, rawS, jump_tol, max_gap):
    keep = []
    n = len(rawH)
    for i in range(n):
        win = rawS[max(0, i - 3):min(n - 1, i + 3) + 1]
        if abs(rawS[i] - median(win)) <= jump_tol:
            keep.append((rawH[i], rawS[i]))
    out = []
    for i, (h, s) in enumerate(keep):
        if i > 0 and h - keep[i - 1][0] > max_gap:
            break
        out.append((h, s))
    return [h for h, _ in out], [s for _, s in out]


def trace(edge, h_limit, d, step=1.0):
    """Mimic traceSide: sample, filter; edgeAt falls back to interpolation on miss/jump."""
    rawH, rawS = [], []
    h = step / 2
    while h <= h_limit:
        s = edge(h)
        if s is not None:
            rawH.append(h)
            rawS.append(s)
        h += step
    jump = max(1.5, 2 * d["gap"])
    H, S = filter_trace(rawH, rawS, jump, 8.0)

    def edge_at(hq):
        s, guess = edge(hq), interp(H, S, hq)
        return guess if (s is None or abs(s - guess) > jump) else s
    return dict(h=H, s=S, tipAt=edge_at)


def tooth_centers(lo, hi, d):
    nh = d["neckHeight"]
    if hi < lo:
        return []
    mp = max(2 * nh, nh + 0.5)
    if d["attachMode"] == "COUNT":
        n = min(d["toothCount"], math.floor((hi - lo) / mp) + 1)
        p = (hi - lo) / (n - 1) if n > 1 else 0
    else:
        p = max(d["toothSpacing"], mp)
        n = math.floor((hi - lo) / p) + 1
    f = lo + ((hi - lo) - p * (n - 1)) / 2 if n > 1 else (lo + hi) / 2
    return [f + p * k for k in range(n)]


def contact_edge(tr, h_top, sign, d):
    g, ov, nh = d["gap"], d["overlap"], d["neckHeight"]
    pts = [(tr["s"][0] - g * sign, 0.0)]
    for h, s in zip(tr["h"], tr["s"]):
        if h > h_top:
            break
        pts.append((s - g * sign, h))
    top_s = interp(tr["h"], tr["s"], h_top) - g * sign
    if abs(pts[-1][1] - h_top) > TOL:
        pts.append((top_s, h_top))
    necks = []
    for hk in tooth_centers(tr["h"][0] + nh / 2, h_top - nh / 2, d):
        lo, hi = hk - nh / 2, hk + nh / 2
        necks.append([(interp(tr["h"], tr["s"], lo) - (g + NECK_EMBED) * sign, lo),
                      (tr["tipAt"](lo) + ov * sign, lo), (tr["tipAt"](hi) + ov * sign, hi),
                      (interp(tr["h"], tr["s"], hi) - (g + NECK_EMBED) * sign, hi)])
    return dict(points=pts, necks=necks, bottomS=tr["s"][0] - g * sign, topS=top_s)


def single_profile(tr, d):
    g, tW = d["gap"], d["topWidth"]
    h_top = tr["h"][-1]
    inner = contact_edge(tr, h_top, 1, d)
    min_inner = min(s - g for s in tr["s"])
    foot = min(min_inner - d["footReach"], min_inner - tW)
    outer_top = inner["topS"] - tW
    pts = inner["points"] + [(outer_top, h_top)]
    for j in range(len(tr["h"]) - 2, 0, -1):
        if j % 4:
            continue
        h = tr["h"][j]
        pts.append((min(foot + (outer_top - foot) * (h / h_top), tr["s"][j] - g - tW), h))
    pts.append((foot, 0.0))
    return dict(points=pts, necks=inner["necks"], teeth=len(inner["necks"]), height=h_top)


def bridge_profile(trA, trB, d):
    h_top = min(trA["h"][-1], trB["h"][-1])
    left = contact_edge(trA, h_top, -1, d)
    right = contact_edge(trB, h_top, 1, d)
    for h, s in zip(trA["h"], trA["s"]):
        if h > h_top:
            break
        if interp(trB["h"], trB["s"], h) - s - 2 * d["gap"] < d["thickness"] / 2:
            return dict(error="too close")
    necks = left["necks"] + right["necks"]
    return dict(points=left["points"] + right["points"][::-1], necks=necks, teeth=len(necks), height=h_top)


# ---------------------------------------------------------------- test edges
random.seed(2)


def perforated(base, top):
    """Edge with holes: rays miss in 2 mm bands every 7 mm, and sometimes slip through to a far wall."""
    def e(h):
        if h > top:
            return None
        band = h % 7.0
        if 3.0 < band < 5.0:
            return None if random.random() < 0.6 else base(h) + 9.0  # miss, or jump to far side
        return base(h)
    return e


CASES = {
    "leaning fin, perforated": ("single", perforated(lambda h: 40 - 0.35 * h - 0.002 * h * h, 110), None, 150),
    "S-bulge": ("single", lambda h: 10 + 8 * math.sin(h / 15) if h <= 90 else None, None, 150),
    "floating base": ("single", lambda h: (5 + 0.2 * h) if 12 < h < 100 else None, None, 150),
    "bridge two fins, perforated": ("bridge", perforated(lambda h: 2 + 6 * math.sin(h / 25), 70),
                                    perforated(lambda h: 22 - 4 * math.sin(h / 30) - 0.05 * h, 64), 150),
}


def run():
    from shapely.geometry import Polygon
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    fig, ax = plt.subplots(1, len(CASES), figsize=(4.5 * len(CASES), 5.5))
    ok = True
    for a, (name, (kind, eA, eB, lim)) in zip(ax, CASES.items()):
        trA = trace(eA, lim, D)
        if kind == "single":
            pr = single_profile(trA, D)
        else:
            # A side traced with rays along -d: same edge function, the sign lives in toothed_edge
            pr = bridge_profile(trA, trace(eB, lim, D), D)
        if "error" in pr:
            print(f"{name:30s} ERROR {pr['error']}")
            ok = False
            continue
        P = Polygon(pr["points"])
        necks = [Polygon(q) for q in pr["necks"]]
        fused = all(q.is_valid and q.intersection(P).area > 0 for q in necks)
        print(f"{name:30s} attach={pr['teeth']:3d} height={pr['height']:6.1f} wall valid={P.is_valid} "
              f"attach points valid+fused={fused}")
        ok &= P.is_valid and fused
        xs, ys = zip(*(pr["points"] + [pr["points"][0]]))
        a.fill(xs, ys, color="#4a7", alpha=.6)
        a.plot(xs, ys, "k", lw=.5)
        for q in pr["necks"]:
            qx, qy = zip(*(q + [q[0]]))
            a.fill(qx, qy, color="#d62", lw=0)
        for e in (eA, eB):
            if e:
                hh = [x / 4 for x in range(0, 600)]
                pts = [(e(h), h) for h in hh]
                a.plot([p[0] for p in pts if p[0] is not None and p[0] < 60],
                       [p[1] for p in pts if p[0] is not None and p[0] < 60], "r.", ms=1)
        a.axhline(0, color="gray")
        a.set_aspect("equal")
        a.set_title(f"{name}\n{pr['teeth']} attach pts", fontsize=9)
    plt.tight_layout()
    import os
    out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "docs", "profile_preview.png")
    plt.savefig(out, dpi=90)
    print("all valid" if ok else "SOME INVALID")


if __name__ == "__main__":
    run()
