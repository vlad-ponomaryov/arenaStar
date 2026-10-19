# Image-to-3D fighter preparation (spike, Docs/Spike-ImageTo3D.md). Runs headless:
#   blender -b --factory-startup --python Tools/Blender/prepare_fighter.py -- <generated.glb> <Tools/Blender/fighters/<id>.json> <out_dir> [--tris 4000] [--snap] [--tag game]
# Steps: import -> normalise (height 1, feet on ground, front -Y) -> voxel remesh (one closed surface) -> drop tiny islands ->
# vertex colours projected from the Flow reference views (occlusion by ray cast, outlines masked) -> optional palette snap ->
# decimate to the triangle budget -> armature from the config (FighterBone names) -> automatic (bone heat) weights ->
# export FBX (Unity) + GLB, plus a posed copy for the deformation check.
import bpy, bmesh, sys, os, json, math
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

argv = sys.argv[sys.argv.index("--") + 1:]
src, cfg_path, out_dir = (os.path.abspath(a) for a in argv[:3])
tris = int(argv[argv.index("--tris") + 1]) if "--tris" in argv else 4000
tag = argv[argv.index("--tag") + 1] if "--tag" in argv else f"{tris}"
snap = "--snap" in argv
repo = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
cfg = json.load(open(cfg_path, encoding="utf-8"))
os.makedirs(out_dir, exist_ok=True)
log = lambda *a: print("[prepare]", *a, flush=True)

# ---------- import + normalise ----------
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
for o in bpy.context.scene.objects:
    o.select_set(o.type == "MESH")
bpy.context.view_layer.objects.active = meshes[0]
bpy.ops.object.parent_clear(type="CLEAR_KEEP_TRANSFORM")
if len(meshes) > 1:
    bpy.ops.object.join()
obj = bpy.context.view_layer.objects.active
for o in list(bpy.context.scene.objects):
    if o is not obj:
        bpy.data.objects.remove(o, do_unlink=True)
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
turn = {"-Y": 0.0, "+X": math.pi / 2, "+Y": math.pi, "-X": -math.pi / 2}[cfg.get("frontAxis", "-Y")]
obj.rotation_euler = (0, 0, -turn)
bpy.ops.object.transform_apply(rotation=True)

def verts_np(o):
    v = np.empty(len(o.data.vertices) * 3, dtype=np.float64)
    o.data.vertices.foreach_get("co", v)
    return v.reshape(-1, 3)

v = verts_np(obj)
lo, hi = v.min(0), v.max(0)
s = 1.0 / (hi[2] - lo[2])
obj.scale = (s, s, s)
obj.location = (-(lo[0] + hi[0]) / 2 * s, -(lo[1] + hi[1]) / 2 * s, -lo[2] * s)
bpy.ops.object.transform_apply(location=True, scale=True)
log("imported tris", sum(len(p.vertices) - 2 for p in obj.data.polygons))

# ---------- voxel remesh + islands ----------
for mat_slot in list(obj.material_slots):
    pass
obj.data.materials.clear()
mod = obj.modifiers.new("Remesh", "REMESH")
mod.mode = "VOXEL"
mod.voxel_size = cfg.get("voxelSize", 0.006)
mod.use_smooth_shade = True
bpy.ops.object.modifier_apply(modifier=mod.name)
bm = bmesh.new()
bm.from_mesh(obj.data)
bm.verts.ensure_lookup_table()
seen, islands = set(), []
for start in bm.verts:
    if start.index in seen:
        continue
    stack, isl = [start], []
    seen.add(start.index)
    while stack:
        cur = stack.pop()
        isl.append(cur)
        for e in cur.link_edges:
            w = e.other_vert(cur)
            if w.index not in seen:
                seen.add(w.index)
                stack.append(w)
    islands.append(isl)
limit = cfg.get("minIslandFraction", 0.002) * len(bm.verts)
drop = [vv for isl in islands if len(isl) < limit for vv in isl]
bmesh.ops.delete(bm, geom=drop, context="VERTS")
bm.to_mesh(obj.data)
bm.free()
log("remesh islands", len(islands), "dropped verts", len(drop), "verts", len(obj.data.vertices))

# ---------- colour projection ----------
def load_image(path):
    img = bpy.data.images.load(os.path.join(repo, path), check_existing=True)
    w, h = img.size
    px = np.empty(w * h * 4, dtype=np.float32)
    img.pixels.foreach_get(px)
    return px.reshape(h, w, 4)[::-1, :, :3].copy()  # row 0 = top

def window(a, k, fn):
    pad = k // 2
    p = np.pad(a, pad, mode="edge")
    out = a.copy()
    for dy in range(k):
        for dx in range(k):
            out = fn(out, p[dy:dy + a.shape[0], dx:dx + a.shape[1]])
    return out

