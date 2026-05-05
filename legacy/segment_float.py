"""Segment the bobber (float) inside the right-side preview circle.

Pipeline:
  1. Detect (or accept) the preview circle ROI.
  2. Build a circular mask so only the in-window content is considered.
  3. In HSV, threshold for the orange tip (most distinctive color of the float)
     and combine with a saturated-pixel mask so the green/yellow body is also
     captured. The pale-blue water and dark-grey sky are excluded.
  4. Keep only the largest connected component touching the center column —
     that's the float. Report its centroid and bounding box.

Outputs a side-by-side debug image: original ROI | mask | overlay.
"""

from __future__ import annotations

import argparse
import os
import sys
from dataclasses import dataclass

import cv2
import numpy as np

from config import PREVIEW_CIRCLE, PREVIEW_BORDER_PAD


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


@dataclass
class FloatDetection:
    found: bool
    cx: int = 0           # centroid x in full-frame coords
    cy: int = 0           # centroid y in full-frame coords
    tip_y: int = 0        # topmost y of the float (the antenna tip)
    bbox: tuple[int, int, int, int] = (0, 0, 0, 0)  # x, y, w, h in full frame
    area: int = 0


def preview_visible(bgr: np.ndarray, preview: tuple[int, int, int]) -> bool:
    """Check whether the dark border ring of the preview UI is present.

    Samples pixels on a thin annulus at the configured radius. When the UI
    is shown the ring is uniformly very dark; when the UI is hidden those
    pixels show whatever scene is behind it (water, sky, grass) and have
    much higher mean brightness. A simple V threshold separates the two
    cases reliably.
    """
    cx, cy, r = preview
    H, W = bgr.shape[:2]
    angles = np.linspace(0, 2 * np.pi, 64, endpoint=False)
    xs = np.clip((cx + r * np.cos(angles)).astype(int), 0, W - 1)
    ys = np.clip((cy + r * np.sin(angles)).astype(int), 0, H - 1)
    samples = bgr[ys, xs]
    v = cv2.cvtColor(samples.reshape(1, -1, 3), cv2.COLOR_BGR2HSV)[0, :, 2]
    # Border ring is very dark (V < ~50) on >70% of samples when UI present.
    return float((v < 60).mean()) > 0.7


def circular_mask(shape: tuple[int, int], cx: int, cy: int, r: int, shrink: int = 6) -> np.ndarray:
    mask = np.zeros(shape, dtype=np.uint8)
    cv2.circle(mask, (cx, cy), max(1, r - shrink), 255, -1)
    return mask


