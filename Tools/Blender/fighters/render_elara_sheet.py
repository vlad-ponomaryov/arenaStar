# Preview sheet for Elara (Docs/Briefs/A-Roster3D.md), copy of render_theo_sheet.py without the reference panels. Headless:
#   blender -b --factory-startup --python Tools/Blender/fighters/render_elara_sheet.py -- [model_dir] [out.png] [--size 480]
#       [--body elara_game.glb] [--gun elara_weapon.glb]
# Row: front | 3/4 | back | top | game camera (55 deg) | weapon side.
# Imports the body (default theo_game.glb) + the blaster (default theo_blaster.glb; origin snapped to the Prop bone head),
# shades the vertex colours with gloss = alpha (roughness 1 - 0.7 * alpha) or, when the body material has an image texture
# (v3: RGB = albedo, A = gloss), the texture the same way, and composes one row: reference 001 | front | 3/4 | back | side |
# game camera (55 deg from above) | blaster side next to reference 003. Nothing is written next to the models.
import bpy, sys, os, math
import numpy as np
from mathutils import Vector

here = os.path.dirname(os.path.abspath(__file__))
repo = os.path.abspath(os.path.join(here, "..", "..", ".."))
argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OPT = {k: argv[argv.index(k) + 1] for k in ("--body", "--gun") if k in argv}
pos = [a for i, a in enumerate(argv) if not a.startswith("--") and not a.isdigit() and (i == 0 or argv[i - 1] not in OPT)]
mdir = os.path.abspath(pos[0]) if pos else os.path.join(repo, "Art", "Source", "3D", "elara")
dst = os.path.abspath(pos[1]) if len(pos) > 1 else os.path.join(repo, "Tools", "out", "art", "roster3d", "elara_sheet.png")
S = int(argv[argv.index("--size") + 1]) if "--size" in argv else 480
REF = os.path.join(repo, "Art", "Source", "Flow", "2026-10-19_theo-3dref")

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
before = set(scene.objects)
bpy.ops.import_scene.gltf(filepath=os.path.join(mdir, OPT.get("--body", "elara_game.glb")))
body_objs = set(scene.objects) - before
arm = next(o for o in body_objs if o.type == "ARMATURE")
shapes = {pb.custom_shape for pb in arm.pose.bones if pb.custom_shape}
for o in shapes:
    bpy.data.objects.remove(o, do_unlink=True)
prop = arm.matrix_world @ arm.data.bones["Prop"].head_local
before = set(scene.objects)
bpy.ops.import_scene.gltf(filepath=os.path.join(mdir, OPT.get("--gun", "elara_weapon.glb")))
gun = [o for o in set(scene.objects) - before if o.parent is None]
for o in gun:
    o.location = prop

# one material: vertex colour -> base colour, alpha (gloss) -> roughness
mat = bpy.data.materials.new("VC")
mat.use_nodes = True
nt = mat.node_tree
bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
vc = nt.nodes.new("ShaderNodeVertexColor")
mul = nt.nodes.new("ShaderNodeMath")
mul.operation, mul.inputs[1].default_value = "MULTIPLY_ADD", -0.65
mul.inputs[2].default_value = 0.95
nt.links.new(vc.outputs["Color"], bsdf.inputs["Base Color"])
nt.links.new(vc.outputs["Alpha"], mul.inputs[0])
nt.links.new(mul.outputs[0], bsdf.inputs["Roughness"])
bsdf.inputs["Specular IOR Level"].default_value = 0.4
for o in scene.objects:
    if o.type == "MESH":
        img = next((n.image for m in o.data.materials if m and m.node_tree for n in m.node_tree.nodes
                    if n.type == "TEX_IMAGE" and n.image), None)
        if img is not None:  # textured (v3): albedo + gloss in alpha
            img.alpha_mode = "CHANNEL_PACKED"
            tm = bpy.data.materials.new("TEX")
            tm.use_nodes = True
            tt = tm.node_tree
            tb = next(n for n in tt.nodes if n.type == "BSDF_PRINCIPLED")
            ti = tt.nodes.new("ShaderNodeTexImage")
            ti.image = img
            tmul = tt.nodes.new("ShaderNodeMath")
            tmul.operation, tmul.inputs[1].default_value = "MULTIPLY_ADD", -0.65
            tmul.inputs[2].default_value = 0.95
            tt.links.new(ti.outputs["Color"], tb.inputs["Base Color"])
            tt.links.new(ti.outputs["Alpha"], tmul.inputs[0])
            tt.links.new(tmul.outputs[0], tb.inputs["Roughness"])
            tb.inputs["Specular IOR Level"].default_value = 0.4
            o.data.materials.clear()
            o.data.materials.append(tm)
            continue
        vc.layer_name = o.data.color_attributes[0].name
        o.data.materials.clear()
        o.data.materials.append(mat)

