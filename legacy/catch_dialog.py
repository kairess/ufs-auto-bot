"""Detect when the post-catch dialog is on screen.

The dialog has a bright green "판매하기 +N $" sell button at a fixed position.
When the dialog is closed, that pixel region is empty water/scene with 0%
green fill. When open, it shows ~89%. A simple HSV-mask + fill-fraction check
gives a clean binary signal.
"""

from __future__ import annotations

from dataclasses import dataclass

import cv2
import numpy as np

from config import CATCH_DIALOG_FILL_THRESHOLD, SELL_BUTTON_BOX, SELL_BUTTON_CENTER


@dataclass
class CatchDialogReading:
    visible: bool
    fill: float


def read_catch_dialog(bgr: np.ndarray) -> CatchDialogReading:
    x0, y0, x1, y1 = SELL_BUTTON_BOX
    roi = bgr[y0:y1, x0:x1]
    if roi.size == 0:
        return CatchDialogReading(False, 0.0)
    hsv = cv2.cvtColor(roi, cv2.COLOR_BGR2HSV)
    H, S, V = hsv[..., 0], hsv[..., 1], hsv[..., 2]
    mask = (H >= 40) & (H <= 68) & (S > 120) & (V > 100)
    fill = float(mask.mean())
    return CatchDialogReading(visible=fill >= CATCH_DIALOG_FILL_THRESHOLD, fill=fill)


def sell_click_target() -> tuple[int, int]:
    return SELL_BUTTON_CENTER
