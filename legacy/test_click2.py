"""Round 2: variants designed for Unity / SDL games that ignore plain CGEvents.

Unity on macOS commonly reads `Input.mouseDelta` from raw IOHID. A vanilla
CGEvent has cursor=(x,y) but delta=(0,0), so Unity logs the click but its
mouse-position delta is zero, and depending on the game's input wrapper the
event is discarded.

Strategies tried here:
  A) cgevent_with_delta            — set kCGMouseEventDeltaX/Y on every event
  B) cgevent_noncoalesced          — set kCGEventFlagMaskNonCoalesced flag
  C) cgevent_warp_delta            — warp cursor + send event with delta
  D) cgpost_deprecated             — call deprecated CGPostMouseEvent (Carbon)
  E) double_click_warp             — warp, two quick down/up pairs (some games
                                     need a "real-feeling" click cadence)

Run with the same options as test_click.py:
    python test_click2.py --at 1280,720
"""

from __future__ import annotations

import argparse
import time

import Quartz


def _src_hid():
    return Quartz.CGEventSourceCreate(Quartz.kCGEventSourceStateHIDSystemState)


def _post(ev):
    Quartz.CGEventPost(Quartz.kCGHIDEventTap, ev)


def get_pos() -> tuple[float, float]:
    loc = Quartz.CGEventGetLocation(Quartz.CGEventCreate(None))
    return (loc.x, loc.y)


def _click_with_delta(x, y, dx, dy, noncoalesced=False):
    src = _src_hid()
    # MOVE first, with delta
    move = Quartz.CGEventCreateMouseEvent(src, Quartz.kCGEventMouseMoved,
                                          (x, y), Quartz.kCGMouseButtonLeft)
    Quartz.CGEventSetIntegerValueField(move, Quartz.kCGMouseEventDeltaX, dx)
    Quartz.CGEventSetIntegerValueField(move, Quartz.kCGMouseEventDeltaY, dy)
    if noncoalesced:
        Quartz.CGEventSetFlags(move, Quartz.kCGEventFlagMaskNonCoalesced)
    _post(move)
    time.sleep(0.03)

    down = Quartz.CGEventCreateMouseEvent(src, Quartz.kCGEventLeftMouseDown,
                                          (x, y), Quartz.kCGMouseButtonLeft)
    Quartz.CGEventSetIntegerValueField(down, Quartz.kCGMouseEventClickState, 1)
    Quartz.CGEventSetIntegerValueField(down, Quartz.kCGMouseEventDeltaX, dx)
    Quartz.CGEventSetIntegerValueField(down, Quartz.kCGMouseEventDeltaY, dy)
    if noncoalesced:
        Quartz.CGEventSetFlags(down, Quartz.kCGEventFlagMaskNonCoalesced)
    _post(down)
    time.sleep(0.06)

    up = Quartz.CGEventCreateMouseEvent(src, Quartz.kCGEventLeftMouseUp,
                                        (x, y), Quartz.kCGMouseButtonLeft)
    Quartz.CGEventSetIntegerValueField(up, Quartz.kCGMouseEventClickState, 1)
    if noncoalesced:
        Quartz.CGEventSetFlags(up, Quartz.kCGEventFlagMaskNonCoalesced)
    _post(up)


def cgevent_with_delta(x, y):
    cur = get_pos()
    _click_with_delta(x, y, int(x - cur[0]), int(y - cur[1]))


def cgevent_noncoalesced(x, y):
    cur = get_pos()
    _click_with_delta(x, y, int(x - cur[0]), int(y - cur[1]), noncoalesced=True)


def cgevent_warp_delta(x, y):
    cur = get_pos()
    Quartz.CGAssociateMouseAndMouseCursorPosition(False)
    Quartz.CGWarpMouseCursorPosition((x, y))
    Quartz.CGAssociateMouseAndMouseCursorPosition(True)
    time.sleep(0.05)
    _click_with_delta(x, y, int(x - cur[0]), int(y - cur[1]), noncoalesced=True)


def cgpost_deprecated(x, y):
    # The Carbon-era API. Still present in PyObjC. Some games respect it
    # because it more closely models the old hardware path.
    if not hasattr(Quartz, "CGPostMouseEvent"):
        raise RuntimeError("CGPostMouseEvent not exposed in this PyObjC build")
    Quartz.CGWarpMouseCursorPosition((x, y))
    time.sleep(0.04)
    Quartz.CGPostMouseEvent((x, y), True, 1, True)   # button down
    time.sleep(0.06)
    Quartz.CGPostMouseEvent((x, y), True, 1, False)  # button up


def double_click_warp(x, y):
    Quartz.CGAssociateMouseAndMouseCursorPosition(False)
    Quartz.CGWarpMouseCursorPosition((x, y))
    Quartz.CGAssociateMouseAndMouseCursorPosition(True)
    time.sleep(0.07)
    src = _src_hid()
    for click_state in (1, 2):
        d = Quartz.CGEventCreateMouseEvent(src, Quartz.kCGEventLeftMouseDown,
                                           (x, y), Quartz.kCGMouseButtonLeft)
        Quartz.CGEventSetIntegerValueField(d, Quartz.kCGMouseEventClickState, click_state)
        _post(d)
        time.sleep(0.04)
        u = Quartz.CGEventCreateMouseEvent(src, Quartz.kCGEventLeftMouseUp,
                                           (x, y), Quartz.kCGMouseButtonLeft)
        Quartz.CGEventSetIntegerValueField(u, Quartz.kCGMouseEventClickState, click_state)
        _post(u)
        time.sleep(0.04)


STRATEGIES = [
    ("cgevent_with_delta",     cgevent_with_delta),
    ("cgevent_noncoalesced",   cgevent_noncoalesced),
    ("cgevent_warp_delta",     cgevent_warp_delta),
    ("cgpost_deprecated",      cgpost_deprecated),
    ("double_click_warp",      double_click_warp),
]


def parse_xy(s: str):
    a, b = s.split(",")
    return float(a), float(b)


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--at", type=parse_xy, default=None,
                    help="Click target in screen-absolute pixels.")
    ap.add_argument("--countdown", type=int, default=4)
    ap.add_argument("--gap", type=float, default=3.0)
    args = ap.parse_args()

    if args.at is None:
        bounds = Quartz.CGDisplayBounds(Quartz.CGMainDisplayID())
        args.at = (int(bounds.size.width // 2), int(bounds.size.height // 2))
        print(f"no --at; defaulting to primary monitor center {args.at}")
    print(f"target = {args.at}")
    print("\nFocus UFS. Tests fire one strategy every "
          f"{args.gap}s — observe whether the click registers.\n")
    for i in range(args.countdown, 0, -1):
        print(f"  starting in {i}...", end="\r", flush=True)
        time.sleep(1)
    print(" " * 60, end="\r", flush=True)

    x, y = args.at
    for name, fn in STRATEGIES:
        before = get_pos()
        print(f"--- {name} ---  (cursor was at {int(before[0])},{int(before[1])})")
        try:
            fn(x, y)
            after = get_pos()
            print(f"    sent. cursor now at {int(after[0])},{int(after[1])}")
        except Exception as e:
            print(f"    SKIPPED: {e}")
        time.sleep(args.gap)

    print("\nTell me which one (if any) UFS accepted.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
