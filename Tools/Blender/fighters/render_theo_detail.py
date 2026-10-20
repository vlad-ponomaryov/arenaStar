# Close-up "before / after" sheet for Theo models (Docs/Briefs/A-Theo3D.md step 2c). Headless:
#   blender -b --factory-startup --python Tools/Blender/fighters/render_theo_detail.py -- <before.glb> <after.glb> <out.png>
#       [--size 420]
# Row 1 = before, row 2 = after; columns: shoulder pad (3/4 front), right hand, crown (from above), chest plate (3/4 from
# the character's left-front), hair tips against the background (front). Same shading as render_theo_sheet.py (sun + grey
# world, texture or vertex colours, gloss = alpha -> roughness).
import bpy, sys, os, math
import numpy as np
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
S = int(argv[argv.index("--size") + 1]) if "--size" in argv else 420
pos = [a for i, a in enumerate(argv) if not a.startswith("--") and (i == 0 or argv[i - 1] != "--size")]
BEFORE, AFTER, DST = (os.path.abspath(p) for p in pos[:3])
VIEWS = [  # target, yaw (0 = front, 90 = character's left), pitch, ortho scale
    ((-0.150, 0.0, 0.640), -35, 15, 0.20),
    ((-0.258, -0.010, 0.372), -10, 5, 0.17),
    ((0.0, 0.010, 0.920), 0, 75, 0.30),
    ((0.0, -0.070, 0.600), 50, 0, 0.25),
    ((0.015, -0.050, 0.915), 0, 0, 0.21),
]


def setup(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    bpy.ops.import_scene.gltf(filepath=path)
    shapes = {pb.custom_shape.name for o in sc.objects if o.type == "ARMATURE" for pb in o.pose.bones if pb.custom_shape}
    for n in shapes:
        bpy.data.objects.remove(bpy.data.objects[n], do_unlink=True)
    for o in sc.objects:
        if o.type != "MESH":
            continue
        img = next((n.image for m in o.data.materials if m and m.node_tree for n in m.node_tree.nodes
                    if n.type == "TEX_IMAGE" and n.image), None)
        mat = bpy.data.materials.new("M")
        mat.use_nodes = True
        nt = mat.node_tree
        bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
        mul = nt.nodes.new("ShaderNodeMath")
        mul.operation, mul.inputs[1].default_value = "MULTIPLY_ADD", -0.65
        mul.inputs[2].default_value = 0.95
        if img is not None:
            img.alpha_mode = "CHANNEL_PACKED"
            src = nt.nodes.new("ShaderNodeTexImage")
            src.image = img
        else:
            src = nt.nodes.new("ShaderNodeVertexColor")
            src.layer_name = o.data.color_attributes[0].name
        nt.links.new(src.outputs["Color"], bsdf.inputs["Base Color"])
        nt.links.new(src.outputs["Alpha"], mul.inputs[0])
        nt.links.new(mul.outputs[0], bsdf.inputs["Roughness"])
        bsdf.inputs["Specular IOR Level"].default_value = 0.4
        o.data.materials.clear()
        o.data.materials.append(mat)
    sc.render.engine = "BLENDER_EEVEE"
    sc.render.resolution_x = sc.render.resolution_y = S
    sc.view_settings.view_transform = "Standard"
    w = bpy.data.worlds.new("W")
    w.use_nodes = True
    w.node_tree.nodes["Background"].inputs[0].default_value = (0.82, 0.82, 0.84, 1)
    w.node_tree.nodes["Background"].inputs[1].default_value = 0.9
    sc.world = w
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
    sun.data.energy = 2.6
    sun.data.angle = math.radians(15)
    sc.collection.objects.link(sun)
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
    cam.data.type = "ORTHO"
    sc.collection.objects.link(cam)
    sc.camera = cam
    return sc, cam, sun


def load(path):
    img = bpy.data.images.load(path, check_existing=False)
    px = np.empty(S * S * 4, np.float32)
    img.pixels.foreach_get(px)
    bpy.data.images.remove(img)
    return px.reshape(S, S, 4)


rows = []
for model in (BEFORE, AFTER):
    sc, cam, sun = setup(model)
    panels = []
    for k, (tgt, yaw, pitch, sca) in enumerate(VIEWS):
        yr, pr = math.radians(yaw), math.radians(pitch)
        t = Vector(tgt)
        dv = Vector((math.sin(yr) * math.cos(pr), -math.cos(yr) * math.cos(pr), math.sin(pr)))
        cam.location = t + dv * 3
        cam.rotation_euler = (t - cam.location).to_track_quat("-Z", "Y").to_euler()
        cam.data.ortho_scale = sca
        sun.rotation_euler = (math.radians(45), 0, yr + math.radians(30))
        fp = os.path.join(os.path.dirname(DST), f"_detail_{k}.png")
        sc.render.filepath = fp
        bpy.ops.render.render(write_still=True)
        panels.append(load(fp))
        os.remove(fp)
    gap = np.ones((S, 8, 4), np.float32)
    row = []
    for p in panels:
        row += [p, gap]
    rows.append(np.concatenate(row[:-1], 1))
sheet = np.concatenate([rows[1], np.ones((8, rows[0].shape[1], 4), np.float32), rows[0]], 0)  # bottom-up rows: after below
out = bpy.data.images.new("detail", sheet.shape[1], sheet.shape[0])
out.pixels.foreach_set(np.ascontiguousarray(sheet).ravel())
out.filepath_raw = DST
out.file_format = "PNG"
out.save()
print("DETAIL:", DST)
