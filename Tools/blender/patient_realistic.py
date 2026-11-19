# patient_realistic.py - realistic 34-week pregnant patient lying supine (MgSO4 AR sim)
# Source: Blender Studio "Human Base Meshes" bundle v1.4.1 (CC0), object GEO-body_female_realistic.
# Run inside Blender 5.x:  blender --python patient_realistic.py   (or exec() it in a running session)
# Writes Resources/Models/{head,trunk,armL,armR,thighL,thighR,shinL,shinR}.bytes (MGM1) and BuildLogs/patient_pivots.json.
# Everything is authored in patient space = Unity local numbers: X along the bed (head -X), Y up, Z across (patient left = -Z).
# Work happens in a separate scene "PAT"; the source mesh lives in scene "HBM" (appended from the bundle if missing).
import bpy, bmesh, math, os, struct
from mathutils import Vector, Matrix
BASE = r"C:\Users\ahmed\OneDrive\Documents\AR Recearch\UnityProject"
LOGS = os.path.join(BASE, "BuildLogs")
SHOTS = os.path.join(LOGS, "screens")
OUT = os.path.join(BASE, "MgSO4_AR_Simulation", "Assets", "Resources", "Models")

def get_scene():
    sc = bpy.data.scenes.get("PAT")
    if sc is None:
        sc = bpy.data.scenes.new("PAT")
    sc.render.engine = 'BLENDER_WORKBENCH'
    sh = sc.display.shading
    sh.light = 'STUDIO'; sh.color_type = 'MATERIAL'; sh.show_shadows = False
    sh.show_cavity = True
    try: sh.cavity_type = 'BOTH'
    except Exception: pass
    sc.render.resolution_x = 1100; sc.render.resolution_y = 800
    if sc.world is None:
        sc.world = bpy.data.worlds.new("pw")
    sc.world.color = (0.85, 0.87, 0.9)
    if sc.camera is None:
        cd = bpy.data.cameras.new("pcam"); cam = bpy.data.objects.new("pcam", cd)
        sc.collection.objects.link(cam); sc.camera = cam
    return sc

MATS = {}
def mat(name, rgba, smooth=0.3):
    m = bpy.data.materials.get(name)
    if m is None:
        m = bpy.data.materials.new(name)
    if len(rgba) == 3: rgba = (*rgba, 1.0)
    m.diffuse_color = rgba; m["smooth"] = smooth
    MATS[name] = m
    return m
SKIN = mat("skin", (0.80, 0.60, 0.47), 0.35)
LIPS = mat("lips", (0.70, 0.40, 0.38), 0.4)
HAIR = mat("hair", (0.12, 0.08, 0.06), 0.45)
BROW = mat("brow", (0.16, 0.11, 0.08), 0.2)
GOWN = mat("gown", (0.56, 0.74, 0.90), 0.15)
GOWN_D = mat("gown_trim", (0.40, 0.58, 0.78), 0.15)
SCLERA = mat("sclera", (0.95, 0.94, 0.92), 0.7)
IRIS = mat("iris", (0.22, 0.14, 0.09), 0.8)
PUPIL = mat("pupil", (0.03, 0.03, 0.03), 0.9)
MATT = mat("mattress", (0.93, 0.95, 0.97), 0.2)
PILLOW = mat("pillow", (0.99, 0.99, 1.0), 0.15)

def look(cam, eye, target, lens=35, up=(0, 1, 0)):
    eye, target = Vector(eye), Vector(target)
    f = (target - eye).normalized()
    up = Vector(up)
    if abs(f.dot(up)) > 0.95: up = Vector((-1, 0, 0))
    zc = -f; xc = up.cross(zc).normalized(); yc = zc.cross(xc)
    M = Matrix((xc, yc, zc)).transposed().to_4x4(); M.translation = eye
    cam.matrix_world = M; cam.data.lens = lens

def shot(name, eye, target, lens=35, up=(0, 1, 0)):
    sc = get_scene()
    look(sc.camera, eye, target, lens, up)
    sc.render.filepath = os.path.join(SHOTS, name + ".png")
    win = bpy.context.window
    if win is None:
        bpy.ops.render.render(write_still=True, scene=sc.name)
        return sc.render.filepath
    old = win.scene
    win.scene = sc
    try:
        bpy.ops.render.render(write_still=True, scene=sc.name)
    finally:
        win.scene = old
    return sc.render.filepath

def put(name, me, mats=None):
    sc = get_scene()
    o = bpy.data.objects.get(name)
    if o is None:
        o = bpy.data.objects.new(name, me); sc.collection.objects.link(o)
    else:
        old = o.data; o.data = me
        if old.users == 0: bpy.data.meshes.remove(old)
    if mats is not None:
        me.materials.clear()
        for m in mats: me.materials.append(m)
    return o

def clear_scene():
    sc = get_scene()
    for o in list(sc.collection.objects):
        if o.type == 'MESH': bpy.data.objects.remove(o, do_unlink=True)


# ---------------------------------------------------------------- posing (standing coords -> lying patient coords)
import numpy as np

# standing-frame landmarks for the LEFT side (x>0); right side mirrors x
J_SH = np.array([0.165, 0.008, 1.325])   # gleno-humeral joint
E_EL = np.array([0.265, 0.017, 1.010])   # elbow
W_WR = np.array([0.335, -0.020, 0.875])  # wrist
F_TIP = np.array([0.410, -0.100, 0.690]) # mid fingertips
H_HIP = np.array([0.088, -0.010, 0.850]) # hip joint
K_KN = np.array([0.103, 0.005, 0.475])   # knee joint
A_AN = np.array([0.123, 0.040, 0.090])   # ankle joint
C_X = 0.721    # lying x' = -z + C_X  (head top at -0.92)
D_Y = 0.095    # lying y' = -y + D_Y
MATTRESS = 0.016

ARM_IN = 13.0      # deg arm rotated toward the body
ARM_BACK = 6.0     # deg upper arm swung back (down onto the mattress)
LEG_ABD = 2.5      # deg hip abduction
FOOT_PF = 20.0     # deg plantar flexion

def sstep(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3 - 2 * t)

def rot(axis, deg):
    return np.array(Matrix.Rotation(math.radians(deg), 3, axis))

def rot_between(a, b):
    q = Vector(a).normalized().rotation_difference(Vector(b).normalized())
    return np.array(q.to_matrix())

def apply_rot(P, R, c):
    return (P - c) @ R.T + c

