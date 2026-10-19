# Preview renderer for imported character models (spike image-to-3D, Docs/Spike-ImageTo3D.md).
# Usage (no GUI):
#   blender -b --factory-startup --python Tools/Blender/render_preview.py -- <in.glb|in.fbx> <out.png> [--height 360] [--engine EEVEE|CYCLES] [--front -Y|+Y|+X|-X] [--clay]
# Renders one strip: front, three-quarter, side, back, game camera (55 deg from above) and a tiny game-scale inset.
# The model is normalised to height 1 and stood on the ground; nothing is written next to the input.
import bpy, sys, math, os
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
src, dst = os.path.abspath(argv[0]), os.path.abspath(argv[1])
height = int(argv[argv.index("--height") + 1]) if "--height" in argv else 360
engine = argv[argv.index("--engine") + 1] if "--engine" in argv else "EEVEE"
# which Blender axis the character faces after import (generators differ): -Y (default), +Y, +X, -X
front = argv[argv.index("--front") + 1] if "--front" in argv else "-Y"
front_yaw = {"-Y": 0.0, "+X": 90.0, "+Y": 180.0, "-X": -90.0}[front]

bpy.ops.wm.read_factory_settings(use_empty=True)
ext = os.path.splitext(src)[1].lower()
if ext in (".glb", ".gltf"):
    bpy.ops.import_scene.gltf(filepath=src)
elif ext == ".fbx":
    bpy.ops.import_scene.fbx(filepath=src)
else:
    bpy.ops.wm.obj_import(filepath=src)

# skip hidden helpers (the glTF importer adds a bone-shape icosphere for skinned files)
shapes = {pb.custom_shape for a in bpy.context.scene.objects if a.type == "ARMATURE" for pb in a.pose.bones if pb.custom_shape}
for o in shapes:
    bpy.data.objects.remove(o, do_unlink=True)
meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
if "--clay" in argv:  # judge the shape alone
    clay = bpy.data.materials.new("Clay")
    clay.diffuse_color = (0.75, 0.72, 0.68, 1)
    for o in meshes:
        o.data.materials.clear()
        o.data.materials.append(clay)
# Our toon shader uses albedo only: drop generated metallic/roughness maps (TRELLIS.2 PBR often reads as black metal).
for mat in bpy.data.materials:
    if not mat.use_nodes:
        continue
    for n in mat.node_tree.nodes:
        if n.type == "BSDF_PRINCIPLED":
            for sock, val in (("Metallic", 0.0), ("Roughness", 0.85)):
                for l in list(n.inputs[sock].links):
                    mat.node_tree.links.remove(l)
                n.inputs[sock].default_value = val
bpy.context.view_layer.update()
dg = bpy.context.evaluated_depsgraph_get()
pts = []
for o in meshes:  # evaluated vertices: skinned meshes report bound boxes in armature space
    ev = o.evaluated_get(dg)
    pts += [ev.matrix_world @ v.co for v in ev.to_mesh().vertices]
    ev.to_mesh_clear()
lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
size = hi - lo
root = bpy.data.objects.new("PreviewRoot", None)
bpy.context.scene.collection.objects.link(root)
for o in bpy.context.scene.objects:
    if o.parent is None and o is not root:
        o.parent = root
s = 1.0 / max(size.z, 1e-6)
root.scale = (s, s, s)
root.location = (-(lo.x + hi.x) * 0.5 * s, -(lo.y + hi.y) * 0.5 * s, -lo.z * s)

scene = bpy.context.scene
scene.render.engine = "BLENDER_EEVEE_NEXT" if engine == "EEVEE" and "BLENDER_EEVEE_NEXT" in [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items] else ("BLENDER_EEVEE" if engine == "EEVEE" else "CYCLES")
if scene.render.engine == "CYCLES":
    scene.cycles.samples = 32
    scene.cycles.device = "CPU"
scene.render.resolution_x = height
scene.render.resolution_y = height
scene.render.film_transparent = False
scene.view_settings.view_transform = "Standard"
world = bpy.data.worlds.new("W")
world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (0.13, 0.15, 0.2, 1)
world.node_tree.nodes["Background"].inputs[1].default_value = 0.9
scene.world = world
sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
sun.data.energy = 3.0
sun.rotation_euler = (math.radians(50), 0, math.radians(-35))
scene.collection.objects.link(sun)

cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
cam.data.type = "ORTHO"
cam.data.ortho_scale = 1.25
scene.collection.objects.link(cam)
scene.camera = cam

def look(yaw_deg, pitch_deg, dist=4.0):
    yaw, pitch = math.radians(yaw_deg + front_yaw), math.radians(pitch_deg)
    target = Vector((0, 0, 0.5))
    d = Vector((math.sin(yaw) * math.cos(pitch), -math.cos(yaw) * math.cos(pitch), math.sin(pitch)))
    cam.location = target + d * dist
    cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()
    sun.rotation_euler = (math.radians(50), 0, yaw + math.radians(35))

# glTF front faces -Y after import (+Z forward in glTF -> -Y in Blender)
views = [("front", 0, 5), ("34", 40, 10), ("side", 90, 5), ("back", 180, 5), ("game", 0, 55)]
tmp = []
base = os.path.splitext(dst)[0]
for name, yaw, pitch in views:
    look(yaw, pitch)
    scene.render.filepath = f"{base}__{name}.png"
    bpy.ops.render.render(write_still=True)
    tmp.append(scene.render.filepath)

# compose the strip with Blender's image API (no external Python packages)
imgs = [bpy.data.images.load(p) for p in tmp]
w = height * len(imgs)
out = bpy.data.images.new("strip", w, height)
px = [0.0] * (w * height * 4)
for k, im in enumerate(imgs):
    src_px = list(im.pixels)
    for y in range(height):
        row = y * height * 4
        px[(y * w + k * height) * 4:(y * w + k * height + height) * 4] = src_px[row:row + height * 4]
out.pixels = px
out.filepath_raw = dst
out.file_format = "PNG"
out.save()
for p in tmp:
    os.remove(p)
print("PREVIEW:", dst)
