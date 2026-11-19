# MgSO4 AR Sim - procedural ward assets for Blender 5.x (run headless: blender -b --python ward_models.py)
# Everything is authored directly in Unity local coordinates (Y up, X along the bed: head at -X).
# Meshes are written as compact binary files that the app loads at runtime (Resources/Models/*.bytes)
# so no FBX import settings / axis conversions are involved.
import bpy, bmesh, math, struct, os, sys
from mathutils import Vector, Matrix

script = os.path.abspath(sys.argv[sys.argv.index("--python") + 1]) if "--python" in sys.argv else os.path.abspath(__file__)
LOGS = os.path.dirname(script)
ROOT = os.path.dirname(LOGS)
OUT = os.path.join(ROOT, "MgSO4_AR_Simulation", "Assets", "Resources", "Models")
SHOTS = os.path.join(LOGS, "screens")
os.makedirs(OUT, exist_ok=True)
os.makedirs(SHOTS, exist_ok=True)

# ------------------------------------------------------------------ scene reset
for o in list(bpy.data.objects):
    bpy.data.objects.remove(o, do_unlink=True)
for m in list(bpy.data.meshes):
    bpy.data.meshes.remove(m)
COLL = bpy.context.scene.collection
DG = None

# ------------------------------------------------------------------ materials
MATS = {}
def mat(name, rgba, smooth=0.3):
    if name in MATS:
        return MATS[name]
    m = bpy.data.materials.new(name)
    if len(rgba) == 3:
        rgba = (*rgba, 1.0)
    m.diffuse_color = rgba
    m["smooth"] = smooth
    MATS[name] = m
    return m

SKIN = mat("skin", (0.80, 0.60, 0.47), 0.35)
LIPS = mat("lips", (0.70, 0.40, 0.38), 0.4)
HAIR = mat("hair", (0.12, 0.08, 0.06), 0.45)
BROW = mat("brow", (0.16, 0.11, 0.08), 0.2)
GOWN = mat("gown", (0.56, 0.74, 0.90), 0.15)
GOWN_D = mat("gown_trim", (0.40, 0.58, 0.78), 0.15)
MATT = mat("mattress", (0.93, 0.95, 0.97), 0.2)
SHEET = mat("sheet", (0.98, 0.98, 0.99), 0.1)
BLANKET = mat("blanket", (0.68, 0.80, 0.90), 0.1)
PILLOW = mat("pillow", (0.99, 0.99, 1.0), 0.15)
CHROME = mat("chrome", (0.80, 0.82, 0.85), 0.8)
METAL = mat("metal", (0.70, 0.73, 0.77), 0.6)
PANEL = mat("panel", (0.33, 0.52, 0.64), 0.5)
PANEL_L = mat("panel_light", (0.88, 0.90, 0.92), 0.5)
PAD = mat("pad", (0.86, 0.42, 0.40), 0.25)
DARK = mat("dark", (0.14, 0.15, 0.17), 0.35)
RUBBER = mat("rubber", (0.09, 0.09, 0.10), 0.2)
PLASTIC = mat("plastic", (0.92, 0.93, 0.95), 0.45)
SCREEN = mat("screen", (0.03, 0.05, 0.07), 0.9)
BEZEL = mat("bezel", (0.20, 0.22, 0.25), 0.5)
GREEN = mat("green_btn", (0.20, 0.70, 0.32), 0.5)
RED = mat("red_btn", (0.85, 0.24, 0.24), 0.5)
BLUE = mat("blue_cap", (0.18, 0.38, 0.88), 0.5)
ORANGE = mat("orange_cap", (0.95, 0.58, 0.10), 0.5)
CAPRED = mat("red_cap", (0.84, 0.14, 0.20), 0.5)
LABEL = mat("label", (0.97, 0.97, 0.95), 0.2)
TAG = mat("tag", (0.95, 0.45, 0.20), 0.3)
GLASS = mat("glass", (0.90, 0.95, 1.0, 0.40), 0.9)
FLUID = mat("fluid", (0.75, 0.88, 1.0, 0.65), 0.9)
BAGPL = mat("bag", (0.94, 0.96, 0.98, 0.45), 0.8)
MASKG = mat("mask", (0.62, 0.92, 0.70, 0.55), 0.8)
TUBEG = mat("tube", (0.85, 0.93, 1.0, 0.75), 0.8)
TABLE = mat("table_top", (0.86, 0.83, 0.78), 0.35)
FLOOR = mat("floor", (0.80, 0.84, 0.88), 0.3)
FLOOR_E = mat("floor_edge", (0.62, 0.68, 0.74), 0.3)
HUB = mat("hub", (0.30, 0.75, 0.45), 0.5)
STOPPER = mat("stopper", (0.10, 0.10, 0.11), 0.3)
SCALE = mat("scale_marks", (0.35, 0.38, 0.42), 0.2)

# ------------------------------------------------------------------ object helpers
def link(name, me, m):
    o = bpy.data.objects.new(name, me)
    COLL.objects.link(o)
    if isinstance(m, (list, tuple)):
        for mm in m:
            o.data.materials.append(mm)
    else:
        o.data.materials.append(m)
    return o

def finish(o, smooth=True, sharp=None, subdiv=0, bevel=None):
    me = o.data
    for p in me.polygons:
        p.use_smooth = smooth
    if bevel:
        b = o.modifiers.new("bevel", 'BEVEL')
        b.width = bevel[0]
        b.segments = bevel[1]
        b.limit_method = 'NONE'
    if subdiv:
        s = o.modifiers.new("sub", 'SUBSURF')
        s.levels = subdiv
        s.render_levels = subdiv
    apply_mods(o)
    for p_ in o.data.polygons:
        p_.use_smooth = smooth
    if sharp is not None:
        try:
            o.data.set_sharp_from_angle(angle=math.radians(sharp))
        except Exception as e:
            print("sharp:", e)
    return o

def apply_mods(o):
    if not o.modifiers:
        return
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(o.evaluated_get(dg))
    o.modifiers.clear()
    old = o.data
    o.data = me
    if old.users == 0:
        bpy.data.meshes.remove(old)

def join_remesh(name, objs, m, voxel=0.006, smooth_iter=10, ratio=0.25):
    """Union of overlapping parts into one watertight, smoothly blended surface."""
    bm = bmesh.new()
    for o in objs:
        bm.from_mesh(o.data)
        bpy.data.objects.remove(o, do_unlink=True)
    o = bm_obj(name, bm, m)
    r = o.modifiers.new("remesh", 'REMESH')
    r.mode = 'VOXEL'
    r.voxel_size = voxel
    r.use_smooth_shade = True
    sm = o.modifiers.new("smooth", 'CORRECTIVE_SMOOTH')
    sm.factor = 0.8
    sm.iterations = smooth_iter
    sm.smooth_type = 'SIMPLE'
    sm.use_only_smooth = True
    if ratio < 1.0:
        d = o.modifiers.new("dec", 'DECIMATE')
        d.ratio = ratio
    finish(o, True)
    return o

def bm_obj(name, bm, m):
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    return link(name, me, m)

