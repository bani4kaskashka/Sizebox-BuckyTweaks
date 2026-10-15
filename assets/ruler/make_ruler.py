"""Builds the Height Ruler mesh and exports HeightRuler.fbx (plus the .blend) next to this file.

Run headless:
  "C:\\Program Files\\Blender Foundation\\Blender 5.2\\blender.exe" -b --python make_ruler.py

The ruler is a 2.0 m surveyor post modelled in metres at 1:1 (Sizebox objects imported with
Scale Factor 1 are micro-sized, 1 Unity unit = 1 m on the default maps), Y up, origin at the
bottom centre so it stands on the ground where it is placed. Alternating red/white 10 cm bands,
black ticks every 10 cm on all four faces (longer at 50 cm, full width at 1 m), and label flags
with "0.5 m", "1 m", "1.5 m", "2 m" as real text geometry, readable from the front and the back.

Materials are plain Principled BSDF colours with no textures, so the FBX carries them as flat
diffuse colours and the Sizebox SDK (Unity, Standard shader) picks them up as albedo. Drop the
.fbx into SizeboxSdk/Assets/Import/Object/HeightRuler/ and use "Sizebox SDK > Export Model".

Blender is Z up, -Y front. The FBX exporter is told forward=-Z, up=Y with the transform baked,
which lands heights on Unity's Y and Blender's front (-Y) on Unity's +Z.
"""
import math
import os
import bmesh
import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
FBX_PATH = os.path.join(HERE, "HeightRuler.fbx")
BLEND_PATH = os.path.join(HERE, "height_ruler.blend")

HEIGHT = 2.0          # m
WIDTH = 0.08          # post is 8 cm square
BAND = 0.10           # red/white band height
BASE_SIZE = 0.24      # base plate side
BASE_THICK = 0.012
TICK_DEPTH = 0.003    # how far ticks stand out from the face

# Label flags hang off the +X side of the post at every 50 cm.
LABELS = [(0.5, "0.5 m"), (1.0, "1 m"), (1.5, "1.5 m"), (2.0, "2 m")]
FLAG_W = 0.30
FLAG_H = 0.12
FLAG_T = 0.012        # flag thickness (Y)
TEXT_SIZE = 0.075     # Blender font size; cap height is about 0.7 of it
TEXT_EXTRUDE = 0.0015

# material slot index -> (name, colour)
MATERIALS = [
    ("Red", (0.80, 0.08, 0.06, 1.0)),
    ("White", (0.95, 0.95, 0.95, 1.0)),
    ("Black", (0.03, 0.03, 0.03, 1.0)),
    ("Base", (0.25, 0.25, 0.27, 1.0)),
]
RED, WHITE, BLACK, BASE = range(4)


def add_box(bm, center, size, mat, skip=()):
    """Axis-aligned box. `skip` lists faces to leave out (hidden interior caps between
    stacked bands, or the face glued to the post). Blender coordinates: X right, Y back, Z up."""
    cx, cy, cz = center
    sx, sy, sz = size[0] / 2.0, size[1] / 2.0, size[2] / 2.0
    v = [bm.verts.new((cx + dx * sx, cy + dy * sy, cz + dz * sz))
         for dz in (-1, 1) for dy in (-1, 1) for dx in (-1, 1)]
    # index = dx_bit + 2*dy_bit + 4*dz_bit, bit 0 = negative side
    faces = {
        "bottom": (0, 1, 3, 2),
        "top": (4, 6, 7, 5),
        "front": (0, 4, 5, 1),   # -Y
        "back": (2, 3, 7, 6),    # +Y
        "left": (0, 2, 6, 4),    # -X
        "right": (1, 5, 7, 3),   # +X
    }
    for name, idx in faces.items():
        if name in skip:
            continue
        f = bm.faces.new([v[i] for i in idx])
        f.material_index = mat


def add_text(bm, body, matrix, mat, align):
    """Converts a text object to mesh, applies `matrix` and merges it into `bm`."""
    curve = bpy.data.curves.new("tmp_text", type="FONT")
    # Blender's built-in Bfont draws "1" like an "I"; use Arial when Windows has it.
    arial = r"C:\Windows\Fonts\arialbd.ttf"
    if os.path.exists(arial):
        curve.font = bpy.data.fonts.get("Arial Bold") or bpy.data.fonts.load(arial)
    curve.body = body
    curve.size = TEXT_SIZE
    curve.extrude = TEXT_EXTRUDE
    curve.fill_mode = "BOTH"
    curve.align_x = align
    curve.align_y = "CENTER"
    curve.resolution_u = 4
    obj = bpy.data.objects.new("tmp_text", curve)
    bpy.context.scene.collection.objects.link(obj)
    depsgraph = bpy.context.evaluated_depsgraph_get()
    mesh = bpy.data.meshes.new_from_object(obj.evaluated_get(depsgraph))
    mesh.transform(matrix)
    first = len(bm.faces)
    bm.from_mesh(mesh)
    bm.faces.ensure_lookup_table()
    for f in bm.faces[first:]:
        f.material_index = mat
    bpy.data.objects.remove(obj)
    bpy.data.meshes.remove(mesh)
    bpy.data.curves.remove(curve)