def to_lying(P):
    return np.stack([-P[:, 2] + C_X, -P[:, 1] + D_Y, -P[:, 0]], axis=1)

def lying_pt(p):
    p = np.asarray(p, float)
    return np.array([-p[2] + C_X, -p[1] + D_Y, -p[0]])

def pose_points(P, side):
    """Pose one side (side=+1: standing x>0 = patient left)."""
    s = side
    M = np.array([s, 1, 1.0])
    J, E, W, T = J_SH * M, E_EL * M, W_WR * M, F_TIP * M
    H, K, A = H_HIP * M, K_KN * M, A_AN * M
    x, z = P[:, 0] * s, P[:, 2]
    # ---------------- arm weights
    zs = [0.60, 0.80, 0.95, 1.10, 1.20, 1.30, 1.40, 1.46]
    xs = [0.29, 0.26, 0.20, 0.15, 0.155, 0.145, 0.13, 0.60]
    xsplit = np.interp(z, zs, xs)
    band = np.interp(z, [1.0, 1.15, 1.25, 1.4], [0.012, 0.02, 0.05, 0.05])
    w_arm = sstep(-0.5, 0.5, (x - xsplit) / band)
    w_arm[z > 1.45] = 0
    # rotations
    R1 = rot('X', ARM_BACK) @ rot('Y', s * ARM_IN)
    E1 = (E - J) @ R1.T + J
    W1 = (W - J) @ R1.T + J
    T1 = (T - J) @ R1.T + J
    Wt = W1.copy(); Wt[1] = 0.085 - 0.026
    d = Wt - E1; d *= np.linalg.norm(W1 - E1) / np.linalg.norm(d)
    R2 = rot_between(W1 - E1, d)
    W2 = E1 + d
    T2 = (T1 - E1) @ R2.T + E1
    Tt = T2.copy(); Tt[1] = 0.085 - 0.012
    d3 = Tt - W2; d3 *= np.linalg.norm(T2 - W2) / np.linalg.norm(d3)
    R3 = rot_between(T2 - W2, d3)
    u = (E - J) / np.linalg.norm(E - J)
    w_el = sstep(-0.035, 0.035, (P - E) @ u)
    uf = (W - E) / np.linalg.norm(W - E)
    w_wr = sstep(-0.02, 0.02, (P - W) @ uf)
    P1 = apply_rot(P, R1, J)
    P2 = apply_rot(P1, R2, E1)
    P3 = apply_rot(P2, R3, W2)
    Parm = P1 * (1 - w_el)[:, None] + (P2 * (1 - w_wr)[:, None] + P3 * w_wr[:, None]) * w_el[:, None]
    # ---------------- leg
    w_leg = sstep(0.90, 0.80, z) * sstep(-0.004, 0.02, x) * (1 - w_arm)
    RL = rot('Y', -s * LEG_ABD)
    w_ft = sstep(0.13, 0.065, z) * (x > 0)
    RF = rot('X', FOOT_PF)
    Pf = apply_rot(P, RF, A)
    Pfoot = P * (1 - w_ft)[:, None] + Pf * w_ft[:, None]
    Pleg = apply_rot(Pfoot, RL, H)
    out = P.copy()
    out = out * (1 - w_leg)[:, None] + Pleg * w_leg[:, None]
    out = out * (1 - w_arm)[:, None] + Parm * w_arm[:, None]
    joints = {"shoulder": J, "elbow": E1, "wrist": W2, "hip": H,
              "knee": (K - H) @ RL.T + H, "ankle": (A - H) @ RL.T + H}
    return out, w_arm, joints

def pose_body(P):
    Pl, wl, jl = pose_points(P, 1)
    Pr, wr, jr = pose_points(P, -1)
    left = (P[:, 0] >= 0)
    out = np.where(left[:, None], Pl, Pr)
    warm = np.where(left, wl, wr)
    return out, warm, jl, jr

BELLY_C = (0.0, 0.972)            # x, z centre of the dome (standing coords)
BELLY = dict(A=0.128, rx=0.165, rzt=0.190, rzb=0.150, p=0.7)

def belly_disp(P, amount=1.0):
    """Forward (-y) displacement of the front abdomen forming a 34-week dome (standing coords)."""
    b = BELLY
    dz = P[:, 2] - BELLY_C[1]
    rz = np.where(dz > 0, b["rzt"], b["rzb"])
    s = (P[:, 0] / b["rx"]) ** 2 + (dz / rz) ** 2
    prof = np.clip(1 - s, 0, 1) ** b["p"]
    front = sstep(0.04, -0.06, P[:, 1])
    return b["A"] * amount * prof * front

def edges_of(me):
    e = np.empty(len(me.edges) * 2, dtype=np.int64); me.edges.foreach_get("vertices", e); return e.reshape(-1, 2)

def laplace(P, E, mask, it=10, lam=0.5):
    n = len(P)
    deg = np.zeros(n); np.add.at(deg, E[:, 0], 1); np.add.at(deg, E[:, 1], 1)
    for _ in range(it):
        acc = np.zeros_like(P)
        np.add.at(acc, E[:, 0], P[E[:, 1]]); np.add.at(acc, E[:, 1], P[E[:, 0]])
        avg = acc / np.maximum(deg, 1)[:, None]
        P = P + (avg - P) * (lam * mask)[:, None]
    return P

def belly(P, E=None, amount=1.0, smooth_it=15):
    D = belly_disp(P, amount)
    out = P.copy(); out[:, 1] -= D
    if E is not None and smooth_it:
        m = np.clip(D / 0.02, 0, 1)
        out = laplace(out, E, m, smooth_it, 0.5)
    return out

def mattress(Pl, f0=MATTRESS, w=0.010):
    """Soft contact with the mattress: anything below f0 is squashed asymptotically toward f0 - w."""
    y = Pl[:, 1]
    u = np.maximum(f0 - y, 0)
    nu = w * (1 - np.exp(-u / w))
    out = Pl.copy(); out[:, 1] = np.where(y < f0, f0 - nu, y)
    return out

def get_co(me):
    a = np.empty(len(me.vertices) * 3); me.vertices.foreach_get("co", a); return a.reshape(-1, 3)

def set_co(me, P):
    me.vertices.foreach_set("co", P.reshape(-1)); me.update()


# ---------------------------------------------------------------- build stages
from mathutils.bvhtree import BVHTree
from mathutils.kdtree import KDTree

SRC_OBJ = "GEO-body_female_realistic"
HEAD_C_ST = np.array([0.0, -0.037, 1.517])        # between the ears (standing)
X_NECK = -0.690                                    # head / trunk cut (lying x)
OV_HEAD = 0.015
INSET = 0.0015

