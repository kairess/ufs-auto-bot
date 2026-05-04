"""Walk a fishing video and dump per-frame float-segmentation results.

Useful for validating that the segmenter tracks the bobber across all states
(idle, cast, wait, bite, sink, reel). Outputs CSV: time_s, found, tip_y, area.
"""

from __future__ import annotations

import argparse
import csv
import os
import sys

import cv2

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from config import PREVIEW_CIRCLE
from segment_float import preview_visible, segment_float


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("video")
    ap.add_argument("--every", type=int, default=10, help="Sample every N frames")
    ap.add_argument("--out", default="trace.csv")
    args = ap.parse_args()

    cap = cv2.VideoCapture(args.video)
    if not cap.isOpened():
        print(f"Cannot open {args.video}", file=sys.stderr)
        return 1
    fps = cap.get(cv2.CAP_PROP_FPS) or 60.0
    nframes = int(cap.get(cv2.CAP_PROP_FRAME_COUNT))

    preview = PREVIEW_CIRCLE

    with open(args.out, "w", newline="") as f:
        w = csv.writer(f)
        w.writerow(["frame", "time_s", "preview_visible", "float_found", "tip_y", "cx", "cy", "area"])

        i = 0
        while True:
            ok, frame = cap.read()
            if not ok:
                break
            if i % args.every == 0:
                vis = preview_visible(frame, preview)
                if vis:
                    det, _ = segment_float(frame, preview)
                else:
                    from segment_float import FloatDetection
                    det = FloatDetection(found=False)
                w.writerow([
                    i,
                    f"{i / fps:.3f}",
                    int(vis),
                    int(det.found),
                    det.tip_y,
                    det.cx,
                    det.cy,
                    det.area,
                ])
            i += 1
            if i >= nframes:
                break
    cap.release()
    print(f"wrote {args.out} ({i} frames scanned)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
