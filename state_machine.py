"""Fishing-loop state machine.

Consumes per-frame observations and emits a state. The job of this module is
to be the single source of truth about what the bot should be doing right now.
Action execution (mouse press/release, casting input) is wired up separately.

States
------
IDLE        : nothing happening. Preview UI hidden, bot has not cast.
CASTING     : we just sent a cast input; waiting for the preview UI + float to
              appear and settle. Used to suppress the brief "float underwater
              while cast is settling" false positive.
WAITING     : preview UI visible, float visible at its baseline. The normal
              fishing wait state.
SUNK        : preview visible but the float has been missing for >SUNK_DEBOUNCE.
              Treated as "fish on" — the bot should hold left mouse to reel.
REELING     : preview UI has disappeared while we believe a fish is on (came
              from SUNK). Hold-reel continues. Exits to IDLE when the catch
              UI / inventory screen is detected, or after a hard timeout.

Signals consumed
----------------
preview_visible : bool   — UI ring is currently rendered
float_found     : bool   — float was segmented this frame
tip_y           : int    — antenna-tip y in full-frame pixels (only meaningful
                            when float_found is True)
t               : float  — wall/video time in seconds

Tunables are at the top so we can tweak them based on real video behavior.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from enum import Enum
from typing import Callable, Optional


class State(str, Enum):
    IDLE = "IDLE"
    AUTOCAST = "AUTOCAST"        # bot is actively driving a new cast
    CASTING = "CASTING"          # cast in flight, waiting for rig to settle
    WAITING = "WAITING"
    SUNK = "SUNK"
    REELING = "REELING"
    CATCH_DIALOG = "CATCH_DIALOG"  # post-catch sell/release prompt is up


# How long the float must be CONTINUOUSLY visible after a cast before we
# accept the rig as settled and start watching for bites. Splashing on cast
# landing makes the float flicker in/out of segmentation for 1-3s; until it's
# steady we must not trust a "float gone" signal as a bite.
CAST_SETTLE_S = 2.0

# How long the float must remain "not found" inside a visible preview before
# we call it a real bite. Real bites in s3 lasted ~100 ms; we set the gate
# slightly under that so we never miss one but still reject single-frame noise.
SUNK_DEBOUNCE_S = 0.08

# How long the preview must remain hidden after SUNK before we commit to the
# REELING state (avoids momentary UI flicker dropping us out of SUNK too fast).
PREVIEW_GONE_DEBOUNCE_S = 0.15

# Hard cap on REELING duration (failsafe so we never hold the mouse forever
# if the catch-UI detector misses).
REELING_TIMEOUT_S = 60.0

# Once we've entered SUNK, the float briefly bobbing back into view should not
# kick us back to WAITING — real bites in s3 had the float pop up for ~300 ms
# between sink and the player reeling. Float must stay continuously visible
# for this long to count as a false alarm.
SUNK_CANCEL_S = 0.5

# Read at construction time from config.py. The FSM doesn't import config
# directly so this stays unit-testable; the bot wires the values in.
AUTOSTART_DEFAULT = True
AUTOSTART_DELAY_DEFAULT = 2.0


@dataclass
class Observation:
    t: float
    preview_visible: bool
    float_found: bool
    tip_y: int = 0
    catch_dialog_visible: bool = False


@dataclass
class FishingFSM:
    state: State = State.IDLE
    state_since: float = 0.0
    last_seen_float: float = 0.0       # last t at which float was found
    last_lost_float: float = 0.0       # last t at which float was missing
    last_seen_preview: float = 0.0     # last t at which preview was visible
    autostart_first_cast: bool = AUTOSTART_DEFAULT
    autostart_delay_s: float = AUTOSTART_DELAY_DEFAULT
    on_transition: Optional[Callable[[State, State, float], None]] = None
    history: list[tuple[float, State]] = field(default_factory=list)

    def _go(self, new: State, t: float) -> None:
        if new == self.state:
            return
        if self.on_transition:
            self.on_transition(self.state, new, t)
        self.state = new
        self.state_since = t
        self.history.append((t, new))

    def notify_cast_sent(self, t: float) -> None:
        """Call this the moment the bot sends a cast input."""
        self._go(State.CASTING, t)

    def step(self, obs: Observation) -> State:
        if obs.preview_visible:
            self.last_seen_preview = obs.t
        if obs.float_found:
            self.last_seen_float = obs.t
        else:
            self.last_lost_float = obs.t

        s = self.state
        elapsed = obs.t - self.state_since

        # Catch dialog has highest priority — if it's up, we're done with the
        # current cycle regardless of other state. Driver clicks the sell
        # button and then returns us to AUTOCAST.
        if obs.catch_dialog_visible and s != State.CATCH_DIALOG:
            self._go(State.CATCH_DIALOG, obs.t)
            return self.state

        if s == State.IDLE:
            # If a cast already happened (manual or from a previous cycle),
            # the preview UI shows up — slip into CASTING and let it settle.
            if obs.preview_visible:
                self._go(State.CASTING, obs.t)
            elif self.autostart_first_cast and elapsed >= self.autostart_delay_s:
                # No preview after the autostart grace window: assume the rod
                # is ready and the player wants the bot to do the first cast.
                self._go(State.AUTOCAST, obs.t)

        elif s == State.AUTOCAST:
            # Driver actually performed the cast. Wait until preview shows up
            # again, then move into CASTING (which then settles into WAITING).
            if obs.preview_visible:
                self._go(State.CASTING, obs.t)
            elif elapsed > 6.0:
                # Cast input did not produce a preview — give up and idle.
                self._go(State.IDLE, obs.t)

        elif s == State.CASTING:
            # Promote to WAITING only when the float has been CONTINUOUSLY
            # visible for CAST_SETTLE_S. Using `last_lost_float` here means a
            # single-frame splash flicker resets the timer, so we never enter
            # WAITING while the rig is still settling — which would otherwise
            # immediately trip the SUNK debounce on the next missed frame.
            steady_visible_s = obs.t - self.last_lost_float
            if obs.preview_visible and obs.float_found and \
                    steady_visible_s >= CAST_SETTLE_S:
                self._go(State.WAITING, obs.t)
            elif elapsed > 30.0 and (obs.t - self.last_seen_preview) > 25.0:
                # Hard failsafe: 30s in CASTING with no preview UI seen for
                # most of that time means the cast almost certainly never
                # launched (window lost focus, rod not equipped, etc.).
                # Drop to IDLE so the autostart loop retries cleanly.
                self._go(State.IDLE, obs.t)

        elif s == State.WAITING:
            if not obs.preview_visible:
                # UI hidden mid-wait — likely the player/bot started reeling
                # without a bite, or the rig was lost. Drop to IDLE.
                if obs.t - self.last_seen_preview > PREVIEW_GONE_DEBOUNCE_S:
                    self._go(State.IDLE, obs.t)
            else:
                # Float-disappearance while UI is up = potential bite.
                gone_for = obs.t - self.last_seen_float
                if not obs.float_found and gone_for >= SUNK_DEBOUNCE_S:
                    self._go(State.SUNK, obs.t)

        elif s == State.SUNK:
            # Preview gone -> we've started reeling. Check this BEFORE the
            # float-reappeared exit so a brief mid-bite bob doesn't cancel
            # the bite right when the rod is being raised.
            if not obs.preview_visible and \
                    (obs.t - self.last_seen_preview) > PREVIEW_GONE_DEBOUNCE_S:
                self._go(State.REELING, obs.t)
            elif obs.float_found and \
                    (obs.t - self.last_lost_float) >= SUNK_CANCEL_S:
                # Float has been continuously visible for SUNK_CANCEL_S — real
                # false alarm. Resume waiting.
                self._go(State.WAITING, obs.t)

        elif s == State.REELING:
            # We exit reeling either when the catch dialog appears (handled by
            # the priority check at the top of step) or via the failsafe.
            if obs.preview_visible and elapsed > 1.0:
                # Preview reappearing without a catch dialog means the rig
                # came back empty (lost the fish, or false alarm). Cycle.
                self._go(State.IDLE, obs.t)
            elif elapsed > REELING_TIMEOUT_S:
                self._go(State.IDLE, obs.t)

        elif s == State.CATCH_DIALOG:
            # Driver clicks sell and the dialog goes away on its own; once
            # the green button area is gone, kick off the next auto-cast.
            if not obs.catch_dialog_visible and elapsed > 0.3:
                self._go(State.AUTOCAST, obs.t)
            elif elapsed > 10.0:
                self._go(State.IDLE, obs.t)

        return self.state

    def notify_autocast_started(self, t: float) -> None:
        """Driver tells us it just performed the cast input."""
        self._go(State.AUTOCAST, t)

    def notify_cast_input_completed(self, t: float) -> None:
        """Driver finished the LMB hold-and-release. Move into CASTING so the
        next loop iterations don't re-fire the cast input. CASTING then waits
        for the preview UI + float to appear and settle.
        """
        if self.state == State.AUTOCAST:
            self._go(State.CASTING, t)


def reel_should_be_held(state: State) -> bool:
    """Convenience: is the left mouse button supposed to be down right now?"""
    return state in (State.SUNK, State.REELING)


def reel_action(state: State, tension_danger: bool) -> str:
    """Combine state + tension into the action the input layer should perform.

    Returns one of: "idle" (no input), "reel" (hold LMB), "ease" (release LMB
    while remaining in the reel cycle so the line doesn't snap).

    AUTOCAST and CATCH_DIALOG are driver-managed (not held-button states), so
    they return "idle" here — the bot loop handles them with one-shot inputs.
    """
    if not reel_should_be_held(state):
        return "idle"
    return "ease" if tension_danger else "reel"