eng = [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items]
scene.render.engine = "BLENDER_EEVEE_NEXT" if "BLENDER_EEVEE_NEXT" in eng else "BLENDER_EEVEE"
scene.render.resolution_x = scene.render.resolution_y = S
scene.view_settings.view_transform = "Standard"
world = bpy.data.worlds.new("W")
world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (0.82, 0.82, 0.84, 1)
world.node_tree.nodes["Background"].inputs[1].default_value = 0.9
scene.world = world
sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
sun.data.energy = 2.6
sun.data.angle = math.radians(15)
scene.collection.objects.link(sun)
cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
cam.data.type = "ORTHO"
scene.collection.objects.link(cam)
scene.camera = cam


def look(yaw, pitch, target=(0, 0, 0.5), scale=1.3, dist=4.0):
    yaw_r, pitch_r = math.radians(yaw), math.radians(pitch)
    t = Vector(target)
    d = Vector((math.sin(yaw_r) * math.cos(pitch_r), -math.cos(yaw_r) * math.cos(pitch_r), math.sin(pitch_r)))
    cam.location = t + d * dist
    cam.rotation_euler = (t - cam.location).to_track_quat("-Z", "Y").to_euler()
    cam.data.ortho_scale = scale
    sun.rotation_euler = (math.radians(45), 0, yaw_r + math.radians(30))


def render(name):
    path = os.path.join(os.path.dirname(dst), f"_tmp_{name}.png")
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    return path


def load(path, w, h, crop=None):
    img = bpy.data.images.load(path, check_existing=False)
    if crop:  # (x0, y0, x1, y1) in top-left pixel coords
        W, H = img.size
        px = np.empty(W * H * 4, dtype=np.float32)
        img.pixels.foreach_get(px)
        px = px.reshape(H, W, 4)[::-1]
        x0, y0, x1, y1 = crop
        sub = np.ascontiguousarray(px[y0:y1, x0:x1][::-1])
        img2 = bpy.data.images.new("crop", x1 - x0, y1 - y0)
        img2.pixels.foreach_set(sub.ravel())
        img = img2
    img.scale(w, h)
    px = np.empty(w * h * 4, dtype=np.float32)
    img.pixels.foreach_get(px)
    return px.reshape(h, w, 4)  # bottom-up rows


os.makedirs(os.path.dirname(dst), exist_ok=True)
panels = []
tmp = []
for name, yaw, pitch in (("front", 0, 4), ("34", 40, 10), ("back", 180, 4), ("top", 0, 89), ("game", 0, 55)):
    look(yaw, pitch, target=(0, 0, 0.5), scale=1.3)
    tmp.append(render(name))
    panels.append(load(tmp[-1], S, S))
for o in scene.objects:
    if o.type == "MESH" and o not in gun:
        o.hide_render = True
look(-90, 2, target=prop + Vector((0, -0.15, 0.05)), scale=0.7)
tmp.append(render("weapon"))
panels.append(load(tmp[-1], S, S))
sheet = np.concatenate(panels, axis=1)
out = bpy.data.images.new("sheet", sheet.shape[1], sheet.shape[0])
out.pixels.foreach_set(np.ascontiguousarray(sheet).ravel())
out.filepath_raw = dst
out.file_format = "PNG"
out.save()
for p in tmp:
    os.remove(p)
print("PREVIEW:", dst, "prop", tuple(round(c, 3) for c in prop))
