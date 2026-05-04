"""Find the game window's screen rect by clicking its corners.

Run, then within 5 seconds focus the game and:
  1. Hover the cursor over the TOP-LEFT pixel of the game's rendered area
     (just inside the title bar / window border) — the script reads it.
  2. Move to the BOTTOM-RIGHT pixel.
  3. The script prints the rect ready to paste into config.GAME_WINDOW_RECT
     or to pass via `bot.py --game-rect x,y,w,h`.

Each corner is captured 4 seconds after the previous step so you have time to
move the mouse without clicking (clicking would change the focused window).
"""

from __future__ import annotations

import time

from mouse_input import get_cursor_position


def wait_and_read(prompt: str, seconds: int = 4) -> tuple[int, int]:
    print(prompt)
    for i in range(seconds, 0, -1):
        x, y = get_cursor_position()
        print(f"  ...{i}s   cursor=({int(x)},{int(y)})", end="\r", flush=True)
        time.sleep(1)
    x, y = get_cursor_position()
    print(f"  captured: ({int(x)}, {int(y)})" + " " * 30)
    return int(x), int(y)


def main() -> int:
    print("Game-window calibration\n")
    print("Step 1: focus the game, then hover TOP-LEFT corner of the game's")
    print("        rendered area (just inside any window border).")
    time.sleep(2)
    x1, y1 = wait_and_read("→ reading TOP-LEFT in...")
    print()
    print("Step 2: now hover the BOTTOM-RIGHT corner.")
    x2, y2 = wait_and_read("→ reading BOTTOM-RIGHT in...")
    w, h = x2 - x1, y2 - y1
    print()
    print(f"GAME_WINDOW_RECT = ({x1}, {y1}, {w}, {h})")
    print()
    print("Paste the line above into config.py, OR run the bot like:")
    print(f"  .venv/bin/python bot.py --game-rect {x1},{y1},{w},{h}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