def dgraph():
    dg = get_scene().view_layers[0].depsgraph
    dg.update()
    return dg

SRC_BLEND = os.path.join(LOGS, "hbm", "human-base-meshes-bundle-v1.4.1", "human_base_meshes_bundle.blend")

def ensure_source():
    """Make sure the CC0 Blender Studio realistic female base mesh is loaded (scene 'HBM')."""
    if SRC_OBJ in bpy.data.objects: return
    sc = bpy.data.scenes.get("HBM") or bpy.data.scenes.new("HBM")
    with bpy.data.libraries.load(SRC_BLEND, link=False) as (src, dst):
        dst.collections = [c for c in src.collections if c == "Body Female - Realistic"]
    for c in dst.collections:
        sc.collection.children.link(c)

def standing_mesh(level=2):
    ensure_source()
    o = bpy.data.objects[SRC_OBJ]
    mr = o.modifiers["Multires"]
    mr.levels = level
    dg = bpy.data.scenes["HBM"].view_layers[0].depsgraph; dg.update()
    me = bpy.data.meshes.new_from_object(o.evaluated_get(dg))
    return me

def new_mesh_obj(name, me, mats):
    o = put(name, me, mats)
    for p in o.data.polygons: p.use_smooth = True
    return o

def build_posed():
    me = standing_mesh(2); me.name = "P_body"
    P0 = get_co(me); E = edges_of(me)
    P = belly(P0, E)
    Pp, warm, jl, jr = pose_body(P)
    L = mattress(to_lying(Pp))
    set_co(me, L)
    a = me.attributes.new("w_arm", 'FLOAT', 'POINT'); a.data.foreach_set("value", warm.astype(np.float32))
    for i, nm in enumerate(("sx", "sy", "sz")):
        a = me.attributes.new(nm, 'FLOAT', 'POINT'); a.data.foreach_set("value", P0[:, i].astype(np.float32))
    o = new_mesh_obj("P_body", me, [SKIN, LIPS, BROW])
    # eyes
    for side, nm in ((1, "L"), (-1, "R")):
        src = bpy.data.objects[SRC_OBJ + ".eye." + nm]
        em = src.data.copy(); em.name = "P_eye" + nm
        Q = get_co(em)
        mw = np.array(src.matrix_world)
        Q = Q @ mw[:3, :3].T + mw[:3, 3]; Q[:, 0] += 1.34
        set_co(em, to_lying(Q))
        eo = new_mesh_obj("P_eye" + nm, em, [SCLERA, IRIS, PUPIL])
        c = (to_lying(Q).min(0) + to_lying(Q).max(0)) / 2
        bm = bmesh.new(); bm.from_mesh(em)
        def cosf(f):
            d = np.array(f.calc_center_median()) - c; return d[1] / np.linalg.norm(d)
        fr = [f for f in bm.faces if cosf(f) > math.cos(math.radians(40))]
        es = list({e for f in fr for e in f.edges})
        bmesh.ops.subdivide_edges(bm, edges=es, cuts=1, use_grid_fill=True)
        def cosv(co):
            d = np.array(co) - c; return d[1] / np.linalg.norm(d)
        for f in bm.faces: f.material_index = 0; f.smooth = True
        paint_field(bm, lambda co: math.cos(math.radians(31)) - cosv(co), 1)
        paint_field(bm, lambda co: math.cos(math.radians(12)) - cosv(co), 2)
        bm.to_mesh(em); bm.free()
    joints = {}
    for side, jd in (("L", jl), ("R", jr)):
        for k, v in jd.items():
            joints[k + side] = [float(x) for x in lying_pt(v)]
    o["joints"] = str(joints)
    return joints

def attr(me, nm):
    a = np.empty(len(me.vertices), dtype=np.float32); me.attributes[nm].data.foreach_get("value", a); return a

# ---------------------------------------------------------------- bmesh utilities
def bm_from(obj):
    bm = bmesh.new(); bm.from_mesh(obj.data); return bm

def keep_faces(bm, pred):
    """pred(vert)->bool ; delete faces having any vert failing pred; drop loose verts."""
    ok = {v: pred(v) for v in bm.verts}
    kill = [f for f in bm.faces if not all(ok[v] for v in f.verts)]
    bmesh.ops.delete(bm, geom=kill, context='FACES')
    loose = [v for v in bm.verts if not v.link_faces]
    bmesh.ops.delete(bm, geom=loose, context='VERTS')

def bisect(bm, co, no, geom_filter=None):
    geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
    if geom_filter is not None:
        fs = [f for f in bm.faces if geom_filter(f)]
        es = list({e for f in fs for e in f.edges}); vs = list({v for f in fs for v in f.verts})
        geom = vs + es + fs
    bmesh.ops.bisect_plane(bm, geom=geom, dist=1e-6, plane_co=Vector(co), plane_no=Vector(no))

def boundary_loops(bm):
    edges = [e for e in bm.edges if e.is_boundary]
    seen = set(); loops = []
    adj = {}
    for e in edges:
        for v in e.verts: adj.setdefault(v, []).append(e)
    for e0 in edges:
        if e0 in seen: continue
        loop = [e0.verts[0]]; seen.add(e0); cur = e0.verts[1]; prev_e = e0
        while cur is not loop[0]:
            loop.append(cur)
            nxt = [e for e in adj[cur] if e not in seen]
            if not nxt: break
            prev_e = nxt[0]; seen.add(prev_e); cur = prev_e.other_vert(cur)
        loops.append(loop)
    return loops

def dome_cap(bm, loop, out_dir, height_frac=0.5, rings=4, mat_index=0):
    """Close a boundary loop with a dome bulging along out_dir."""
    pts = [v.co.copy() for v in loop]
    c = sum(pts, Vector()) / len(pts)
    r = sum(((p - c).length for p in pts)) / len(pts)
    n = Vector(out_dir).normalized()
    h = r * height_frac
    prev = loop
    for k in range(1, rings):
        t = k / rings
        ring = []
        for v in loop:
            q = c + (v.co - c) * math.cos(t * math.pi / 2) + n * h * math.sin(t * math.pi / 2)
            ring.append(bm.verts.new(q))
        for i in range(len(loop)):
            j = (i + 1) % len(loop)
            try:
                f = bm.faces.new((prev[i], prev[j], ring[j], ring[i])); f.material_index = mat_index; f.smooth = True
            except ValueError:
                pass
        prev = ring
    top = bm.verts.new(c + n * h)
    for i in range(len(loop)):
        j = (i + 1) % len(loop)
        try:
            f = bm.faces.new((prev[i], prev[j], top)); f.material_index = mat_index; f.smooth = True
        except ValueError:
            pass

