# Silhouette check of a Theo body model against the Flow references (Docs/Briefs/A-Theo3D.md, step 2c). Headless:
#   blender -b --factory-startup --python Tools/Blender/fighters/theo_silhouette.py -- <model.glb|.fbx> [overlay.png]
# Renders the model's silhouette (Workbench, orthographic, film alpha) at the exact pixel scale of each reference figure:
#   front   = 001.png (cut-out alpha of 001_rgba.png, 936 px per unit, soles on the bottom row),
#   profile = 002.png figure 3 (Theo's left side, face to the image left, camera on +X),
#   back    = 002.png figure 4 (camera on +Y),
# and prints IoU, IoU at the best horizontal shift (+-8 px) and contour distances (mean / p95 / max, px of that view).
# The model must use the shared format: Blender Z up, faces -Y, height 1.0, feet on z = 0, centred on X/Y by its bbox.
# Library use: import theo_silhouette as ts; views = ts.ref_views(); masks = ts.render_masks(views, objects);
# ts.report(views, masks, overlay_path). Only numpy + bpy (no scipy in Blender's Python).
import bpy, sys, os, math
import numpy as np
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
REF = os.path.join(REPO, "Art", "Source", "Flow", "2026-10-19_theo-3dref")
SHIFT = 8


def load_rgba(path):
    """(H, W, 4) float32, rows top-down."""
    img = bpy.data.images.load(path, check_existing=False)
    W, H = img.size
    px = np.empty(W * H * 4, np.float32)
    img.pixels.foreach_get(px)
    bpy.data.images.remove(img)
    return px.reshape(H, W, 4)[::-1].copy()


def save_rgb(arr, path):
    """arr (H, W, 3|4) float 0..1, rows top-down -> PNG."""
    H, W = arr.shape[:2]
    if arr.shape[2] == 3:
        arr = np.concatenate([arr, np.ones((H, W, 1), arr.dtype)], 2)
    img = bpy.data.images.new("out", W, H, alpha=True)
    img.pixels.foreach_set(np.ascontiguousarray(arr[::-1], np.float32).ravel())
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)


def _dilate(m):
    o = m.copy()
    o[1:] |= m[:-1]
    o[:-1] |= m[1:]
    o[:, 1:] |= m[:, :-1]
    o[:, :-1] |= m[:, 1:]
    return o


def _erode(m):
    return ~_dilate(~m)


def flood_background(fg):
    """Background = non-foreground pixels connected to the image border (iterative geodesic dilation)."""
    free = ~fg
    bg = np.zeros_like(fg)
    bg[0], bg[-1], bg[:, 0], bg[:, -1] = free[0], free[-1], free[:, 0], free[:, -1]
    for _ in range(4000):
        nb = _dilate(bg) & free
        if (nb == bg).all():
            break
        bg = nb
    return bg


def ref_views():
    """Reference figure masks with their pixel mapping: u = u0 + s * h, v = v0 - s * z (h = horizontal model axis)."""
    views = []
    a = load_rgba(os.path.join(REF, "001_rgba.png"))
    rgb = load_rgba(os.path.join(REF, "001.png"))[..., :3]
    m = a[..., 3] > 0.5
    ys, xs = np.nonzero(m)
    views.append(dict(name="front", mask=m, rgb=rgb, win=(0, 0, m.shape[1], m.shape[0]),
                      s=float(ys.max() + 1 - ys.min()), u0=(xs.min() + xs.max() + 1) / 2.0, v0=float(ys.max() + 1),
                      cam=(0, -1, 0), right=(1, 0, 0)))
    im = load_rgba(os.path.join(REF, "002.png"))[..., :3]
    fg = np.sqrt(((1 - im) ** 2).sum(-1)) * 255 > 40
    fg = _dilate(_erode(fg))  # opening: drop single-pixel specks
    fg = ~flood_background(fg)
    cols = np.nonzero(fg.any(0))[0]
    runs, s0 = [], cols[0]
    for c0, c1 in zip(cols[:-1], cols[1:]):
        if c1 != c0 + 1:
            runs.append((s0, c0))
            s0 = c1
    runs.append((s0, cols[-1]))
    runs = [r for r in runs if r[1] - r[0] > 60]
    assert len(runs) == 4, runs
    for name, k, cam, right in (("profile", 2, (1, 0, 0), (0, 1, 0)), ("back", 3, (0, 1, 0), (-1, 0, 0))):
        c0, c1 = runs[k]
        fm = np.zeros_like(fg)
        fm[:, c0:c1 + 1] = fg[:, c0:c1 + 1]
        ys, xs = np.nonzero(fm)
        x0, x1 = max(0, c0 - 60), min(fg.shape[1], c1 + 61)
        crgb = im[:, x0:x1].copy()
        other = fg[:, x0:x1] & ~fm[:, x0:x1]
        for _ in range(3):
            other = _dilate(other)
        crgb[other & ~fm[:, x0:x1]] = 1.0
        views.append(dict(name=name, mask=fm[:, x0:x1], rgb=crgb, win=(x0, 0, x1, fg.shape[0]),
                          s=float(ys.max() + 1 - ys.min()), u0=(xs.min() + xs.max() + 1) / 2.0 - x0, v0=float(ys.max() + 1),
                          cam=cam, right=right))
    return views