def rbox(name, center, size, m, bevel=0.01, seg=3, sharp=40, subdiv=0):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co = Vector((v.co.x * size[0], v.co.y * size[1], v.co.z * size[2])) + Vector(center)
    o = bm_obj(name, bm, m)
    bv = min(bevel, min(size) * 0.49) if bevel else 0
    return finish(o, True, sharp, subdiv, (bv, seg) if bv > 0 else None)

def ring_pts(n, r1, r2, p=2.0, phase=0.0):
    pts = []
    e = 2.0 / p
    for i in range(n):
        a = 2 * math.pi * i / n + phase
        ca, sa = math.cos(a), math.sin(a)
        u = math.copysign(abs(ca) ** e, ca)
        v = math.copysign(abs(sa) ** e, sa)
        pts.append((r1 * v, r2 * u))
    return pts

def loft(name, secs, m, axis='x', n=28, subdiv=1, cap=True, sharp=None, mats=None):
    """secs: (t, c1, c2, r1, r2, p). axis x -> point (t, c1+r1*v, c2+r2*u); y -> (c1+r1*v, t, c2+r2*u); z -> (c1+r1*v, c2+r2*u, t).
    A section with r1 == r2 == 0 becomes a single pole vertex (rounded tip)."""
    bm = bmesh.new()
    rings = []
    for (t, c1, c2, r1, r2, p) in secs:
        if r1 <= 1e-6 and r2 <= 1e-6:
            rings.append([bm.verts.new(P(axis, t, c1, c2))])
            continue
        ring = []
        for (a, b) in ring_pts(n, r1, r2, p):
            ring.append(bm.verts.new(P(axis, t, c1 + a, c2 + b)))
        rings.append(ring)
    for k in range(len(rings) - 1):
        A, B = rings[k], rings[k + 1]
        if len(A) == 1 and len(B) == 1:
            continue
        if len(A) == 1:
            for i in range(n):
                bm.faces.new((A[0], B[i], B[(i + 1) % n]))
        elif len(B) == 1:
            for i in range(n):
                bm.faces.new((A[i], A[(i + 1) % n], B[0]))
        else:
            for i in range(n):
                bm.faces.new((A[i], A[(i + 1) % n], B[(i + 1) % n], B[i]))
    if cap:
        for R, t in ((rings[0], secs[0][0]), (rings[-1], secs[-1][0])):
            if len(R) > 1:
                bm.faces.new(R)
    o = bm_obj(name, bm, m)
    return finish(o, True, sharp, subdiv)

def P(axis, t, a, b):
    if axis == 'x':
        return (t, a, b)
    if axis == 'y':
        return (a, t, b)
    return (a, b, t)

def lathe(name, prof, m, center=(0, 0, 0), n=32, subdiv=0, sharp=50, sx=1.0, sz=1.0, axis='y', cap=True):
    """Revolve a profile of (r, h) around an axis through center. r = 0 -> pole."""
    cx, cy, cz = center
    secs = []
    for (r, h) in prof:
        if axis == 'y':
            secs.append((cy + h, cx, cz, r * sx, r * sz, 2))
        elif axis == 'x':
            secs.append((cx + h, cy, cz, r * sx, r * sz, 2))
        else:
            secs.append((cz + h, cx, cy, r * sx, r * sz, 2))
    return loft(name, secs, m, axis, n, subdiv, cap, sharp)

def cyl(name, a, b, r, m, n=20, sharp=40):
    a, b = Vector(a), Vector(b)
    d = b - a
    L = d.length
    o = lathe(name, [(0, 0), (r, 0), (r, L), (0, L)], m, (0, 0, 0), n, 0, sharp)
    rot = Vector((0, 1, 0)).rotation_difference(d.normalized()).to_matrix().to_4x4()
    o.data.transform(Matrix.Translation(a) @ rot)
    return o

def tube(name, pts, r, m, res=10, n=12, closed=False):
    cu = bpy.data.curves.new(name, 'CURVE')
    cu.dimensions = '3D'
    cu.bevel_depth = r
    cu.bevel_resolution = max(1, n // 4)
    cu.use_fill_caps = True
    cu.resolution_u = res
    sp = cu.splines.new('BEZIER')
    sp.bezier_points.add(len(pts) - 1)
    for bp, p in zip(sp.bezier_points, pts):
        bp.co = Vector(p)
        bp.handle_left_type = bp.handle_right_type = 'AUTO'
    sp.use_cyclic_u = closed
    co = bpy.data.objects.new(name + "_c", cu)
    COLL.objects.link(co)
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(co.evaluated_get(dg))
    bpy.data.objects.remove(co, do_unlink=True)
    o = link(name, me, m)
    for p in o.data.polygons:
        p.use_smooth = True
    return o

def ellipsoid(name, c, r, m, n=32, rings=16):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=n, v_segments=rings, radius=1.0)
    for v in bm.verts:
        v.co = Vector((v.co.x * r[0] + c[0], v.co.y * r[1] + c[1], v.co.z * r[2] + c[2]))
    o = bm_obj(name, bm, m)
    return finish(o, True)

def mirror_z(o, name):
    me = o.data.copy()
    me.transform(Matrix.Scale(-1, 4, Vector((0, 0, 1))))
    me.flip_normals()
    o2 = link(name, me, list(o.data.materials))
    return o2

def caster(name, x, z, h=0.05, r=0.04):
    parts = []
    parts.append(cyl(name + "_w", (x, r, z - 0.018), (x, r, z + 0.018), r, RUBBER, 24))
    parts.append(cyl(name + "_hub", (x, r, z - 0.02), (x, r, z + 0.02), r * 0.45, METAL, 16))
    parts.append(rbox(name + "_fork", (x - 0.01, r + 0.03, z), (0.05, 0.03, 0.05), METAL, 0.008))
    parts.append(cyl(name + "_stem", (x, r + 0.04, z), (x, r + 0.04 + h, z), 0.012, METAL, 12))
    return parts

def star_base(name, y, r_leg, n_legs=5, center=(0, 0)):
    parts = []
    cx, cz = center
    parts.append(lathe(name + "_hubb", [(0, 0), (0.05, 0), (0.05, 0.05), (0.03, 0.07), (0, 0.07)], DARK, (cx, y, cz), 24, 0, 40))
    for i in range(n_legs):
        a = 2 * math.pi * i / n_legs + 0.3
        ex, ez = cx + math.cos(a) * r_leg, cz + math.sin(a) * r_leg
        leg = rbox(name + f"_leg{i}", (0, 0, 0), (r_leg, 0.035, 0.04), DARK, 0.012)
        leg.data.transform(Matrix.Translation(((cx + ex) / 2, y + 0.04, (cz + ez) / 2)) @ Matrix.Rotation(-a, 4, 'Y'))
        parts.append(leg)
        parts += caster(name + f"_c{i}", ex, ez, 0.0, 0.03)
    return parts

