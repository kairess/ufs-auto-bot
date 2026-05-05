"""Read FishingFloat state directly from the running game via the
BepInEx plugin (UfsFloatBridge.dll), instead of CV-segmenting the preview.

Wire format: the plugin sends one UTF-8 JSON object per UDP packet to
127.0.0.1:18500 at ~60 Hz. We keep the most recent packet in memory and
expose it as a dataclass; callers poll latest() each frame.
"""

from __future__ import annotations

import json
import socket
import threading
from dataclasses import dataclass
from typing import Optional


@dataclass
class FloatState:
    ok: bool = False
    has_float: bool = False
    t: float = 0.0
    bait_was_thrown: bool = False
    is_on_water: bool = False
    can_hit_water: bool = False
    is_loose: bool = True
    is_try_animation: bool = False        # binary "fish is biting" signal
    fish_tries_timer: float = 0.0          # 0..4s while a bite is active
    fish_tries_pull_timer: float = 0.0     # >0 only during the pull pulse
    burden_factor: float = 0.0             # -1 (too light) .. +1 (too heavy)
    floating_eased_offset: float = 0.0
    vel_y: float = 0.0
    vel_mag: float = 0.0
    world: tuple = (0.0, 0.0, 0.0)
    screen: tuple = (-1.0, -1.0)           # top-left origin pixel coords
    cam_size: tuple = (0, 0)
    screen_visible: bool = False

    # Line state. Mirrors FishingLine.* — `current_tension` (0..1) drives the
    # in-game tension HUD bar; >=1 starts the break timer countdown.
    has_line: bool = False
    current_tension: float = 0.0
    pump_tension: float = 0.0
    try_pull_tension: float = 0.0
    current_try_reel_tension: float = 0.0
    stretch_to_distance: float = 0.0
    stretch_factor: float = 0.0
    break_tension_timer: float = 3.0       # counts down from 1.5 once tension hits 1.0
    loose_tension_factor: float = 0.0
    loose_length: float = 0.0
    line_is_loose: bool = True
    line_is_loose_colliders: bool = True
    is_reeling: float = 0.0                # >0 reel-in, <0 reel-out
    durability: float = 0.5
    line_state: int = 0                    # 0=IDLE 1=FLY 2=REEL_IN 3=REEL_OUT 4=NEAR_COAST
    line_type: int = 0                     # 0=MONO 1=BRAID 2=FLUORO 3=FLY

    # Player/game state. `watch_fish` == True is the catch dialog being shown
    # (the in-game "watch caught fish" screen — sell / release / keep).
    player_state: str = ""
    watch_fish: bool = False
    has_fish: bool = False                 # fishingPlayer.fish != null -> hooked
    has_junk: bool = False                 # fishingPlayer.junk != null -> snagged trash

    @classmethod
    def from_json(cls, d: dict) -> "FloatState":
        s = cls(ok=bool(d.get("ok", False)))
        if not s.ok:
            return s
        s._fill_line(d)
        if not d.get("hasFloat", False):
            s.has_float = False
            return s
        s.has_float = True
        s.t = float(d.get("t", 0.0))
        s.bait_was_thrown = bool(d.get("baitWasThrown", False))
        s.is_on_water = bool(d.get("isOnWater", False))
        s.can_hit_water = bool(d.get("canHitWater", False))
        s.is_loose = bool(d.get("isLoose", True))
        s.is_try_animation = bool(d.get("isTryAnimation", False))
        s.fish_tries_timer = float(d.get("fishTriesTimer", 0.0))
        s.fish_tries_pull_timer = float(d.get("fishTriesPullTimer", 0.0))
        s.burden_factor = float(d.get("burdenFactor", 0.0))
        s.floating_eased_offset = float(d.get("floatingEasedOffset", 0.0))
        s.vel_y = float(d.get("velY", 0.0))
        s.vel_mag = float(d.get("velMag", 0.0))
        s.world = tuple(d.get("world", [0.0, 0.0, 0.0]))
        s.screen = tuple(d.get("screen", [-1.0, -1.0]))
        s.cam_size = tuple(d.get("camSize", [0, 0]))
        s.screen_visible = bool(d.get("screenVisible", False))
        return s

    def _fill_line(self, d: dict) -> None:
        if not d.get("hasLine", False):
            self.has_line = False
            return
        self.has_line = True
        self.current_tension = float(d.get("currentTension", 0.0))
        self.pump_tension = float(d.get("pumpTension", 0.0))
        self.try_pull_tension = float(d.get("tryPullTension", 0.0))
        self.current_try_reel_tension = float(d.get("currentTryReelTension", 0.0))
        self.stretch_to_distance = float(d.get("stretchToDistance", 0.0))
        self.stretch_factor = float(d.get("stretchFactor", 0.0))
        self.break_tension_timer = float(d.get("breakTensionTimer", 3.0))
        self.loose_tension_factor = float(d.get("looseTensionFactor", 0.0))
        self.loose_length = float(d.get("looseLength", 0.0))
        self.line_is_loose = bool(d.get("lineIsLoose", True))
        self.line_is_loose_colliders = bool(d.get("lineIsLooseColliders", True))
        self.is_reeling = float(d.get("isReeling", 0.0))
        self.durability = float(d.get("durability", 0.5))
        self.line_state = int(d.get("lineState", 0))
        self.line_type = int(d.get("lineType", 0))
        self.player_state = str(d.get("playerState", ""))
        self.watch_fish = bool(d.get("watchFish", False))
        self.has_fish = bool(d.get("hasFish", False))
        self.has_junk = bool(d.get("hasJunk", False))

    @property
    def tension_danger(self) -> bool:
        """Mirrors the in-game alarm: tension >= 1.0 -> break timer counting down."""
        return self.current_tension >= 1.0

    @property
    def tension_warn(self) -> bool:
        return self.current_tension >= 0.7


