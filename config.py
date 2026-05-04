"""User-tunable settings for the UFS auto-bot.

Coordinates below are in GAME-LOCAL pixels — i.e. relative to the top-left
of the game window's rendered area, NOT screen-absolute. When the game runs
fullscreen at the same resolution as your monitor, GAME_WINDOW_RECT covers
the whole screen and game-local == screen-absolute. In windowed mode you set
GAME_WINDOW_RECT to where the window actually sits and everything else still
works.

Quick way to find your game window rect:
  - hit Shift+Cmd+4 in macOS; it shows live pixel coords as you drag.
  - hover the top-left of the game's rendered area, note (x, y).
  - hover the bottom-right, note (x2, y2). Width = x2-x, Height = y2-y.
  - put those in GAME_WINDOW_RECT below, or pass --game-rect on the CLI.
"""

# (left, top, width, height) of the game window's rendered area on the
# physical screen. Default = full primary monitor at 2560x1440.
GAME_WINDOW_RECT = (0, 0, 2559, 1439)

# (cx, cy, r) of the right-side circular bobber preview window — game-local.
PREVIEW_CIRCLE = (2396, 734, 126)

# Inner-radius shrink (px) to ignore the dark border ring.
PREVIEW_BORDER_PAD = 8

# Bottom tension bar ROI (x0, y0, x1, y1) at 2560x1440. Calibrated from s3
# during reeling at t=45-50s. The bar is hidden when not reeling.
TENSION_BAR_ROI = (720, 1393, 1840, 1408)

# Fraction of bar pixels that must be in the red/orange hue range to trigger
# the "release reel" command.
TENSION_DANGER_FRACTION = 0.20

# Sell button on the catch dialog. (x, y) is where to click; the box is the
# region we sample to confirm the dialog is open before clicking. Calibrated
# from s2/s3, both showed 89% green fill here when dialog is open and 0%
# otherwise.
SELL_BUTTON_BOX = (950, 1268, 1249, 1339)
SELL_BUTTON_CENTER = (1100, 1303)
CATCH_DIALOG_FILL_THRESHOLD = 0.50  # >50% green fill = dialog is open

# Cast input: hold LMB for this long, then release. Tune to taste — UFS shows
# a charging power bar; ~1.5s gives a medium-power cast on default settings.
CAST_HOLD_S = 1.5

# After a sell click or auto-cast, wait this long before re-entering the loop
# so the game can update its UI / animations.
POST_ACTION_PAUSE_S = 0.8
