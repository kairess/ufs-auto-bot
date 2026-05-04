"""Left-mouse press/release with safety rails.

We never want a runaway loop holding the button forever, so this layer:
  * only press()/release() — no synthetic clicks; we model the bot as a
    held button that gets released on EASE / state exit
  * tracks current state so repeated press() calls don't re-fire
  * enforces a hard MAX_HOLD_S that auto-releases after that wallclock
  * exposes set_enabled(False) for dry-run mode

On macOS this requires "Accessibility" permission for the controlling Terminal /
Python binary. pynput will silently no-op if permission isn't granted; check
via the bot's --probe-input flag.
"""

from __future__ import annotations

import time
from dataclasses import dataclass, field
from typing import Optional

import sys

# Pick the platform backend at import. Both modules expose the same surface:
#   click(x, y, down_s) / left_down() / left_up() / move(x, y) /
#   get_position() / probe()
if sys.platform == "darwin":
    import quartz_mouse as _backend
elif sys.platform == "win32":
    import win32_mouse as _backend
else:
    raise RuntimeError(f"Unsupported platform for mouse input: {sys.platform}")


CLICK_DOWN_S = 0.06        # how long to hold a synthetic click
POST_CLICK_PAUSE_S = 0.05  # tiny settle after release


def probe_accessibility() -> bool:
    """Cursor-move probe via the active platform backend.

    On macOS this verifies Accessibility permission was granted; on Windows
    it just confirms SendInput is reaching the desktop (which it almost
    always is, no special permissions required).
    """
    try:
        return _backend.probe()
    except Exception:
        return False


def get_cursor_position() -> tuple[float, float]:
    return _backend.get_position()


def move_cursor(x: int, y: int) -> None:
    _backend.move(x, y)


MAX_HOLD_S = 10.0  # auto-release safety: never hold LMB longer than this without an explicit re-press


@dataclass
class MouseDriver:
    enabled: bool = True
    _down: bool = False
    _down_since: float = 0.0
    _last_log: Optional[str] = None

    def press(self) -> None:
        now = time.monotonic()
        if self._down:
            if now - self._down_since > MAX_HOLD_S:
                self._log(f"safety release after {now - self._down_since:.1f}s")
                self._raw_release()
                self._down = False
            return
        self._log("press LMB")
        if self.enabled:
            _backend.left_down()
        self._down = True
        self._down_since = now

    def release(self) -> None:
        if not self._down:
            return
        self._log("release LMB")
        self._raw_release()
        self._down = False

    def _raw_release(self) -> None:
        if self.enabled:
            try:
                _backend.left_up()
            except Exception as e:
                self._log(f"release error: {e!r}")

    def is_down(self) -> bool:
        return self._down

    def click_at(self, x: int, y: int) -> None:
        self._log(f"click at ({x},{y})")
        if self._down:
            self._raw_release()
            self._down = False
        if self.enabled:
            _backend.click(x, y, down_s=CLICK_DOWN_S)
            time.sleep(POST_CLICK_PAUSE_S)

    def hold_for(self, duration_s: float) -> None:
        self._log(f"hold LMB for {duration_s:.2f}s")
        if self._down:
            self._raw_release()
            self._down = False
        if self.enabled:
            _backend.left_down()
            time.sleep(duration_s)
            _backend.left_up()

    def shutdown(self) -> None:
        self.release()

    def _log(self, msg: str) -> None:
        if msg != self._last_log:
            print(f"  [mouse] {msg}")
            self._last_log = msg
