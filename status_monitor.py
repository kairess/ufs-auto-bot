"""Always-on-top floating status panel.

Tkinter widget so we have no extra deps. Runs in its own thread so the bot's
capture/decision loop is never blocked by GUI repaints. Main thread pushes
updates via `update(...)`; the GUI thread polls the latest snapshot ~10x/s.

Position defaults to LEFT-CENTER of the primary display. The window is
borderless and always-on-top so it overlays the game without stealing focus.
On Windows it's also marked "tool window" via ctypes so it doesn't show in
the alt-tab list.
"""

from __future__ import annotations

import sys
import threading
import time
import tkinter as tk
from dataclasses import dataclass
from typing import Optional


# State -> hex color (matches the existing overlay palette).
STATE_COLORS = {
    "IDLE":         "#a0a0a0",
    "AUTOCAST":     "#ffc800",
    "CASTING":      "#00c8ff",
    "WAITING":      "#00dc00",
    "SUNK":         "#ff5a00",
    "REELING":      "#ff0000",
    "CATCH_DIALOG": "#c800c8",
}

ACTION_COLORS = {
    "idle":  "#888888",
    "reel":  "#ff3030",
    "ease":  "#ffd200",
}


@dataclass
class _Snapshot:
    state: str = "IDLE"
    action: str = "idle"
    lmb_down: bool = False
    tension_visible: bool = False
    tension_fill: float = 0.0
    tension_danger: float = 0.0
    catch_visible: bool = False
    fps: float = 0.0
    loops: int = 0


