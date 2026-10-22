# Builds The Hollow's body parts in Blender and exports them for Unity.
#
#   blender --background --python Tools/blender/build_hollow.py -- <out.fbx>
#   (or exec() it from a live Blender session; see OUT below)
#
# Every part is modelled in the local space of the HollowVisual transform it
# is parented to in Unity, so the existing procedural animation (pose blend,
# glitches, cloth waves) drives the new meshes unchanged:
#
#   part           Unity parent   origin
#   Hollow_Head    Head           head pivot (top of neck)
#   Hollow_Hood    Head           head pivot
#   Hollow_Neck    Neck           neck pivot (base of neck)
#   Hollow_Torso   Chest          chest pivot, 1.45 m above the floor
#   Hollow_Robe    Body           floor, centre of the figure
#   Hollow_UpperArm Arm (shoulder) shoulder joint, hangs along -Y
#   Hollow_Forearm Fore           elbow joint, includes the hand and fingers
#
# Coordinates in this file are written in Unity's convention (x right, y up,
# z forward); U() converts to Blender (the figure faces -Y in Blender). The FBX
# is exported with axis conversion baked in, so Unity sees identity rotations.
#
# Organic forms are metaball fields (positive and negative ellipsoids)
# converted to meshes, given surface relief with a displacement texture,
# decimated to a game budget and UV-unwrapped. The robe and hood are built
# directly with bmesh: torn strips and holes come from a seeded generator, so
# the output is the same on every run.

import math
import os
import sys

import bmesh
import bpy
from mathutils import Vector

K = 0.575  # visible radius / influence radius (stiffness 2, threshold 0.6)


def U(x, y, z):
    return Vector((-x, -z, y))


def clear():
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    for coll in (bpy.data.meshes, bpy.data.metaballs, bpy.data.textures, bpy.data.materials):
        for item in list(coll):
            coll.remove(item)


def activate(ob):
    bpy.ops.object.select_all(action='DESELECT')
    bpy.context.view_layer.objects.active = ob
    ob.select_set(True)


def blob(name, elems, res=0.004):
    """elems: (unity_centre, unity_semi_axes, negative, stiffness)."""
    mb = bpy.data.metaballs.new(name)
    mb.resolution = res
    mb.render_resolution = res
    mb.threshold = 0.6
    ob = bpy.data.objects.new(name, mb)
    bpy.context.scene.collection.objects.link(ob)
    for pos, semi, neg, stiff in elems:
        m = max(semi)
        el = mb.elements.new(type='ELLIPSOID')
        el.co = U(*pos)
        el.radius = m / K
        el.stiffness = stiff
        el.use_negative = neg
        sx, sy, sz = semi
        el.size_x, el.size_y, el.size_z = sx / m, sz / m, sy / m
    activate(ob)
    bpy.ops.object.convert(target='MESH')
    out = bpy.context.active_object
    out.name = name
    out.data.name = name
    return out


def segment(a, b, ra, rb, steps, neg=False, stiff=2.0):
    """Metaball chain from a to b (a limb or bone), radii lerped. The step
    count is raised until neighbours overlap enough to fuse smoothly."""
    length = math.dist(a, b)
    steps = max(steps, int(math.ceil(length / (min(ra, rb) * 0.45))))
    out = []
    for i in range(steps + 1):
        t = i / steps
        p = tuple(a[k] + (b[k] - a[k]) * t for k in range(3))
        r = ra + (rb - ra) * t
        out.append((p, (r, r, r), neg, stiff))
    return out


def relief(ob, strength, scale, kind='VORONOI'):
    tex = bpy.data.textures.new(ob.name + "_Relief", type=kind)
    if kind == 'VORONOI':
        tex.distance_metric = 'DISTANCE'
        tex.noise_scale = scale
    else:
        tex.noise_scale = scale
    mod = ob.modifiers.new("Relief", 'DISPLACE')
    mod.texture = tex
    mod.texture_coords = 'OBJECT'
    mod.strength = strength
    mod.mid_level = 0.5
    activate(ob)
    bpy.ops.object.modifier_apply(modifier=mod.name)