def cap_all(bm, out_dir_fn, height_frac=0.5, rings=4, min_len=6):
    for loop in boundary_loops(bm):
        if len(loop) < min_len: continue
        pts = [v.co for v in loop]
        c = sum(pts, Vector()) / len(pts)
        dome_cap(bm, loop, out_dir_fn(c), height_frac, rings)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)

def inset_band(bm, dist_fn, band):
    """Push verts that lie beyond a cut (dist_fn>0 means inside the parent part) inward a little."""
    bm.normal_update()
    moves = []
    for v in bm.verts:
        d = dist_fn(v.co)
        if d > 0:
            t = min(1.0, d / (band * 0.5))
            moves.append((v, -v.normal * INSET * t))
    for v, m in moves: v.co += m

def obj_from_bm(name, bm, mats):
    me = bpy.data.meshes.new(name)
    for m in mats: me.materials.append(m)
    bm.to_mesh(me); bm.free()
    o = new_mesh_obj(name, me, None)
    return o

def decimate(o, target_verts):
    n = len(o.data.vertices)
    if n <= target_verts: return o
    d = o.modifiers.new("dec", 'DECIMATE'); d.ratio = target_verts / n
    d.use_collapse_triangulate = True
    dg = dgraph()
    me = bpy.data.meshes.new_from_object(o.evaluated_get(dg))
    o.modifiers.clear(); old = o.data; o.data = me
    if old.users == 0: bpy.data.meshes.remove(old)
    for p in o.data.polygons: p.use_smooth = True
    return o

# ---------------------------------------------------------------- face regions (standing coords recovered from lying coords)
def st(co):
    return (-co.z, D_Y - co.y, C_X - co.x)

def lip_field(co):
    x, y, z = st(co)
    zc = 1.4685 + 0.0018 * max(0.0, 1 - abs(x) / 0.010)       # cupid's bow
    rz = 0.0105 if z > zc else 0.0098
    f = (x / 0.0245) ** 2 + ((z - zc) / rz) ** 2 - 1.0
    return f + max(0.0, (y + 0.139) * 150.0)

def brow_field(co):
    x, y, z = st(co)
    ax = abs(x)
    t = (ax - 0.012) / 0.043
    tc = min(max(t, 0.0), 1.0)
    zc = 1.5530 + 0.0058 * math.sin(math.pi * min(1.0, tc * 1.15)) - 0.0015 * tc
    half = 0.0033 * (1 - tc) ** 0.7 + 0.0010 * tc
    f = abs(z - zc) - half
    if t < 0: f = max(f, -t * 0.043 * 1.5)
    if t > 1: f = max(f, (t - 1) * 0.043 * 1.5)
    return f + max(0.0, (y + 0.085) * 10)

HAIR_PHI = [0, 25, 45, 60, 72, 85, 100, 112, 125, 145, 180]
HAIR_Z = [1.600, 1.597, 1.588, 1.574, 1.562, 1.552, 1.542, 1.515, 1.490, 1.470, 1.462]
def hairline_z(co):
    x, y, z = st(co)
    rx, ry = x - HEAD_C_ST[0], y - HEAD_C_ST[1]
    phi = math.degrees(math.atan2(abs(rx), -ry))
    return float(np.interp(phi, HAIR_PHI, HAIR_Z)), (x, y, z)

def hair_field(co):
    zh, (x, y, z) = hairline_z(co)
    e = ((abs(x) - 0.082) / 0.024) ** 2 + ((y + 0.034) / 0.032) ** 2 + ((z - 1.532) / 0.060) ** 2 - 1.0
    a, c = zh - z, -e * 0.01
    return 0.5 * (a + c + math.sqrt((a - c) ** 2 + 0.008 ** 2))

def lying_head_centre():
    return lying_pt(HEAD_C_ST)

def part_head(target=16000):
    body = bpy.data.objects["P_body"]
    bm = bm_from(body)
    wl = bm.verts.layers.float.get("w_arm")
    xc = X_NECK + OV_HEAD
    bisect(bm, (xc, 0, 0), (1, 0, 0), lambda f: abs(f.calc_center_median().x - xc) < 0.05 and all(v[wl] < 0.5 for v in f.verts))
    keep_faces(bm, lambda v: v.co.x <= xc + 1e-6 and v[wl] < 0.5)
    inset_band(bm, lambda co: co.x - X_NECK, OV_HEAD)
    cap_all(bm, lambda c: Vector((1, 0, 0)), 0.15, 3)
    o = obj_from_bm("X_head", bm, [SKIN, LIPS, BROW])
    decimate(o, target)
    bm = bm_from(o)
    paint_field(bm, lip_field, 1)
    paint_field(bm, brow_field, 2)
    bm.normal_update()
    bv = {v for f in bm.faces if f.material_index == 2 for v in f.verts}
    for v in bv:
        if all(f.material_index == 2 for f in v.link_faces): v.co += v.normal * 0.0007
    bm.to_mesh(o.data); bm.free()
    return o

def build_hair(target=4000):
    body = bpy.data.objects["P_body"]
    bm = bm_from(body)
    wl = bm.verts.layers.float.get("w_arm")
    keep_faces(bm, lambda v: v.co.x < X_NECK + 0.03 and v[wl] < 0.5)
    cut_field(bm, hair_field)
    o = obj_from_bm("X_hair", bm, [HAIR])
    decimate(o, target)
    bm = bm_from(o)
    bm.normal_update()
    mv = []
    for v in bm.verts:
        zh, (x, y, z) = hairline_z(v.co)
        h = z - zh
        psi = math.atan2(x, z - 1.50)
        groove = 0.0007 * math.sin(psi * 64.0) * float(sstep(0.004, 0.02, h))
        t = 0.0015 + 0.0062 * float(sstep(0.0, 0.03, h)) + 0.0035 * float(sstep(1.57, 1.63, z)) + groove
        mv.append((v, v.normal * t))
    for v, m in mv: v.co += m
    bm_laplace(bm, 2, 0.3, True)
    rim(bm, 0.0026)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(o.data); bm.free()
    # low bun resting on the pillow beyond the crown
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=28, v_segments=16, radius=1.0)
    c = Vector((-0.938, 0.086, 0.0))
    for v in bm.verts:
        n = v.co.copy()
        th = math.atan2(n.z, n.x); ph = math.asin(max(-1, min(1, n.y)))
        rr = 1.0 + 0.05 * math.sin(5 * th + 4 * ph)
        v.co = c + Vector((n.x * 0.033, n.y * 0.030, n.z * 0.043)) * rr
    for f in bm.faces: f.smooth = True
    bun = obj_from_bm("X_bun", bm, [HAIR])
    return o, bun