def segment_float(
    bgr: np.ndarray,
    preview: tuple[int, int, int],
) -> tuple[FloatDetection, np.ndarray]:
    """Return detection + binary float mask (same size as full frame)."""
    H, W = bgr.shape[:2]
    cx_p, cy_p, r_p = preview

    # Tight bounding box around the circle for fast HSV work.
    x0 = max(0, cx_p - r_p)
    y0 = max(0, cy_p - r_p)
    x1 = min(W, cx_p + r_p)
    y1 = min(H, cy_p + r_p)
    roi = bgr[y0:y1, x0:x1]

    # Circular mask in ROI coords, shrunk to avoid the dark border ring.
    rh, rw = roi.shape[:2]
    roi_mask = np.zeros((rh, rw), dtype=np.uint8)
    cv2.circle(roi_mask, (cx_p - x0, cy_p - y0), r_p - PREVIEW_BORDER_PAD, 255, -1)

    hsv = cv2.cvtColor(roi, cv2.COLOR_BGR2HSV)

    # Orange tip: H 0-25. S threshold kept high so unsaturated water/skin
    # doesn't sneak in, but V floor is low because the same float can render
    # quite dark when shot from distance / under flat light.
    orange = cv2.inRange(hsv, (0, 110, 55), (25, 255, 255))
    # Yellow/green body (chartreuse): H 22-60.
    body = cv2.inRange(hsv, (22, 90, 60), (60, 255, 255))
    # Black antenna line: very low V. Keep narrow band only near center column,
    # otherwise it picks up the dark border.
    dark = cv2.inRange(hsv, (0, 0, 0), (180, 90, 70))

    color_mask = cv2.bitwise_or(orange, body)
    color_mask = cv2.bitwise_and(color_mask, roi_mask)

    # Clean up.
    kernel = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (3, 3))
    color_mask = cv2.morphologyEx(color_mask, cv2.MORPH_OPEN, kernel, iterations=1)
    color_mask = cv2.morphologyEx(color_mask, cv2.MORPH_CLOSE, kernel, iterations=2)

    # The float sits on the camera's center column. Score components by:
    #   - small horizontal distance from preview center
    #   - tall-and-narrow aspect (real bobbers are vertical sticks)
    #   - reasonable area
    # This rejects UI overlays in the corners and side-of-frame objects
    # (bait containers, distant cones) that share the float's color palette.
    num, labels, stats, _ = cv2.connectedComponentsWithStats(color_mask, connectivity=8)
    full_mask = np.zeros((H, W), dtype=np.uint8)
    if num <= 1:
        return FloatDetection(found=False), full_mask

    cx_center_roi = cx_p - x0
    best = -1
    best_score = -1.0
    for i in range(1, num):
        x, y, w_, h_, area = stats[i]
        if area < 12 or w_ > r_p:  # too small or absurdly wide
            continue
        comp_cx = x + w_ / 2
        dx = abs(comp_cx - cx_center_roi)
        if dx > r_p * 0.35:        # must be near center column
            continue
        if w_ > 40 and h_ < w_:    # squat blob -> not a stick
            continue
        # Prefer tall, near-center, sufficiently large.
        score = area * (1.0 - dx / r_p) * (1.0 + h_ / max(1, w_))
        if score > best_score:
            best_score = score
            best = i
    if best < 0:
        return FloatDetection(found=False), full_mask

    blob_mask = (labels == best).astype(np.uint8) * 255

    # Optionally extend upward by including dark pixels directly above the blob
    # (to capture the antenna line for a more accurate tip).
    blob_dark = cv2.bitwise_and(dark, roi_mask)
    bx, by, bw, bh, _ = stats[best]
    col_lo = max(0, bx + bw // 2 - 6)
    col_hi = min(rw, bx + bw // 2 + 6)
    antenna = np.zeros_like(blob_mask)
    antenna[:by + 2, col_lo:col_hi] = blob_dark[:by + 2, col_lo:col_hi]
    blob_mask = cv2.bitwise_or(blob_mask, antenna)

    ys, xs = np.where(blob_mask > 0)
    cx_roi = int(xs.mean())
    cy_roi = int(ys.mean())
    tip_y_roi = int(ys.min())
    bx2, by2, bw2, bh2 = cv2.boundingRect(blob_mask)

    full_mask[y0:y1, x0:x1] = blob_mask
    det = FloatDetection(
        found=True,
        cx=cx_roi + x0,
        cy=cy_roi + y0,
        tip_y=tip_y_roi + y0,
        bbox=(bx2 + x0, by2 + y0, bw2, bh2),
        area=int((blob_mask > 0).sum()),
    )
    return det, full_mask


def make_debug(bgr: np.ndarray, preview, det: FloatDetection, mask: np.ndarray) -> np.ndarray:
    cx_p, cy_p, r_p = preview
    pad = 10
    x0 = max(0, cx_p - r_p - pad)
    y0 = max(0, cy_p - r_p - pad)
    x1 = min(bgr.shape[1], cx_p + r_p + pad)
    y1 = min(bgr.shape[0], cy_p + r_p + pad)

    roi = bgr[y0:y1, x0:x1].copy()
    mask_roi = mask[y0:y1, x0:x1]
    mask_vis = cv2.cvtColor(mask_roi, cv2.COLOR_GRAY2BGR)

    overlay = roi.copy()
    overlay[mask_roi > 0] = (0.35 * overlay[mask_roi > 0] + 0.65 * np.array([0, 255, 0])).astype(np.uint8)
    cv2.circle(overlay, (cx_p - x0, cy_p - y0), r_p, (0, 200, 255), 2)
    if det.found:
        cv2.circle(overlay, (det.cx - x0, det.cy - y0), 5, (0, 0, 255), -1)
        cv2.line(overlay,
                 (det.cx - x0 - 15, det.tip_y - y0),
                 (det.cx - x0 + 15, det.tip_y - y0),
                 (255, 255, 0), 2)
        bx, by, bw, bh = det.bbox
        cv2.rectangle(overlay, (bx - x0, by - y0), (bx - x0 + bw, by - y0 + bh), (255, 0, 255), 2)

    return np.hstack([roi, mask_vis, overlay])


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("source")
    ap.add_argument("--time", type=float, default=18.0)
    ap.add_argument("--out", default=None)
    args = ap.parse_args()

    frame = load_frame(args.source, args.time)
    preview = PREVIEW_CIRCLE
    visible = preview_visible(frame, preview)
    print(f"preview circle (config): {preview}  visible={visible}")

    det, mask = segment_float(frame, preview)
    if det.found:
        print(f"float: centroid=({det.cx},{det.cy}) tip_y={det.tip_y} "
              f"bbox={det.bbox} area={det.area}")
    else:
        print("float not found")

    debug = make_debug(frame, preview, det, mask)
    out_path = args.out or os.path.join(
        "frames",
        f"{os.path.splitext(os.path.basename(args.source))[0]}_t{int(args.time)}_segment.png",
    )
    os.makedirs(os.path.dirname(out_path) or ".", exist_ok=True)
    cv2.imwrite(out_path, debug)
    print(f"wrote {out_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