images = {}
def view_image(path):
    if path not in images:
        rgb = load_image(path)
        lum = rgb @ np.array([0.2126, 0.7152, 0.0722], dtype=np.float32)
        fg = np.sqrt(((1.0 - rgb) ** 2).sum(-1)) > 0.12
        dark = lum < 0.25  # outlines and paws (both warm dark brown); thin dark = outline
        thick = window(window(dark, 7, np.logical_and), 7, np.logical_or)  # opening: paws survive, outlines do not
        valid = fg & ~(dark & ~thick)
        images[path] = (rgb, valid)
    return images[path]

me = obj.data
me.calc_loop_triangles() if hasattr(me, "calc_loop_triangles") else None
co = verts_np(obj)
nrm = np.empty(len(me.vertices) * 3)
me.vertices.foreach_get("normal", nrm)
nrm = nrm.reshape(-1, 3)
lo, hi = co.min(0), co.max(0)
bvh = BVHTree.FromObject(obj, bpy.context.evaluated_depsgraph_get())
views = cfg["views"]
specs = [  # name, camera direction (object -> camera), screen-u axis function, config view
    ("front", np.array([0, -1, 0.0]), lambda p: p[:, 0], (lo[0], hi[0]), views["front"]),
    ("back", np.array([0, 1, 0.0]), lambda p: -p[:, 0], (-hi[0], -lo[0]), views["back"]),
    ("left", np.array([1, 0, 0.0]), lambda p: p[:, 1], (lo[1], hi[1]), views["left"]),
    ("right", np.array([-1, 0, 0.0]), lambda p: p[:, 1], (lo[1], hi[1]), views["left"]),  # mirrored side
]
acc = np.zeros((len(co), 3))
wsum = np.zeros(len(co))
for name, d, ufn, urange, view in specs:
    rgb, valid = view_image(view["image"])
    x0, y0, x1, y1 = view["bbox"]
    u = (ufn(co) - urange[0]) / (urange[1] - urange[0])
    t = (co[:, 2] - lo[2]) / (hi[2] - lo[2])
    px = np.clip((x0 + u * (x1 - x0)).astype(int), 0, rgb.shape[1] - 1)
    py = np.clip((y1 - t * (y1 - y0)).astype(int), 0, rgb.shape[0] - 1)
    facing = np.clip(nrm @ d, 0, 1) ** 2
    vis = np.zeros(len(co), dtype=bool)
    dv = Vector(d.tolist())
    for i in np.nonzero(facing > 0.01)[0]:
        p = Vector(co[i].tolist()) + dv * 0.004
        vis[i] = bvh.ray_cast(p, dv, 10.0)[0] is None
    wv = facing * vis * valid[py, px] * view.get("weight", 1.0)
    acc += rgb[py, px] * wv[:, None]
    wsum += wv
    log("view", name, "coloured verts", int((wv > 0).sum()))
col = np.where(wsum[:, None] > 0, acc / np.maximum(wsum, 1e-9)[:, None], np.nan)
# fill vertices no view could see (armpits, between legs) from neighbours
edges = np.empty(len(me.edges) * 2, dtype=np.int64)
me.edges.foreach_get("vertices", edges)
edges = edges.reshape(-1, 2)
for _ in range(200):
    unknown = np.isnan(col[:, 0])
    if not unknown.any():
        break
    s_ = np.zeros_like(acc); c_ = np.zeros(len(co))
    for a, b in ((0, 1), (1, 0)):
        src_known = ~unknown[edges[:, a]]
        np.add.at(s_, edges[src_known, b], col[edges[src_known, a]])
        np.add.at(c_, edges[src_known, b], 1)
    fill = unknown & (c_ > 0)
    col[fill] = s_[fill] / c_[fill][:, None]
col = np.nan_to_num(col, nan=0.5)  # sRGB (image pixels are not linearised)
if snap and cfg.get("palette"):
    pal = np.array([[int(h[i:i + 2], 16) / 255.0 for i in (1, 3, 5)] for h in cfg["palette"]])
    col = pal[((col[:, None, :] - pal[None]) ** 2).sum(-1).argmin(1)]
col = np.where(col <= 0.04045, col / 12.92, ((np.clip(col, 0, 1) + 0.055) / 1.055) ** 2.4)  # FLOAT_COLOR is linear
attr = me.color_attributes.new("Col", "FLOAT_COLOR", "POINT")
attr.data.foreach_set("color", np.hstack([col, np.ones((len(col), 1))]).astype(np.float32).ravel())
me.color_attributes.active_color = attr
mat = bpy.data.materials.new("FighterVC")
mat.use_nodes = True
nt = mat.node_tree
bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
vc = nt.nodes.new("ShaderNodeVertexColor")
vc.layer_name = "Col"
nt.links.new(vc.outputs["Color"], bsdf.inputs["Base Color"])
bsdf.inputs["Roughness"].default_value = 0.85
me.materials.append(mat)