def part_trunk_skin(depth=0.22):
    body = bpy.data.objects["P_body"]
    bm = bm_from(body)
    wl = bm.verts.layers.float.get("w_arm")
    bisect(bm, (X_NECK, 0, 0), (1, 0, 0), lambda f: abs(f.calc_center_median().x - X_NECK) < 0.05 and all(v[wl] < 0.5 for v in f.verts))
    keep_faces(bm, lambda v: X_NECK - 1e-6 <= v.co.x <= X_NECK + depth and v[wl] < 0.5)
    return obj_from_bm("X_trunkskin", bm, [SKIN])

def part_arm(side, J):
    sg = -1 if side == "L" else 1          # patient left is z' < 0
    body = bpy.data.objects["P_body"]
    bm = bm_from(body)
    wl = bm.verts.layers.float.get("w_arm")
    keep_faces(bm, lambda v: v[wl] > 0.22 and v.co.z * sg > 0.05)
    bm.normal_update()
    mv = [(v, -v.normal * INSET * min(1.0, (0.5 - v[wl]) / 0.12)) for v in bm.verts if v[wl] < 0.5]
    for v, m in mv: v.co += m
    bmesh.ops.holes_fill(bm, edges=[e for e in bm.edges if e.is_boundary], sides=0)
    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return obj_from_bm("X_arm" + side, bm, [SKIN])

def leg_axis(J, side):
    H = Vector(J["hip" + side]); K = Vector(J["knee" + side]); return H, K, (K - H).normalized()

def part_thigh(side, J):
    sg = -1 if side == "L" else 1
    H, K, d = leg_axis(J, side)
    body = bpy.data.objects["P_body"]
    bm = bm_from(body)
    wl = bm.verts.layers.float.get("w_arm")
    legf = lambda f: all(v[wl] < 0.5 for v in f.verts) and abs(f.calc_center_median().z) < 0.3
    top = H - d * 0.03
    bisect(bm, top, d, lambda f: legf(f) and abs((f.calc_center_median() - top).dot(d)) < 0.06)
    bisect(bm, K, d, lambda f: legf(f) and abs((f.calc_center_median() - K).dot(d)) < 0.06)
    bisect(bm, (0, 0, 0), (0, 0, 1), lambda f: legf(f) and abs(f.calc_center_median().z) < 0.03 and f.calc_center_median().x > -0.3)
    keep_faces(bm, lambda v: v[wl] < 0.5 and (v.co - top).dot(d) >= -1e-6 and (v.co - K).dot(d) <= 1e-6 and v.co.z * sg >= -1e-6)
    cap_all(bm, lambda c: d if (c - K).dot(d) > -0.1 else -d, 0.45, 4)
    return obj_from_bm("X_thigh" + side, bm, [SKIN])

def part_shin(side, J, ov=0.02):
    sg = -1 if side == "L" else 1
    H, K, d = leg_axis(J, side)
    body = bpy.data.objects["P_body"]
    bm = bm_from(body)
    wl = bm.verts.layers.float.get("w_arm")
    top = K - d * ov
    bisect(bm, top, d, lambda f: all(v[wl] < 0.5 for v in f.verts) and abs((f.calc_center_median() - top).dot(d)) < 0.06)
    keep_faces(bm, lambda v: v[wl] < 0.5 and (v.co - top).dot(d) >= -1e-6 and v.co.z * sg > 0)
    inset_band(bm, lambda co: -(co - K).dot(d), ov)
    cap_all(bm, lambda c: -d, 0.45, 4)
    return obj_from_bm("X_shin" + side, bm, [SKIN])

# ---------------------------------------------------------------- scalar-field cut (clean curved edges)
def split_field(bm, fn, per_vert=False):
    """Split every edge crossed by the zero set of fn and connect the crossings. Returns the value dict."""
    val = {v: (fn(v) if per_vert else fn(v.co)) for v in bm.verts}
    newv = set()
    for e in list(bm.edges):
        a, b = e.verts
        va, vb = val[a], val[b]
        if (va > 0) != (vb > 0) and abs(va - vb) > 1e-12:
            t = va / (va - vb)
            if t < 1e-3:
                val[a] = 0.0; newv.add(a); continue
            if t > 1 - 1e-3:
                val[b] = 0.0; newv.add(b); continue
            ne, nv = bmesh.utils.edge_split(e, a, t)
            val[nv] = 0.0; newv.add(nv)
    for f in list(bm.faces):
        zs = [v for v in f.verts if v in newv]
        if len(zs) == 2:
            a, b = zs
            if b not in [e.other_vert(a) for e in a.link_edges]:
                try:
                    bmesh.utils.face_split(f, a, b)
                except Exception:
                    pass
    return val

def face_side(f, val):
    s = sum(val.get(v, 0.0) for v in f.verts)
    return s

def cut_field(bm, fn, per_vert=False):
    """Clean cut along the zero set of fn; deletes the fn>0 side."""
    val = split_field(bm, fn, per_vert)
    kill = [v for v in bm.verts if val.get(v, 0.0) > 1e-9]
    bmesh.ops.delete(bm, geom=kill, context='VERTS')

def paint_field(bm, fn, mat_index):
    """Faces on the fn<0 side get mat_index (edges split along the boundary first)."""
    val = split_field(bm, fn)
    for f in bm.faces:
        if all(val.get(v, 0.0) <= 1e-9 for v in f.verts) and any(val.get(v, 0.0) < -1e-9 for v in f.verts):
            f.material_index = mat_index
    return val

def smooth_boundary(bm, it=6, lam=0.5):
    for _ in range(it):
        upd = {}
        for v in bm.verts:
            be = [e for e in v.link_edges if e.is_boundary]
            if len(be) == 2:
                upd[v] = v.co.lerp((be[0].other_vert(v).co + be[1].other_vert(v).co) / 2, lam)
        for v, c in upd.items(): v.co = c