# ------------------------------------------------------------------ export
def export(asset, objs, origin=(0, 0, 0)):
    """Writes Resources/Models/<asset>.bytes : submeshes per material (Unity local coords relative to origin)."""
    dg = bpy.context.evaluated_depsgraph_get()
    subs = {}
    order = []
    org = Vector(origin)
    for o in objs:
        eo = o.evaluated_get(dg)
        me = eo.to_mesh()
        me.calc_loop_triangles()
        mw = o.matrix_world
        nm = mw.to_3x3().inverted().transposed()
        cn = me.corner_normals
        for tri in me.loop_triangles:
            mt = o.material_slots[tri.material_index].material if o.material_slots else None
            key = mt.name if mt else "default"
            if key not in subs:
                subs[key] = {"mat": mt, "verts": [], "map": {}, "tris": []}
                order.append(key)
            s = subs[key]
            idx = []
            for li in tri.loops:
                v = me.loops[li].vertex_index
                p = mw @ me.vertices[v].co - org
                nrm = (nm @ cn[li].vector).normalized()
                k = (round(p.x, 5), round(p.y, 5), round(p.z, 5), round(nrm.x, 3), round(nrm.y, 3), round(nrm.z, 3))
                i = s["map"].get(k)
                if i is None:
                    i = len(s["verts"])
                    s["map"][k] = i
                    s["verts"].append((p.x, p.y, p.z, nrm.x, nrm.y, nrm.z))
                idx.append(i)
            s["tris"].append(idx)
        eo.to_mesh_clear()
    # skin first (the app tints submesh 0 for facial flushing)
    order.sort(key=lambda k: 0 if k == "skin" else 1)
    data = bytearray(b"MGM1")
    data += struct.pack("<i", len(order))
    total = 0
    for key in order:
        s = subs[key]
        mt = s["mat"]
        col = tuple(mt.diffuse_color) if mt else (1, 1, 1, 1)
        sm = float(mt.get("smooth", 0.3)) if mt else 0.3
        name = key.encode("utf8")
        data += struct.pack("<i", len(name)) + name
        data += struct.pack("<5f", col[0], col[1], col[2], col[3], sm)
        data += struct.pack("<i", len(s["verts"]))
        for v in s["verts"]:
            data += struct.pack("<6f", *v)
        data += struct.pack("<i", len(s["tris"]) * 3)
        for t in s["tris"]:
            data += struct.pack("<3i", *t)
        total += len(s["verts"])
    with open(os.path.join(OUT, asset + ".bytes"), "wb") as f:
        f.write(data)
    print(f"EXPORT {asset}: {len(order)} submeshes, {total} verts, {len(data)//1024} KB")
    return objs

GROUPS = {}
def group(asset, objs, origin=(0, 0, 0), world=(0, 0, 0)):
    """Record an asset; 'world' is where the group sits in ward space (for preview renders)."""
    GROUPS[asset] = (objs, origin, world)

# ==================================================================== FLOOR
def build_floor():
    objs = [rbox("floor", (0.1, -0.012, 0.1), (2.9, 0.024, 2.1), FLOOR, 0.01, 2)]
    group("floor", objs)

# ==================================================================== BED
def build_bed():
    o = []
    # chassis
    for z in (-0.36, 0.36):
        o.append(rbox(f"rail_low{z}", (0, 0.40, z), (1.86, 0.05, 0.05), METAL, 0.01))
    for x in (-0.86, 0.86):
        o.append(rbox(f"cross{x}", (x, 0.40, 0), (0.05, 0.05, 0.72), METAL, 0.01))
    o.append(rbox("cross_mid", (0, 0.40, 0), (0.05, 0.05, 0.72), METAL, 0.01))
    # legs + casters
    for x in (-0.86, 0.86):
        for z in (-0.36, 0.36):
            o.append(rbox(f"leg{x}{z}", (x, 0.25, z), (0.06, 0.28, 0.06), METAL, 0.012))
            o += caster(f"cast{x}{z}", x, z, 0.04, 0.05)
    # scissor lift hint
    o.append(rbox("lift", (0, 0.30, 0), (1.1, 0.14, 0.5), DARK, 0.02))
    # deck
    o.append(rbox("deck", (0, 0.47, 0), (1.98, 0.04, 0.9), PANEL_L, 0.012))
    # mattress (soft, rounded) + fitted sheet look
    o.append(rbox("mattress", (0, 0.565, 0), (1.94, 0.15, 0.86), MATT, 0.06, 5, 30))
    o.append(rbox("mattress_seam", (0, 0.565, 0), (1.95, 0.012, 0.87), PANEL_L, 0.005, 2))
    # headboard / footboard panels with rounded corners
    o.append(rbox("headboard", (-1.0, 0.80, 0), (0.05, 0.62, 0.94), PANEL, 0.03, 4))
    o.append(rbox("headboard_in", (-0.97, 0.82, 0), (0.012, 0.40, 0.70), PANEL_L, 0.01, 3))
    o.append(tube("head_handle", [(-1.0, 1.12, -0.40), (-1.0, 1.16, -0.2), (-1.0, 1.16, 0.2), (-1.0, 1.12, 0.40)], 0.014, CHROME))
    o.append(rbox("footboard", (1.0, 0.69, 0), (0.05, 0.42, 0.94), PANEL, 0.03, 4))
    o.append(rbox("foot_ctrl", (1.03, 0.78, 0.25), (0.02, 0.10, 0.22), BEZEL, 0.01, 2))
    # side rails (tubular frames)
    for z, name in ((-0.47, "railL"), (0.47, "railR")):
        x0, x1 = -0.78, 0.30
        o.append(tube(name + "_top", [(x0, 0.86, z), (x0 + 0.05, 0.88, z), (x1 - 0.05, 0.88, z), (x1, 0.86, z)], 0.014, CHROME))
        o.append(tube(name + "_mid", [(x0, 0.74, z), (x1, 0.74, z)], 0.011, CHROME, 2))
        for x in (x0, -0.24, x1):
            o.append(cyl(name + f"_post{x}", (x, 0.62, z), (x, 0.87, z), 0.012, CHROME, 12))
    # padded rail cover on the right side (seizure precautions)
    o.append(rbox("pad", (-0.24, 0.79, 0.49), (1.16, 0.22, 0.04), PAD, 0.018, 4, 30))
    # pillow: soft rounded cushion with a dip where the head rests
    pil = rbox("pillow", (-0.80, 0.69, 0), (0.36, 0.11, 0.60), PILLOW, 0.05, 4, 0, 1)
    for v in pil.data.vertices:
        d = math.exp(-((v.co.x + 0.80) / 0.11) ** 2 - (v.co.z / 0.13) ** 2)
        if v.co.y > 0.69:
            v.co.y -= 0.035 * d
    o.append(pil)
    # blanket draped over the left leg and off the left side
    bm = bmesh.new()
    nx, nz = 34, 30
    x0, x1, z0, z1 = 0.16, 0.99, -0.52, 0.03
    grid = []
    for i in range(nx + 1):
        row = []
        x = x0 + (x1 - x0) * i / nx
        for j in range(nz + 1):
            z = z0 + (z1 - z0) * j / nz
            # follows the realistic left leg: thigh/knee high, shin lower, feet (toes up) lift it again
            zc = -0.095 - 0.065 * min(1.0, max(0.0, x / 0.65))
            if x < 0.30:
                hh = 0.175
            elif x < 0.60:
                hh = 0.175 - 0.07 * (x - 0.30) / 0.30
            else:
                hh = 0.105 + 0.15 * math.exp(-((x - 0.73) / 0.075) ** 2)
            leg = hh * math.exp(-((z - zc) / 0.085) ** 2)
            y = 0.655 + leg
            if z < -0.42:  # drape down the side of the mattress
                k = (-0.42 - z) / 0.10
                y = 0.655 - 0.24 * k * k
                z = -0.42 - 0.035 * math.sin(min(1, k) * math.pi / 2) - 0.012
            y += 0.006 * math.sin(x * 23 + z * 7)  # gentle folds
            row.append(bm.verts.new((x, y, z)))
        grid.append(row)
    for i in range(nx):
        for j in range(nz):
            bm.faces.new((grid[i][j], grid[i + 1][j], grid[i + 1][j + 1], grid[i][j + 1]))
    bl = bm_obj("blanket", bm, BLANKET)
    s = bl.modifiers.new("solid", 'SOLIDIFY')
    s.thickness = 0.012
    finish(bl, True, None, 1)
    o.append(bl)
    group("bed", o)