class FloatMemoryReader:
    """Background UDP listener; latest() returns the most recent state or None.

    Also exposes send_command() which fires a one-shot UDP packet to the
    plugin's command port (default 18501). Supported commands:
      "sell"    -> click the watch-fish sell button via UI.Button.onClick.Invoke
      "release" -> same, but the release button
      "take"    -> same, but the keep/take button
    These are resolution-independent and require no game-window focus.
    """

    def __init__(self, port: int = 18500, cmd_port: int = 18501,
                 host: str = "127.0.0.1"):
        self._sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self._sock.bind((host, port))
        self._sock.settimeout(0.5)
        self._cmd_sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self._cmd_addr = (host, cmd_port)
        self._lock = threading.Lock()
        self._latest: Optional[FloatState] = None
        self._stop = threading.Event()
        self._thread = threading.Thread(target=self._loop, daemon=True)
        self._thread.start()

    def send_command(self, cmd: str) -> None:
        try:
            self._cmd_sock.sendto(cmd.encode("utf-8"), self._cmd_addr)
        except Exception:
            pass

    def _loop(self) -> None:
        while not self._stop.is_set():
            try:
                data, _ = self._sock.recvfrom(4096)
            except socket.timeout:
                continue
            except OSError:
                break
            try:
                d = json.loads(data.decode("utf-8"))
                state = FloatState.from_json(d)
            except Exception:
                continue
            with self._lock:
                self._latest = state

    def latest(self) -> Optional[FloatState]:
        with self._lock:
            return self._latest

    def close(self) -> None:
        self._stop.set()
        try:
            self._sock.close()
        except Exception:
            pass
        try:
            self._cmd_sock.close()
        except Exception:
            pass


def _demo() -> None:
    import time
    r = FloatMemoryReader()
    print("Listening on 127.0.0.1:18500. Start the game with the plugin loaded.")
    try:
        last_t = -1.0
        while True:
            s = r.latest()
            if s and s.t != last_t:
                last_t = s.t
                bite = "BITE" if s.is_try_animation else "    "
                pull = "PULL" if s.fish_tries_pull_timer > 0 else "    "
                if s.has_line:
                    if s.tension_danger:
                        tcolor = "DANGER"
                    elif s.tension_warn:
                        tcolor = "warn  "
                    else:
                        tcolor = "ok    "
                    tension_part = (
                        f"T={s.current_tension:.2f} {tcolor} "
                        f"break={s.break_tension_timer:4.2f} "
                        f"reel={s.is_reeling:+.0f} "
                    )
                else:
                    tension_part = "T=---- "
                if not s.has_float:
                    print(f"[t={s.t:7.2f}] no_float {tension_part}")
                else:
                    print(
                        f"[t={s.t:7.2f}] {bite} {pull} "
                        f"{tension_part}"
                        f"water={int(s.is_on_water)} loose={int(s.is_loose)} "
                        f"screen=({s.screen[0]:6.1f},{s.screen[1]:6.1f}) "
                        f"vis={int(s.screen_visible)}"
                    )
            time.sleep(0.01)
    except KeyboardInterrupt:
        pass
    finally:
        r.close()


if __name__ == "__main__":
    _demo()