def bm_laplace(bm, it=10, lam=0.5, pin_boundary=True):
    for _ in range(it):
        upd = {}
        for v in bm.verts:
            if pin_boundary and v.is_boundary: continue
            nb = [e.other_vert(v).co for e in v.link_edges]
            if nb: upd[v] = v.co.lerp(sum(nb, Vector()) / len(nb), lam)
        for v, c in upd.items(): v.co = c

def rim(bm, depth=0.004, mat_index=0):
    """Turn open edges into a folded fabric edge (gives the shell visible thickness)."""
    bm.normal_update()
    be = [e for e in bm.edges if e.is_boundary]
    if not be: return
    nrm = {v: v.normal.copy() for e in be for v in e.verts}
    r = bmesh.ops.extrude_edge_only(bm, edges=be)
    nv = [g for g in r["geom"] if isinstance(g, bmesh.types.BMVert)]
    # map new verts to originals through their connecting edge
    for v in nv:
        src = [e.other_vert(v) for e in v.link_edges if e.other_vert(v) in nrm]
        if src:
            v.co = src[0].co - nrm[src[0]] * depth
    for f in bm.faces:
        if f.material_index < 0: f.material_index = mat_index

# ---------------------------------------------------------------- gown
HEM_X = 0.085             # mid-thigh hem (lying x)
SLEEVE_LEN = 0.150        # sleeve length from the shoulder joint along the upper arm
GOWN_OFF = 0.012
NECK_N = Vector((-1.0, 0.65, 0.0))

def neck_field(co):
    az = abs(co.z)
    x0 = -0.585 - 0.085 * min(1.0, (az / 0.16)) ** 2
    return -(co.x - x0) + 0.65 * (co.y - 0.15)

def arm_axis(J, side):
    S = Vector(J["shoulder" + side]); E = Vector(J["elbow" + side]); return S, (E - S).normalized()

def thigh_tops(P, W, xs):
    out = []
    for x in xs:
        m = (np.abs(P[:, 0] - x) < 0.012) & (W < 0.5) & (np.abs(P[:, 2]) < 0.22)
        out.append(float(P[m, 1].max()) if m.any() else 0.15)
    return out

def build_gown(J, voxel=0.007):
    body = bpy.data.objects["P_body"]
    P = get_co(body.data); W = attr(body.data, "w_arm")
    bm = bm_from(body)
    wl = bm.verts.layers.float.get("w_arm")
    axes = {s: arm_axis(J, s) for s in "LR"}
    def keep(v):
        if v.co.x < X_NECK - 0.01 or v.co.x > HEM_X + 0.04: return False
        if v[wl] > 0.5:
            S, u = axes["L" if v.co.z < 0 else "R"]
            return (v.co - S).dot(u) < SLEEVE_LEN + 0.04
        return True
    keep_faces(bm, keep)
    bm.normal_update()
    for v in bm.verts:
        flare = 0.009 * float(sstep(HEM_X - 0.16, HEM_X + 0.02, v.co.x)) if v.co.x > -0.07 else 0.0
        v.co += v.normal * (GOWN_OFF + flare)
    bmesh.ops.holes_fill(bm, edges=[e for e in bm.edges if e.is_boundary], sides=0)
    # filler drape between the thighs and over the groin
    xs = list(np.linspace(-0.07, HEM_X + 0.04, 8))
    tops = thigh_tops(P, W, xs)
    pts = []
    for x, yt in zip(xs, tops):
        for z in (-0.075, 0.075):
            pts += [Vector((x, 0.04, z)), Vector((x, yt - 0.012, z * 0.9))]
    hull = bmesh.new()
    vv = [hull.verts.new(p) for p in pts]
    bmesh.ops.convex_hull(hull, input=vv)
    me_h = bpy.data.meshes.new("tmp_hull"); hull.to_mesh(me_h); hull.free()
    bm.from_mesh(me_h); bpy.data.meshes.remove(me_h)
    o = obj_from_bm("G_raw", bm, [GOWN])
    r = o.modifiers.new("rm", 'REMESH'); r.mode = 'VOXEL'; r.voxel_size = voxel
    dg = dgraph()
    me = bpy.data.meshes.new_from_object(o.evaluated_get(dg)); o.modifiers.clear()
    old = o.data; o.data = me; bpy.data.meshes.remove(old)
    me.materials.clear(); me.materials.append(GOWN)
    # smooth away anatomy, then keep it outside the skin
    G = get_co(me); E = edges_of(me)
    G = laplace(G, E, np.ones(len(G)), 25, 0.5)
    _b = bm_from(body); bvh = BVHTree.FromBMesh(_b); _b.free()
    for i in range(len(G)):
        loc, nrm, idx, dist = bvh.find_nearest(Vector(G[i]))
        if loc is None: continue
        d = (Vector(G[i]) - loc).dot(nrm)
        if d < 0.006:
            G[i] = np.array(loc + nrm * 0.006)
    G = laplace(G, E, np.ones(len(G)), 3, 0.3)
    G = mattress(G, MATTRESS + 0.002)
    set_co(me, G)
    # open the neckline, hem and sleeves
    bm = bmesh.new(); bm.from_mesh(me)
    cut_field(bm, neck_field)
    cut_field(bm, lambda co: co.x - HEM_X)
    kd = body_kd()
    for s in "LR":
        S, u = axes[s]
        sg = -1 if s == "L" else 1
        def fs(co, S=S, u=u, sg=sg):
            if co.z * sg < 0.08: return -1.0
            if W[kd.find(co)[1]] < 0.5: return -1.0
            return (co - S).dot(u) - SLEEVE_LEN
        cut_field(bm, fs)
    # drop stray islands (keep the biggest)
    bm.verts.ensure_lookup_table()
    smooth_boundary(bm, 4, 0.5)
    bm.to_mesh(me); bm.free()
    return o

_KD = {}
def body_kd():
    if "kd" not in _KD:
        P = get_co(bpy.data.objects["P_body"].data)
        kd = KDTree(len(P))
        for i, p in enumerate(P): kd.insert(p, i)
        kd.balance(); _KD["kd"] = kd
    return _KD["kd"]

