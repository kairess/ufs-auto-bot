"""Play a fishing video with the float-segmentation overlay drawn live.

Two output modes:
  - default: opens an OpenCV window (q to quit, space to pause, ←/→ to seek 1s)
  - --render PATH: writes an annotated mp4 to PATH (no window, headless-safe)

Overlay shows:
  - the configured preview circle (yellow when UI visible, grey when hidden)
  - the float mask tinted green
  - tip-y crosshair (cyan) and centroid (red dot)
  - status banner: VISIBLE/HIDDEN, FLOAT-FOUND/LOST, tip_y
"""

from __future__ import annotations

import argparse
import sys

import cv2
import numpy as np

from catch_dialog import CatchDialogReading, read_catch_dialog
from config import PREVIEW_CIRCLE, SELL_BUTTON_BOX, TENSION_BAR_ROI
from segment_float import FloatDetection, preview_visible, segment_float
from state_machine import FishingFSM, Observation, State, reel_action, reel_should_be_held
from tension import TensionReading, read_tension


STATE_COLORS = {
    State.IDLE:         (160, 160, 160),
    State.AUTOCAST:     (255, 200, 0),
    State.CASTING:      (0, 200, 255),
    State.WAITING:      (0, 220, 0),
    State.SUNK:         (0, 90, 255),
    State.REELING:      (0, 0, 255),
    State.CATCH_DIALOG: (200, 0, 200),
}


def annotate(frame: np.ndarray, preview, vis: bool, det, mask,
             state: State | None = None,
             tension: TensionReading | None = None,
             catch: CatchDialogReading | None = None) -> np.ndarray:
    out = frame.copy()
    cx_p, cy_p, r_p = preview
    ring_color = (0, 220, 255) if vis else (120, 120, 120)
    cv2.circle(out, (cx_p, cy_p), r_p, ring_color, 2)

    if vis and mask is not None:
        m = mask > 0
        out[m] = (0.4 * out[m] + 0.6 * np.array([0, 255, 0])).astype(np.uint8)

    if det.found:
        cv2.circle(out, (det.cx, det.cy), 6, (0, 0, 255), -1)
        cv2.line(out,
                 (det.cx - 22, det.tip_y),
                 (det.cx + 22, det.tip_y),
                 (255, 255, 0), 2)
        bx, by, bw, bh = det.bbox
        cv2.rectangle(out, (bx, by), (bx + bw, by + bh), (255, 0, 255), 2)

    H, W = out.shape[:2]
    banner = np.zeros((90, W, 3), dtype=np.uint8)
    pv = "PREVIEW: VISIBLE" if vis else "PREVIEW: HIDDEN"
    cv2.putText(banner, pv, (16, 34), cv2.FONT_HERSHEY_SIMPLEX, 0.9,
                (0, 220, 255) if vis else (120, 120, 120), 2)
    if vis:
        ftxt = f"FLOAT: tip_y={det.tip_y} cy={det.cy} area={det.area}" if det.found else "FLOAT: LOST (sunk?)"
        fc = (0, 255, 0) if det.found else (0, 90, 255)
        cv2.putText(banner, ftxt, (430, 34), cv2.FONT_HERSHEY_SIMPLEX, 0.9, fc, 2)

    if state is not None:
        sc = STATE_COLORS[state]
        cv2.putText(banner, f"STATE: {state.value}", (16, 76),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.95, sc, 2)
        action = reel_action(state, tension.is_danger if tension else False)
        if action == "reel":
            cv2.putText(banner, "[REEL: LMB DOWN]", (430, 76),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.95, (0, 0, 255), 2)
            cv2.rectangle(out, (0, 0), (W - 1, H - 1), (0, 0, 255), 8)
        elif action == "ease":
            cv2.putText(banner, "[EASE: LMB UP - tension!]", (430, 76),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.95, (0, 200, 255), 2)
            cv2.rectangle(out, (0, 0), (W - 1, H - 1), (0, 200, 255), 8)

    if catch is not None and catch.visible:
        x0, y0, x1, y1 = SELL_BUTTON_BOX
        cv2.rectangle(out, (x0 - 4, y0 - 4), (x1 + 4, y1 + 4), (200, 0, 200), 4)
        cv2.putText(banner, f"CATCH UI fill={catch.fill:.0%}", (1180, 76),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.85, (200, 0, 200), 2)

    if tension is not None:
        x0, y0, x1, y1 = TENSION_BAR_ROI
        ring_color = ((0, 0, 255) if tension.is_danger
                      else (0, 220, 0) if tension.visible
                      else (120, 120, 120))
        cv2.rectangle(out, (x0 - 4, y0 - 4), (x1 + 4, y1 + 4), ring_color, 3)
        if tension.visible:
            ttxt = f"TENSION fill={tension.fill:.0%} danger={tension.danger:.0%}"
            cv2.putText(banner, ttxt, (900, 34), cv2.FONT_HERSHEY_SIMPLEX, 0.85,
                        ring_color, 2)

    return np.vstack([banner, out])


