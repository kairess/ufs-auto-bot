"""Lightweight screen capture using python-mss.

mss is a thin wrapper over the OS's native screenshot APIs (CGDisplay on macOS,
GDI on Windows, X11 on Linux). It returns raw BGRA byte buffers, no PIL/numpy
copy overhead. We convert to BGR once per frame.

Usage:
    cap = ScreenCapture(monitor=1)            # whole monitor
    cap = ScreenCapture(region=(x,y,w,h))     # specific rect
    frame = cap.grab()                        # bgr ndarray, full-res
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import Optional

import mss
import numpy as np


@dataclass
class ScreenCapture:
    monitor: int = 1                # mss numbering: 0=virtual all, 1=primary
    region: Optional[tuple[int, int, int, int]] = None  # (left, top, width, height)
    _sct: Optional[mss.base.MSSBase] = None

    def __post_init__(self) -> None:
        # Lazy: created on first grab so the bot can fork/thread freely.
        pass

    def _ensure(self) -> None:
        if self._sct is None:
            self._sct = mss.mss()

    def bbox(self) -> dict:
        self._ensure()
        if self.region is not None:
            x, y, w, h = self.region
            return {"left": x, "top": y, "width": w, "height": h}
        mon = self._sct.monitors[self.monitor]
        return {"left": mon["left"], "top": mon["top"],
                "width": mon["width"], "height": mon["height"]}

    def grab(self) -> np.ndarray:
        """Return a BGR ndarray of shape (H, W, 3)."""
        self._ensure()
        raw = self._sct.grab(self.bbox())
        # mss returns BGRA; drop the alpha to get BGR for OpenCV consumers.
        arr = np.frombuffer(raw.bgra, dtype=np.uint8).reshape(raw.height, raw.width, 4)
        return arr[:, :, :3]

    def close(self) -> None:
        if self._sct is not None:
            self._sct.close()
            self._sct = None
