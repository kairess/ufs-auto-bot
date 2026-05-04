import os
import time

# pygame/SDL import 전에 설정해야 함
os.environ["SDL_JOYSTICK_HIDAPI"] = "1"
os.environ["SDL_JOYSTICK_HIDAPI_PS5"] = "1"
os.environ["SDL_JOYSTICK_HIDAPI_PS5_RUMBLE"] = "1"
os.environ["SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS"] = "1"

import pygame
from pygame._sdl2 import controller

pygame.init()
controller.init()

count = controller.get_count()
print("controller count:", count)

if count == 0:
    raise RuntimeError("No controller detected by SDL/pygame.")

for i in range(count):
    print(i, controller.name_forindex(i))

pad = controller.Controller(0)
print("Using:", controller.name_forindex(0))

ok = pad.rumble(0.4, 1.0, 500)  # low motor, high motor, duration ms
print("rumble result:", ok)

time.sleep(1.0)
pad.stop_rumble()