class StatusMonitor:
    def __init__(self, width: int = 240, height: int = 200,
                 position: str = "left-center", refresh_ms: int = 100) -> None:
        self._snap = _Snapshot()
        self._lock = threading.Lock()
        self._stop = threading.Event()
        self._width = width
        self._height = height
        self._position = position
        self._refresh_ms = refresh_ms
        self._ready = threading.Event()
        self._thread = threading.Thread(target=self._run, daemon=True,
                                        name="status-monitor")
        self._thread.start()
        # Wait briefly so the window is visible before the bot starts logging.
        self._ready.wait(timeout=1.5)

    def update(self, **fields) -> None:
        with self._lock:
            for k, v in fields.items():
                if hasattr(self._snap, k):
                    setattr(self._snap, k, v)

    def shutdown(self) -> None:
        self._stop.set()

    # ----- GUI thread -----
    def _place(self, root: tk.Tk) -> None:
        sw = root.winfo_screenwidth()
        sh = root.winfo_screenheight()
        if self._position == "left-center":
            x, y = 16, max(0, (sh - self._height) // 2)
        elif self._position == "right-center":
            x, y = sw - self._width - 16, max(0, (sh - self._height) // 2)
        elif self._position == "top-left":
            x, y = 16, 16
        else:
            x, y = 16, max(0, (sh - self._height) // 2)
        root.geometry(f"{self._width}x{self._height}+{x}+{y}")

    def _force_topmost_windows(self, root: tk.Tk) -> None:
        """Pin to top-of-z and exclude from alt-tab on Windows via ctypes."""
        if sys.platform != "win32":
            return
        try:
            import ctypes
            hwnd = ctypes.windll.user32.GetParent(root.winfo_id())
            GWL_EXSTYLE = -20
            WS_EX_TOOLWINDOW = 0x00000080
            WS_EX_TOPMOST = 0x00000008
            ex = ctypes.windll.user32.GetWindowLongW(hwnd, GWL_EXSTYLE)
            ctypes.windll.user32.SetWindowLongW(
                hwnd, GWL_EXSTYLE, ex | WS_EX_TOOLWINDOW | WS_EX_TOPMOST
            )
            HWND_TOPMOST = -1
            SWP_NOMOVE = 0x0002
            SWP_NOSIZE = 0x0001
            SWP_SHOWWINDOW = 0x0040
            ctypes.windll.user32.SetWindowPos(
                hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW,
            )
        except Exception:
            pass  # not fatal — tk's -topmost flag still applies

    def _run(self) -> None:
        root = tk.Tk()
        root.title("ufs-bot")
        root.configure(bg="#0a0a0a")
        root.overrideredirect(True)        # borderless
        root.attributes("-topmost", True)
        try:
            root.attributes("-alpha", 0.92)  # slight transparency
        except tk.TclError:
            pass
        self._place(root)
        self._force_topmost_windows(root)

        # Layout
        font_lg = ("Helvetica", 16, "bold")
        font_md = ("Helvetica", 11, "bold")
        font_sm = ("Helvetica", 10)

        state_lbl = tk.Label(root, text="IDLE", font=font_lg,
                             fg="#a0a0a0", bg="#0a0a0a", pady=6)
        state_lbl.pack(fill="x")

        action_lbl = tk.Label(root, text="LMB ⏸", font=font_md,
                              fg="#888888", bg="#0a0a0a")
        action_lbl.pack(fill="x")

        tension_lbl = tk.Label(root, text="tension —", font=font_sm,
                               fg="#666666", bg="#0a0a0a")
        tension_lbl.pack(fill="x", pady=(4, 0))

        bar_canvas = tk.Canvas(root, width=self._width - 32, height=10,
                               bg="#1a1a1a", highlightthickness=0)
        bar_canvas.pack(pady=2)

        catch_lbl = tk.Label(root, text="", font=font_sm,
                             fg="#666666", bg="#0a0a0a")
        catch_lbl.pack(fill="x")

        meta_lbl = tk.Label(root, text="0.0 fps", font=font_sm,
                            fg="#444444", bg="#0a0a0a", pady=4)
        meta_lbl.pack(fill="x", side="bottom")

        # Drag-to-move support so the user can reposition without quitting.
        drag = {"x": 0, "y": 0}

        def on_press(e):
            drag["x"], drag["y"] = e.x_root - root.winfo_x(), e.y_root - root.winfo_y()

        def on_drag(e):
            root.geometry(f"+{e.x_root - drag['x']}+{e.y_root - drag['y']}")

        for w in (state_lbl, action_lbl, tension_lbl, catch_lbl, meta_lbl):
            w.bind("<ButtonPress-1>", on_press)
            w.bind("<B1-Motion>", on_drag)

        self._ready.set()

        def tick():
            if self._stop.is_set():
                root.destroy()
                return
            with self._lock:
                s = self._snap
                snap = _Snapshot(**vars(s))

            color = STATE_COLORS.get(snap.state, "#a0a0a0")
            state_lbl.configure(text=snap.state, fg=color)

            action_text = {"idle": "LMB —",
                           "reel": "● REEL (LMB DOWN)",
                           "ease": "▲ EASE (LMB UP)"}.get(snap.action, snap.action)
            action_lbl.configure(text=action_text,
                                 fg=ACTION_COLORS.get(snap.action, "#888888"))

            if snap.tension_visible:
                tension_lbl.configure(
                    text=f"tension fill {snap.tension_fill:.0%}  danger {snap.tension_danger:.0%}",
                    fg="#ffd200" if snap.tension_danger < 0.2 else "#ff3030",
                )
                bar_canvas.delete("all")
                w = self._width - 32
                fill_w = int(w * snap.tension_fill)
                danger_w = int(w * snap.tension_danger)
                bar_canvas.create_rectangle(0, 0, fill_w, 10,
                                            fill="#3a8a3a", outline="")
                bar_canvas.create_rectangle(0, 0, danger_w, 10,
                                            fill="#cc2020", outline="")
            else:
                tension_lbl.configure(text="tension —", fg="#444444")
                bar_canvas.delete("all")

            catch_lbl.configure(
                text="● catch dialog open" if snap.catch_visible else "",
                fg="#c800c8" if snap.catch_visible else "#444444",
            )

            meta_lbl.configure(text=f"{snap.fps:.1f} fps   loops {snap.loops}")

            root.after(self._refresh_ms, tick)

        root.after(self._refresh_ms, tick)
        try:
            root.mainloop()
        except Exception:
            pass
