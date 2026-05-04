"""Click-strategy bench. Tries every input-injection method we have one at a
time and asks you to confirm which one the game accepts.

Usage:
    python test_click.py            # interactive walk-through, clicks at cursor
    python test_click.py --at 1100,1303

Strategies tested (in order):
    1) quartz_hid_click_state    - Quartz CGEvent, HID source, click_state=1  (current default)
    2) quartz_hid_no_state       - Quartz CGEvent, HID source, no click_state
    3) quartz_session            - Quartz CGEvent, session-level event tap
    4) quartz_warp_then_hid      - CGWarp the cursor, then HID click
    5) applescript               - osascript "tell System Events to click at"
    6) cliclick                  - external `cliclick` binary if installed (brew install cliclick)

Open the game window, run the script, and bring the game to the front during
the 4-second countdown. The script will perform one click per strategy with a
3-second pause between each so you can observe whether the game registered it.
"""

from __future__ import annotations

import argparse
import shutil
import subprocess
import time

import Quartz
import mss


def detect_scale() -> tuple[float, tuple[int, int], tuple[int, int]]:
    """Return (scale, logical_size, physical_size).

    Quartz / cursor coords use logical pixels (points). mss screenshots use
    physical pixels. On a Retina display the ratio is typically 2.0 — clicking
    at "physical" coords like (2400, 700) when logical is 1280x720 sends the
    cursor far off-screen.
    """
    bounds = Quartz.CGDisplayBounds(Quartz.CGMainDisplayID())
    logical = (int(bounds.size.width), int(bounds.size.height))
    with mss.mss() as sct:
        m = sct.monitors[1]
        physical = (m["width"], m["height"])
    scale = physical[0] / logical[0] if logical[0] else 1.0
    return scale, logical, physical


def _src_hid():
    return Quartz.CGEventSourceCreate(Quartz.kCGEventSourceStateHIDSystemState)


def _src_combined():
    return Quartz.CGEventSourceCreate(Quartz.kCGEventSourceStateCombinedSessionState)


def _post(tap, ev):
    Quartz.CGEventPost(tap, ev)


def get_pos() -> tuple[float, float]:
    loc = Quartz.CGEventGetLocation(Quartz.CGEventCreate(None))
    return (loc.x, loc.y)


# --- Strategy 1: Quartz HID + click_state=1 (current bot default) ---
def quartz_hid_click_state(x: float, y: float) -> None:
    src = _src_hid()
    move = Quartz.CGEventCreateMouseEvent(src, Quartz.kCGEventMouseMoved, (x, y), Quartz.kCGMouseButtonLeft)
    _post(Quartz.kCGHIDEventTap, move)
    time.sleep(0.02)
    down = Quartz.CGEventCreateMouseEvent(src, Quartz.kCGEventLeftMouseDown, (x, y), Quartz.kCGMouseButtonLeft)
    Quartz.CGEventSetIntegerValueField(down, Quartz.kCGMouseEventClickState, 1)
    _post(Quartz.kCGHIDEventTap, down)
    time.sleep(0.06)
    up = Quartz.CGEventCreateMouseEvent(src, Quartz.kCGEventLeftMouseUp, (x, y), Quartz.kCGMouseButtonLeft)
    Quartz.CGEventSetIntegerValueField(up, Quartz.kCGMouseEventClickState, 1)
    _post(Quartz.kCGHIDEventTap, up)


# --- Strategy 2: Quartz HID, no click_state ---
def quartz_hid_no_state(x: float, y: float) -> None:
    src = _src_hid()
    for et in (Quartz.kCGEventMouseMoved, Quartz.kCGEventLeftMouseDown):
        ev = Quartz.CGEventCreateMouseEvent(src, et, (x, y), Quartz.kCGMouseButtonLeft)
        _post(Quartz.kCGHIDEventTap, ev)
        time.sleep(0.03)
    up = Quartz.CGEventCreateMouseEvent(src, Quartz.kCGEventLeftMouseUp, (x, y), Quartz.kCGMouseButtonLeft)
    _post(Quartz.kCGHIDEventTap, up)


# --- Strategy 3: Quartz combined session source + session tap ---
def quartz_session(x: float, y: float) -> None:
    src = _src_combined()
    for et in (Quartz.kCGEventMouseMoved, Quartz.kCGEventLeftMouseDown):
        ev = Quartz.CGEventCreateMouseEvent(src, et, (x, y), Quartz.kCGMouseButtonLeft)
        if et == Quartz.kCGEventLeftMouseDown:
            Quartz.CGEventSetIntegerValueField(ev, Quartz.kCGMouseEventClickState, 1)
        _post(Quartz.kCGSessionEventTap, ev)
        time.sleep(0.04)
    up = Quartz.CGEventCreateMouseEvent(src, Quartz.kCGEventLeftMouseUp, (x, y), Quartz.kCGMouseButtonLeft)
    Quartz.CGEventSetIntegerValueField(up, Quartz.kCGMouseEventClickState, 1)
    _post(Quartz.kCGSessionEventTap, up)


