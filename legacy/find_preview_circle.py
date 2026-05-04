"""Locate the right-side circular bobber-zoom preview window in a frame.

The preview window is a fixed-position UI overlay drawn as a dark-bordered
circle on the right half of the screen. It does not move while fishing, so we
only need to find it once per session/resolution.

Usage:
    python find_preview_circle.py <image_or_video> [--time SEC]

Prints the detected (cx, cy, r) and writes a debug overlay to
frames/<basename>_preview_detected.png
"""

import argparse
import os
import sys

import cv2
import numpy as np


def detect_preview_circle(bgr: np.ndarray) -> tuple[int, int, int] | None:
    h, w = bgr.shape[:2]
    # Right ~third of the screen, vertical middle band.
    x0 = int(w * 0.62)
    y0 = int(h * 0.25)
    x1 = w
    y1 = int(h * 0.85)
    roi = bgr[y0:y1, x0:x1]
    gray = cv2.cvtColor(roi, cv2.COLOR_BGR2GRAY)
    gray = cv2.medianBlur(gray, 5)

    # Expected radius range scales with image height. At 1440p the circle is
    # roughly 150-220 px radius based on visual inspection.
    min_r = int(h * 0.07)
    max_r = int(h * 0.18)
    circles = cv2.HoughCircles(
        gray,
        cv2.HOUGH_GRADIENT,
        dp=1.2,
        minDist=h,
        param1=120,
        param2=40,
        minRadius=min_r,
        maxRadius=max_r,
    )
    if circles is None:
        return None
    c = circles[0, 0]
    cx, cy, r = int(c[0]) + x0, int(c[1]) + y0, int(c[2])
    return cx, cy, r


def load_frame(path: str, t_sec: float | None) -> np.ndarray:
    if path.lower().endswith((".mov", ".mp4", ".mkv", ".avi")):
        cap = cv2.VideoCapture(path)
        if t_sec is not None:
            cap.set(cv2.CAP_PROP_POS_MSEC, t_sec * 1000.0)
        ok, frame = cap.read()
        cap.release()
        if not ok:
            raise RuntimeError(f"Could not read frame from {path}")
        return frame
    img = cv2.imread(path)
    if img is None:
        raise RuntimeError(f"Could not read image {path}")
    return img


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("source")
    ap.add_argument("--time", type=float, default=18.0)
    ap.add_argument("--out", default=None)
    args = ap.parse_args()

    frame = load_frame(args.source, args.time)
    h, w = frame.shape[:2]
    print(f"frame: {w}x{h}")
    found = detect_preview_circle(frame)
    if found is None:
        print("No circle detected", file=sys.stderr)
        return 1
    cx, cy, r = found
    print(f"preview circle: center=({cx}, {cy}) radius={r}")
    print(f"as fraction:    cx={cx / w:.4f} cy={cy / h:.4f} r={r / h:.4f}")

    overlay = frame.copy()
    cv2.circle(overlay, (cx, cy), r, (0, 255, 0), 4)
    cv2.circle(overlay, (cx, cy), 4, (0, 0, 255), -1)

    out_path = args.out or os.path.join(
        "frames", f"{os.path.splitext(os.path.basename(args.source))[0]}_preview_detected.png"
    )
    os.makedirs(os.path.dirname(out_path) or ".", exist_ok=True)
    cv2.imwrite(out_path, overlay)
    print(f"wrote {out_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