def finish(ob, target_tris, smooth_iters=2):
    activate(ob)
    if smooth_iters:
        m = ob.modifiers.new("Smooth", 'SMOOTH')
        m.iterations = smooth_iters
        m.factor = 0.5
        bpy.ops.object.modifier_apply(modifier=m.name)
    tris = sum(len(p.vertices) - 2 for p in ob.data.polygons)
    if tris > target_tris:
        d = ob.modifiers.new("Decimate", 'DECIMATE')
        d.ratio = target_tris / tris
        bpy.ops.object.modifier_apply(modifier=d.name)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.01)
    bpy.ops.object.mode_set(mode='OBJECT')
    bpy.ops.object.shade_smooth()
    return ob


def rand_gen(seed):
    state = [seed & 0xFFFFFFFF or 1]

    def r():
        s = state[0]
        s ^= (s << 13) & 0xFFFFFFFF
        s ^= s >> 17
        s ^= (s << 5) & 0xFFFFFFFF
        state[0] = s
        return (s & 0xFFFFFF) / 16777215.0
    return r


P, N = False, True

# ------------------------------------------------------------------ head


def build_head():
    head = blob("Hollow_Head", [
        ((0, 0.10, -0.03), (0.08, 0.135, 0.105), P, 2.0),       # cranium
        ((0, 0.18, -0.085), (0.065, 0.09, 0.08), P, 2.0),       # swept-back occiput
        ((0, 0.058, 0.058), (0.082, 0.03, 0.048), P, 2.0),      # brow ridge
        ((-0.05, -0.005, 0.052), (0.028, 0.024, 0.034), P, 2.0),  # cheekbones
        ((0.05, -0.005, 0.052), (0.028, 0.024, 0.034), P, 2.0),
        ((0, -0.02, 0.04), (0.045, 0.04, 0.055), P, 2.0),       # maxilla
        ((-0.036, -0.15, 0.035), (0.022, 0.09, 0.03), P, 2.2),  # unhinged mandible
        ((0.036, -0.15, 0.035), (0.022, 0.09, 0.03), P, 2.2),
        ((0, -0.235, 0.05), (0.04, 0.026, 0.035), P, 2.0),      # chin
        ((-0.038, 0.035, 0.098), (0.028, 0.03, 0.035), N, 2.2),  # deep sockets
        ((0.038, 0.035, 0.098), (0.028, 0.03, 0.035), N, 2.2),
        ((-0.078, 0.07, 0.0), (0.02, 0.05, 0.04), N, 1.5),      # sunken temples
        ((0.078, 0.07, 0.0), (0.02, 0.05, 0.04), N, 1.5),
        ((0, -0.13, 0.075), (0.028, 0.1, 0.06), N, 2.2),        # gaping mouth
        ((0, -0.012, 0.088), (0.012, 0.018, 0.02), N, 2.0),     # nasal cavity
    ], res=0.0035)
    relief(head, 0.004, 0.02)
    return finish(head, 2600)


def build_hood():
    """Tattered cowl over the back and top of the skull, open at the face."""
    r = rand_gen(777)
    bm = bmesh.new()
    rings, segs = 12, 26
    centre = (0.0, 0.09, -0.035)
    crown = bm.verts.new(U(centre[0], centre[1] + 0.205, centre[2] - 0.01))
    verts = []
    for i in range(1, rings + 1):
        v_t = i / rings                        # 0 crown .. 1 drape edge
        theta = math.radians(8 + 112 * v_t)    # polar angle from the crown
        # the face opening widens from the peak of the cowl downward
        gap = math.radians(8 + 40 * min(1.0, v_t * 1.6))
        row = []
        for j in range(segs + 1):
            phi = gap + (math.tau - 2 * gap) * j / segs   # 0 = straight ahead (+z)
            rad = 0.142 + 0.02 * v_t
            x = math.sin(theta) * math.sin(phi) * rad
            y = math.cos(theta) * rad * 1.45
            z = math.sin(theta) * math.cos(phi) * rad * 1.08
            if theta > math.pi / 2:            # drape falls toward the shoulders
                y -= (theta - math.pi / 2) * 0.25
                x *= 1.15
            if i == rings:
                y -= 0.01 + r() * 0.08         # ragged edge
            if i > 1:
                x *= 1 + 0.05 * (r() - 0.5)
                z *= 1 + 0.05 * (r() - 0.5)
            row.append(bm.verts.new(U(centre[0] + x, centre[1] + y, centre[2] + z)))
        verts.append(row)
    for j in range(segs):
        bm.faces.new((crown, verts[0][j], verts[0][j + 1]))
    for i in range(rings - 1):
        for j in range(segs):
            if i >= rings - 3 and r() < 0.16:
                continue  # tears in the drape
            bm.faces.new((verts[i][j], verts[i + 1][j], verts[i + 1][j + 1], verts[i][j + 1]))
    me = bpy.data.meshes.new("Hollow_Hood")
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new("Hollow_Hood", me)
    bpy.context.scene.collection.objects.link(ob)
    activate(ob)
    sub = ob.modifiers.new("Sub", 'SUBSURF')
    sub.levels = 1
    bpy.ops.object.modifier_apply(modifier=sub.name)
    relief(ob, 0.012, 0.05, 'CLOUDS')
    sol = ob.modifiers.new("Solid", 'SOLIDIFY')
    sol.thickness = 0.006
    bpy.ops.object.modifier_apply(modifier=sol.name)
    return finish(ob, 2200, smooth_iters=0)