def _setup_scene():
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"
    sc.display.shading.light = "FLAT"
    sc.display.shading.color_type = "SINGLE"
    sc.display.shading.single_color = (0, 0, 0)
    sc.render.film_transparent = True
    sc.display.render_aa = "8"
    sc.view_settings.view_transform = "Standard"
    sc.render.image_settings.file_format = "PNG"
    sc.render.image_settings.color_mode = "RGBA"
    sc.render.resolution_percentage = 100
    cam = bpy.data.objects.get("SilCam")
    if cam is None:
        cam = bpy.data.objects.new("SilCam", bpy.data.cameras.new("SilCam"))
        sc.collection.objects.link(cam)
    cam.data.type = "ORTHO"
    cam.data.clip_start, cam.data.clip_end = 0.01, 100
    sc.camera = cam
    return sc, cam


def render_masks(views, objects, tmp_dir=None):
    """objects: the meshes that form the silhouette (all other objects are hidden from the render)."""
    sc, cam = _setup_scene()
    keep = set(objects)
    hidden = []
    for o in sc.objects:
        if o.type in ("MESH", "CURVE", "SURFACE", "META", "FONT") and o not in keep and not o.hide_render:
            o.hide_render = True
            hidden.append(o)
    tmp_dir = tmp_dir or bpy.app.tempdir
    out = {}
    for v in views:
        H, W = v["mask"].shape
        uc, vc = W / 2.0, H / 2.0
        h_c = (uc - v["u0"]) / v["s"]
        z_c = (v["v0"] - vc) / v["s"]
        right, back = np.array(v["right"], float), np.array(v["cam"], float)
        centre = right * h_c + np.array([0, 0, z_c])
        cam.location = Vector(centre + back * 10)
        cam.rotation_euler = Vector(-back).to_track_quat("-Z", "Y").to_euler()
        cam.data.ortho_scale = max(W, H) / v["s"]
        sc.render.resolution_x, sc.render.resolution_y = W, H
        path = os.path.join(tmp_dir, f"_sil_{v['name']}.png")
        sc.render.filepath = path
        bpy.ops.render.render(write_still=True)
        out[v["name"]] = load_rgba(path)[..., 3] > 0.5
        os.remove(path)
    for o in hidden:
        o.hide_render = False
    return out


def render_shaded(views, objects, color_type="VERTEX", tmp_dir=None, light="STUDIO"):
    """Same cameras as render_masks, Workbench (studio light, or FLAT = unlit texture): RGB images aligned pixel to pixel
    with the references."""
    sc, cam = _setup_scene()
    sc.display.shading.light = light
    sc.display.shading.color_type = color_type
    sc.display.shading.show_cavity = False
    sc.render.film_transparent = True
    keep = set(objects)
    hidden = []
    for o in sc.objects:
        if o.type in ("MESH", "CURVE", "SURFACE", "META", "FONT") and o not in keep and not o.hide_render:
            o.hide_render = True
            hidden.append(o)
    tmp_dir = tmp_dir or bpy.app.tempdir
    out = {}
    for v in views:
        H, W = v["mask"].shape
        h_c = (W / 2.0 - v["u0"]) / v["s"]
        z_c = (v["v0"] - H / 2.0) / v["s"]
        right, back = np.array(v["right"], float), np.array(v["cam"], float)
        cam.location = Vector(right * h_c + np.array([0, 0, z_c]) + back * 10)
        cam.rotation_euler = Vector(-back).to_track_quat("-Z", "Y").to_euler()
        cam.data.ortho_scale = max(W, H) / v["s"]
        sc.render.resolution_x, sc.render.resolution_y = W, H
        path = os.path.join(tmp_dir, f"_shd_{v['name']}.png")
        sc.render.filepath = path
        bpy.ops.render.render(write_still=True)
        img = load_rgba(path)
        os.remove(path)
        out[v["name"]] = img[..., :3] * img[..., 3:] + (1 - img[..., 3:])
    for o in hidden:
        o.hide_render = False
    sc.display.shading.light = "FLAT"
    sc.display.shading.color_type = "SINGLE"
    return out


def compare_sheet(views, shaded, path, h=768):
    """Per view: reference | model render, same scale."""
    gap = np.ones((h, 12, 3), np.float32)
    row = []
    for v in views:
        row += [_resize_h(v["rgb"], h), gap, _resize_h(shaded[v["name"]], h), gap, gap]
    save_rgb(np.concatenate(row[:-2], 1), path)
    print("COMPARE:", path, flush=True)