# ---------- decimate ----------
cur = sum(len(p.vertices) - 2 for p in me.polygons)
dec = obj.modifiers.new("Decimate", "DECIMATE")
dec.ratio = min(1.0, tris / cur)
bpy.ops.object.modifier_apply(modifier=dec.name)
tri = obj.modifiers.new("Tri", "TRIANGULATE")
bpy.ops.object.modifier_apply(modifier=tri.name)
for p in obj.data.polygons:
    p.use_smooth = True
log("decimated", cur, "->", len(obj.data.polygons))

# ---------- armature ----------
bvh = BVHTree.FromObject(obj, bpy.context.evaluated_depsgraph_get())
fx0, fy0, fx1, fy1 = views["front"]["bbox"]
co = verts_np(obj)
lo, hi = co.min(0), co.max(0)
def joint(spec):
    pxu, pxv, layer = spec
    x = lo[0] + (pxu - fx0) / (fx1 - fx0) * (hi[0] - lo[0])
    z = lo[2] + (fy1 - pxv) / (fy1 - fy0) * (hi[2] - lo[2])
    z = min(max(z, lo[2] + 0.01), hi[2] - 0.01)
    hits, origin = [], Vector((x, lo[1] - 1.0, z))
    while True:
        h = bvh.ray_cast(origin, Vector((0, 1, 0)), 10.0)
        if h[0] is None:
            break
        hits.append(h[0].y)
        origin = h[0] + Vector((0, 1e-4, 0))
    if len(hits) >= 2:
        seg = (hits[0], hits[1]) if layer == "front" else (hits[-2], hits[-1])
        y = (seg[0] + seg[1]) / 2
    else:
        y = 0.0
    return Vector((x, y, z))

arm_data = bpy.data.armatures.new("Rig")
arm = bpy.data.objects.new("Rig", arm_data)
bpy.context.scene.collection.objects.link(arm)
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode="EDIT")
for b in cfg["bones"]:
    eb = arm_data.edit_bones.new(b["name"])
    eb.head, eb.tail = joint(b["head"]), joint(b["tail"])
    if b["parent"]:
        eb.parent = arm_data.edit_bones[b["parent"]]
bpy.ops.object.mode_set(mode="OBJECT")
bpy.ops.object.select_all(action="DESELECT")
obj.select_set(True)
arm.select_set(True)
bpy.context.view_layer.objects.active = arm
bpy.ops.object.parent_set(type="ARMATURE_AUTO")
groups = {g.index: g.name for g in obj.vertex_groups}
unweighted = sum(1 for vv in obj.data.vertices if sum(g.weight for g in vv.groups) < 1e-4)
infl = [sum(1 for g in vv.groups if g.weight > 0.01) for vv in obj.data.vertices]
log("weights groups", len(groups), "unweighted verts", unweighted, "max influences", max(infl), "mean", round(sum(infl) / len(infl), 2))

# ---------- export ----------
base = os.path.join(out_dir, f"{cfg['id']}_{tag}")
bpy.ops.object.select_all(action="DESELECT")
obj.select_set(True)
arm.select_set(True)
bpy.ops.export_scene.fbx(filepath=base + ".fbx", use_selection=True, object_types={"ARMATURE", "MESH"}, add_leaf_bones=False,
                         apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", colors_type="SRGB",
                         mesh_smooth_type="FACE", bake_anim=False)
bpy.ops.export_scene.gltf(filepath=base + ".glb", export_format="GLB", use_selection=True)
log("exported", base + ".fbx", base + ".glb")

# posed copy for the deformation check (rendered by render_preview.py)
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode="POSE")
for name, rot in cfg.get("testPose", {}).items():
    pb = arm.pose.bones[name]
    pb.rotation_mode = "XYZ"
    pb.rotation_euler = [math.radians(a) for a in rot]
bpy.ops.object.mode_set(mode="OBJECT")
dg = bpy.context.evaluated_depsgraph_get()
posed = bpy.data.objects.new("Posed", bpy.data.meshes.new_from_object(obj.evaluated_get(dg)))
bpy.context.scene.collection.objects.link(posed)
bpy.ops.object.select_all(action="DESELECT")
posed.select_set(True)
bpy.context.view_layer.objects.active = posed
posed_path = os.path.join(repo, "Tools", "out", "spike3d", f"{cfg['id']}_{tag}_posed.glb")
os.makedirs(os.path.dirname(posed_path), exist_ok=True)
bpy.ops.export_scene.gltf(filepath=posed_path, export_format="GLB", use_selection=True)
log("RESULT tris", len(obj.data.polygons), "verts", len(obj.data.vertices), "posed", posed_path)