def build_neck():
    # long enough to bury its root in the shoulders: the gap between the
    # collar and the jaw reads as a stretched, sinewy neck
    elems = segment((0, -0.16, -0.03), (0, 0.29, 0.03), 0.034, 0.026, 7)
    # vertebrae along the back of the neck
    for i in range(8):
        t = i / 7
        elems.append(((0, -0.12 + 0.38 * t, -0.055 + 0.05 * t), (0.017, 0.015, 0.017), P, 2.0))
    # sinew cords at the front
    elems += segment((-0.024, -0.1, 0.0), (-0.012, 0.27, 0.05), 0.011, 0.009, 6)
    elems += segment((0.024, -0.1, 0.0), (0.012, 0.27, 0.05), 0.011, 0.009, 6)
    neck = blob("Hollow_Neck", elems, res=0.004)
    relief(neck, 0.003, 0.015)
    return finish(neck, 1100)


# ----------------------------------------------------------------- torso


def build_torso():
    e = []
    e.append(((0, -0.06, 0.0), (0.15, 0.2, 0.1), P, 2.0))       # ribcage
    e.append(((0, 0.08, -0.01), (0.17, 0.07, 0.085), P, 2.0))   # upper chest
    e.append(((0, 0.17, -0.03), (0.1, 0.06, 0.06), P, 1.8))     # trapezius, rising to the neck
    for s in (-1, 1):
        e.append(((0.2 * s, 0.12, -0.005), (0.045, 0.04, 0.045), P, 2.0))   # shoulder knobs
        e += segment((0.02 * s, 0.15, 0.06), (0.18 * s, 0.13, 0.02), 0.014, 0.012, 5)  # clavicles
        e.append(((0.08 * s, 0.02, -0.085), (0.06, 0.09, 0.02), P, 2.0))  # shoulder blades
        for k in range(5):  # intercostal grooves carve the ribs
            y = 0.03 - k * 0.055
            e.append(((0.085 * s, y, 0.075 - k * 0.006), (0.065, 0.011, 0.04), N, 2.2))
    e.append(((0, -0.33, 0.0), (0.085, 0.1, 0.07), P, 2.0))     # wasted belly
    e.append(((0, -0.27, 0.075), (0.09, 0.07, 0.035), N, 1.6))  # sunken stomach
    for i in range(10):  # spine ridge
        y = 0.14 - i * 0.055
        e.append(((0, y, -0.085 - 0.012 * math.sin(i * 0.4)), (0.016, 0.017, 0.016), P, 2.0))
    e.append(((0, -0.06, 0.1), (0.012, 0.16, 0.012), P, 2.0))   # sternum
    torso = blob("Hollow_Torso", e, res=0.006)
    relief(torso, 0.005, 0.03)
    return finish(torso, 3200)


# ------------------------------------------------------------------ arms


def build_upper_arm():
    e = segment((0, 0.0, 0.0), (0, -0.5, 0.0), 0.032, 0.022, 10)
    e.append(((0, -0.52, -0.01), (0.03, 0.034, 0.032), P, 2.0))  # knobbed elbow
    e += segment((0.012, -0.08, 0.012), (0.006, -0.38, 0.01), 0.014, 0.008, 5)  # wasted bicep
    arm = blob("Hollow_UpperArm", e, res=0.004)
    relief(arm, 0.003, 0.02)
    return finish(arm, 900)


