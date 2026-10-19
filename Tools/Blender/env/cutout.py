# White-key cutout for image-to-3D input (A.4a). Plain Python 3 + Pillow + NumPy + SciPy, no network.
#   python -I Tools/Blender/env/cutout.py <in.jpg|png> <out_rgba.png> [--tol 0.10] [--size 1024]
# Background = near-white pixels connected to the image border (flood fill), so white spots inside the object
# (mushroom caps) survive. Edge alpha is feathered over ~1.5 px; the object is re-centred on a square canvas
# with a 10 % margin. We supply our own alpha so the demo does not run its non-commercial background remover.
import sys
import numpy as np
from PIL import Image
from scipy import ndimage

args = sys.argv[1:]
src, dst = args[0], args[1]
tol = float(args[args.index("--tol") + 1]) if "--tol" in args else 0.10
size = int(args[args.index("--size") + 1]) if "--size" in args else 1024

rgb = np.asarray(Image.open(src).convert("RGB")).astype(np.float32) / 255.0
dist = np.sqrt(((1.0 - rgb) ** 2).sum(-1))
near_white = dist < tol
lab, _ = ndimage.label(near_white)
border = np.unique(np.concatenate([lab[0], lab[-1], lab[:, 0], lab[:, -1]]))
bg = np.isin(lab, border[border > 0])
fg = ~bg
fg = ndimage.binary_opening(fg, iterations=1)
lab2, n = ndimage.label(fg)
if n > 1:  # keep the largest object plus anything ≥ 2 % of it (detached leaves)
    sizes = ndimage.sum(fg, lab2, range(1, n + 1))
    keep = [i + 1 for i, s in enumerate(sizes) if s >= 0.02 * sizes.max()]
    fg = np.isin(lab2, keep)
fg = ndimage.binary_fill_holes(fg)
alpha = ndimage.gaussian_filter(fg.astype(np.float32), 0.8)
alpha = np.clip((alpha - 0.25) / 0.5, 0, 1)
# un-premultiply the white fringe at the edge
a3 = np.maximum(alpha[..., None], 1e-3)
edge = (alpha > 0) & (alpha < 1)
rgb[edge] = np.clip((rgb[edge] - (1 - a3[edge])) / a3[edge], 0, 1)
ys, xs = np.nonzero(alpha > 0.01)
y0, y1, x0, x1 = ys.min(), ys.max() + 1, xs.min(), xs.max() + 1
crop = np.dstack([rgb, alpha])[y0:y1, x0:x1]
side = int(max(crop.shape[:2]) / 0.8)
canvas = np.zeros((side, side, 4), np.float32)
oy, ox = (side - crop.shape[0]) // 2, (side - crop.shape[1]) // 2
canvas[oy:oy + crop.shape[0], ox:ox + crop.shape[1]] = crop
img = Image.fromarray((canvas * 255 + 0.5).astype(np.uint8), "RGBA").resize((size, size), Image.LANCZOS)
img.save(dst)
print(f"CUTOUT: {dst} object {x1 - x0}x{y1 - y0} px, fg {fg.mean():.3f}")
