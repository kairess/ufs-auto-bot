"""Memory-driven UFS auto-fishing bot.

Reads game state from the BepInEx plugin (`UfsFloatBridge.dll`) over UDP and
drives the fishing loop. The bot starts in PAUSED mode and only acts after
auto-mode is enabled.

Controls
--------
Delete  toggle auto-mode on/off (passive observation when off)
Ctrl+C  exit (terminal only)

Run:
    .venv\\Scripts\\python bot_memory.py
    .venv\\Scripts\\python bot_memory.py --dry           # no input, just log
    .venv\\Scripts\\python bot_memory.py --no-cast       # only fight, never cast
    .venv\\Scripts\\python bot_memory.py --release-all   # release fish instead of selling
"""

from __future__ import annotations

import argparse
import time

from pynput import keyboard

from config import (
    AUTOSTART_DELAY_S, CAST_HOLD_S, GAME_WINDOW_RECT, POST_ACTION_PAUSE_S,
    POST_CATCH_DELAY_S,
)
from float_memory import FloatMemoryReader, FloatState
from mouse_input import MouseDriver, get_cursor_position, probe_accessibility
from state_machine_memory import (
    FishingFSM, State, reel_action,
)
from status_monitor import StatusMonitor


CATCH_CLICK_DELAY_S = 2.0  # let the dialog finish animating in


class BotMode:
    """Mutable auto-on/auto-off flag toggled by the global Delete hotkey."""

    def __init__(self) -> None:
        self.auto = False

    def toggle(self) -> bool:
        self.auto = not self.auto
        return self.auto


def install_toggle_hotkey(mode: BotMode, drv: MouseDriver,
                          fsm: FishingFSM, allow_autocast: bool) -> keyboard.Listener:
    """Delete toggles auto-mode. Does NOT stop the bot — Ctrl+C does that."""
    def on_press(key):
        if key == keyboard.Key.delete:
            now_on = mode.toggle()
            if now_on:
                fsm.autostart_first_cast = allow_autocast
                print("\n[hotkey] Delete -> AUTO ON"
                      + ("" if allow_autocast else " (no-cast)"))
            else:
                fsm.autostart_first_cast = False
                # Safe parking: drop the mouse so the rod doesn't keep reeling.
                drv.release()
                print("\n[hotkey] Delete -> AUTO OFF (LMB released)")
    listener = keyboard.Listener(on_press=on_press)
    listener.daemon = True
    listener.start()
    return listener


def fmt_state(fs: FloatState) -> str:
    if not fs.ok:
        return "memory: link down"
    parts = []
    if fs.has_float:
        parts.append(f"water={int(fs.is_on_water)}")
        if fs.is_try_animation:
            parts.append("BITE")
        if fs.fish_tries_pull_timer > 0:
            parts.append("PULL")
        parts.append(f"timer={fs.fish_tries_timer:.2f}")
    else:
        parts.append("nofloat")
    if fs.has_line:
        parts.append(f"T={fs.current_tension:.2f}")
        if fs.tension_danger:
            parts.append("DANGER")
        if fs.is_reeling != 0:
            parts.append(f"reel{fs.is_reeling:+.0f}")
    if fs.has_fish:
        parts.append("FISH")
    if fs.has_junk:
        parts.append("JUNK")
    if fs.watch_fish:
        parts.append("WATCH")
    if fs.player_state and fs.player_state not in ("FISHING", "NORMAL"):
        parts.append(f"ps={fs.player_state}")
    return " ".join(parts)


