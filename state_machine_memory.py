"""Memory-driven fishing FSM.

This is a re-write of `state_machine.py` for the world where we read
`FishingFloat` and `FishingLine` state directly out of the running game via
`float_memory.FloatState`. The original FSM had to debounce noisy CV signals
(float lost vs sunk vs splash flicker); here every transition has a clean
discrete predicate, so the timers are short and the dead-reckoning is gone.

States
------
IDLE         Nothing cast. If autostart is enabled we drop into AUTOCAST after
             a grace window.
AUTOCAST     Driver is firing the cast input (held LMB). Driver must call
             notify_cast_input_completed when the hold ends -> CASTING.
CASTING      Cast in flight. Watch for `is_on_water` to flip True.
WAITING      Float is on water, no bite. Watch `is_try_animation`.
BITE         Fish is nibbling. Wait for a strong pull then hook.
HOOK         Driver fires a single-tick click. Watch for tension or reel
             engagement to confirm hookup -> FIGHT, otherwise back to WAITING.
FIGHT        Hooked. Drive reel-in, easing on high tension.
LANDED       Fight resolved (tension idle long enough, or float gone).
             Waits briefly for the catch dialog.
CATCH_DIALOG Sell/release prompt up. Driver clicks sell -> AUTOCAST after delay.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from enum import Enum
from typing import Callable, Optional

from float_memory import FloatState


class State(str, Enum):
    IDLE = "IDLE"
    AUTOCAST = "AUTOCAST"
    CASTING = "CASTING"
    WAITING = "WAITING"
    BITE = "BITE"
    HOOK = "HOOK"
    FIGHT = "FIGHT"
    LANDED = "LANDED"
    CATCH_DIALOG = "CATCH_DIALOG"


# How long the cast can take to land before we assume it failed.
CASTING_TIMEOUT_S = 15.0

# Minimum bite duration before we'll consider hooking. Filters out tiny taps.
BITE_HOLD_S = 0.4

# fishTriesTimer ramps 0..4s; force scales with it. Hooking on a strong pull
# after this much accumulated bite gives the cleanest hookup.
BITE_TIMER_HOOK_MIN = 0.8

# After the bite signal disappears, how long to wait for it to come back
# before we treat it as the fish having lost interest.
BITE_RECOVER_S = 1.5

# Once we start reeling on a hook, this is how long we keep going before
# concluding nothing is on the line. Has to be long enough for tension to
# build up against a fighting fish.
HOOK_CONFIRM_S = 3.0

# Tension/reel must be quiet for this long for us to call the fight finished.
FIGHT_DONE_S = 1.5

# Hard ceiling on a fight (failsafe).
FIGHT_TIMEOUT_S = 120.0

# After LANDED, how long we wait for the catch dialog before bailing.
LANDED_TIMEOUT_S = 8.0

# Tension thresholds.
TENSION_DANGER = 1.0    # game itself starts the break timer at this point
TENSION_EASE = 0.80     # back off the reel before we're at the limit


@dataclass
class FishingFSM:
    state: State = State.IDLE
    state_since: float = 0.0
    bite_started: float = 0.0
    last_bite_seen: float = 0.0
    last_fight_signal: float = 0.0
    autostart_first_cast: bool = True
    autostart_delay_s: float = 2.0
    post_catch_delay_s: float = 6.0
    on_transition: Optional[Callable[[State, State, float], None]] = None
    history: list = field(default_factory=list)

    def _go(self, new: State, t: float) -> None:
        if new == self.state:
            return
        if self.on_transition:
            self.on_transition(self.state, new, t)
        self.state = new
        self.state_since = t
        self.history.append((t, new))

    # ---- driver callbacks --------------------------------------------------

    def notify_cast_input_completed(self, t: float) -> None:
        if self.state == State.AUTOCAST:
            self._go(State.CASTING, t)

    def notify_hook_fired(self, t: float) -> None:
        # Driver tells us it issued the strike click. Stay in HOOK; the next
        # observation will move us forward based on tension/reel signals.
        pass

    # ---- main step ---------------------------------------------------------

    def step(self, t: float, fs: FloatState, catch_visible: bool) -> State:
        s = self.state
        elapsed = t - self.state_since

        # Catch dialog wins, regardless of state — except don't re-enter.
        if catch_visible and s != State.CATCH_DIALOG:
            self._go(State.CATCH_DIALOG, t)
            return self.state

        # Memory link not up yet -> just sit in IDLE. The bot driver should
        # not act on FSM output until this clears.
        if not fs.ok:
            return self.state

        if s == State.IDLE:
            if fs.has_float and fs.is_on_water:
                # Already cast (player kicked it off, or prior session).
                self._go(State.WAITING, t)
            elif self.autostart_first_cast and elapsed >= self.autostart_delay_s:
                self._go(State.AUTOCAST, t)

        elif s == State.AUTOCAST:
            # Driver-managed. Only failsafe out if it never returns.
            if elapsed > 12.0:
                self._go(State.IDLE, t)

        elif s == State.CASTING:
            if fs.has_float and fs.is_on_water:
                self._go(State.WAITING, t)
            elif elapsed > CASTING_TIMEOUT_S:
                self._go(State.IDLE, t)

        elif s == State.WAITING:
            if fs.has_fish:
                # Fish hooked itself / we missed the bite frame -> straight to fight.
                self.last_fight_signal = t
                self._go(State.FIGHT, t)
            elif not fs.has_float and elapsed > 0.5:
                # Rig disappeared mid-wait — line break, equipment swap.
                self._go(State.IDLE, t)
            elif fs.has_float and not fs.is_on_water:
                # Player reeled it back manually.
                self._go(State.IDLE, t)
            elif fs.is_try_animation:
                self.bite_started = t
                self.last_bite_seen = t
                self._go(State.BITE, t)

        elif s == State.BITE:
            if fs.is_try_animation:
                self.last_bite_seen = t
            quiet_for = t - self.last_bite_seen

            if fs.has_fish:
                # Fish locked in — go fight directly.
                self.last_fight_signal = t
                self._go(State.FIGHT, t)
            elif (fs.fish_tries_pull_timer > 0
                  and fs.fish_tries_timer >= BITE_TIMER_HOOK_MIN
                  and (t - self.bite_started) >= BITE_HOLD_S):
                # Sustained bite + pull pulse: best moment to hook.
                self._go(State.HOOK, t)
            elif quiet_for > BITE_RECOVER_S:
                self._go(State.WAITING, t)

        elif s == State.HOOK:
            if fs.has_fish:
                self.last_fight_signal = t
                self._go(State.FIGHT, t)
            elif elapsed > HOOK_CONFIRM_S:
                # Reel-in produced no fish object — empty hook, back to wait.
                self._go(State.WAITING, t)

        elif s == State.FIGHT:
            if fs.has_fish or abs(fs.is_reeling) > 0 or fs.current_tension > 0.05:
                self.last_fight_signal = t

            if fs.watch_fish:
                # Catch dialog handler will pick this up via the priority
                # check at the top of step().
                self._go(State.LANDED, t)
            elif not fs.has_fish and not fs.has_float:
                # Both gone — line broke or bait flew out of water.
                self._go(State.LANDED, t)
            elif not fs.has_fish and (t - self.last_fight_signal) >= FIGHT_DONE_S:
                # Fish object gone for a while without a catch dialog -> escaped.
                self._go(State.LANDED, t)
            elif elapsed > FIGHT_TIMEOUT_S:
                self._go(State.IDLE, t)

        elif s == State.LANDED:
            # Wait briefly for the catch dialog to spawn (handled by the
            # priority check at the top of step). If it never does, the fish
            # got away clean.
            if elapsed > LANDED_TIMEOUT_S:
                self._go(State.IDLE, t)

        elif s == State.CATCH_DIALOG:
            if not catch_visible and elapsed >= self.post_catch_delay_s:
                self._go(State.AUTOCAST, t)
            elif elapsed > 20.0:
                self._go(State.IDLE, t)

        return self.state


# ---------------------------------------------------------------------------
# Action selectors
# ---------------------------------------------------------------------------


def reel_action(state: State, fs: FloatState) -> str:
    """Translate (state, line tension) into the desired LMB behaviour.

    Returns one of:
      'idle' : no input
      'reel' : hold LMB (reeling in)
      'ease' : release LMB momentarily; line tension is too high

    UFS treats LMB as a continuous reel-in — short clicks don't strike, they
    only nudge the reel a frame and stop. So HOOK and FIGHT both want LMB
    held: HOOK starts the reel which self-hooks the fish via line tension,
    and FIGHT keeps reeling unless tension is in the danger zone.
    """
    if state in (State.HOOK, State.FIGHT):
        if fs.current_tension >= TENSION_EASE:
            return "ease"
        return "reel"
    return "idle"


def reel_should_be_held(state: State) -> bool:
    return state in (State.HOOK, State.FIGHT)