def build_forearm():
    e = segment((0, 0.0, 0.0), (0, -0.5, 0.0), 0.024, 0.016, 10)
    e += segment((0.01, -0.03, 0.006), (0.008, -0.46, 0.004), 0.011, 0.008, 6)  # second bone
    e.append(((0, -0.52, 0.0), (0.026, 0.02, 0.014), P, 2.0))   # wrist
    e.append(((0, -0.58, 0.01), (0.04, 0.05, 0.013), P, 2.0))   # narrow palm
    # four overlong fingers, slightly curled toward the palm, plus a thumb
    for f in range(4):
        x = (f - 1.5) * 0.019
        length = 0.2 + (0.025 if f in (1, 2) else 0.0)
        base = (x, -0.62, 0.012)
        mid = (x * 1.25, -0.62 - length * 0.55, 0.03)
        tip = (x * 1.35, -0.62 - length, 0.06)
        e += segment(base, mid, 0.0085, 0.007, 6)
        e += segment(mid, tip, 0.007, 0.0035, 6)
        e.append((base, (0.0105, 0.0105, 0.0105), P, 2.0))      # knuckles
        e.append((mid, (0.0085, 0.0085, 0.0085), P, 2.0))
    e += segment((-0.04, -0.55, 0.02), (-0.06, -0.66, 0.05), 0.009, 0.004, 6)
    fore = blob("Hollow_Forearm", e, res=0.003)
    relief(fore, 0.002, 0.012)
    return finish(fore, 2200)


# ------------------------------------------------------------------ robe


def build_robe():
    """A torn burial shroud draped from the shoulders to the floor.

    Rings from the collar (y 1.62) to the hem (y 0); the lower third is cut
    into hanging strips of different lengths, and a few holes show the body.
    Openings at the shoulders let the bare arms through."""
    r = rand_gen(4242)
    bm = bmesh.new()
    rings, segs = 28, 48
    hem_len = [0.0] * segs
    for j in range(segs):
        hem_len[j] = r() * 0.42
    grid = []
    for i in range(rings):
        k = i / (rings - 1)
        y = 1.62 - k * 1.62
        # collar -> shoulders -> chest -> a narrow, gaunt skirt
        if y > 1.5:
            rad = 0.13 + (1.62 - y) / 0.12 * 0.1
        else:
            rad = 0.23 + (1.5 - y) / 1.5 * 0.15
        row = []
        for j in range(segs):
            a = j / segs * math.tau
            fold = 1 + 0.07 * math.sin(a * 7 + k * 3) * k + 0.04 * math.sin(a * 13) * k
            x = math.sin(a) * rad * fold
            z = math.cos(a) * rad * 0.82 * fold
            yy = y
            if i == rings - 1:
                yy = hem_len[j] * 0.4
            elif k > 0.66:
                # strips: each column frays upward by its own amount
                yy = max(y, hem_len[j] * (k - 0.66) / 0.34 * 0.9)
            row.append(bm.verts.new(U(x, yy, z)))
        grid.append(row)

    def is_opening(i, j):
        a = j / segs * 360.0                  # 0 = front (+z), 90 = +x
        y = 1.62 - i / (rings - 1) * 1.62
        arm = 1.36 < y < 1.56 and (abs(a - 90) < 20 or abs(a - 270) < 20)
        # the shroud is torn open over the chest: a ragged gash that shows
        # the ribcage, narrowing toward the belly
        width = 34.0 * max(0.0, min(1.0, (y - 1.02) / 0.3))
        front = min(a, 360.0 - a)
        chest = 1.02 < y < 1.5 and front < width + 6.0 * math.sin(i * 2.3)
        return arm or chest

    for i in range(rings - 1):
        k = i / (rings - 1)
        for j in range(segs):
            jn = (j + 1) % segs
            if is_opening(i, j):
                continue
            # vertical tears between hanging strips
            if k > 0.62 and j % 3 == 0 and r() < 0.75:
                continue
            # moth-eaten holes in the body of the shroud
            if 0.3 < k < 0.62 and r() < 0.04:
                continue
            bm.faces.new((grid[i][j], grid[i + 1][j], grid[i + 1][jn], grid[i][jn]))
    loose = [v for v in bm.verts if not v.link_faces]
    bmesh.ops.delete(bm, geom=loose, context='VERTS')
    me = bpy.data.meshes.new("Hollow_Robe")
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new("Hollow_Robe", me)
    bpy.context.scene.collection.objects.link(ob)
    activate(ob)
    sub = ob.modifiers.new("Sub", 'SUBSURF')
    sub.levels = 1
    bpy.ops.object.modifier_apply(modifier=sub.name)
    relief(ob, 0.02, 0.12, 'CLOUDS')
    sol = ob.modifiers.new("Solid", 'SOLIDIFY')
    sol.thickness = 0.008
    bpy.ops.object.modifier_apply(modifier=sol.name)
    return finish(ob, 5200, smooth_iters=1)


