"""Windows mouse backend via SendInput (ctypes, no third-party deps).

SendInput is the modern Windows input-injection API. Events look identical to
real hardware input to anything reading via the standard input stack — which
includes Steam Remote Play's input layer, Unity's `Input.mousePosition`/
`Input.GetMouseButton`, and SDL2's mouse state. This is what AutoHotkey,
xdotool-on-WSL, and almost every game-bot library uses on Windows.

Coordinates are in physical screen pixels. Multi-monitor handling is built in:
we use MOUSEEVENTF_VIRTUALDESK so coords cover the whole virtual desktop.
"""

from __future__ import annotations

import ctypes
import time
from ctypes import wintypes


# --- Win32 constants & structs ---
INPUT_MOUSE = 0
MOUSEEVENTF_MOVE = 0x0001
MOUSEEVENTF_LEFTDOWN = 0x0002
MOUSEEVENTF_LEFTUP = 0x0004
MOUSEEVENTF_ABSOLUTE = 0x8000
MOUSEEVENTF_VIRTUALDESK = 0x4000

SM_XVIRTUALSCREEN = 76
SM_YVIRTUALSCREEN = 77
SM_CXVIRTUALSCREEN = 78
SM_CYVIRTUALSCREEN = 79


class _MOUSEINPUT(ctypes.Structure):
    _fields_ = (
        ("dx", wintypes.LONG),
        ("dy", wintypes.LONG),
        ("mouseData", wintypes.DWORD),
        ("dwFlags", wintypes.DWORD),
        ("time", wintypes.DWORD),
        ("dwExtraInfo", ctypes.c_void_p),
    )


class _INPUT_UNION(ctypes.Union):
    _fields_ = (("mi", _MOUSEINPUT),)


class _INPUT(ctypes.Structure):
    _anonymous_ = ("u",)
    _fields_ = (("type", wintypes.DWORD), ("u", _INPUT_UNION))


_user32 = ctypes.WinDLL("user32", use_last_error=True)
_user32.SendInput.argtypes = (wintypes.UINT, ctypes.POINTER(_INPUT), ctypes.c_int)
_user32.SendInput.restype = wintypes.UINT
_user32.GetSystemMetrics.argtypes = (ctypes.c_int,)
_user32.GetSystemMetrics.restype = ctypes.c_int


class _POINT(ctypes.Structure):
    _fields_ = (("x", wintypes.LONG), ("y", wintypes.LONG))


_user32.GetCursorPos.argtypes = (ctypes.POINTER(_POINT),)
_user32.GetCursorPos.restype = wintypes.BOOL

# SetCursorPos updates cursor position WITHOUT generating an input event.
# In FPS games that read mouse-look via raw delta (Unity Input.GetAxis,
# WM_INPUT, etc.), this lets us reposition the cursor without registering a
# fake camera rotation. SendInput MOUSEEVENTF_MOVE in contrast does generate
# the delta and would swing the camera.
_user32.SetCursorPos.argtypes = (ctypes.c_int, ctypes.c_int)
_user32.SetCursorPos.restype = wintypes.BOOL


def _virtual_desktop() -> tuple[int, int, int, int]:
    return (
        _user32.GetSystemMetrics(SM_XVIRTUALSCREEN),
        _user32.GetSystemMetrics(SM_YVIRTUALSCREEN),
        _user32.GetSystemMetrics(SM_CXVIRTUALSCREEN),
        _user32.GetSystemMetrics(SM_CYVIRTUALSCREEN),
    )


def _to_absolute(x: float, y: float) -> tuple[int, int]:
    """Convert pixel coords to MOUSEEVENTF_ABSOLUTE 0..65535 normalized space.

    With MOUSEEVENTF_VIRTUALDESK the normalization spans the entire virtual
    desktop (all monitors), so multi-monitor setups behave correctly.
    """
    vx, vy, vw, vh = _virtual_desktop()
    nx = int(((x - vx) / vw) * 65535)
    ny = int(((y - vy) / vh) * 65535)
    return nx, ny


def _send(flags: int, x: float | None = None, y: float | None = None) -> None:
    if x is not None and y is not None:
        nx, ny = _to_absolute(x, y)
        flags |= MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK
    else:
        nx = ny = 0
    inp = _INPUT(type=INPUT_MOUSE)
    inp.mi = _MOUSEINPUT(dx=nx, dy=ny, mouseData=0, dwFlags=flags,
                         time=0, dwExtraInfo=None)
    n = _user32.SendInput(1, ctypes.byref(inp), ctypes.sizeof(_INPUT))
    if n != 1:
        err = ctypes.get_last_error()
        raise OSError(f"SendInput failed (sent {n}, GetLastError={err})")


# --- Public API (matches quartz_mouse.py) ---

def get_position() -> tuple[float, float]:
    p = _POINT()
    _user32.GetCursorPos(ctypes.byref(p))
    return float(p.x), float(p.y)


def move(x: float, y: float) -> None:
    _send(MOUSEEVENTF_MOVE, x, y)


def warp(x: float, y: float) -> None:
    """Reposition cursor without generating any mouse input event."""
    _user32.SetCursorPos(int(x), int(y))


def left_down(x: float | None = None, y: float | None = None) -> None:
    _send(MOUSEEVENTF_LEFTDOWN, x, y)


def left_up(x: float | None = None, y: float | None = None) -> None:
    _send(MOUSEEVENTF_LEFTUP, x, y)


def click(x: float, y: float, down_s: float = 0.06) -> None:
    move(x, y)
    time.sleep(0.02)
    left_down(x, y)
    time.sleep(down_s)
    left_up(x, y)


def probe() -> bool:
    bx, by = get_position()
    move(bx + 1, by)
    time.sleep(0.05)
    ax, ay = get_position()
    move(bx, by)
    return (ax, ay) != (bx, by)
