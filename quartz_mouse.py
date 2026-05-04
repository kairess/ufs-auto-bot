"""macOS-native mouse driver via Quartz Core Graphics events.

Why this exists
---------------
pynput's Mac backend posts events with the *combined-session* event source.
That works for ordinary apps (text editors, browsers) but is silently dropped
or filtered by some games — notably Unity titles like UFS — because the game
runs its own NSEvent loop and rejects events that don't look "real" enough.

We instead create our events from `kCGEventSourceStateHIDSystemState`, which
is the lowest user-space level, and post them with `kCGHIDEventTap`. This
mimics actual hardware closely enough that Unity's input system accepts them.

We also explicitly set `kCGMouseEventClickState = 1` on down/up events, which
some Unity input wrappers require to count an event as a real click.
"""

from __future__ import annotations

import time

import Quartz


def _source():
    return Quartz.CGEventSourceCreate(Quartz.kCGEventSourceStateHIDSystemState)


def _post(ev) -> None:
    Quartz.CGEventPost(Quartz.kCGHIDEventTap, ev)


def _make(event_type, x: float, y: float, click_state: int = 0):
    src = _source()
    ev = Quartz.CGEventCreateMouseEvent(
        src, event_type, (float(x), float(y)), Quartz.kCGMouseButtonLeft
    )
    if click_state:
        Quartz.CGEventSetIntegerValueField(
            ev, Quartz.kCGMouseEventClickState, click_state
        )
    return ev


def get_position() -> tuple[float, float]:
    loc = Quartz.CGEventGetLocation(Quartz.CGEventCreate(None))
    return (loc.x, loc.y)


def move(x: float, y: float) -> None:
    _post(_make(Quartz.kCGEventMouseMoved, x, y))


def left_down(x: float | None = None, y: float | None = None) -> None:
    if x is None or y is None:
        x, y = get_position()
    _post(_make(Quartz.kCGEventLeftMouseDown, x, y, click_state=1))


def left_up(x: float | None = None, y: float | None = None) -> None:
    if x is None or y is None:
        x, y = get_position()
    _post(_make(Quartz.kCGEventLeftMouseUp, x, y, click_state=1))


def click(x: float, y: float, down_s: float = 0.06) -> None:
    move(x, y)
    time.sleep(0.02)
    left_down(x, y)
    time.sleep(down_s)
    left_up(x, y)


def probe() -> bool:
    """Move cursor 1px and verify the OS reports the new position."""
    bx, by = get_position()
    move(bx + 1, by)
    time.sleep(0.05)
    ax, ay = get_position()
    move(bx, by)
    return (ax, ay) != (bx, by)
