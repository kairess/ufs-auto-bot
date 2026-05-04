"""UFS auto-fishing bot — capture → analyze → act loop.

Cross-platform: macOS (Quartz mouse backend) or Windows (SendInput backend).
On Steam Remote Play setups the bot MUST run on the Windows host; macOS
clients filter synthetic input and the game never sees it.

Usage:
    # First time: confirm input actually reaches the OS
    python bot.py --probe-input

    # Headless live mode (recommended). Runs without an overlay window so it
    # cannot steal focus from the game. 5-second countdown gives you time to
    # focus the game window after launching.
    python bot.py

    # Show overlay window (DEBUG only — window steals focus from the game)
    python bot.py --window

    # Dry run: no mouse, just log decisions
    python bot.py --dry

    # Override calibrated game window rect from CLI:
    python bot.py --game-rect 0,0,1920,1080

Stop the running bot at any time:
    - press Delete anywhere on screen (global hotkey, no focus needed)
    - or Ctrl+C in the terminal

Setup
-----
Windows (recommended for Steam Remote Play):
    1. Install Python 3.11+ from python.org (check "Add to PATH").
    2. Copy this folder to the Windows machine.
    3. In a terminal there:
           py -m venv .venv
           .venv\\Scripts\\python -m pip install -r requirements.txt
    4. Calibrate the game window:
           .venv\\Scripts\\python calibrate_window.py
       Paste the printed GAME_WINDOW_RECT into config.py (or pass --game-rect).
    5. Start the game, then run:
           .venv\\Scripts\\python bot.py

macOS (only useful when the game runs natively on the Mac):
    1. python3 -m venv .venv && .venv/bin/pip install -r requirements.txt
    2. System Settings -> Privacy & Security:
       grant Accessibility AND Screen Recording to your terminal, then
       fully quit (Cmd+Q) and relaunch it.
    3. python bot.py --probe-input  # confirms input injection works
    4. python bot.py
"""

from __future__ import annotations

import argparse
import base64
import sys
import threading
import time

import cv2
import numpy as np
from pynput import keyboard

from catch_dialog import read_catch_dialog, sell_click_target
from config import (
    AUTOSTART_DELAY_S, AUTOSTART_FIRST_CAST, CAST_HOLD_S, GAME_WINDOW_RECT,
    POST_ACTION_PAUSE_S, PREVIEW_CIRCLE,
)
from mouse_input import MouseDriver, get_cursor_position, probe_accessibility
from play_segmentation import annotate
from screen_capture import ScreenCapture
from segment_float import FloatDetection, preview_visible, segment_float
from state_machine import FishingFSM, Observation, State, reel_action
from status_monitor import StatusMonitor
from tension import read_tension


def build_segmentation_thumbnail(frame: np.ndarray, preview, det,
                                 mask, max_w: int = 224) -> str | None:
    """Crop the preview-circle ROI, overlay the float mask + tip marker, and
    return a base64-encoded PNG that the status monitor can display."""
    h_frame, w_frame = frame.shape[:2]
    cx, cy, r = preview
    pad = 12
    x0 = max(0, cx - r - pad)
    y0 = max(0, cy - r - pad)
    x1 = min(w_frame, cx + r + pad)
    y1 = min(h_frame, cy + r + pad)
    roi = frame[y0:y1, x0:x1].copy()
    if roi.size == 0:
        return None

    if mask is not None:
        mask_roi = mask[y0:y1, x0:x1]
        m = mask_roi > 0
        if m.any():
            roi[m] = (0.35 * roi[m] + 0.65 * np.array([0, 255, 0])).astype(np.uint8)

    # Outline the preview circle so we can see the ROI extent.
    cv2.circle(roi, (cx - x0, cy - y0), r, (0, 220, 255), 2)
    if det.found:
        cv2.circle(roi, (det.cx - x0, det.cy - y0), 4, (0, 0, 255), -1)
        cv2.line(roi,
                 (det.cx - x0 - 14, det.tip_y - y0),
                 (det.cx - x0 + 14, det.tip_y - y0),
                 (255, 255, 0), 2)
        bx, by, bw, bh = det.bbox
        cv2.rectangle(roi, (bx - x0, by - y0), (bx - x0 + bw, by - y0 + bh),
                      (255, 0, 255), 2)

    h, w = roi.shape[:2]
    if w > max_w:
        s = max_w / w
        roi = cv2.resize(roi, (max_w, int(h * s)), interpolation=cv2.INTER_AREA)

    ok, buf = cv2.imencode(".png", roi)
    if not ok:
        return None
    return base64.b64encode(buf.tobytes()).decode("ascii")


def parse_region(s: str) -> tuple[int, int, int, int]:
    parts = [int(p) for p in s.split(",")]
    if len(parts) != 4:
        raise argparse.ArgumentTypeError("--region must be x,y,w,h")
    return tuple(parts)  # type: ignore[return-value]