def build():
    bm = bmesh.new()

    # Base plate so the post stands; it also hides the bottom of the first band.
    add_box(bm, (0, 0, BASE_THICK / 2.0), (BASE_SIZE, BASE_SIZE, BASE_THICK), BASE)

    # Alternating 10 cm bands, red first so every full metre starts red.
    bands = int(round(HEIGHT / BAND))
    for i in range(bands):
        z0 = i * BAND
        skip = ["bottom"]
        if i < bands - 1:
            skip.append("top")
        add_box(bm, (0, 0, z0 + BAND / 2.0), (WIDTH, BAND, WIDTH),
                RED if i % 2 == 0 else WHITE, skip=skip)

    # Tick marks on all four faces: every 10 cm, longer at 50 cm, full width at 1 m.
    half = WIDTH / 2.0
    for i in range(1, bands + 1):
        h = i * BAND
        if i % 10 == 0:
            length, thick = WIDTH, 0.012
        elif i % 5 == 0:
            length, thick = 0.05, 0.009
        else:
            length, thick = 0.024, 0.006
        zc = min(h, HEIGHT - thick / 2.0)   # keep the top tick inside the post
        d = TICK_DEPTH
        add_box(bm, (0, -half - d / 2.0, zc), (length, d, thick), BLACK, skip=("back",))
        add_box(bm, (0, half + d / 2.0, zc), (length, d, thick), BLACK, skip=("front",))
        add_box(bm, (-half - d / 2.0, 0, zc), (d, length, thick), BLACK, skip=("right",))
        add_box(bm, (half + d / 2.0, 0, zc), (d, length, thick), BLACK, skip=("left",))

    # Label flags: a black plate on the +X side with white text on the front and the back.
    for h, body in LABELS:
        zc = min(h, HEIGHT - FLAG_H / 2.0)  # the top flag's upper edge sits at the top of the post
        x0 = half + FLAG_W / 2.0
        add_box(bm, (x0, 0, zc), (FLAG_W, FLAG_T, FLAG_H), BLACK, skip=("left",))
        text_x = half + 0.02
        y_face = FLAG_T / 2.0 + TEXT_EXTRUDE
        # Text is built in the XY plane facing +Z; rotate it upright to face -Y (front),
        # left-aligned so it runs from the flag's inner edge outwards.
        front = Matrix.Translation((text_x, -y_face, zc)) @ Matrix.Rotation(math.pi / 2, 4, "X")
        add_text(bm, body, front, WHITE, "LEFT")
        # The back copy is spun around Z to face +Y, which also mirrors its run direction,
        # so right-align it: it then ends at the inner edge and extends outwards too.
        back = Matrix.Translation((text_x, y_face, zc)) @ Matrix.Rotation(math.pi, 4, "Z") @ Matrix.Rotation(math.pi / 2, 4, "X")
        add_text(bm, body, back, WHITE, "RIGHT")

    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bmesh.ops.triangulate(bm, faces=bm.faces)
    mesh = bpy.data.meshes.new("HeightRuler")
    bm.to_mesh(mesh)
    bm.free()

    for name, colour in MATERIALS:
        m = bpy.data.materials.new(name)
        m.diffuse_color = colour
        m.use_nodes = True
        bsdf = m.node_tree.nodes.get("Principled BSDF")
        if bsdf:
            bsdf.inputs["Base Color"].default_value = colour
            bsdf.inputs["Roughness"].default_value = 0.6
        mesh.materials.append(m)

    obj = bpy.data.objects.new("HeightRuler", mesh)
    bpy.context.scene.collection.objects.link(obj)
    for p in mesh.polygons:
        p.use_smooth = False   # hard edges on a boxy object
    return obj


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    obj = build()
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj

    bpy.ops.export_scene.fbx(
        filepath=FBX_PATH,
        use_selection=True,
        object_types={"MESH"},
        axis_forward="-Z",
        axis_up="Y",
        global_scale=1.0,
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_ALL",
        use_space_transform=True,
        bake_space_transform=True,
        use_mesh_modifiers=True,
        mesh_smooth_type="OFF",
        use_triangles=True,
        use_tspace=False,
        colors_type="NONE",
        add_leaf_bones=False,
        bake_anim=False,
        path_mode="AUTO",
        embed_textures=False,
    )
    bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH, compress=True)

    me = obj.data
    xs = [v.co.x for v in me.vertices]
    ys = [v.co.y for v in me.vertices]
    zs = [v.co.z for v in me.vertices]
    print(f"Height ruler: {len(me.vertices)} verts, {len(me.polygons)} tris, "
          f"x {min(xs):.3f}..{max(xs):.3f} y {min(ys):.3f}..{max(ys):.3f} z {min(zs):.3f}..{max(zs):.3f} -> {FBX_PATH}")


if __name__ == "__main__":
    main()
