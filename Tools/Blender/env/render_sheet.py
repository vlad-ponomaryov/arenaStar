# Overview sheet of all A.4a environment models at true scale on sand, from the game camera (55 deg, long lens). Headless:
#   Tools/blender.ps1 -Script Tools/Blender/env/render_sheet.py -ScriptArgs @('Tools/out/art/A.4a/models/sheet.png')
# Layout comes from sheet.json (name -> cell position, yaw); models missing from export/ are skipped.
import bpy, sys, os, json, math
from mathutils import Vector

here = os.path.dirname(os.path.abspath(__file__))
repo = os.path.abspath(os.path.join(here, "..", "..", ".."))
argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
dst = os.path.abspath(argv[0]) if argv else os.path.join(repo, "Tools", "out", "art", "A.4a", "models", "sheet.png")
lay = json.load(open(os.path.join(here, "sheet.json"), encoding="utf-8"))
exp = os.path.join(repo, "Art", "Source", "3D", "env", "export")

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
placed = []
for name, (x, y, yaw) in lay["items"].items():
    path = os.path.join(exp, name + ".glb")
    if not os.path.exists(path):
        continue
    before = set(bpy.context.scene.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    for o in set(bpy.context.scene.objects) - before:
        if o.parent is None:
            o.location = (x, y, 0)
            o.rotation_euler = (0, 0, math.radians(yaw))
    placed.append(name)
for mat in bpy.data.materials:
    if mat.use_nodes:
        for n in mat.node_tree.nodes:
            if n.type == "BSDF_PRINCIPLED":
                n.inputs["Roughness"].default_value = 0.75
                n.inputs["Metallic"].default_value = 0.0

w, h = lay["ground"]
bpy.ops.mesh.primitive_plane_add(size=1, location=(lay["center"][0], lay["center"][1], 0))
g = bpy.context.active_object
g.scale = (w, h, 1)
gm = bpy.data.materials.new("Sand")
gm.use_nodes = True
b = gm.node_tree.nodes["Principled BSDF"]
b.inputs["Base Color"].default_value = (0.93, 0.64, 0.25, 1)  # ~#F9D28A in linear
b.inputs["Roughness"].default_value = 1.0
g.data.materials.append(gm)

scene.render.engine = "BLENDER_EEVEE_NEXT" if "BLENDER_EEVEE_NEXT" in [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items] else "BLENDER_EEVEE"
scene.render.resolution_x, scene.render.resolution_y = lay["resolution"]
scene.view_settings.view_transform = "Standard"
world = bpy.data.worlds.new("W")
world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (0.75, 0.82, 0.95, 1)
world.node_tree.nodes["Background"].inputs[1].default_value = 0.55
scene.world = world
sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
sun.data.energy = 3.2
sun.data.color = (1.0, 0.95, 0.85)
sun.data.angle = math.radians(12)  # soft shadows
sun.rotation_euler = (math.radians(40), math.radians(-18), math.radians(-30))
scene.collection.objects.link(sun)
cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
cam.data.lens_unit = "FOV"
cam.data.sensor_fit = "VERTICAL"
cam.data.angle = math.radians(35 if lay.get("fovDeg") is None else lay["fovDeg"])
target = Vector((lay["center"][0], lay["center"][1], 0.4))
pitch = math.radians(55)
dist = lay["distance"]
cam.location = target + Vector((0, -math.cos(pitch) * dist, math.sin(pitch) * dist))
cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()
cam.data.clip_end = 200
scene.collection.objects.link(cam)
scene.camera = cam
os.makedirs(os.path.dirname(dst), exist_ok=True)
scene.render.filepath = dst
bpy.ops.render.render(write_still=True)
print("PREVIEW:", dst, "models", len(placed), ",".join(placed))