class StopSignal:
    """Thread-safe stop flag flipped by the global Delete hotkey."""

    def __init__(self) -> None:
        self._stop = False

    def set(self) -> None:
        self._stop = True

    def __bool__(self) -> bool:
        return self._stop


def install_stop_hotkey(stop: StopSignal) -> keyboard.Listener:
    def on_press(key):
        if key == keyboard.Key.delete:
            print("\n[hotkey] Delete -> stopping")
            stop.set()
            return False  # stop listener
    listener = keyboard.Listener(on_press=on_press)
    listener.daemon = True
    listener.start()
    return listener


def countdown(seconds: int) -> None:
    for i in range(seconds, 0, -1):
        print(f"  starting in {i}... (focus the game window now; press Delete to abort)", end="\r", flush=True)
        time.sleep(1)
    print(" " * 80, end="\r", flush=True)


def run(cap: ScreenCapture, dry: bool, probe: bool, show_window: bool,
        scale: float, target_fps: float, countdown_s: int,
        game_rect: tuple[int, int, int, int],
        show_monitor: bool = True) -> int:
    gx, gy, gw, gh = game_rect

    def to_screen(local_x: int, local_y: int) -> tuple[int, int]:
        return gx + int(local_x), gy + int(local_y)

    stop = StopSignal()
    install_stop_hotkey(stop)

    # Pre-flight diagnostics.
    print(f"capture bbox: {cap.bbox()}")
    if not dry:
        ok = probe_accessibility()
        if not ok:
            print("[WARN] Accessibility probe failed — pynput could not move the cursor.")
            print("       Grant 'Accessibility' to your terminal in System Settings,")
            print("       relaunch it, and rerun. Continuing anyway in case the probe")
            print("       was wrong; if clicks still don't land, that's the cause.")
        else:
            print("accessibility: OK (cursor move verified)")
    print(f"mouse: {'DRY (no input)' if dry else 'LIVE'}    overlay window: {show_window}")

    if countdown_s > 0:
        countdown(countdown_s)

    fsm = FishingFSM(
        autostart_first_cast=AUTOSTART_FIRST_CAST,
        autostart_delay_s=AUTOSTART_DELAY_S,
    )
    fsm.on_transition = lambda old, new, t: print(f"  [{t:6.2f}s] {old.value} -> {new.value}")
    monitor = StatusMonitor() if show_monitor else None
    drv = MouseDriver(enabled=not dry)
    period = 1.0 / target_fps if target_fps > 0 else 0.0
    win = "ufs-bot"
    if show_window:
        cv2.namedWindow(win, cv2.WINDOW_NORMAL)
    t_start = time.monotonic()
    loops = 0
    fps_window_t0 = t_start
    fps_window_n = 0
    fps = 0.0

    try:
        while not stop:
            loop_start = time.monotonic()
            frame = cap.grab()
            t = time.monotonic() - t_start
            vis = preview_visible(frame, PREVIEW_CIRCLE)
            if vis:
                det, mask = segment_float(frame, PREVIEW_CIRCLE)
            else:
                det, mask = FloatDetection(found=False), None
            tension = read_tension(frame)
            catch = read_catch_dialog(frame)

            if probe:
                state = State.IDLE
            else:
                state = fsm.step(Observation(
                    t=t,
                    preview_visible=vis,
                    float_found=det.found,
                    tip_y=det.tip_y,
                    catch_dialog_visible=catch.visible,
                ))

                if state == State.CATCH_DIALOG:
                    drv.release()
                    lx, ly = sell_click_target()
                    drv.click_at(*to_screen(lx, ly))
                    time.sleep(POST_ACTION_PAUSE_S)
                elif state == State.AUTOCAST:
                    # DO NOT move the cursor before holding LMB. UFS is a
                    # first-person game: in fishing mode the cursor is
                    # captured and any synthetic warp registers as a huge
                    # camera-rotation delta, swinging the rod skyward and
                    # casting into the air. Just hold LMB at the current
                    # captured position — the camera was already facing the
                    # spot the player wants to fish.
                    drv.hold_for(CAST_HOLD_S)
                    time.sleep(POST_ACTION_PAUSE_S)
                    # Critical: leave AUTOCAST immediately so subsequent loop
                    # iterations don't re-press LMB while the cast is still in
                    # flight (which would yank the rod / partially reel in).
                    fsm.notify_cast_input_completed(time.monotonic() - t_start)
                else:
                    action = reel_action(state, tension.is_danger)
                    if action == "reel":
                        drv.press()
                    else:
                        drv.release()

            if show_window:
                shown = annotate(frame, PREVIEW_CIRCLE, vis, det, mask,
                                 state=state, tension=tension, catch=catch)
                if scale != 1.0:
                    shown = cv2.resize(shown, None, fx=scale, fy=scale,
                                       interpolation=cv2.INTER_AREA)
                if not drv.enabled:
                    cv2.putText(shown, "DRY", (10, shown.shape[0] - 12),
                                cv2.FONT_HERSHEY_SIMPLEX, 0.8, (0, 200, 255), 2)
                cv2.imshow(win, shown)
                k = cv2.waitKey(1) & 0xFF
                if k == ord("q") or k == 27:
                    break

            loops += 1
            fps_window_n += 1
            now = time.monotonic()
            if now - fps_window_t0 >= 0.5:
                fps = fps_window_n / (now - fps_window_t0)
                fps_window_t0 = now
                fps_window_n = 0
            if monitor is not None:
                action_str = "idle"
                if state in (State.SUNK, State.REELING):
                    action_str = "ease" if tension.is_danger else "reel"
                # Throttle thumbnail generation to ~10 Hz to keep CPU low.
                thumb = None
                if loops % max(1, int((target_fps or 30) // 10)) == 0:
                    thumb = build_segmentation_thumbnail(
                        frame, PREVIEW_CIRCLE, det, mask
                    )
                monitor.update(
                    state=state.value if not probe else "PROBE",
                    action=action_str,
                    lmb_down=drv.is_down(),
                    tension_visible=tension.visible,
                    tension_fill=tension.fill,
                    tension_danger=tension.danger,
                    catch_visible=catch.visible,
                    fps=fps,
                    loops=loops,
                    **({"thumbnail_b64": thumb} if thumb is not None else {}),
                )

            if period > 0:
                elapsed = time.monotonic() - loop_start
                if elapsed < period:
                    time.sleep(period - elapsed)
    except KeyboardInterrupt:
        print("\n[ctrl-c] stopping")
    finally:
        drv.shutdown()
        cap.close()
        if monitor is not None:
            monitor.shutdown()
        if show_window:
            cv2.destroyAllWindows()
    return 0


def cmd_probe_input() -> int:
    print("Probing accessibility / input injection...")
    ok = probe_accessibility()
    print(f"  cursor-move probe: {'OK' if ok else 'FAILED'}")
    if not ok:
        print("  -> pynput cannot inject input. Grant Accessibility to your")
        print("     terminal in System Settings -> Privacy & Security, then")
        print("     RELAUNCH the terminal and rerun this command.")
        return 1
    print("Now testing a synthetic LMB click in 3 seconds — focus a")
    print("safe target window (e.g. an empty TextEdit document) NOW.")
    for i in range(3, 0, -1):
        print(f"  click in {i}...", end="\r", flush=True)
        time.sleep(1)
    drv = MouseDriver(enabled=True)
    px, py = get_cursor_position()
    drv.click_at(int(px), int(py))
    drv.shutdown()
    print("Sent click. Did the target window receive it? If yes, you're set.")
    return 0


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--dry", action="store_true",
                    help="Don't actually press the mouse — log only.")
    ap.add_argument("--probe", action="store_true",
                    help="Show what the bot sees but bypass FSM and input.")
    ap.add_argument("--probe-input", action="store_true",
                    help="One-shot test of OS input injection. Exits after.")
    ap.add_argument("--window", action="store_true",
                    help="Show the overlay window (steals focus — debug only).")
    ap.add_argument("--region", type=parse_region, default=None,
                    help="Capture region as x,y,w,h. Defaults to primary monitor.")
    ap.add_argument("--monitor", type=int, default=1)
    ap.add_argument("--scale", type=float, default=0.5)
    ap.add_argument("--fps", type=float, default=30.0,
                    help="Target loop rate. Set to 0 to run as fast as possible.")
    ap.add_argument("--countdown", type=int, default=5,
                    help="Seconds to wait before starting (gives time to focus the game).")
    ap.add_argument("--game-rect", type=parse_region, default=None,
                    help="Override config.GAME_WINDOW_RECT: x,y,w,h of the game's rendered area.")
    ap.add_argument("--no-monitor", action="store_true",
                    help="Disable the always-on-top floating status panel.")
    args = ap.parse_args()
    if args.probe_input:
        return cmd_probe_input()
    rect = args.game_rect or GAME_WINDOW_RECT
    # Capture exactly the game window so all hardcoded UI coords are valid
    # in the captured frame as well as for click translation.
    cap = ScreenCapture(monitor=args.monitor, region=rect)
    return run(cap, dry=args.dry, probe=args.probe, show_window=args.window,
               scale=args.scale, target_fps=args.fps,
               countdown_s=args.countdown if not args.dry else 0,
               game_rect=rect, show_monitor=not args.no_monitor)


if __name__ == "__main__":
    raise SystemExit(main())