# --- Strategy 4: Physically warp cursor, THEN HID click ---
def quartz_warp_then_hid(x: float, y: float) -> None:
    Quartz.CGWarpMouseCursorPosition((x, y))
    Quartz.CGAssociateMouseAndMouseCursorPosition(True)
    time.sleep(0.04)
    quartz_hid_click_state(x, y)


# --- Strategy 5: AppleScript via osascript ---
def applescript(x: float, y: float) -> None:
    script = f'tell application "System Events" to click at {{{int(x)}, {int(y)}}}'
    subprocess.run(["osascript", "-e", script], check=False)


# --- Strategy 6: cliclick binary (if user has installed it via brew) ---
def cliclick(x: float, y: float) -> None:
    bin_path = shutil.which("cliclick")
    if not bin_path:
        raise RuntimeError("cliclick not installed; `brew install cliclick`")
    subprocess.run([bin_path, f"c:{int(x)},{int(y)}"], check=False)


STRATEGIES = [
    ("quartz_hid_click_state",  quartz_hid_click_state),
    ("quartz_hid_no_state",     quartz_hid_no_state),
    ("quartz_session",          quartz_session),
    ("quartz_warp_then_hid",    quartz_warp_then_hid),
    ("applescript",             applescript),
    ("cliclick",                cliclick),
]


def parse_xy(s: str) -> tuple[float, float]:
    a, b = s.split(",")
    return float(a), float(b)


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--at", type=parse_xy, default=None,
                    help="Click at this x,y in PHYSICAL pixels (same coord space as mss / config). "
                         "Default = current cursor position (already logical).")
    ap.add_argument("--countdown", type=int, default=4)
    ap.add_argument("--gap", type=float, default=3.0,
                    help="Seconds between strategies so you can observe each one.")
    ap.add_argument("--no-scale", action="store_true",
                    help="Skip auto-conversion of physical -> logical coords.")
    args = ap.parse_args()

    scale, logical, physical = detect_scale()
    print(f"display: logical(point) = {logical[0]}x{logical[1]}   "
          f"physical(px) = {physical[0]}x{physical[1]}   scale = {scale:.2f}x")
    if scale != 1.0:
        print("  -> Retina/scaled display detected. Click coords are in points,")
        print("     so we will divide --at by scale before clicking.")

    # Multi-display diagnostic. mss[0] is the union of all monitors.
    with mss.mss() as sct:
        if len(sct.monitors) > 2:
            print("\nmonitors detected (mss numbering):")
            for i, m in enumerate(sct.monitors):
                tag = "  (virtual all)" if i == 0 else ("  (primary)" if i == 1 else "")
                print(f"  [{i}] left={m['left']:>5}  top={m['top']:>5}  "
                      f"w={m['width']:>5}  h={m['height']:>5}{tag}")
            print("  -> with multiple monitors, cursor coords are GLOBAL across all of them.")
            print("     If your cursor is at x>{0} the game on the primary monitor will NOT".format(logical[0]))
            print("     receive a click. Move the cursor onto the game window during countdown.")
        cur = get_pos()
        if cur[0] < 0 or cur[0] >= logical[0] or cur[1] < 0 or cur[1] >= logical[1]:
            print(f"\n[WARN] cursor is OUTSIDE the primary display ({int(cur[0])},{int(cur[1])}).")
            print("       During countdown, move the mouse onto the game window.")

    if args.at is None:
        # Default to the center of the PRIMARY monitor (the game display).
        # Using cursor pos is dangerous in multi-monitor setups because the
        # cursor is usually on the terminal monitor when you launch the test.
        cx, cy = logical[0] // 2, logical[1] // 2
        args.at = (cx, cy)
        target_logical = args.at
        print(f"no --at given; defaulting to PRIMARY monitor center "
              f"(logical {cx},{cy}). Pass --at X,Y to click somewhere specific.")
    else:
        # --at is in physical pixels (matches our config / segmenter).
        # Convert to logical for the click APIs.
        if args.no_scale or scale == 1.0:
            target_logical = args.at
        else:
            target_logical = (args.at[0] / scale, args.at[1] / scale)
        print(f"--at given as physical ({int(args.at[0])},{int(args.at[1])}) "
              f"-> logical ({int(target_logical[0])},{int(target_logical[1])})")

    print("\nBring the GAME WINDOW to the front so the click target")
    print("is over a UI element you can verify (e.g. a menu button).\n")
    for i in range(args.countdown, 0, -1):
        print(f"  starting in {i}...", end="\r", flush=True)
        time.sleep(1)
    print(" " * 60, end="\r", flush=True)

    x, y = target_logical
    for name, fn in STRATEGIES:
        before = get_pos()
        print(f"--- {name} ---  (clicking at logical {int(x)},{int(y)}; cursor was at {int(before[0])},{int(before[1])})")
        try:
            fn(x, y)
            after = get_pos()
            print(f"    sent. cursor now at {int(after[0])},{int(after[1])} (delta {int(after[0]-before[0])},{int(after[1]-before[1])})")
        except Exception as e:
            print(f"    SKIPPED: {e}")
        time.sleep(args.gap)

    print("\nDone. Tell me which one(s) the game registered.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