# ==================================================================== PATIENT
PATIENT = Vector((0, 0.64, 0))   # patient group position in ward space

def head_shape(n):
    x, y, z = n.x * 0.112, n.y * 0.098, n.z * 0.076
    if x > 0.0:                      # jaw / chin taper
        t = min(1.0, x / 0.112)
        z *= 1 - 0.30 * t * t
        if y < 0:
            y *= 1 - 0.35 * t
    if x < -0.02:                    # fuller back of the skull
        z *= 1 + 0.06 * min(1, (-x - 0.02) / 0.05)
    if y > 0:
        ax = abs(z)
        y -= 0.010 * math.exp(-((x + 0.002) / 0.016) ** 2 - ((ax - 0.032) / 0.017) ** 2)   # eye sockets
        y += 0.005 * math.exp(-((x + 0.024) / 0.010) ** 2 - (z / 0.05) ** 2)              # brow ridge
        nose = math.exp(-((x - 0.030) / 0.024) ** 2 - (z / (0.010 + 0.006 * max(0, (x - 0.01) / 0.04))) ** 2)
        y += 0.026 * nose * min(1, max(0, (x + 0.01) / 0.03))                              # nose
        for sgn in (-1, 1):                                                                # nostrils / alae
            y += 0.004 * math.exp(-((x - 0.050) / 0.006) ** 2 - ((z - sgn * 0.011) / 0.006) ** 2)
        y += 0.006 * math.exp(-((x - 0.03) / 0.02) ** 2 - ((ax - 0.042) / 0.016) ** 2)     # cheeks
        y += 0.0065 * math.exp(-((x - 0.069) / 0.0045) ** 2 - (z / 0.021) ** 2)            # upper lip
        y += 0.0060 * math.exp(-((x - 0.079) / 0.0045) ** 2 - (z / 0.019) ** 2)            # lower lip
        y -= 0.0025 * math.exp(-((x - 0.074) / 0.0018) ** 2 - (z / 0.022) ** 2)            # mouth line
        y += 0.006 * math.exp(-((x - 0.098) / 0.012) ** 2 - (z / 0.025) ** 2)              # chin
    return Vector((x, y, z))

def build_head():
    """Head lying face-up: face toward +Y, top of the head toward -X, chin toward +X. Origin = head centre."""
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=96, v_segments=64, radius=1.0)
    for v in bm.verts:
        v.co = head_shape(v.co.copy())
    head = bm_obj("head", bm, SKIN)
    finish(head, True)
    o = [head]
    # lips and eyebrows as small lofts lying on the face surface
    def surf(x, z, lift=0.0):
        a, c = x / 0.112, z / 0.076
        n = Vector((a, math.sqrt(max(0.0, 1 - a * a - c * c)), c))
        p = head_shape(n)
        return p.y + lift
    for (name, xc, rx, ry, half) in (("lip_u", 0.0690, 0.0038, 0.0022, 0.017), ("lip_l", 0.0785, 0.0042, 0.0024, 0.015)):
        secs = [(-half - 0.001, xc, surf(xc, -half) - 0.001, 0, 0, 2)]
        for i in range(13):
            z = -half + 2 * half * i / 12
            k = math.sin(math.pi * (0.08 + 0.84 * i / 12))
            dip = -0.0012 * math.exp(-(z / 0.004) ** 2) if name == "lip_u" else 0.0
            secs.append((z, xc + dip, surf(xc, z) - 0.0015, rx * k, ry * k, 2))
        secs.append((half + 0.001, xc, surf(xc, half) - 0.001, 0, 0, 2))
        o.append(loft(name, secs, LIPS, 'z', 16, 1))
    for sgn in (-1, 1):
        secs = []
        zs = [sgn * (0.013 + 0.038 * i / 10) for i in range(11)]
        if sgn < 0:
            zs = list(reversed(zs))
        for i, z in enumerate(zs):
            xb = -0.021 - 0.007 * ((abs(z) - 0.030) / 0.02) ** 2
            u = (abs(z) - 0.013) / 0.038
            k = 1.0 - 0.6 * u if u > 0.2 else 0.5 + 2.5 * u
            if i in (0, 10):
                secs.append((z, xb, surf(xb, z) - 0.0005, 0, 0, 2))
            else:
                secs.append((z, xb, surf(xb, z) - 0.0006, 0.0028 * k, 0.0016 * k, 2))
        o.append(loft(f"brow{sgn}", secs, BROW, 'z', 12, 1))
    for s_ in (-1, 1):
        o.append(ellipsoid(f"ear{s_}", (0.010, -0.004, s_ * 0.074), (0.030, 0.020, 0.009), SKIN, 20, 10))
    # hair: a shell over the top/back of the head cut by a plane along the hairline
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=72, v_segments=48, radius=1.0)
    for v in bm.verts:
        p = head_shape(v.co.copy())
        n = v.co.copy()
        v.co = p + Vector((n.x * 0.112, n.y * 0.098, n.z * 0.076)).normalized() * 0.008
    A = Vector((-0.050, 0.092, 0))
    nrm = Vector((0.13, 0.105, 0)).normalized()
    geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
    bmesh.ops.bisect_plane(bm, geom=geom, plane_co=A, plane_no=nrm, clear_outer=True)
    hair = bm_obj("hair", bm, HAIR)
    sol = hair.modifiers.new("s", 'SOLIDIFY')
    sol.thickness = 0.005
    sol.offset = -1
    finish(hair, True, None, 1)
    o.append(hair)
    o.append(ellipsoid("bun", (-0.122, -0.028, 0), (0.036, 0.032, 0.044), HAIR, 24, 12))
    group("head", o, (0, 0, 0), PATIENT + Vector((-0.8, 0.14, 0)))