def split_gown(J, gown, decim=None):
    """Split the gown shell into the trunk part and the two sleeves along the arm crease (smooth field cut)."""
    body = bpy.data.objects["P_body"]
    W = attr(body.data, "w_arm")
    kd = body_kd()
    out = {}
    for name in ("trunk", "L", "R"):
        bm = bmesh.new(); bm.from_mesh(gown.data)
        w = {v: float(W[kd.find(v.co)[1]]) for v in bm.verts}
        for _ in range(6):
            nw = {}
            for v in bm.verts:
                nb = [w[e.other_vert(v)] for e in v.link_edges]
                nw[v] = 0.5 * w[v] + 0.5 * sum(nb) / len(nb) if nb else w[v]
            w = nw
        if name == "trunk":
            cut_field(bm, lambda v: w[v] - 0.55 if v in w else 0.0, True)
        else:
            sg = -1 if name == "L" else 1
            cut_field(bm, lambda v: 0.40 - w[v] if v in w else 0.0, True)
            keep_faces(bm, lambda v: v.co.z * sg > 0.0)
            bm.normal_update()
            for v in bm.verts:
                v.co += v.normal * 0.0018
        # remove tiny islands
        islands = []
        seen = set()
        for f in bm.faces:
            if f in seen: continue
            stack = [f]; comp = []
            seen.add(f)
            while stack:
                g = stack.pop(); comp.append(g)
                for e in g.edges:
                    for h in e.link_faces:
                        if h not in seen: seen.add(h); stack.append(h)
            islands.append(comp)
        islands.sort(key=len, reverse=True)
        kill = [f for comp in islands[1:] for f in comp]
        bmesh.ops.delete(bm, geom=kill, context='FACES')
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')
        smooth_boundary(bm, 3, 0.5)
        o = obj_from_bm("G_" + name, bm, [GOWN, GOWN_D])
        if decim: decimate(o, decim[name])
        if name == "trunk": neckline_trim(o)
        bm = bm_from(o); rim(bm, 0.004); bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        bm.to_mesh(o.data); bm.free()
        out[name] = o
    return out

def neckline_trim(gown_trunk, r=0.0045):
    """Tube along the neckline edge of the trunk gown."""
    bm = bmesh.new(); bm.from_mesh(gown_trunk.data)
    loops = boundary_loops(bm)
    best = None
    for lp in loops:
        c = sum((v.co for v in lp), Vector()) / len(lp)
        if c.x < -0.5 and abs(c.z) < 0.08 and len(lp) > 20:
            if best is None or len(lp) > len(best): best = lp
    if best is None:
        bm.free(); return None
    pts = [v.co.copy() for v in best]
    bm.free()
    cu = bpy.data.curves.new("trim", 'CURVE'); cu.dimensions = '3D'
    sp = cu.splines.new('POLY'); sp.points.add(len(pts) - 1)
    for i, p in enumerate(pts): sp.points[i].co = (p.x, p.y, p.z, 1)
    sp.use_cyclic_u = True
    cu.bevel_depth = r; cu.bevel_resolution = 2
    co = bpy.data.objects.new("trim_tmp", cu); get_scene().collection.objects.link(co)
    dg = dgraph()
    me = bpy.data.meshes.new_from_object(co.evaluated_get(dg))
    bpy.data.objects.remove(co); bpy.data.curves.remove(cu)
    me.materials.clear(); me.materials.append(GOWN_D)
    return new_mesh_obj("G_trim", me, None)

