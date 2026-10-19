# Shared helpers for A.4a environment Blender scripts: vertex colours, AO bake by ray casts, report, export.
# Conventions (Docs/Briefs/A-EnvTarget.md §3.1): Blender Z up, 1 unit = 1 cell, pivot at bottom centre; colour only in
# the "Col" attribute (linear FLOAT_COLOR, FBX writes sRGB), alpha = gloss; FBX for Unity (Y up) + GLB for previews.
import bpy, os
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

def hex_rgb(h):
    return np.array([int(h[i:i + 2], 16) / 255.0 for i in (1, 3, 5)])

def srgb_to_lin(c):
    c = np.clip(c, 0, 1)
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)

def verts_np(o):
    v = np.empty(len(o.data.vertices) * 3)
    o.data.vertices.foreach_get("co", v)
    return v.reshape(-1, 3)

def normals_np(o):
    n = np.empty(len(o.data.vertices) * 3)
    o.data.vertices.foreach_get("normal", n)
    return n.reshape(-1, 3)

def tri_count(o):
    return sum(len(p.vertices) - 2 for p in o.data.polygons)

def ambient_occlusion(obj, rays=32, distance=0.3, seed=7):
    """Per-vertex occlusion 0..1 (cosine weighted hemisphere, ground plane z = 0 occludes)."""
    co, nrm = verts_np(obj), normals_np(obj)
    bvh = BVHTree.FromObject(obj, bpy.context.evaluated_depsgraph_get())
    dirs = np.random.default_rng(seed).normal(size=(rays, 3))
    dirs /= np.linalg.norm(dirs, axis=1, keepdims=True)
    occl = np.zeros(len(co))
    for i in range(len(co)):
        n = nrm[i]
        p0 = co[i] + n * 1e-3
        hit = tot = 0.0
        for d in dirs:
            c = d @ n
            if c <= 0:
                d, c = -d, -c
            tot += c
            if p0[2] + d[2] * distance < 0 or bvh.ray_cast(Vector(p0.tolist()), Vector(d.tolist()), distance)[0] is not None:
                hit += c
        occl[i] = hit / max(tot, 1e-6)
    return occl

def corner_colours(obj, vert_cols, face_layer):
    """vert_cols: list of (n_verts,3) colour layers; face_layer: per-face index into that list -> (n_loops,3)."""
    me = obj.data
    lv = np.empty(len(me.loops), dtype=np.int64)
    me.loops.foreach_get("vertex_index", lv)
    lf = np.repeat(np.arange(len(me.polygons)), [len(p.loop_indices) for p in me.polygons])
    stack = np.stack(vert_cols, 0)
    return stack[np.asarray(face_layer)[lf], lv]

def face_centres(obj):
    c = np.empty(len(obj.data.polygons) * 3)
    obj.data.polygons.foreach_get("center", c)
    n = np.empty(len(obj.data.polygons) * 3)
    obj.data.polygons.foreach_get("normal", n)
    return c.reshape(-1, 3), n.reshape(-1, 3)

def finish_colours(obj, srgb, gloss, ao_strength=0.42, ao_distance=0.3, ground_dark=0.75, ground_height=0.12, rays=32):
    """srgb: (n,3) base colours per vertex; applies AO + ground darkening, writes the Col attribute and a VC material."""
    co = verts_np(obj)
    me = obj.data
    ao = 1.0 - ao_strength * ambient_occlusion(obj, rays, ao_distance)
    ground = ground_dark + (1 - ground_dark) * np.clip(co[:, 2] / ground_height, 0, 1)
    shade = ao * ground
    gloss = np.broadcast_to(np.asarray(gloss, dtype=np.float64), (len(co),))
    domain = "POINT"
    if len(srgb) != len(co):  # per face-corner colours (sharp colour borders, e.g. moss caps, spots)
        lv = np.empty(len(me.loops), dtype=np.int64)
        me.loops.foreach_get("vertex_index", lv)
        shade, gloss, domain = shade[lv], gloss[lv], "CORNER"
    col = np.clip(srgb * shade[:, None], 0, 1)
    for a in list(me.color_attributes):
        me.color_attributes.remove(a)
    attr = me.color_attributes.new("Col", "FLOAT_COLOR", domain)
    g = gloss
    attr.data.foreach_set("color", np.hstack([srgb_to_lin(col), g[:, None]]).astype(np.float32).ravel())
    me.color_attributes.active_color = attr
    mat = bpy.data.materials.new("EnvVC")
    mat.use_nodes = True
    bsdf = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    vc = mat.node_tree.nodes.new("ShaderNodeVertexColor")
    vc.layer_name = "Col"
    mat.node_tree.links.new(vc.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.8
    me.materials.clear()
    me.materials.append(mat)
    for p in me.polygons:
        p.use_smooth = True
    return float(ao.mean()), float(ao.min())

def apply_modifier(obj, kind, **props):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    m = obj.modifiers.new(kind, kind)
    for k, v in props.items():
        setattr(m, k, v)
    bpy.ops.object.modifier_apply(modifier=m.name)

def decimate_to(obj, tris):
    cur = tri_count(obj)
    if cur > tris:
        apply_modifier(obj, "DECIMATE", ratio=tris / cur * 0.985)
    apply_modifier(obj, "TRIANGULATE")
    return cur, tri_count(obj)

def export(obj, out_dir, name, budget, tag="[env]"):
    os.makedirs(out_dir, exist_ok=True)
    obj.name = name
    obj.data.name = name
    co = verts_np(obj)
    lo, hi = co.min(0), co.max(0)
    foot = co[co[:, 2] < 0.05]
    foot_r = float(np.sqrt(foot[:, 0] ** 2 + foot[:, 1] ** 2).max()) if len(foot) else 0.0
    tris = tri_count(obj)
    base = os.path.join(out_dir, name)
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.export_scene.fbx(filepath=base + ".fbx", use_selection=True, object_types={"MESH"}, apply_scale_options="FBX_SCALE_ALL",
                             axis_forward="-Z", axis_up="Y", colors_type="SRGB", mesh_smooth_type="FACE", bake_anim=False)
    bpy.ops.export_scene.gltf(filepath=base + ".glb", export_format="GLB", use_selection=True)
    print(f"{tag} RESULT {name} tris {tris}/{budget} {'OK' if tris <= budget else 'OVER'} height {hi[2]:.2f} "
          f"size {hi[0]-lo[0]:.2f}x{hi[1]-lo[1]:.2f} footprint r {foot_r:.2f} colours yes -> {base}.fbx", flush=True)
    return tris