def build_trunk():
    """Gown-covered trunk (chest + pregnant abdomen + hips) as ONE blended surface.
    Origin = (-0.38, 0, 0) in patient space (at the back, so breathing scales the chest/abdomen upward)."""
    ox = Vector((-0.38, 0.0, 0))
    parts = []
    parts.append(loft("t_chest", [
        (-0.612, 0.105, 0, 0.050, 0.12, 2.4),
        (-0.590, 0.110, 0, 0.085, 0.185, 2.8),
        (-0.540, 0.112, 0, 0.100, 0.205, 3.0),
        (-0.460, 0.116, 0, 0.112, 0.195, 3.0),
        (-0.370, 0.118, 0, 0.120, 0.182, 2.8),
        (-0.280, 0.114, 0, 0.110, 0.166, 2.6),
        (-0.200, 0.110, 0, 0.102, 0.160, 2.6),
        (-0.120, 0.105, 0, 0.098, 0.165, 2.6),
    ], GOWN, 'x', 32, 0))
    parts.append(loft("t_belly", [
        (-0.255, 0.100, 0, 0.090, 0.150, 2.2),
        (-0.210, 0.122, 0, 0.128, 0.165, 2.1),
        (-0.160, 0.138, 0, 0.156, 0.172, 2.0),
        (-0.100, 0.143, 0, 0.168, 0.175, 2.0),
        (-0.040, 0.136, 0, 0.156, 0.172, 2.0),
        (0.010, 0.124, 0, 0.126, 0.170, 2.2),
        (0.055, 0.105, 0, 0.090, 0.168, 2.5),
    ], GOWN, 'x', 32, 0))
    parts.append(loft("t_hips", [
        (-0.020, 0.090, 0, 0.090, 0.172, 2.8),
        (0.060, 0.092, 0, 0.092, 0.188, 3.0),
        (0.140, 0.090, 0, 0.088, 0.192, 3.0),
        (0.220, 0.088, 0, 0.084, 0.194, 3.2),
        (0.270, 0.086, 0, 0.078, 0.192, 3.2),
    ], GOWN, 'x', 32, 0))
    # soft breasts blended into the chest
    for s_ in (-1, 1):
        parts.append(ellipsoid(f"t_bust{s_}", (-0.40, 0.200, s_ * 0.072), (0.06, 0.045, 0.065), GOWN, 24, 12))
    trunk = join_remesh("trunk", parts, GOWN, 0.0095, 40, 1.0)
    trunk.data.transform(Matrix.Translation(-ox))
    o = [trunk]
    neck = loft("neck", [(-0.70 - ox.x, 0.115, 0, 0.046, 0.05, 2), (-0.64 - ox.x, 0.12, 0, 0.05, 0.056, 2), (-0.575 - ox.x, 0.13, 0, 0.06, 0.08, 2.2)], SKIN, 'x', 24, 1)
    o.append(neck)
    o.append(tube("neckline", [(-0.605 - ox.x, 0.15, -0.085), (-0.575 - ox.x, 0.205, -0.035), (-0.572 - ox.x, 0.208, 0.035), (-0.605 - ox.x, 0.15, 0.085)], 0.006, GOWN_D))
    group("trunk", o, (0, 0, 0), PATIENT + ox)

def build_arm(side):
    """Arm lying beside the body, palm down. Origin = shoulder. side -1 = left (z-), +1 = right."""
    o = []
    secs = [
        (-0.030, 0, 0, 0.040, 0.044, 2),
        (0.020, 0, 0, 0.043, 0.045, 2),
        (0.120, 0, 0, 0.039, 0.040, 2),
        (0.230, 0, 0, 0.033, 0.035, 2),
        (0.275, 0.002, 0, 0.030, 0.032, 2),   # elbow
        (0.320, 0.002, 0, 0.032, 0.036, 2),
        (0.400, 0.000, 0, 0.028, 0.032, 2.2),
        (0.480, -0.004, 0, 0.019, 0.027, 2.4),  # wrist
        (0.500, -0.006, 0, 0.017, 0.029, 2.6),
    ]
    o.append(loft("arm", secs, SKIN, 'x', 24, 1))
    # hand: flat palm + merged fingers
    hand = [
        (0.495, -0.008, 0, 0.016, 0.030, 2.6),
        (0.540, -0.010, 0, 0.015, 0.036, 3.2),
        (0.580, -0.012, 0, 0.012, 0.035, 3.4),
        (0.620, -0.014, 0, 0.009, 0.031, 3.0),
        (0.650, -0.016, 0, 0.007, 0.024, 2.4),
        (0.662, -0.017, 0, 0.0, 0.0, 2),
    ]
    o.append(loft("hand", hand, SKIN, 'x', 24, 1))
    thumb = [(0.50, -0.006, 0.0, 0.010, 0.010, 2), (0.53, -0.008, 0.0, 0.009, 0.009, 2), (0.56, -0.010, 0.0, 0.007, 0.007, 2), (0.575, -0.010, 0.0, 0, 0, 2)]
    th = loft("thumb", thumb, SKIN, 'x', 16, 1)
    th.data.transform(Matrix.Translation((0, 0, -side * 0.028)) @ Matrix.Rotation(side * 0.45, 4, 'Y'))
    o.append(th)
    # short gown sleeve
    sleeve = [(-0.045, 0.002, 0, 0.050, 0.054, 2.2), (0.06, 0.002, 0, 0.052, 0.054, 2.2), (0.11, 0.002, 0, 0.050, 0.051, 2.2)]
    o.append(loft("sleeve", sleeve, GOWN, 'x', 24, 1))
    name = "armL" if side < 0 else "armR"
    group(name, o, (0, 0, 0), PATIENT + Vector((-0.6, 0.05, side * 0.25)))

def build_thigh(name, z):
    """Thigh from the hip joint toward the knee (+X). Origin = hip."""
    secs = [
        (-0.040, 0.000, 0, 0.070, 0.085, 2.2),
        (0.050, 0.000, 0, 0.082, 0.090, 2.1),
        (0.180, 0.000, 0, 0.074, 0.080, 2.0),
        (0.300, 0.000, 0, 0.060, 0.064, 2.0),
        (0.380, 0.002, 0, 0.052, 0.056, 2.0),
        (0.420, 0.003, 0, 0.050, 0.054, 2.0),
    ]
    o = [loft("thigh", secs, SKIN, 'x', 28, 1)]
    o.append(ellipsoid("patella", (0.405, 0.045, 0), (0.026, 0.014, 0.024), SKIN, 20, 10))
    group(name, o, (0, 0, 0), PATIENT + Vector((0.1, 0.08, z)))

def build_shin(name, world):
    """Lower leg from the knee (+X) with the foot pointing up (+Y). Origin = knee."""
    secs = [
        (-0.030, 0.000, 0, 0.050, 0.053, 2),
        (0.040, -0.004, 0, 0.050, 0.051, 2),
        (0.120, -0.010, 0, 0.052, 0.048, 2),   # calf bulge (underneath)
        (0.220, -0.006, 0, 0.040, 0.040, 2),
        (0.320, -0.004, 0, 0.029, 0.031, 2),
        (0.385, -0.004, 0, 0.026, 0.028, 2),   # ankle
        (0.410, -0.006, 0, 0.030, 0.030, 2),
    ]
    o = [loft("shin", secs, SKIN, 'x', 24, 1)]
    # foot: heel at the end of the shin, toes point up
    foot = [
        (-0.045, 0.418, 0, 0.026, 0.026, 2),  # heel (axis y: c1 = x centre, c2 = z centre)
        (-0.020, 0.420, 0, 0.034, 0.030, 2.2),
        (0.040, 0.425, 0, 0.030, 0.036, 2.6),
        (0.110, 0.432, 0, 0.022, 0.042, 2.8),
        (0.160, 0.440, 0, 0.016, 0.040, 2.6),
        (0.185, 0.446, 0, 0.010, 0.030, 2.2),
        (0.195, 0.448, 0, 0.0, 0.0, 2),
    ]
    o.append(loft("foot", foot, SKIN, 'y', 24, 1))
    group(name, o, (0, 0, 0), world)

