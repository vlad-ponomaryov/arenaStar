# Previews for build_props.py output (A.4a). Imports the exported FBX files (what Unity gets), shows their vertex colours.
#   Tools/blender.ps1 -Script Tools/Blender/env/render_props.py -ScriptArgs @('Art/Source/3D/env/export','Tools/out/art/A.4a/models')
#   options: --only a,b  --size 320  --no-strips  --no-lineup
# Per prop: <out>/<name>.png strip (front, 3/4, side, back, game 55 deg), fitted to the largest dimension (adapted from
# Tools/Blender/render_preview.py). Plus <out>/lineup.png: every prop at true relative scale on sand, game camera 55 deg,
# warm sun with soft shadows. Vertex alpha >= 0.9 (lantern glass) is shown as emission.
import bpy, sys, os, math, json
import numpy as np
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
here = os.path.dirname(os.path.abspath(__file__))
repo = os.path.abspath(os.path.join(here, "..", "..", ".."))
pos = [a for i, a in enumerate(argv) if not a.startswith("--") and (i == 0 or argv[i - 1] not in ("--only", "--size"))]
src_dir = os.path.join(repo, pos[0] if pos else "Art/Source/3D/env/export")
out_dir = os.path.join(repo, pos[1] if len(pos) > 1 else "Tools/out/art/A.4a/models")
size = int(argv[argv.index("--size") + 1]) if "--size" in argv else 320
only = set(argv[argv.index("--only") + 1].split(",")) if "--only" in argv else None
os.makedirs(out_dir, exist_ok=True)
order = list(json.load(open(os.path.join(here, "props.json"), encoding="utf-8"))["props"].keys())
names = [n for n in order if os.path.exists(os.path.join(src_dir, n + ".fbx")) and (not only or n in only)]


def lin(h):
    c = [int(h.lstrip("#")[i:i + 2], 16) / 255.0 for i in (0, 2, 4)]
    return [x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c] + [1.0]


def setup_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    engines = [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items]
    sc.render.engine = "BLENDER_EEVEE_NEXT" if "BLENDER_EEVEE_NEXT" in engines else "BLENDER_EEVEE"
    sc.view_settings.view_transform = "Standard"
    w = bpy.data.worlds.new("W")
    sc.world = w
    bg = w.node_tree.nodes["Background"]
    bg.inputs[0].default_value = lin("#BFD8F0")
    bg.inputs[1].default_value = 0.9
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
    sun.data.energy = 3.2
    sun.data.color = (1.0, 0.93, 0.82)
    sun.data.angle = math.radians(10)
    sc.collection.objects.link(sun)
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
    cam.data.type = "ORTHO"
    sc.collection.objects.link(cam)
    sc.camera = cam
    return sc, sun, cam


def vc_material():
    mat = bpy.data.materials.new("PreviewVC")
    mat.use_backface_culling = True  # Unity culls back faces: flipped faces must show up as holes here
    nt = mat.node_tree
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    attr = nt.nodes.new("ShaderNodeVertexColor")
    nt.links.new(attr.outputs["Color"], bsdf.inputs["Base Color"])
    nt.links.new(attr.outputs["Color"], bsdf.inputs["Emission Color"])
    glow = nt.nodes.new("ShaderNodeMapRange")  # alpha 0.9..1 -> emission 0..2.5
    glow.inputs["From Min"].default_value, glow.inputs["From Max"].default_value = 0.9, 1.0
    glow.inputs["To Min"].default_value, glow.inputs["To Max"].default_value = 0.0, 2.5
    nt.links.new(attr.outputs["Alpha"], glow.inputs["Value"])
    nt.links.new(glow.outputs["Result"], bsdf.inputs["Emission Strength"])
    rough = nt.nodes.new("ShaderNodeMapRange")  # gloss 0..0.3 -> roughness 0.85..0.35
    rough.inputs["From Min"].default_value, rough.inputs["From Max"].default_value = 0.0, 0.3
    rough.inputs["To Min"].default_value, rough.inputs["To Max"].default_value = 0.85, 0.35
    nt.links.new(attr.outputs["Alpha"], rough.inputs["Value"])
    nt.links.new(rough.outputs["Result"], bsdf.inputs["Roughness"])
    return mat, attr


def import_prop(name, mat, attr_node):
    before = set(bpy.context.scene.objects)
    bpy.ops.import_scene.fbx(filepath=os.path.join(src_dir, name + ".fbx"), colors_type="SRGB")
    objs = [o for o in bpy.context.scene.objects if o not in before and o.type == "MESH"]
    o = objs[0]
    if not o.data.color_attributes:
        print("[render] WARNING no vertex colours in", name)
    else:
        attr_node.layer_name = o.data.color_attributes[0].name
    o.data.materials.clear()
    o.data.materials.append(mat)
    bpy.context.view_layer.update()
    return o


