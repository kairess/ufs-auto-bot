"""Find out why synthetic input isn't reaching anything.

Run, then DON'T touch the mouse. The script tries to move the cursor to a
known target via several macOS APIs. Each step prints the cursor position
before and after, so we can tell EXACTLY what the OS is letting through.

If nothing moves the cursor visibly:
  - SecureInput is probably active. Quit any password manager / keychain
    prompt / login screen / VPN auth dialog and try again.
  - The terminal binary still doesn't have Accessibility AND Input Monitoring.
    System Settings -> Privacy & Security -> add it to BOTH lists, then
    fully quit (Cmd+Q) and relaunch the terminal.

If cursor moves for some steps but not others, tell me which ones.
"""

from __future__ import annotations

import subprocess
import time

import Quartz


def pos() -> tuple[float, float]:
    loc = Quartz.CGEventGetLocation(Quartz.CGEventCreate(None))
    return (loc.x, loc.y)


def step(name: str, target: tuple[float, float], action) -> None:
    print(f"\n[{name}] target=({int(target[0])},{int(target[1])})")
    before = pos()
    print(f"  before: ({int(before[0])},{int(before[1])})")
    try:
        action(target)
    except Exception as e:
        print(f"  ERROR: {e!r}")
        return
    time.sleep(0.3)
    after = pos()
    delta = (after[0] - before[0], after[1] - before[1])
    moved = abs(delta[0]) > 1 or abs(delta[1]) > 1
    print(f"  after:  ({int(after[0])},{int(after[1])})  delta=({int(delta[0])},{int(delta[1])})  "
          f"{'MOVED ✓' if moved else 'NOT MOVED ✗'}")


def secure_input_check() -> bool:
    """Best-effort check for SecureInput: ioreg shows it in the HIDSystem."""
    try:
        out = subprocess.run(
            ["ioreg", "-l", "-w", "0"], capture_output=True, text=True, timeout=3
        ).stdout
        # SecureInputProcess will be > 0 if any app has secure input active.
        for line in out.splitlines():
            if "SecureInput" in line or "kCGSSessionSecureInputPID" in line:
                print("  ioreg:", line.strip())
        return True
    except Exception as e:
        print(f"  ioreg probe failed: {e!r}")
        return False


def warp(t):
    Quartz.CGAssociateMouseAndMouseCursorPosition(False)
    Quartz.CGWarpMouseCursorPosition(t)
    Quartz.CGAssociateMouseAndMouseCursorPosition(True)


def event_hid(t):
    src = Quartz.CGEventSourceCreate(Quartz.kCGEventSourceStateHIDSystemState)
    ev = Quartz.CGEventCreateMouseEvent(src, Quartz.kCGEventMouseMoved,
                                        (float(t[0]), float(t[1])),
                                        Quartz.kCGMouseButtonLeft)
    Quartz.CGEventPost(Quartz.kCGHIDEventTap, ev)


def event_session(t):
    src = Quartz.CGEventSourceCreate(Quartz.kCGEventSourceStateCombinedSessionState)
    ev = Quartz.CGEventCreateMouseEvent(src, Quartz.kCGEventMouseMoved,
                                        (float(t[0]), float(t[1])),
                                        Quartz.kCGMouseButtonLeft)
    Quartz.CGEventPost(Quartz.kCGSessionEventTap, ev)


def event_no_source(t):
    ev = Quartz.CGEventCreateMouseEvent(None, Quartz.kCGEventMouseMoved,
                                        (float(t[0]), float(t[1])),
                                        Quartz.kCGMouseButtonLeft)
    Quartz.CGEventPost(Quartz.kCGHIDEventTap, ev)


def applescript_move(t):
    # System Events doesn't have a "move pointer" verb; we use mouse "click at"
    # with movement-only via ui scripting cli `cliclick m:x,y` if available.
    import shutil
    cli = shutil.which("cliclick")
    if cli:
        subprocess.run([cli, f"m:{int(t[0])},{int(t[1])}"], check=False)
    else:
        # Fall back: use python wrapper of NSCursor via osascript
        script = (
            'tell application "System Events" to '
            f'set thePos to {{{int(t[0])}, {int(t[1])}}}'
        )
        subprocess.run(["osascript", "-e", script], check=False)


def main() -> int:
    print("Synthetic input diagnostic\n")
    print("Detecting display & current cursor...")
    bounds = Quartz.CGDisplayBounds(Quartz.CGMainDisplayID())
    main_w, main_h = int(bounds.size.width), int(bounds.size.height)
    print(f"  primary display logical size: {main_w}x{main_h}")
    cur = pos()
    print(f"  cursor right now: ({int(cur[0])},{int(cur[1])})")
    if cur[0] >= main_w or cur[0] < 0:
        print("  -> cursor is on a SECONDARY monitor.")

    print("\nChecking SecureInput state (any line below means input is locked):")
    secure_input_check()

    target1 = (main_w * 0.25, main_h * 0.5)   # left-ish on primary
    target2 = (main_w * 0.75, main_h * 0.5)   # right-ish on primary

    print("\nWAIT — do not touch the mouse. Tests start in 3s.")
    time.sleep(3)

    step("CGWarp + Associate(false/true)",  target1, warp)
    step("CGEvent HID source -> HID tap",   target2, event_hid)
    step("CGEvent session src -> session",  target1, event_session)
    step("CGEvent no source -> HID tap",    target2, event_no_source)
    step("AppleScript / cliclick move",     target1, applescript_move)

    print("\nIf NONE of the steps moved the cursor:")
    print("  1) Check System Settings → Privacy & Security → Accessibility")
    print("     AND Input Monitoring. Add your terminal binary to BOTH.")
    print("     Then quit (Cmd+Q) the terminal completely and relaunch.")
    print("  2) Quit any app that may have grabbed SecureInput (1Password,")
    print("     Bitwarden, password fields, login screens).")
    print("  3) Try `caffeinate -d -s` in another terminal then retry — some")
    print("     screen-savers leave SecureInput stuck on.")
    print("\nIf SOME steps moved the cursor, tell me which ones; we'll wire")
    print("that method into the bot's mouse driver.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