# ==================================================================== IV POLE, BAG, PUMP
def build_ivpole():
    o = star_base("ivb", 0.03, 0.28)
    o.append(cyl("pole", (0, 0.10, 0), (0, 1.95, 0), 0.0135, CHROME, 20))
    o.append(cyl("pole_lock", (0, 1.30, 0), (0, 1.34, 0), 0.02, DARK, 20))
    o.append(cyl("knob", (0, 1.32, 0), (0, 1.32, 0.05), 0.008, DARK, 12))
    for s in (-1, 1):
        o.append(tube(f"hook{s}", [(0, 1.93, 0), (s * 0.08, 1.95, 0), (s * 0.13, 1.93, 0), (s * 0.15, 1.89, 0), (s * 0.135, 1.86, 0)], 0.006, CHROME))
    o.append(lathe("top", [(0, 0), (0.016, 0), (0.016, 0.02), (0, 0.03)], CHROME, (0, 1.95, 0), 16))
    group("ivpole", o, (0, 0, 0), Vector((-1.12, 0, -0.55)))

def build_ivbag():
    """IV bag hanging from the pole. Origin = Bag group (0.1, 1.68, 0) relative to pole."""
    o = []
    shell = rbox("bagshell", (0, 0, 0), (0.15, 0.25, 0.035), BAGPL, 0.016, 4, 0, 1)
    for v in shell.data.vertices:   # pillow-shaped bag, thinner at the edges
        k = max(0.0, 1 - (v.co.x / 0.075) ** 2) * max(0.0, 1 - (v.co.y / 0.125) ** 2)
        v.co.z *= 0.35 + 0.65 * k
    o.append(shell)
    fl = rbox("fluid", (0, -0.03, 0), (0.13, 0.17, 0.026), FLUID, 0.012, 3, 0, 1)
    for v in fl.data.vertices:
        k = max(0.0, 1 - (v.co.x / 0.065) ** 2)
        v.co.z *= 0.3 + 0.7 * k
    o.append(fl)
    o.append(rbox("label", (0, 0.02, 0.019), (0.10, 0.06, 0.002), TAG, 0.002, 1))
    o.append(rbox("tab", (0, 0.135, 0), (0.05, 0.03, 0.004), BAGPL, 0.004, 2))
    for x in (-0.025, 0.025):
        o.append(cyl(f"port{x}", (x, -0.125, 0), (x, -0.16, 0), 0.006, PLASTIC, 12))
    o.append(cyl("port_cap", (0.025, -0.16, 0), (0.025, -0.172, 0), 0.008, BLUE, 12))
    group("ivbag", o, (0, 0, 0), Vector((-1.12 + 0.1, 1.68, -0.55)))

def build_pump():
    """Volumetric infusion pump clamped on the pole. Origin = Pump group (0, 1.15, 0.07) relative to pole. Front = +Z."""
    o = []
    o.append(rbox("body", (0, 0, 0), (0.26, 0.22, 0.11), PLASTIC, 0.03, 5))
    o.append(rbox("frontpanel", (0, 0, 0.054), (0.24, 0.20, 0.006), PANEL_L, 0.02, 3))
    o.append(rbox("screen", (0, 0.03, 0.058), (0.20, 0.10, 0.004), SCREEN, 0.006, 2))
    o.append(rbox("screen_bezel", (0, 0.03, 0.0565), (0.212, 0.112, 0.003), BEZEL, 0.008, 2))
    o.append(rbox("start", (-0.05, -0.065, 0.059), (0.04, 0.026, 0.010), GREEN, 0.006, 3))
    o.append(rbox("stop", (0.05, -0.065, 0.059), (0.04, 0.026, 0.010), RED, 0.006, 3))
    for i in range(4):
        o.append(rbox(f"key{i}", (-0.075 + i * 0.05, -0.095, 0.058), (0.03, 0.014, 0.006), BEZEL, 0.004, 2))
    o.append(rbox("door", (0.135, -0.01, 0.0), (0.012, 0.18, 0.09), PANEL, 0.008, 2))
    # pole clamp at the back
    o.append(rbox("clamp", (0, 0, -0.07), (0.05, 0.08, 0.04), DARK, 0.01, 2))
    o.append(cyl("clamp_screw", (0, 0, -0.07), (0, 0, -0.12), 0.007, CHROME, 12))
    o.append(tube("handle", [(-0.10, 0.11, 0.0), (-0.10, 0.14, 0.0), (0.10, 0.14, 0.0), (0.10, 0.11, 0.0)], 0.009, BEZEL))
    group("pump", o, (0, 0, 0), Vector((-1.12, 1.15, -0.48)))

# ==================================================================== MONITOR
def build_monitor():
    """Patient monitor on a roll stand. Origin = Monitor group; display faces +Z."""
    o = star_base("mb", 0.03, 0.30)
    o.append(cyl("stand", (0, 0.1, 0), (0, 1.26, 0), 0.02, CHROME, 20))
    o.append(rbox("basket", (0, 0.95, 0.07), (0.30, 0.10, 0.14), METAL, 0.01, 2))
    o.append(rbox("mount", (0, 1.29, 0), (0.12, 0.06, 0.08), DARK, 0.015, 3))
    o.append(rbox("housing", (0, 1.52, 0), (0.58, 0.42, 0.08), BEZEL, 0.03, 5))
    o.append(rbox("back", (0, 1.50, -0.05), (0.42, 0.30, 0.04), BEZEL, 0.02, 3))
    o.append(rbox("display", (0, 1.53, 0.037), (0.48, 0.33, 0.006), SCREEN, 0.006, 2))
    o.append(rbox("knob_panel", (0.265, 1.40, 0.04), (0.03, 0.10, 0.008), DARK, 0.004, 2))
    o.append(cyl("knob", (0.265, 1.38, 0.04), (0.265, 1.38, 0.06), 0.014, PLASTIC, 20))
    o.append(tube("mhandle", [(-0.2, 1.73, 0.0), (-0.18, 1.78, 0.0), (0.18, 1.78, 0.0), (0.2, 1.73, 0.0)], 0.012, PLASTIC))
    # leads hanging from the side
    o.append(tube("lead1", [(-0.29, 1.40, 0), (-0.33, 1.2, 0.03), (-0.32, 0.95, 0.06), (-0.30, 0.85, 0.1)], 0.004, DARK))
    group("monitor", o, (0, 0, 0), Vector((-1.15, 0, 0.62)))