def bounds(o):
    pts = [o.matrix_world @ v.co for v in o.data.vertices]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    return lo, hi


def look(cam, sun, target, yaw_deg, pitch_deg, dist=8.0):
    yaw, pitch = math.radians(yaw_deg), math.radians(pitch_deg)
    d = Vector((math.sin(yaw) * math.cos(pitch), -math.cos(yaw) * math.cos(pitch), math.sin(pitch)))
    cam.location = target + d * dist
    cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()
    sun.rotation_euler = (math.radians(45), 0, yaw + math.radians(35))


def render(sc, path, w, h):
    sc.render.resolution_x, sc.render.resolution_y = w, h
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)
    im = bpy.data.images.load(path)
    px = np.array(im.pixels[:], dtype=np.float32).reshape(h, w, 4)
    bpy.data.images.remove(im)
    return px


def save(px, path):
    h, w = px.shape[:2]
    im = bpy.data.images.new("out", w, h)
    im.pixels.foreach_set(px.ravel())
    im.filepath_raw = path
    im.file_format = "PNG"
    im.save()
    bpy.data.images.remove(im)


if "--no-strips" not in argv:
    for name in names:
        sc, sun, cam = setup_scene()
        mat, attr = vc_material()
        o = import_prop(name, mat, attr)
        lo, hi = bounds(o)
        ext = hi - lo
        cam.data.ortho_scale = max(ext.x, ext.y, ext.z) * 1.25
        target = (lo + hi) * 0.5
        tiles = []
        for i, (yaw, pitch) in enumerate([(0, 5), (40, 25), (90, 5), (180, 5), (0, 55)]):
            look(cam, sun, target, yaw, pitch)
            tmp = os.path.join(out_dir, f"_{name}_{i}.png")
            tiles.append(render(sc, tmp, size, size))
            os.remove(tmp)
        save(np.concatenate(tiles, axis=1), os.path.join(out_dir, name + ".png"))
        print("PREVIEW:", os.path.join(out_dir, name + ".png"), flush=True)

if "--no-lineup" not in argv and names:
    sc, sun, cam = setup_scene()
    mat, attr = vc_material()
    # Cycles for the lineup: EEVEE's soft sun shadows detach from the contact points at this scale
    sc.render.engine = "CYCLES"
    sc.cycles.device = "CPU"
    sc.cycles.samples = 48
    sc.cycles.use_denoising = True
    sc.world.node_tree.nodes["Background"].inputs[1].default_value = 0.55
    sun.data.energy = 3.0
    sun.data.angle = math.radians(6)
    ground = bpy.data.meshes.new("Ground")
    ground.from_pydata([(-6, -4, 0), (6, -4, 0), (6, 6, 0), (-6, 6, 0)], [], [(0, 1, 2, 3)])
    gm = bpy.data.materials.new("Sand")
    gb = next(n for n in gm.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    gb.inputs["Base Color"].default_value = lin("#FFD688")
    gb.inputs["Roughness"].default_value = 0.95
    ground.materials.append(gm)
    sc.collection.objects.link(bpy.data.objects.new("Ground", ground))
    # two rows: frame pieces behind, small things in front; spacing by footprint
    rows = [[n for n in names if n.startswith(("fence", "lantern", "rock"))], [n for n in names if not n.startswith(("fence", "lantern", "rock"))]]
    y = 0.0
    xmax = 0.0
    for row in rows:
        objs = [import_prop(n, mat, attr) for n in row]
        widths = [bounds(o)[1].x - bounds(o)[0].x for o in objs]
        x = 0.0
        for o, wd in zip(objs, widths):
            lo, hi = bounds(o)
            o.location.x += x - lo.x
            o.location.y += y
            x += wd + 0.25
        xmax = max(xmax, x)
        y -= 1.1
    for o in sc.objects:
        if o.type == "MESH" and o.name != "Ground":
            o.location.x -= xmax / 2
    target = Vector((0, -0.3, 0.35))
    cam.data.ortho_scale = xmax * 1.02
    look(cam, sun, target, 0, 55, dist=20)
    sun.rotation_euler = (math.radians(50), 0, math.radians(-30))
    path = os.path.join(out_dir, "lineup.png")
    save(render(sc, path, 1600, 600), path)
    print("PREVIEW:", path, flush=True)
