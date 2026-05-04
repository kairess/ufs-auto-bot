"""Sample HSV values around the float in the preview window for tuning."""
import os, sys, cv2, numpy as np
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from config import PREVIEW_CIRCLE
from segment_float import load_frame

def main():
    src, t = sys.argv[1], float(sys.argv[2])
    bgr = load_frame(src, t)
    cx, cy, r = PREVIEW_CIRCLE
    roi = bgr[cy - r:cy + r, cx - r:cx + r]
    hsv = cv2.cvtColor(roi, cv2.COLOR_BGR2HSV)
    # Sample a vertical strip down the middle (where the float sits).
    h, w = roi.shape[:2]
    strip = hsv[:, w // 2 - 8:w // 2 + 9]
    print(f"strip shape: {strip.shape}")
    # Print HSV per row (median across the 17-px column).
    print(" y    H    S    V   B    G    R   (BGR)")
    for y in range(0, h, 4):
        med = np.median(strip[y].reshape(-1, 3), axis=0).astype(int)
        bgr_med = np.median(roi[y, w // 2 - 8:w // 2 + 9].reshape(-1, 3), axis=0).astype(int)
        print(f"{y:3d}  {med[0]:3d}  {med[1]:3d}  {med[2]:3d}   {bgr_med[0]:3d}  {bgr_med[1]:3d}  {bgr_med[2]:3d}")

if __name__ == "__main__":
    main()