# ==================================================================== OVER-BED TABLE
def build_tray():
    o = []
    o.append(rbox("top", (0, 0.80, 0), (0.62, 0.03, 0.42), TABLE, 0.012, 3))
    o.append(rbox("lip", (0, 0.818, 0.205), (0.62, 0.012, 0.012), PANEL_L, 0.004, 2))
    o.append(rbox("arm", (-0.27, 0.77, 0), (0.08, 0.03, 0.30), METAL, 0.01, 2))
    o.append(rbox("column_o", (-0.27, 0.55, 0), (0.06, 0.42, 0.06), METAL, 0.01, 2))
    o.append(rbox("column_i", (-0.27, 0.70, 0), (0.045, 0.16, 0.045), CHROME, 0.008, 2))
    o.append(rbox("base_bar", (-0.27, 0.07, 0), (0.07, 0.04, 0.50), DARK, 0.012, 2))
    for z in (-0.22, 0.22):
        o.append(rbox(f"foot{z}", (-0.02, 0.07, z), (0.56, 0.04, 0.06), DARK, 0.012, 2))
        for x in (-0.27, 0.24):
            o += caster(f"tc{x}{z}", x, z, 0.0, 0.025)
    group("tray", o, (0, 0, 0), Vector((1.12, 0, 0.78)))

TOP = 0.815

def vial(name, x, cap_mat, h=0.06, r=0.016, band=None):
    o = []
    o.append(lathe(name + "_glass", [(0, 0), (r * 0.9, 0), (r, 0.004), (r, h * 0.78), (r * 0.55, h * 0.9), (r * 0.5, h), (r * 0.62, h + 0.004), (r * 0.6, h + 0.008), (0, h + 0.008)], GLASS, (x, 0, 0), 28))
    o.append(lathe(name + "_liquid", [(0, 0.003), (r * 0.85, 0.003), (r * 0.85, h * 0.6), (0, h * 0.6)], FLUID, (x, 0, 0), 24))
    o.append(lathe(name + "_crimp", [(0, h + 0.002), (r * 0.66, h + 0.002), (r * 0.66, h + 0.012), (0, h + 0.012)], METAL, (x, 0, 0), 24))
    o.append(lathe(name + "_cap", [(0, h + 0.012), (r * 0.72, h + 0.012), (r * 0.72, h + 0.020), (r * 0.6, h + 0.022), (0, h + 0.022)], cap_mat, (x, 0, 0), 24))
    o.append(lathe(name + "_label", [(r * 1.02, h * 0.18), (r * 1.02, h * 0.62)], LABEL, (x, 0, 0), 28, cap=False))
    if band:
        o.append(lathe(name + "_band", [(r * 1.03, h * 0.50), (r * 1.03, h * 0.58)], band, (x, 0, 0), 28, cap=False))
    return o

def ampoule(name, x, ring, h=0.075, r=0.014):
    o = [lathe(name + "_glass", [(0, 0), (r * 0.85, 0), (r, 0.006), (r, h * 0.70), (r * 0.45, h * 0.86), (r * 0.36, h * 0.9), (r * 0.6, h * 0.98), (r * 0.62, h * 1.06), (r * 0.3, h * 1.16), (0, h * 1.18)], GLASS, (x, 0, 0), 28)]
    o.append(lathe(name + "_liquid", [(0, 0.004), (r * 0.85, 0.004), (r * 0.85, h * 0.62), (0, h * 0.62)], FLUID, (x, 0, 0), 24))
    o.append(lathe(name + "_ring", [(r * 0.47, h * 0.86), (r * 0.47, h * 0.89)], ring, (x, 0, 0), 24, cap=False))
    o.append(lathe(name + "_label", [(r * 1.02, h * 0.15), (r * 1.02, h * 0.55)], LABEL, (x, 0, 0), 28, cap=False))
    return o

def build_meds():
    tray_w = Vector((1.12, 0, 0.78))
    # MgSO4 50% vials (3)
    o = []
    for i in range(3):
        o += vial(f"mg{i}", i * 0.05 - 0.05, BLUE, 0.062, 0.016, BLUE)
    group("mgvials", o, (0, 0, 0), tray_w + Vector((-0.22, TOP, -0.1)))
    group("water", ampoule("water", 0, PLASTIC, 0.085, 0.015), (0, 0, 0), tray_w + Vector((-0.04, TOP, -0.1)))
    group("lignocaine", vial("lig", 0, ORANGE, 0.05, 0.015, ORANGE), (0, 0, 0), tray_w + Vector((0.1, TOP, -0.1)))
    group("cagluc", ampoule("ca", 0, CAPRED, 0.085, 0.015) + [lathe("ca_band", [(0.0153, 0.03), (0.0153, 0.04)], CAPRED, (0, 0, 0), 24, cap=False)], (0, 0, 0), tray_w + Vector((0.23, TOP, -0.1)))

    # syringe lying along X: barrel -0.07..0.07, plunger out to -0.11, needle to +0.16
    s = []
    s.append(lathe("barrel", [(0, -0.07), (0.0145, -0.07), (0.0145, 0.064), (0.006, 0.072), (0.003, 0.085), (0, 0.085)], GLASS, (0, 0, 0), 28, 0, 50, 1, 1, 'x'))
    for i in range(8):
        xx = -0.06 + i * 0.016
        s.append(lathe(f"mark{i}", [(0.0147, xx), (0.0147, xx + 0.0012)], SCALE, (0, 0, 0), 20, 0, 50, 1, 1, 'x', cap=False))
    fl = rbox("flange", (-0.068, 0, 0), (0.006, 0.07, 0.034), PLASTIC, 0.01, 3)
    s.append(fl)
    s.append(rbox("rod", (-0.095, 0, 0), (0.07, 0.016, 0.004), PLASTIC, 0.002, 1))
    s.append(rbox("rod2", (-0.095, 0, 0), (0.07, 0.004, 0.016), PLASTIC, 0.002, 1))
    s.append(lathe("thumb", [(0, -0.132), (0.016, -0.132), (0.016, -0.128), (0, -0.128)], PLASTIC, (0, 0, 0), 24, 0, 50, 1, 1, 'x'))
    s.append(lathe("stopper", [(0, -0.064), (0.0135, -0.064), (0.0135, -0.056), (0.012, -0.054), (0.0135, -0.052), (0.0135, -0.046), (0, -0.044)], STOPPER, (0, 0, 0), 24, 0, 50, 1, 1, 'x'))
    s.append(lathe("hub", [(0, 0.083), (0.006, 0.083), (0.005, 0.10), (0.0025, 0.104), (0, 0.104)], HUB, (0, 0, 0), 16, 0, 50, 1, 1, 'x'))
    s.append(lathe("needle", [(0, 0.10), (0.0012, 0.10), (0.0012, 0.158), (0, 0.162)], CHROME, (0, 0, 0), 8, 0, 50, 1, 1, 'x'))
    group("syringe", s, (0, 0, 0), tray_w + Vector((-0.12, TOP + 0.016, 0.09)))

    # Taylor reflex hammer: handle along X, rubber triangular head at -X
    h = []
    h.append(lathe("handle", [(0, -0.07), (0.005, -0.07), (0.005, 0.09), (0.0065, 0.095), (0, 0.10)], CHROME, (0, 0, 0), 16, 0, 50, 1, 1, 'x'))
    bm = bmesh.new()
    tri = [(-0.115, 0.0), (-0.062, -0.026), (-0.062, 0.026)]  # triangle in the XZ plane
    top = [bm.verts.new((x, 0.007, z)) for (x, z) in tri]
    bot = [bm.verts.new((x, -0.007, z)) for (x, z) in tri]
    bm.faces.new(top)
    bm.faces.new(list(reversed(bot)))
    for i in range(3):
        bm.faces.new((top[i], bot[i], bot[(i + 1) % 3], top[(i + 1) % 3]))
    head = bm_obj("hhead", bm, CAPRED)
    finish(head, True, 35, 0, (0.006, 4))
    h.append(head)
    group("hammer", h, (0, 0, 0), tray_w + Vector((0.12, TOP + 0.008, 0.1)))

    # oxygen face mask: dome opening downward (-Y), elongated along X
    m = []
    m.append(lathe("mask", [(0.040, -0.018), (0.044, -0.012), (0.042, 0.0), (0.034, 0.016), (0.022, 0.030), (0.012, 0.038), (0.0, 0.040)], MASKG, (0, 0, 0), 32, 0, 60, 1.30, 1.0))
    m.append(lathe("rim", [(0.045, -0.020), (0.047, -0.016), (0.046, -0.013), (0.043, -0.015)], MASKG, (0, 0, 0), 32, 0, 60, 1.30, 1.0))
    m.append(cyl("port", (0.02, 0.025, 0), (0.035, 0.035, 0), 0.007, MASKG, 12))
    m.append(tube("o2tube", [(0.035, 0.035, 0), (0.06, 0.03, 0.02), (0.08, 0.0, 0.05), (0.06, -0.02, 0.09), (0.02, -0.02, 0.1)], 0.0035, TUBEG))
    m.append(tube("strap", [(-0.05, -0.016, -0.03), (-0.06, -0.016, 0.0), (-0.05, -0.016, 0.03)], 0.0018, DARK))
    group("o2mask", m, (0, 0, 0), tray_w + Vector((0.24, TOP + 0.03, 0.1)))