def run_window(video: str, scale: float) -> int:
    cap = cv2.VideoCapture(video)
    if not cap.isOpened():
        print(f"Cannot open {video}", file=sys.stderr)
        return 1
    fps = cap.get(cv2.CAP_PROP_FPS) or 60.0
    delay = max(1, int(1000.0 / fps))
    paused = False
    win = "ufs-segmentation"
    cv2.namedWindow(win, cv2.WINDOW_NORMAL)
    fsm = FishingFSM()
    fsm.on_transition = lambda old, new, t: print(f"  [{t:6.2f}s] {old.value} -> {new.value}")

    while True:
        if not paused:
            ok, frame = cap.read()
            if not ok:
                break
            t = cap.get(cv2.CAP_PROP_POS_MSEC) / 1000.0
            vis = preview_visible(frame, PREVIEW_CIRCLE)
            if vis:
                det, mask = segment_float(frame, PREVIEW_CIRCLE)
            else:
                det, mask = FloatDetection(found=False), None
            tension = read_tension(frame)
            catch = read_catch_dialog(frame)
            state = fsm.step(Observation(
                t=t, preview_visible=vis, float_found=det.found,
                tip_y=det.tip_y, catch_dialog_visible=catch.visible,
            ))
            shown = annotate(frame, PREVIEW_CIRCLE, vis, det, mask,
                             state=state, tension=tension, catch=catch)
            if scale != 1.0:
                shown = cv2.resize(shown, None, fx=scale, fy=scale, interpolation=cv2.INTER_AREA)
            cv2.imshow(win, shown)

        k = cv2.waitKey(delay if not paused else 30) & 0xFF
        if k == ord("q") or k == 27:
            break
        if k == ord(" "):
            paused = not paused
        if k == 81 or k == ord("a"):  # left
            t = cap.get(cv2.CAP_PROP_POS_MSEC)
            cap.set(cv2.CAP_PROP_POS_MSEC, max(0, t - 1000))
        if k == 83 or k == ord("d"):  # right
            t = cap.get(cv2.CAP_PROP_POS_MSEC)
            cap.set(cv2.CAP_PROP_POS_MSEC, t + 1000)

    cap.release()
    cv2.destroyAllWindows()
    return 0


def run_render(video: str, out_path: str, scale: float) -> int:
    cap = cv2.VideoCapture(video)
    if not cap.isOpened():
        print(f"Cannot open {video}", file=sys.stderr)
        return 1
    fps = cap.get(cv2.CAP_PROP_FPS) or 60.0
    nframes = int(cap.get(cv2.CAP_PROP_FRAME_COUNT))
    fsm = FishingFSM()
    fsm.on_transition = lambda old, new, t: print(f"  [{t:6.2f}s] {old.value} -> {new.value}")

    writer = None
    i = 0
    while True:
        ok, frame = cap.read()
        if not ok:
            break
        t = cap.get(cv2.CAP_PROP_POS_MSEC) / 1000.0
        vis = preview_visible(frame, PREVIEW_CIRCLE)
        if vis:
            det, mask = segment_float(frame, PREVIEW_CIRCLE)
        else:
            det, mask = FloatDetection(found=False), None
        tension = read_tension(frame)
        catch = read_catch_dialog(frame)
        state = fsm.step(Observation(
            t=t, preview_visible=vis, float_found=det.found,
            tip_y=det.tip_y, catch_dialog_visible=catch.visible,
        ))
        shown = annotate(frame, PREVIEW_CIRCLE, vis, det, mask,
                         state=state, tension=tension, catch=catch)
        if scale != 1.0:
            shown = cv2.resize(shown, None, fx=scale, fy=scale, interpolation=cv2.INTER_AREA)
        if writer is None:
            h, w = shown.shape[:2]
            writer = cv2.VideoWriter(out_path, cv2.VideoWriter_fourcc(*"mp4v"), fps, (w, h))
        writer.write(shown)
        i += 1
        if i % 60 == 0:
            print(f"  {i}/{nframes}", end="\r", flush=True)
    if writer is not None:
        writer.release()
    cap.release()
    print(f"\nwrote {out_path} ({i} frames)")
    return 0


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("video")
    ap.add_argument("--render", default=None,
                    help="Output mp4 path. If set, writes annotated video instead of showing a window.")
    ap.add_argument("--scale", type=float, default=0.5,
                    help="Display/output scale (1.0 = native 2560x1440 + banner).")
    args = ap.parse_args()
    if args.render:
        return run_render(args.video, args.render, args.scale)
    return run_window(args.video, args.scale)


if __name__ == "__main__":
    raise SystemExit(main())