def _shift(m, dx):
    o = np.zeros_like(m)
    if dx > 0:
        o[:, dx:] = m[:, :-dx]
    elif dx < 0:
        o[:, :dx] = m[:, -dx:]
    else:
        o[:] = m
    return o


def _boundary(m):
    return np.argwhere(m & ~_erode(m)).astype(np.float32)


def _nearest(a, b):
    out = np.empty(len(a), np.float32)
    for i in range(0, len(a), 2048):
        d = ((a[i:i + 2048, None, :] - b[None, :, :]) ** 2).sum(-1)
        out[i:i + 2048] = np.sqrt(d.min(1))
    return out


def metrics(ref, mdl):
    inter, union = (ref & mdl).sum(), (ref | mdl).sum()
    best = (inter / union, 0)
    for dx in range(-SHIFT, SHIFT + 1):
        if dx == 0:
            continue
        sm = _shift(mdl, dx)
        iou = (ref & sm).sum() / (ref | sm).sum()
        if iou > best[0]:
            best = (iou, dx)
    br, bm = _boundary(ref), _boundary(mdl)
    if len(bm) == 0:
        return dict(iou=0.0, iou_best=0.0, dx=0, mean=1e9, p95=1e9, max=1e9, area=0.0)
    d = np.concatenate([_nearest(bm, br), _nearest(br, bm)])
    return dict(iou=float(inter / union), iou_best=float(best[0]), dx=int(best[1]), mean=float(d.mean()),
                p95=float(np.percentile(d, 95)), max=float(d.max()), area=float(mdl.sum() / max(ref.sum(), 1)))


def overlay(v, mdl):
    """Reference faded, model-only pixels red, reference-only pixels blue, model contour dark red."""
    ref = v["mask"]
    img = 1 - (1 - v["rgb"]) * 0.55
    only_m, only_r = mdl & ~ref, ref & ~mdl
    img[only_m] = img[only_m] * 0.3 + np.array([0.95, 0.15, 0.12]) * 0.7
    img[only_r] = img[only_r] * 0.3 + np.array([0.15, 0.40, 0.95]) * 0.7
    edge = mdl & ~_erode(mdl)
    img[edge] = (0.45, 0.0, 0.0)
    return img


def _resize_h(img, h):
    """Nearest-neighbour resize to height h (keeps aspect)."""
    H, W = img.shape[:2]
    w = max(1, int(round(W * h / H)))
    yi = (np.arange(h) * H / h).astype(int)
    xi = (np.arange(w) * W / w).astype(int)
    return img[yi][:, xi]


def report(views, masks, overlay_path=None, label=""):
    res = {}
    for v in views:
        res[v["name"]] = metrics(v["mask"], masks[v["name"]])
    line = " | ".join(f"{k} IoU {r['iou']:.3f} (best {r['iou_best']:.3f} @dx {r['dx']:+d}) contour mean {r['mean']:.1f}px "
                      f"p95 {r['p95']:.1f}px max {r['max']:.0f}px area x{r['area']:.3f}" for k, r in res.items())
    print(f"SILHOUETTE {label}: {line}", flush=True)
    if overlay_path:
        panels = [_resize_h(overlay(v, masks[v["name"]]), 768) for v in views]
        gap = np.ones((768, 12, 3), np.float32)
        row = [panels[0]]
        for p in panels[1:]:
            row += [gap, p]
        os.makedirs(os.path.dirname(os.path.abspath(overlay_path)), exist_ok=True)
        save_rgb(np.concatenate(row, 1), overlay_path)
        print("OVERLAY:", overlay_path, flush=True)
    return res


def import_body(path):
    sc = bpy.context.scene
    before = set(sc.objects)
    if path.lower().endswith(".fbx"):
        bpy.ops.import_scene.fbx(filepath=path)
    else:
        bpy.ops.import_scene.gltf(filepath=path)
    new = set(sc.objects) - before
    shapes = set()
    for o in new:
        if o.type == "ARMATURE":
            shapes |= {pb.custom_shape.name for pb in o.pose.bones if pb.custom_shape}
    keep = [o for o in new if o.name not in shapes and o.type == "MESH"]
    for n in shapes:
        bpy.data.objects.remove(bpy.data.objects[n], do_unlink=True)
    return keep


if __name__ == "__main__":
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    model = os.path.abspath(argv[0])
    ov = os.path.abspath(argv[1]) if len(argv) > 1 else None
    bpy.ops.wm.read_factory_settings(use_empty=True)
    objs = import_body(model)
    vs = ref_views()
    report(vs, render_masks(vs, objs), ov, os.path.basename(model))