# --------------------------------------------------------------- preview


def unity_rotation(ex, ey, ez):
    """Quaternion.Euler(ex, ey, ez) expressed in Blender space."""
    from mathutils import Quaternion

    def axis_angle(ax, deg):
        # U() is a reflection, so a left-handed Unity angle is a negated
        # right-handed angle about the mapped axis.
        return Quaternion(U(*ax), -math.radians(deg))
    return axis_angle((0, 1, 0), ey) @ axis_angle((1, 0, 0), ex) @ axis_angle((0, 0, 1), ez)


def assemble_preview():
    """Places linked copies in the same hierarchy HollowVisual builds, in a
    resting 'Watch' pose, so the silhouette can be judged in Blender."""
    from mathutils import Matrix
    coll = bpy.data.collections.get("Preview") or bpy.data.collections.new("Preview")
    if coll.name not in bpy.context.scene.collection.children:
        bpy.context.scene.collection.children.link(coll)
    for o in list(coll.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    src = {n: bpy.data.objects[n] for n in ("Hollow_Head", "Hollow_Hood", "Hollow_Neck", "Hollow_Torso",
                                             "Hollow_Robe", "Hollow_UpperArm", "Hollow_Forearm")}
    for o in src.values():
        o.hide_set(True)

    def node(parent, pos, rot):
        return parent @ Matrix.Translation(U(*pos)) @ unity_rotation(*rot).to_matrix().to_4x4()

    def put(name, m, mirror=False):
        o = src[name].copy()
        coll.objects.link(o)
        o.matrix_world = m @ (Matrix.Scale(-1, 4, (1, 0, 0)) if mirror else Matrix.Identity(4))
        o.hide_set(False)

    body = node(Matrix.Identity(4), (0, 0.04, 0), (2, 0, 0))
    put("Hollow_Robe", body)
    chest = node(body, (0, 1.45, 0), (8, 0, 0))
    put("Hollow_Torso", chest)
    neck = node(chest, (0, 0.3, 0.02), (14, 0, 0))
    put("Hollow_Neck", neck)
    head = node(neck, (0, 0.27, 0.02), (-4, 0, 10))
    put("Hollow_Head", head)
    put("Hollow_Hood", head)
    for side in (-1, 1):
        shoulder = node(chest, (0.2 * side, 0.12, 0), (-8, 0, 14 * side))
        put("Hollow_UpperArm", shoulder, mirror=side > 0)
        fore = node(shoulder, (0, -0.52, 0), (-20, 0, -6 * side))
        put("Hollow_Forearm", fore, mirror=side > 0)


# ---------------------------------------------------------------- export


def build_all():
    clear()
    parts = [build_head(), build_hood(), build_neck(), build_torso(), build_upper_arm(), build_forearm(), build_robe()]
    for p in parts:
        tris = sum(len(f.vertices) - 2 for f in p.data.polygons)
        print("%-16s tris %5d  size %s" % (p.name, tris, tuple(round(v, 3) for v in p.dimensions)))
    return parts


def export(path):
    bpy.ops.object.select_all(action='DESELECT')
    for o in bpy.context.scene.collection.objects:  # the parts, not the preview copies
        o.hide_set(False)
        o.select_set(True)
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'MESH'},
                             apply_scale_options='FBX_SCALE_UNITS', bake_space_transform=True,
                             axis_forward='-Z', axis_up='Y', mesh_smooth_type='FACE',
                             use_mesh_modifiers=True, add_leaf_bones=False, bake_anim=False)
    print("exported", path)


if __name__ == "__main__":
    build_all()
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if argv:
        export(argv[0])