# ---------------------------------------------------------------- export (MGM1, same layout as ward_models.export)
def export_parts(asset, objs, origin):
    subs = {}; order = []
    org = Vector(origin)
    for o in objs:
        me = o.data
        me.calc_loop_triangles()
        mw = o.matrix_world
        nm = mw.to_3x3().inverted().transposed()
        cn = me.corner_normals
        for tri in me.loop_triangles:
            mt = me.materials[tri.material_index] if len(me.materials) else None
            key = mt.name if mt else "default"
            if key not in subs:
                subs[key] = {"mat": mt, "verts": [], "map": {}, "tris": []}; order.append(key)
            s = subs[key]; idx = []
            for li in tri.loops:
                v = me.loops[li].vertex_index
                p = mw @ me.vertices[v].co - org
                nrm = (nm @ cn[li].vector).normalized()
                k = (round(p.x, 5), round(p.y, 5), round(p.z, 5), round(nrm.x, 3), round(nrm.y, 3), round(nrm.z, 3))
                i = s["map"].get(k)
                if i is None:
                    i = len(s["verts"]); s["map"][k] = i; s["verts"].append((p.x, p.y, p.z, nrm.x, nrm.y, nrm.z))
                idx.append(i)
            s["tris"].append(idx)
    order.sort(key=lambda k: 0 if k == "skin" else 1)
    data = bytearray(b"MGM1"); data += struct.pack("<i", len(order)); total = 0
    for key in order:
        s = subs[key]; mt = s["mat"]
        col = tuple(mt.diffuse_color) if mt else (1, 1, 1, 1)
        sm = float(mt.get("smooth", 0.3)) if mt else 0.3
        name = key.encode("utf8")
        data += struct.pack("<i", len(name)) + name
        data += struct.pack("<5f", col[0], col[1], col[2], col[3], sm)
        data += struct.pack("<i", len(s["verts"]))
        for v in s["verts"]: data += struct.pack("<6f", *v)
        data += struct.pack("<i", len(s["tris"]) * 3)
        for t in s["tris"]: data += struct.pack("<3i", *t)
        total += len(s["verts"])
    with open(os.path.join(OUT, asset + ".bytes"), "wb") as f:
        f.write(data)
    return {"verts": total, "tris": sum(len(subs[k]["tris"]) for k in order), "submeshes": order, "kb": len(data) // 1024}

# ---------------------------------------------------------------- finalize
TRUNK_PIVOT = (-0.50, 0.0, 0.0)
TARGETS = {"X_trunkskin": 1500, "X_armL": 4500, "X_armR": 4500, "X_thighL": 1800, "X_thighR": 1800,
           "X_shinL": 3600, "X_shinR": 3600}

def eye_opening(side):
    """Ray-cast straight down (-Y) over the eye: where the eyeball is hit before the skin, the lids are open."""
    head = bpy.data.objects["X_head"]; eye = bpy.data.objects["P_eye" + side]
    bh = bmesh.new(); bh.from_mesh(head.data); th = BVHTree.FromBMesh(bh); bh.free()
    be = bmesh.new(); be.from_mesh(eye.data); te = BVHTree.FromBMesh(be)
    ev = np.array([v.co[:] for v in be.verts]); be.free()
    c = (ev.min(0) + ev.max(0)) / 2
    xs, zs = [], []
    for dx in np.arange(-0.022, 0.022, 0.0005):
        for dz in np.arange(-0.025, 0.025, 0.0005):
            o = Vector((c[0] + dx, 0.5, c[2] + dz)); d = Vector((0, -1, 0))
            he = te.ray_cast(o, d); hs = th.ray_cast(o, d)
            if he[0] is not None and (hs[0] is None or he[0].y >= hs[0].y - 1e-5):
                xs.append(c[0] + dx); zs.append(c[2] + dz)
    return c, (min(xs), max(xs), min(zs), max(zs)) if xs else None

def finalize(J, export=True):
    for n, t in TARGETS.items(): decimate(bpy.data.objects[n], t)
    hc = lying_head_centre()
    groups = {
        "head": (["X_head", "P_eyeL", "P_eyeR", "X_hair", "X_bun"], hc),
        "trunk": (["X_trunkskin", "G_trunk", "G_trim"], TRUNK_PIVOT),
        "armL": (["X_armL", "G_L"], J["shoulderL"]), "armR": (["X_armR", "G_R"], J["shoulderR"]),
        "thighL": (["X_thighL"], J["hipL"]), "thighR": (["X_thighR"], J["hipR"]),
        "shinL": (["X_shinL"], J["kneeL"]), "shinR": (["X_shinR"], J["kneeR"]),
    }
    stats = {}
    for a, (names, piv) in groups.items():
        objs = [bpy.data.objects[n] for n in names if n in bpy.data.objects]
        if export:
            stats[a] = export_parts(a, objs, piv)
        else:
            stats[a] = {"verts": sum(len(o.data.vertices) for o in objs)}
    # pivots / measurements (patient space)
    eyes = {}
    for s in "LR":
        c, box = eye_opening(s)
        eyes[s] = {"centre": [round(float(v - h), 4) for v, h in zip(c, hc)],
                   "opening_x": [round(box[0] - hc[0], 4), round(box[1] - hc[0], 4)],
                   "opening_z": [round(box[2] - hc[2], 4), round(box[3] - hc[2], 4)],
                   "opening_size_xz": [round(box[1] - box[0], 4), round(box[3] - box[2], 4)]}
    allv = []
    for n in ("X_head", "X_hair", "X_bun", "X_trunkskin", "G_trunk", "X_armL", "X_armR", "X_thighL", "X_thighR", "X_shinL", "X_shinR", "G_L", "G_R"):
        allv.append(get_co(bpy.data.objects[n].data))
    A = np.concatenate(allv)
    shin = np.concatenate([get_co(bpy.data.objects[n].data) for n in ("X_shinL", "X_shinR")])
    hd = np.concatenate([get_co(bpy.data.objects[n].data) for n in ("X_head", "X_hair", "X_bun")])
    R = lambda v: [round(float(x), 4) for x in v]
    piv = {
        "space": "patient space (Unity local): X along bed (head -X, feet +X), Y up, Z across (patient left = -Z)",
        "head_centre": R(hc), "trunk_pivot": R(TRUNK_PIVOT),
        "shoulderL": R(J["shoulderL"]), "shoulderR": R(J["shoulderR"]),
        "elbowL": R(J["elbowL"]), "elbowR": R(J["elbowR"]), "wristL": R(J["wristL"]), "wristR": R(J["wristR"]),
        "hipL": R(J["hipL"]), "hipR": R(J["hipR"]),
        "kneeL": R(J["kneeL"]), "kneeR": R(J["kneeR"]),
        "kneeL_rel_hip": R(np.array(J["kneeL"]) - np.array(J["hipL"])),
        "kneeR_rel_hip": R(np.array(J["kneeR"]) - np.array(J["hipR"])),
        "ankleL": R(J["ankleL"]), "ankleR": R(J["ankleR"]),
        "eyes_head_local": eyes,
        "extents": {"head_top_x": round(float(hd[:, 0].min()), 4), "toe_tip_x": round(float(shin[:, 0].max()), 4),
                    "heel_x": round(float(shin[shin[:, 1] < 0.04][:, 0].max()), 4),
                    "mattress_contact_y": round(float(A[:, 1].min()), 4),
                    "head_back_y": round(float(hd[:, 1].min()), 4),
                    "max_y_belly": round(float(get_co(bpy.data.objects["G_trunk"].data)[:, 1].max()), 4),
                    "half_width_z": round(float(np.abs(A[:, 2]).max()), 4)},
        "neck_cut_x": X_NECK, "gown_hem_x": HEM_X,
        "vertex_counts": {k: v["verts"] for k, v in stats.items()},
    }
    piv["vertex_counts"]["total"] = sum(v["verts"] for v in stats.values())
    import json
    with open(os.path.join(LOGS, "patient_pivots.json"), "w") as f:
        json.dump(piv, f, indent=2)
    return piv, stats

def preview_props():
    sc = get_scene()
    for n in ("V_mattress", "V_pillow"):
        if n in bpy.data.objects: bpy.data.objects.remove(bpy.data.objects[n])
    bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts: v.co = Vector((v.co.x * 2.0, v.co.y * 0.16 - 0.07, v.co.z * 0.9))
    obj_from_bm("V_mattress", bm, [MATT])
    bm = bmesh.new(); bmesh.ops.create_uvsphere(bm, u_segments=32, v_segments=16, radius=1.0)
    for v in bm.verts:
        n = v.co.copy(); v.co = Vector((n.x * 0.20 - 0.83, max(n.y, -0.2) * 0.022 + 0.03, n.z * 0.32))
    obj_from_bm("V_pillow", bm, [PILLOW])

# ---------------------------------------------------------------- main
def main(export=True, renders=True, tag="final"):
    clear_scene()
    J = build_posed()
    part_head(16000)
    part_trunk_skin()
    for s in "LR":
        part_arm(s, J); part_thigh(s, J); part_shin(s, J)
    build_hair()
    g = build_gown(J)
    split_gown(J, g, {"trunk": 7000, "L": 900, "R": 900})
    g.hide_render = True; g.hide_viewport = True
    bpy.data.objects["P_body"].hide_render = True; bpy.data.objects["P_body"].hide_viewport = True
    piv, stats = finalize(J, export)
    if renders:
        preview_props()
        shot("hbm_%s_overview" % tag, (0.55, 1.45, 1.35), (-0.15, 0.1, 0), 30)
        shot("hbm_%s_face" % tag, (-0.78, 0.62, 0.0), (-0.80, 0.12, 0), 42)
        shot("hbm_%s_side" % tag, (-0.1, 0.25, 2.6), (-0.1, 0.15, 0), 30)
        shot("hbm_%s_headside" % tag, (-0.70, 0.32, -0.42), (-0.83, 0.12, 0), 42)
    return piv, stats


if __name__ == "__main__":
    piv, stats = main(export=True, renders=True)
    print("PATIENT", stats)