def run(args: argparse.Namespace) -> int:
    if not args.dry:
        if not probe_accessibility():
            print("[WARN] input probe failed — clicks may not register.")

    reader = FloatMemoryReader()
    print("listening UDP 127.0.0.1:18500 (BepInEx plugin) ...")
    print("AUTO is OFF. Press Delete in the game to toggle. Ctrl+C to quit.")

    drv = MouseDriver(enabled=not args.dry)

    # Bot starts paused; FSM doesn't initiate casts until the user toggles on.
    fsm = FishingFSM(
        autostart_first_cast=False,
        autostart_delay_s=AUTOSTART_DELAY_S,
        post_catch_delay_s=POST_CATCH_DELAY_S,
    )
    fsm.on_transition = lambda old, new, t: print(
        f"  [{t:6.2f}s] {old.value:>13} -> {new.value}"
    )

    mode = BotMode()
    install_toggle_hotkey(mode, drv, fsm, allow_autocast=not args.no_cast)

    monitor = StatusMonitor() if args.tk_monitor else None
    if not args.no_overlay:
        reader.send_command("hud:show")
    else:
        reader.send_command("hud:hide")

    t_start = time.monotonic()
    period = 1.0 / args.fps if args.fps > 0 else 0.0
    sold_for_this_catch = False
    hook_fired_for_this = False
    cast_in_flight = False
    last_status_t = 0.0
    loops = 0
    fps_window_t0 = t_start
    fps_window_n = 0
    fps = 0.0

    try:
        while True:
            loop_t = time.monotonic()
            t = loop_t - t_start

            fs = reader.latest()
            if fs is None:
                if monitor is not None:
                    monitor.update(state="IDLE", auto_mode=mode.auto,
                                   action="idle", lmb_down=False)
                time.sleep(0.1)
                continue

            state = fsm.step(t, fs, fs.watch_fish)

            # One-shot edges (re-arm whenever we leave the state).
            if state != State.HOOK:
                hook_fired_for_this = False
            if state != State.CATCH_DIALOG:
                sold_for_this_catch = False

            action_name = "idle"  # for the status panel display

            if not mode.auto:
                # Passive observation — never drive any input.
                drv.release()
                action_name = "paused"
            elif state == State.AUTOCAST and not cast_in_flight:
                cast_in_flight = True
                cur = get_cursor_position()
                print(f"  [bot] AUTOCAST: hold LMB {CAST_HOLD_S}s "
                      f"@ ({int(cur[0])},{int(cur[1])})")
                try:
                    drv.hold_for(CAST_HOLD_S)
                except Exception as e:
                    print(f"  [bot] AUTOCAST hold_for raised: {e!r}")
                time.sleep(POST_ACTION_PAUSE_S)
                fsm.notify_cast_input_completed(time.monotonic() - t_start)
                cast_in_flight = False
            elif state == State.HOOK and not hook_fired_for_this:
                hook_fired_for_this = True
                print(f"  [bot] STRIKE  bite_age={t - fsm.bite_started:.2f}s "
                      f"timer={fs.fish_tries_timer:.2f} "
                      f"pull={fs.fish_tries_pull_timer:.2f} -> reel-in begins")
                fsm.notify_hook_fired(time.monotonic() - t_start)
                action_name = reel_action(state, fs)
                if action_name == "reel":
                    drv.press()
                else:
                    drv.release()
            elif state == State.CATCH_DIALOG and not sold_for_this_catch:
                drv.release()
                time.sleep(CATCH_CLICK_DELAY_S)
                cmd = "release" if args.release_all else "sell"
                print(f"  [bot] CATCH_DIALOG: invoke {cmd} via plugin")
                reader.send_command(cmd)
                time.sleep(POST_ACTION_PAUSE_S)
                sold_for_this_catch = True
                action_name = "idle"
            else:
                action_name = reel_action(state, fs)
                if action_name == "reel":
                    drv.press()
                else:
                    drv.release()

            # ---- counters / status panel / log ---------------------------
            loops += 1
            fps_window_n += 1
            now = time.monotonic()
            if now - fps_window_t0 >= 0.5:
                fps = fps_window_n / (now - fps_window_t0)
                fps_window_t0 = now
                fps_window_n = 0

            display_action = action_name if action_name in ("reel", "ease", "paused") else "idle"
            if monitor is not None:
                monitor.update(
                    state=state.value,
                    action=display_action if display_action != "paused" else "idle",
                    lmb_down=drv.is_down(),
                    auto_mode=mode.auto,
                    has_fish=fs.has_fish,
                    has_junk=fs.has_junk,
                    watch_fish=fs.watch_fish,
                    tension_value=fs.current_tension if fs.has_line else 0.0,
                    player_state=fs.player_state,
                    catch_visible=fs.watch_fish,
                    fps=fps,
                    loops=loops,
                )

            # Push status to the in-game overlay (visible via Steam Remote Play).
            # Format: S|<state>|<action>|<auto:0/1>|<fps>
            if not args.no_overlay:
                reader.send_command(
                    f"S|{state.value}|{display_action}|{1 if mode.auto else 0}|{fps:.1f}"
                )

            if t - last_status_t >= 1.0:
                last_status_t = t
                auto_str = "AUTO" if mode.auto else "PAUSE"
                print(f"  [{t:6.2f}s] {auto_str:5} {state.value:>13} | {fmt_state(fs)}"
                      + ("" if drv.enabled else "  DRY"))

            if period > 0:
                elapsed = time.monotonic() - loop_t
                if elapsed < period:
                    time.sleep(period - elapsed)
    except KeyboardInterrupt:
        print("\n[ctrl-c] stopping")
    finally:
        drv.shutdown()
        reader.close()
        if monitor is not None:
            monitor.shutdown()
    return 0


def parse_region(s: str) -> tuple[int, int, int, int]:
    parts = [int(p) for p in s.split(",")]
    if len(parts) != 4:
        raise argparse.ArgumentTypeError("--game-rect must be x,y,w,h")
    return tuple(parts)  # type: ignore[return-value]


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--dry", action="store_true", help="No input, just log.")
    ap.add_argument("--no-cast", action="store_true",
                    help="Don't auto-cast; assume player casts. Only fight + sell.")
    ap.add_argument("--release-all", action="store_true",
                    help="Release every fish instead of selling (XP grind mode).")
    ap.add_argument("--tk-monitor", action="store_true",
                    help="Show the legacy Tkinter floating panel (host-only; not visible via Remote Play).")
    ap.add_argument("--no-overlay", action="store_true",
                    help="Disable the in-game overlay drawn by the plugin.")
    ap.add_argument("--fps", type=float, default=60.0,
                    help="Loop rate. Memory feed is 60 Hz so matching it is optimal.")
    ap.add_argument("--monitor", type=int, default=1)
    ap.add_argument("--game-rect", type=parse_region, default=None)
    args = ap.parse_args()
    return run(args)


if __name__ == "__main__":
    raise SystemExit(main())
