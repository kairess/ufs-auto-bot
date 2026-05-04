# ufs-auto-bot

![demo](assets/result.gif)

Auto-fishing bot for **Ultimate Fishing Simulator 1** (Windows). Watches the screen, segments the bobber preview circle, detects bites and tension, and drives the mouse to cast → wait → reel → sell → cast again.

The bot must run on the same Windows machine the game is running on. Synthetic mouse input does not pass through remote-desktop / streaming clients (Steam Remote Play, Moonlight, etc.).

## How it works

```
capture (mss) ──► segment float ──► tension monitor ──► catch-dialog detect
                                          │
                                          ▼
                               FSM (state_machine.py)
                                          │
                                          ▼
              IDLE → AUTOCAST → CASTING → WAITING → SUNK → REELING
                ▲                                                │
                └────────────── CATCH_DIALOG ◄───────────────────┘
                                          │
                            mouse driver (SendInput / Quartz)
```

- **Float segmentation** — HSV thresholding inside the right-side bobber preview circle. Tracks the antenna tip's `y` coordinate.
- **Bite detection** — float disappears for >80 ms while the preview UI is visible → SUNK → reel.
- **Tension monitor** — bottom bar's hue. Yellow/green = safe (hold LMB), red/orange = release LMB so the line doesn't snap.
- **Catch dialog** — green sell button at a fixed location. Bot clicks it then waits before next cast.
- **Auto-cast** — bot holds LMB for `CAST_HOLD_S` to charge, releases.

## Setup

```bat
py -m venv .venv
.venv\Scripts\python -m pip install -r requirements.txt

:: Calibrate the game-window rect (hover top-left then bottom-right corners)
.venv\Scripts\python calibrate_window.py
:: paste the printed GAME_WINDOW_RECT into config.py

:: Run
.venv\Scripts\python bot.py
```

## Tunables (config.py)

| Setting | Default | Purpose |
|--|--|--|
| `GAME_WINDOW_RECT` | `(0, 0, 2560, 1440)` | Screen rect of the game's rendered area |
| `PREVIEW_CIRCLE` | `(2396, 734, 126)` | Game-local `(cx, cy, r)` of the bobber preview |
| `TENSION_BAR_ROI` | `(720, 1393, 1840, 1408)` | Game-local tension bar rect |
| `SELL_BUTTON_CENTER` | `(1100, 1303)` | Game-local sell button click target |
| `CAST_HOLD_S` | `1.5` | LMB hold duration for cast charge |
| `POST_CATCH_DELAY_S` | `8.0` | Wait after catch dialog closes before re-casting |
| `AUTOSTART_FIRST_CAST` | `True` | Bot does the first cast itself after `AUTOSTART_DELAY_S` of IDLE |

All UI coordinates are **game-local** — they're translated to screen-absolute via `GAME_WINDOW_RECT`. Calibrated for `2560x1440`; scale proportionally for other resolutions.

## Controls

- `Delete` (anywhere on screen) — stop the bot, release the mouse
- `Ctrl+C` (terminal) — same

## CLI flags

```
bot.py [--dry] [--window] [--no-monitor] [--game-rect x,y,w,h]
       [--probe] [--probe-input] [--fps N] [--countdown N]
```

- `--dry` — no mouse input, log only
- `--window` — show OpenCV overlay (debug; steals focus)
- `--no-monitor` — disable the floating status panel
- `--probe-input` — one-shot test that synthetic input reaches the OS
- `--game-rect x,y,w,h` — override `GAME_WINDOW_RECT` from CLI

## Status panel

A small always-on-top window appears at the **left-center** of the primary display showing the FSM state, action (REEL / EASE), tension bar, catch-dialog flag, fps, and a live thumbnail of the segmented preview circle. Drag it anywhere with the mouse.

| WAITING | REELING |
|--|--|
| ![waiting](assets/waiting.png) | ![reeling](assets/reeling.png) |

## Files

| Module | Role |
|--|--|
| `bot.py` | Main capture → analyze → act loop |
| `config.py` | All tunable constants |
| `state_machine.py` | Fishing FSM |
| `screen_capture.py` | mss wrapper |
| `segment_float.py` | Float segmentation in preview circle |
| `tension.py` | Tension bar monitor |
| `catch_dialog.py` | Post-catch dialog detection |
| `mouse_input.py` | MouseDriver (Win32 SendInput backend) |
| `win32_mouse.py` | Windows mouse backend |
| `status_monitor.py` | Floating Tk status panel |
| `calibrate_window.py` | Game-window rect helper |
| `play_segmentation.py` | Offline video annotation tool |
| `legacy/` | One-shot dev/debug scripts |

## Limitations

- Windows only.
- Calibrated for the default UFS UI at `2560x1440`. Other resolutions need new coordinate values in `config.py`.
- Synthetic input doesn't survive remote-desktop / streaming clients — the bot must run on the same machine as the game.
- Only standard rod fishing (one bobber) is supported.
