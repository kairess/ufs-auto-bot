"""Tension-bar monitor.

The bar appears at the bottom of the screen only while reeling. It fills with
color whose hue indicates tension severity:
  - yellow / chartreuse  (H ~35-65)  : safe, keep reeling
  - orange / red         (H 0-25 or 170-180): danger, release the reel

We classify each ROI pixel into one of those buckets (plus "empty" for
unsaturated/dark pixels) and report the counts so the FSM can decide.
"""

from __future__ import annotations

from dataclasses import dataclass

import cv2
import numpy as np

from config import TENSION_BAR_ROI, TENSION_DANGER_FRACTION


@dataclass
class TensionReading:
    visible: bool         # is the bar showing at all?
    fill: float           # 0..1 fraction of ROI that is saturated bar pixels
    danger: float         # 0..1 fraction of ROI that is red/orange
    safe: float           # 0..1 fraction of ROI that is yellow/green
    is_danger: bool       # convenience: danger >= TENSION_DANGER_FRACTION


def read_tension(bgr: np.ndarray) -> TensionReading:
    x0, y0, x1, y1 = TENSION_BAR_ROI
    roi = bgr[y0:y1, x0:x1]
    if roi.size == 0:
        return TensionReading(False, 0.0, 0.0, 0.0, False)
    hsv = cv2.cvtColor(roi, cv2.COLOR_BGR2HSV)
    H, S, V = hsv[..., 0], hsv[..., 1], hsv[..., 2]

    # "Active" bar pixel = clearly saturated and bright. Background scene
    # behind a hidden bar tends to be dark and/or low-saturation.
    active = (S >= 120) & (V >= 130)

    # Hue buckets (OpenCV H is 0-180).
    safe_hue = ((H >= 30) & (H <= 75))
    danger_hue = ((H <= 25) | (H >= 170))

    safe_px = int((active & safe_hue).sum())
    danger_px = int((active & danger_hue).sum())
    total = roi.shape[0] * roi.shape[1]
    fill = (safe_px + danger_px) / total
    safe = safe_px / total
    danger = danger_px / total

    visible = fill >= 0.10  # at least 10% of ROI must be lit-up bar pixels
    is_danger = visible and danger >= TENSION_DANGER_FRACTION
    return TensionReading(visible=visible, fill=fill, danger=danger,
                          safe=safe, is_danger=is_danger)