def build_urinebag():
    """Urine drainage bag hanging on the bed frame. Origin = UrineBag group (0.45, 0.3, 0.5). Front = +Z."""
    o = []
    bag = rbox("ubag", (0, 0, 0), (0.17, 0.22, 0.022), BAGPL, 0.012, 3, 0, 1)
    for v in bag.data.vertices:
        k = max(0.0, 1 - (v.co.x / 0.085) ** 2) * max(0.0, 1 - (v.co.y / 0.11) ** 2)
        v.co.z *= 0.4 + 0.6 * k
    o.append(bag)
    for i in range(5):
        y = -0.08 + i * 0.035
        o.append(rbox(f"grad{i}", (0.055, y, 0.0115), (0.03, 0.0018, 0.001), SCALE, 0.0, 1))
    o.append(cyl("outlet", (0, -0.11, 0), (0, -0.15, 0), 0.006, PLASTIC, 12))
    o.append(rbox("tap", (0, -0.155, 0), (0.025, 0.012, 0.012), RED, 0.004, 2))
    o.append(tube("hanger", [(-0.06, 0.11, -0.005), (-0.06, 0.15, -0.02), (-0.04, 0.17, -0.06), (-0.02, 0.14, -0.08)], 0.004, PLASTIC))
    o.append(tube("hanger2", [(0.06, 0.11, -0.005), (0.06, 0.15, -0.02), (0.04, 0.17, -0.06), (0.02, 0.14, -0.08)], 0.004, PLASTIC))
    o.append(cyl("inlet", (0, 0.11, 0), (0, 0.135, 0), 0.007, PLASTIC, 12))
    group("urinebag", o, (0, 0, 0), Vector((0.45, 0.3, 0.5)))

# ==================================================================== build all
build_floor()
build_bed()
# build_head()
# build_trunk()
# build_arm(-1)
# build_arm(1)
# build_thigh("thighL", -0.105)
# build_thigh("thighR", 0.105)
# build_shin("shinL", PATIENT + Vector((0.1 + 0.4, 0.08, -0.105)))
# build_shin("shinR", PATIENT + Vector((0.1 + 0.4, 0.08, 0.105)))
build_ivpole()
build_ivbag()
build_pump()
build_monitor()
build_tray()
build_meds()
build_urinebag()

for name, (objs, origin, world) in GROUPS.items():
    export(name, objs, origin)

# ------------------------------------------------------------------ preview renders
# Place each asset at its ward position (monitor and tray rotated like in the app) for a visual check.
ROT = {"monitor": 30, "tray": -10}
for name, (objs, origin, world) in GROUPS.items():
    if name in ("water", "lignocaine", "cagluc", "mgvials", "syringe", "hammer", "o2mask"):
        # tray items: position + tray rotation about the tray origin
        tw = Vector((1.12, 0, 0.78))
        rel = Vector(world) - tw
        m = Matrix.Translation(tw) @ Matrix.Rotation(math.radians(10), 4, 'Y') @ Matrix.Translation(rel)
    else:
        m = Matrix.Translation(Vector(world))
        if name in ROT:
            m = m @ Matrix.Rotation(math.radians(-ROT[name]), 4, 'Y')
    for o in objs:
        o.matrix_world = m @ o.matrix_world

sc = bpy.context.scene
sc.render.engine = 'BLENDER_WORKBENCH'
sh = sc.display.shading
sh.light = 'STUDIO'
sh.color_type = 'MATERIAL'
sh.show_shadows = True
sh.show_cavity = True
try:
    sh.cavity_type = 'BOTH'
except Exception:
    pass
sc.render.resolution_x = 1280
sc.render.resolution_y = 900
sc.render.film_transparent = False
sc.world = sc.world or bpy.data.worlds.new("w")
cam_data = bpy.data.cameras.new("cam")
cam = bpy.data.objects.new("cam", cam_data)
COLL.objects.link(cam)
sc.camera = cam

def look(eye, target, lens=35):
    eye, target = Vector(eye), Vector(target)
    f = (target - eye).normalized()
    up = Vector((0, 1, 0)) if abs(f.y) < 0.95 else Vector((-1, 0, 0))
    zc = -f
    xc = up.cross(zc).normalized()
    yc = zc.cross(xc)
    M = Matrix((xc, yc, zc)).transposed().to_4x4()
    M.translation = eye
    cam.matrix_world = M
    cam_data.lens = lens

def shot(name, eye, target, lens=35):
    look(eye, target, lens)
    sc.render.filepath = os.path.join(SHOTS, "bl_" + name + ".png")
    bpy.ops.render.render(write_still=True)
    print("RENDER", sc.render.filepath)

# Note: Blender is right-handed, Unity left-handed, so these previews are mirrored left-right
# compared with the app (geometry numbers are identical).
shot("ward", (1.6, 2.0, -2.6), (0, 0.7, 0), 30)
shot("patient", (0.2, 1.7, -1.3), (-0.2, 0.75, 0), 38)
shot("head", (-0.78, 1.20, 0.0), (-0.79, 0.78, 0), 60)
shot("head_side", (-0.60, 0.92, -0.22), (-0.80, 0.78, 0), 55)
shot("tray", (1.35, 1.25, 0.30), (1.10, 0.84, 0.75), 45)
shot("equipment", (-0.2, 1.5, -0.3), (-1.12, 1.25, 0), 30)
print("DONE")
