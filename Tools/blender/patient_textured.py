# patient_textured.py - TEXTURED realistic 34-week pregnant patient lying supine (MgSO4 AR sim)
#
# Source assets (all CC0):
#   * MakeHuman base mesh + targets + rig via the MPFB 2 Blender extension (extensions.blender.org, pkg "mpfb")
#   * MakeHuman system assets pack (makehuman_system_assets_cc0.zip, files.makehumancommunity.org):
#       skin young_caucasian_female (re-toned to light-brown/olive), eyes high-poly + brown, eyebrow001,
#       eyelashes01, hair ponytail01
#   * geometry helpers (cuts, caps, gown shell) are reused from patient_realistic.py in this folder
#
# Run inside Blender 5.x with MPFB 2 installed (the CC0 system-asset pack is downloaded on first run):
#     exec(open(r"...\BuildLogs\patient_textured.py").read()); main()
# Writes Resources/Models/{head,eyelids,trunk,armL,armR,thighL,thighR,shinL,shinR}.bytes (MGM2),
#        Resources/Models/Tex/patient_*.png, Resources/Models/patient_pivots.txt, BuildLogs/patient_pivots.json
#
# Patient space = Unity local numbers: X along the bed (head -X, feet +X), Y up, Z across (patient right = +Z).
import bpy, bmesh, math, os, struct, json, importlib.util
import numpy as np
from mathutils import Vector, Matrix, Quaternion
from mathutils.bvhtree import BVHTree
from mathutils.kdtree import KDTree

BASE = r"C:\Users\ahmed\OneDrive\Documents\AR Recearch\UnityProject"
LOGS = os.path.join(BASE, "BuildLogs")
SHOTS = os.path.join(LOGS, "screens")
OUT = os.path.join(BASE, "MgSO4_AR_Simulation", "Assets", "Resources", "Models")
TEX = os.path.join(OUT, "Tex")
APPDATA = os.path.join(os.environ.get("APPDATA", r"C:\Users\ahmed\AppData\Roaming"), "Blender Foundation", "Blender", "5.2")
MHD = os.path.join(APPDATA, "extensions", ".user", "user_default", "mpfb", "data")      # system asset pack
TGT = os.path.join(APPDATA, "extensions", "user_default", "mpfb", "data", "targets")      # MPFB bundled targets
ASSET_ZIP_URL = "https://files.makehumancommunity.org/asset_packs/makehuman_system_assets/makehuman_system_assets_cc0.zip"

# helper module (old generator) - geometry utilities, gown builder
_spec = importlib.util.spec_from_file_location("patient_realistic", os.path.join(LOGS, "patient_realistic.py"))
pr = importlib.util.module_from_spec(_spec); _spec.loader.exec_module(pr)

# ---------------------------------------------------------------- MakeHuman character
MACRO = {"gender": 0.0, "age": 0.5, "muscle": 0.45, "weight": 0.55, "proportions": 0.6, "height": 0.42,
         "cupsize": 0.6, "firmness": 0.5, "race": {"asian": 0.05, "caucasian": 0.70, "african": 0.25}}
TARGETS = {"stomach/stomach-pregnant-incr": 1.0, "head/head-oval": 0.45, "chin/chin-width-decr": 0.35,
           "chin/chin-prominent-decr": 0.2, "mouth/mouth-upperlip-volume-incr": 0.35,
           "mouth/mouth-lowerlip-volume-incr": 0.3, "nose/nose-scale-horiz-decr": 0.15,
           "nose/nose-point-width-decr": 0.25, "cheek/l-cheek-bones-incr": 0.25, "cheek/r-cheek-bones-incr": 0.25,
           "eyebrows/eyebrows-trans-up": 0.15, "neck/measure-neck-circ-decr": 0.3}
PROXIES = [("Eyes", r"eyes\high-poly\high-poly.mhclo", "eyes"),
           ("Eyebrows", r"eyebrows\eyebrow001\eyebrow001.mhclo", "brows"),
           ("Eyelashes", r"eyelashes\eyelashes01\eyelashes01.mhclo", "lashes"),
           ("Hair", r"hair\ponytail01\ponytail01.mhclo", "hair")]
SKIN_MHMAT = r"skins\young_caucasian_female\young_caucasian_female.mhmat"
SKIN_SRC = r"skins\young_caucasian_female\young_lightskinned_female_diffuse.png"
SKIN_TARGET_SRGB = (0.72, 0.53, 0.41)      # median trunk tone after re-toning (light brown / olive)

# pose (degrees)
ARM_ABD, FOREARM_ABD, FOREARM_FWD = 13.0, 16.0, -0.06
ARM_BACK_Y = 0.22          # backward (toward the mattress) component of the upper-arm direction
PRONATE, WRIST_TW = 40.0, 25.0
NECK_FLEX = 9.0
LEG_ABD = 2.5
HIP_EXT = 4.0
FOOT_PF = 22.0
LID_UP_CLOSED, LID_LO_CLOSED = -34.0, 5.0
HAIR_CLIP_Z = 1.40         # ponytail tail below this (standing) would hang under the pillow/back -> removed

HEAD_TOP_X = -0.92
BACK_Y = 0.008             # lowest point of the back before the soft mattress squash
PILLOW_Y = 0.05
OV_HEAD = 0.015

MH = {}

def mpfb():
    from bl_ext.user_default.mpfb.services.humanservice import HumanService
    from bl_ext.user_default.mpfb.services.targetservice import TargetService
    return HumanService, TargetService

def ensure_assets():
    """Download + extract the MakeHuman CC0 system assets into MPFB's user data dir if missing."""
    if os.path.exists(os.path.join(MHD, "skins", "young_caucasian_female")): return
    import urllib.request, zipfile
    os.makedirs(MHD, exist_ok=True)
    zp = os.path.join(os.path.dirname(MHD), "makehuman_system_assets_cc0.zip")
    req = urllib.request.Request(ASSET_ZIP_URL, headers={"User-Agent": "Mozilla/5.0"})
    with urllib.request.urlopen(req, timeout=120) as f, open(zp, "wb") as o:
        while True:
            b = f.read(1 << 20)
            if not b: break
            o.write(b)
    zipfile.ZipFile(zp).extractall(MHD)

class InScene:
    def __init__(self, name): self.name = name
    def __enter__(self):
        self.sc = bpy.data.scenes.get(self.name) or bpy.data.scenes.new(self.name)
        self.win = bpy.context.window; self.old = self.win.scene; self.win.scene = self.sc
        return self.sc
    def __exit__(self, *a):
        self.win.scene = self.old

def build_mh():
    ensure_assets()
    H, T = mpfb()
    with InScene("MH") as sc:
        for o in list(sc.objects): bpy.data.objects.remove(o, do_unlink=True)
        b = H.create_human(mask_helpers=True, detailed_helpers=True, extra_vertex_groups=True,
                           feet_on_ground=True, scale=0.1, macro_detail_dict=json.loads(json.dumps(MACRO)))
        b.name = "MH_body"
        for k, w in TARGETS.items():
            T.load_target(b, os.path.join(TGT, k.replace("/", os.sep) + ".target.gz"), weight=w)
        H.add_builtin_rig(b, "default")
        objs = {"body": b}
        for typ, f, key in PROXIES:
            objs[key] = H.add_mhclo_asset(os.path.join(MHD, f), b, asset_type=typ, subdiv_levels=0, material_type="MAKESKIN")
        H.set_character_skin(os.path.join(MHD, SKIN_MHMAT), b, skin_type="MAKESKIN")
        objs["rig"] = [o for o in sc.objects if o.type == "ARMATURE"][0]
        add_arm_weight(b)
    MH.clear(); MH.update({k: o.name for k, o in objs.items()})
    return objs

def mh(key):
    if not MH:
        sc = bpy.data.scenes["MH"]
        for o in sc.objects:
            if o.type == "ARMATURE": MH["rig"] = o.name
            elif o.name == "MH_body": MH["body"] = o.name
            elif "high-poly" in o.name: MH["eyes"] = o.name
            elif "eyebrow" in o.name: MH["brows"] = o.name
            elif "eyelash" in o.name: MH["lashes"] = o.name
            elif "ponytail" in o.name: MH["hair"] = o.name
    return bpy.data.objects[MH[key]]

ARM_BONES = ("upperarm01", "upperarm02", "lowerarm01", "lowerarm02", "wrist", "finger", "metacarpal")
def add_arm_weight(b):
    """Point attribute w_arm = share of the vertex's deform weight that belongs to the arm chain."""
    me = b.data
    isarm = {g.index: g.name.startswith(ARM_BONES) for g in b.vertex_groups}
    bones = {bn.name for bn in mh_rig_bones()}
    isdef = {g.index: g.name in bones for g in b.vertex_groups}
    w = np.zeros(len(me.vertices), np.float32)
    for v in me.vertices:
        tot = arm = 0.0
        for g in v.groups:
            if not isdef.get(g.group): continue
            tot += g.weight
            if isarm[g.group]: arm += g.weight
        w[v.index] = arm / tot if tot > 1e-6 else 0.0
    a = me.attributes.get("w_arm") or me.attributes.new("w_arm", 'FLOAT', 'POINT')
    a.data.foreach_set("value", w)

def mh_rig_bones():
    for o in bpy.data.scenes["MH"].objects:
        if o.type == "ARMATURE": return o.data.bones
    return []

# ---------------------------------------------------------------- posing
def _upd():
    bpy.context.view_layer.update()

def rot_world(pb, axis, deg):
    M = pb.matrix.copy()
    R = Quaternion(Vector(axis).normalized(), math.radians(deg)).to_matrix().to_4x4()
    Mn = R @ M; Mn.translation = M.translation
    pb.matrix = Mn; _upd()

def aim(pb, d):
    M = pb.matrix.copy()
    cur = (M.to_3x3() @ Vector((0, 1, 0))).normalized()
    q = cur.rotation_difference(Vector(d).normalized())
    Mn = q.to_matrix().to_4x4() @ M; Mn.translation = M.translation
    pb.matrix = Mn; _upd()

def pose_rig(closed=False):
    rig = mh("rig")
    with InScene("MH"):
        for pb in rig.pose.bones:
            pb.rotation_mode = 'QUATERNION'; pb.rotation_quaternion = (1, 0, 0, 0)
            pb.location = (0, 0, 0); pb.scale = (1, 1, 1)
        _upd()
        P = rig.pose.bones
        rot_world(P["neck01"], (1, 0, 0), NECK_FLEX)
        for s, sg in (("L", 1), ("R", -1)):
            a = math.radians(ARM_ABD)
            aim(P["upperarm01." + s], (sg * math.sin(a), ARM_BACK_Y, -math.cos(a)))
            a2 = math.radians(FOREARM_ABD)
            aim(P["lowerarm01." + s], (sg * math.sin(a2), -FOREARM_FWD, -math.cos(a2)))
            P["lowerarm02." + s].rotation_quaternion = Quaternion((0, 1, 0), math.radians(sg * PRONATE))
            P["wrist." + s].rotation_quaternion = Quaternion((0, 1, 0), math.radians(sg * WRIST_TW))
            for f in range(2, 6):
                for k in range(1, 4):
                    pb = P.get("finger%d-%d.%s" % (f, k, s))
                    if pb: pb.rotation_quaternion = Quaternion((1, 0, 0), math.radians(8 if k == 1 else 11))
            _upd()
            rot_world(P["upperleg01." + s], (0, 1, 0), -sg * LEG_ABD)
            if HIP_EXT: rot_world(P["upperleg01." + s], (1, 0, 0), HIP_EXT)
            rot_world(P["foot." + s], (1, 0, 0), FOOT_PF)
            if closed:
                P["orbicularis03." + s].rotation_quaternion = Quaternion((1, 0, 0), math.radians(LID_UP_CLOSED))
                P["orbicularis04." + s].rotation_quaternion = Quaternion((1, 0, 0), math.radians(LID_LO_CLOSED))
        _upd()

def joints_standing():
    rig = mh("rig"); mw = rig.matrix_world
    J = {}
    names = {"shoulder": "upperarm01", "elbow": "lowerarm01", "wrist": "wrist", "hip": "upperleg01",
             "knee": "lowerleg01", "ankle": "foot"}
    for k, bn in names.items():
        for s in "LR":
            J[k + s] = mw @ rig.pose.bones[bn + "." + s].head
    J["neck"] = mw @ rig.pose.bones["neck01"].head
    J["head"] = mw @ rig.pose.bones["head"].head
    J["headtail"] = mw @ rig.pose.bones["head"].tail
    return J

# ---------------------------------------------------------------- baking (evaluated MH meshes, standing world coords)
def bake(closed=False):
    pose_rig(closed)
    out = {}
    with InScene("MH") as sc:
        dg = bpy.context.evaluated_depsgraph_get(); dg.update()
        for key in ("body", "eyes", "brows", "lashes", "hair"):
            o = mh(key)
            me = bpy.data.meshes.new_from_object(o.evaluated_get(dg), preserve_all_data_layers=True, depsgraph=dg)
            me.transform(o.matrix_world)
            me.name = "B_%s%s" % (key, "_c" if closed else "")
            out[key] = me
        # smooth the body once (Catmull-Clark, UVs kept)
        tmp = bpy.data.objects.new("tmp_sub", out["body"]); sc.collection.objects.link(tmp)
        m = tmp.modifiers.new("sub", 'SUBSURF'); m.levels = 1; m.render_levels = 1; m.uv_smooth = 'PRESERVE_BOUNDARIES'
        dg.update()
        me = bpy.data.meshes.new_from_object(tmp.evaluated_get(dg), preserve_all_data_layers=True, depsgraph=dg)
        old = out["body"]; bpy.data.objects.remove(tmp); bpy.data.meshes.remove(old)
        me.name = "B_body%s" % ("_c" if closed else ""); out["body"] = me
        J = joints_standing()
    # high-poly eyes = eyeball + cornea shell; the cornea (UVs in the 0.88-0.99 corner of the atlas) is meant to be
    # transparent - rendered opaque it hides the iris (black eyes) -> drop it, keep the textured eyeball
    bm = bmesh.new(); bm.from_mesh(out["eyes"]); uvl = bm.loops.layers.uv.active
    bm.verts.ensure_lookup_table(); seen = set(); kill = []
    for v0 in bm.verts:
        if v0 in seen: continue
        comp = [v0]; seen.add(v0); i = 0
        while i < len(comp):
            for e in comp[i].link_edges:
                b = e.other_vert(comp[i])
                if b not in seen: seen.add(b); comp.append(b)
            i += 1
        us = [l[uvl].uv.x for v in comp for l in v.link_loops]
        if us and min(us) > 0.85: kill += comp
    bmesh.ops.delete(bm, geom=kill, context='VERTS')
    bm.to_mesh(out["eyes"]); bm.free()
    # ponytail: the tail (below HAIR_CLIP_Z standing) would hang through the pillow/back when lying.
    # Keep it and remember the standing coords; place_tail() re-lays it flat on the pillow beside the head.
    hm = out["hair"]
    a = hm.attributes.get("st_co") or hm.attributes.new("st_co", 'FLOAT_VECTOR', 'POINT')
    a.data.foreach_set("vector", np.array([v.co[:] for v in hm.vertices], np.float32).ravel())
    return out, J

TAIL_TH0, TAIL_TH1 = 4.0, 34.0      # deg: tail direction on the pillow, from +Z (patient right) toward the feet
TAIL_ROOT = (-0.800, 0.035)          # (x, z) where the tail path starts (under the back of the head)
def place_tail(me):
    n = len(me.vertices)
    S = np.empty(n * 3, np.float32); me.attributes["st_co"].data.foreach_get("vector", S); S = S.reshape(-1, 3)
    P = pr.get_co(me)
    tail = S[:, 2] < HAIR_CLIP_Z
    if not tail.any(): return
    s = HAIR_CLIP_Z - S[:, 2]
    L = float(s[tail].max())
    # per-slice centre of the tail cross-section (standing x/y)
    nb = 24; bins = np.minimum((s / L * nb).astype(int), nb - 1)
    cx = np.zeros(nb); cy = np.zeros(nb)
    for b in range(nb):
        m = tail & (bins == b)
        if m.any(): cx[b] = S[m, 0].mean(); cy[b] = S[m, 1].mean()
        elif b: cx[b], cy[b] = cx[b - 1], cy[b - 1]
    sc = (np.arange(nb) + 0.5) / nb * L
    # path on the pillow
    ds = L / 200.0; path = [np.array([TAIL_ROOT[0], TAIL_ROOT[1]])]; th = []
    for i in range(201):
        t = math.radians(TAIL_TH0 + (TAIL_TH1 - TAIL_TH0) * (i / 200.0) ** 1.3); th.append(t)
        path.append(path[-1] + ds * np.array([math.sin(t), math.cos(t)]))
    path = np.array(path); th = np.array(th)
    for i in np.nonzero(tail)[0]:
        si = s[i]; k = min(int(si / ds), 200)
        xc = np.interp(si, sc, cx); yc = np.interp(si, sc, cy)
        a = S[i, 0] - xc; t = S[i, 1] - yc
        c = path[k]; thk = th[k]
        perp = np.array([math.cos(thk), -math.sin(thk)])       # in-plane, across the tail
        q = c + perp * (a * 1.3 + t * 0.7)               # lies flat: the tail's depth spreads on the pillow
        top = pillow_top(float(q[0]), float(q[1]))
        if top is None: top = PILLOW_Y
        taper = 1.0 - 0.5 * si / L
        P[i] = (q[0], top + 0.003 + 0.18 * taper * (t + 0.03), q[1])
    pr.set_co(me, P); me.update()

# ---------------------------------------------------------------- textures
def _px(im):
    a = np.empty(im.size[0] * im.size[1] * 4, np.float32); im.pixels.foreach_get(a)
    return a.reshape(im.size[1], im.size[0], 4)

def _lin(x): return np.where(x <= 0.04045, x / 12.92, ((x + 0.055) / 1.055) ** 2.4)
def _srgb(x):
    x = np.clip(x, 0, 1); return np.where(x <= 0.0031308, x * 12.92, 1.055 * x ** (1 / 2.4) - 0.055)

def save_png(name, arr, size=None):
    h, w = arr.shape[:2]
    im = bpy.data.images.get(name)
    if im is not None: bpy.data.images.remove(im)
    im = bpy.data.images.new(name, w, h, alpha=True)
    im.pixels.foreach_set(np.ascontiguousarray(arr, np.float32).ravel())
    if size and size != w: im.scale(size, size)
    im.filepath_raw = os.path.join(TEX, name + ".png"); im.file_format = 'PNG'; im.save()
    return im

def _load(rel):
    return bpy.data.images.load(os.path.join(MHD, rel), check_existing=True)

def make_textures():
    os.makedirs(TEX, exist_ok=True)
    info = {}
    # skin: MakeHuman photographic albedo, re-toned in linear space to a light-brown/olive Egyptian complexion
    a = _px(_load(SKIN_SRC))
    L = _lin(a[..., :3])
    med = np.median(L[600:1400:4, 600:1400:4].reshape(-1, 3), 0)
    L = L * (_lin(np.array(SKIN_TARGET_SRGB)) / med)
    save_png("patient_skin", np.concatenate([_srgb(L), np.ones_like(L[..., :1])], -1))
    info["patient_skin"] = 2048
    # eyes
    e = _px(_load(r"eyes\materials\brown_eye.png")).copy()
    save_png("patient_eye", e); info["patient_eye"] = 1024
    # hair: darken to near-black brown, keep strand alpha, 1024
    h = _px(_load(r"hair\ponytail01\ponytail01_diffuse.png"))
    lum = h[..., :3] @ np.array([0.3, 0.59, 0.11])
    rel = lum / max(float(np.median(lum[h[..., 3] > 0.5])), 1e-3)
    rgb = np.clip(np.array([0.10, 0.068, 0.052])[None, None, :] * rel[..., None] ** 1.1, 0, 1)
    save_png("patient_hair", np.concatenate([rgb, h[..., 3:4]], -1), 1024); info["patient_hair"] = 1024
    # brows / lashes: dark brown, alpha boosted a little so thin strands survive the 0.45 alpha test
    for src, name, col, boost in ((r"eyebrows\eyebrow001\eyebrow001.png", "patient_brows", (0.075, 0.05, 0.04), 1.5),
                                  (r"eyelashes\eyelashes01\eyelashes01.png", "patient_lashes", (0.03, 0.02, 0.018), 1.35)):
        b = _px(_load(src))
        out = np.empty_like(b); out[..., :3] = col; out[..., 3] = np.clip(b[..., 3] * boost, 0, 1)
        save_png(name, out); info[name] = 512
    # gown: light-blue cotton with a fine weave and a small darker print (tileable, 512)
    n = 512
    y, x = np.mgrid[0:n, 0:n].astype(np.float32)
    rng = np.random.default_rng(7)
    weave = 0.5 + 0.5 * np.sin(x * 2 * np.pi / 4.0) * np.sin(y * 2 * np.pi / 4.0)
    noise = rng.normal(0, 1, (n, n)).astype(np.float32)
    k = np.fft.fftfreq(n)[:, None] ** 2 + np.fft.fftfreq(n)[None, :] ** 2
    blot = np.real(np.fft.ifft2(np.fft.fft2(noise) * np.exp(-k * 900)))
    blot = blot / (np.abs(blot).max() + 1e-6)
    base = np.array([0.60, 0.76, 0.90])
    rgb = base[None, None, :] * (0.95 + 0.04 * weave[..., None] + 0.03 * blot[..., None])
    # small diamonds every 64 px, offset rows
    cx = (x % 64) - 32; cy = (y % 64) - 32
    cx2 = ((x + 32) % 64) - 32; cy2 = ((y + 32) % 64) - 32
    d = np.minimum(np.abs(cx) + np.abs(cy), np.abs(cx2) + np.abs(cy2))
    m = np.clip((6.5 - d) / 1.5, 0, 1)[..., None]
    rgb = rgb * (1 - m) + np.array([0.33, 0.50, 0.74])[None, None, :] * m
    save_png("patient_gown", np.concatenate([np.clip(rgb, 0, 1), np.ones((n, n, 1))], -1)); info["patient_gown"] = 512
    return info

# ---------------------------------------------------------------- materials (export spec + preview nodes)
MATSPEC = {   # name: (rgba, smoothness, texture)
    "skin": ((1, 1, 1, 1), 0.35, "patient_skin"),
    "eyes": ((1, 1, 1, 1), 0.85, "patient_eye"),
    "cutout_brows": ((1, 1, 1, 1), 0.10, "patient_brows"),
    "cutout_lashes": ((1, 1, 1, 1), 0.10, "patient_lashes"),
    "cutout_hair": ((1, 1, 1, 1), 0.35, "patient_hair"),
    "gown": ((1, 1, 1, 1), 0.12, "patient_gown"),
    "gown_trim": ((0.40, 0.58, 0.78, 1), 0.15, ""),
}

def M(name):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    rgba, sm, tex = MATSPEC[name]
    m.diffuse_color = rgba; m["smooth"] = sm; m["tex"] = tex
    return m

def preview_material(m, tex, rgba=(1, 1, 1, 1), cutout=False, rough=0.6):
    m.use_nodes = True
    nt = m.node_tree; nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial"); bs = nt.nodes.new("ShaderNodeBsdfPrincipled")
    bs.inputs["Roughness"].default_value = rough
    nt.links.new(bs.outputs[0], out.inputs[0])
    if tex:
        it = nt.nodes.new("ShaderNodeTexImage")
        it.image = bpy.data.images.load(os.path.join(TEX, tex + ".png"), check_existing=True)
        it.image.reload()
        nt.links.new(it.outputs["Color"], bs.inputs["Base Color"])
        if cutout:
            gt = nt.nodes.new("ShaderNodeMath"); gt.operation = 'GREATER_THAN'; gt.inputs[1].default_value = 0.45
            nt.links.new(it.outputs["Alpha"], gt.inputs[0]); nt.links.new(gt.outputs[0], bs.inputs["Alpha"])
            try: m.surface_render_method = 'DITHERED'
            except Exception: pass
            try: m.use_backface_culling = False
            except Exception: pass
    else:
        bs.inputs["Base Color"].default_value = rgba

def setup_preview_materials():
    for n, (rgba, sm, tex) in MATSPEC.items():
        preview_material(M(n), tex, rgba, n.startswith("cutout_"), 1.0 - sm)
    preview_material(pr.MATT, "", (0.90, 0.92, 0.94, 1), rough=0.9)
    preview_material(pr.PILLOW, "", (0.97, 0.97, 0.98, 1), rough=0.9)

# ---------------------------------------------------------------- lying transform
LY = {}
def lying_matrix():
    return Matrix(((0, 0, -1, LY["C_X"]), (0, -1, 0, LY["D_Y"]), (-1, 0, 0, 0), (0, 0, 0, 1)))

def lying_pt(p):
    return Vector((-p[2] + LY["C_X"], -p[1] + LY["D_Y"], -p[0]))

def calibrate(meshes):
    P = pr.get_co(meshes["body"]); H = pr.get_co(meshes["hair"])
    LY["C_X"] = float(max(P[:, 2].max(), H[:, 2].max())) + HEAD_TOP_X
    w = attr(meshes["body"], "w_arm")
    back = (w < 0.5) & (P[:, 2] > 0.95) & (P[:, 2] < 1.40)
    LY["D_Y"] = BACK_Y + float(P[back, 1].max())

def attr(me, nm):
    a = np.empty(len(me.vertices), dtype=np.float32); me.attributes[nm].data.foreach_get("value", a); return a

def squash(P, f0, w, mask=None):
    y = P[:, 1]; u = np.maximum(f0 - y, 0); nu = w * (1 - np.exp(-u / w))
    ny = np.where(y < f0, f0 - nu, y)
    if mask is not None: ny = np.where(mask, ny, y)
    out = P.copy(); out[:, 1] = ny; return out

def lay(me, xneck):
    me.transform(lying_matrix())
    P = pr.get_co(me)
    head = P[:, 0] < xneck - 0.02
    P = squash(P, PILLOW_Y + 0.004, 0.006, head)          # back of the skull sinks a little into the pillow
    P = squash(P, pr.MATTRESS, 0.010, ~head)              # body on the mattress
    pr.set_co(me, P)
    me.update()

# ---------------------------------------------------------------- scene objects
def obj(name, me, mats):
    o = pr.put(name, me, mats)
    for p in o.data.polygons: p.use_smooth = True
    return o

def single_material(me, m):
    me.materials.clear(); me.materials.append(m)
    n = len(me.polygons)
    me.polygons.foreach_set("material_index", np.zeros(n, np.int32))

def stage_posed(meshes, J):
    sk = M("skin")
    single_material(meshes["body"], sk)
    for key, mn in (("eyes", "eyes"), ("brows", "cutout_brows"), ("lashes", "cutout_lashes"), ("hair", "cutout_hair")):
        single_material(meshes[key], M(mn))
    Jl = {k: lying_pt(v) for k, v in J.items()}
    xneck = Jl["neck"].x + 0.45 * (Jl["head"].x - Jl["neck"].x)
    for k in ("body", "eyes", "brows", "lashes", "hair"):
        lay(meshes[k], xneck)
    place_tail(meshes["hair"])
    return Jl, xneck

# ---------------------------------------------------------------- parts
# head / trunk seam: a plane slanted along the neck (back of the neck near the skull base, front on the throat
# just below the chin) so the whole chin + jaw belong to the head and no cut runs through the face.
NECK_SLOPE = 0.73          # dx/dy of the seam
def neck_plane(xneck):
    n = Vector((1.0, -NECK_SLOPE, 0.0)).normalized()
    return Vector((xneck - 0.003, 0.06, 0.0)), n

def part_head(xneck):
    body = bpy.data.objects["P_body"]
    bm = pr.bm_from(body)
    wl = bm.verts.layers.float.get("w_arm")
    p0, n = neck_plane(xneck)
    pc = p0 + n * OV_HEAD
    pr.bisect(bm, pc, n, lambda f: abs((f.calc_center_median() - pc).dot(n)) < 0.05 and all(v[wl] < 0.5 for v in f.verts))
    pr.keep_faces(bm, lambda v: (v.co - pc).dot(n) <= 1e-6 and v[wl] < 0.5 and v.co.x < xneck + 0.12)
    pr.inset_band(bm, lambda co: (co - p0).dot(n), OV_HEAD)
    pr.cap_all(bm, lambda c: n, 0.15, 3)
    return pr.obj_from_bm("X_head", bm, [M("skin")])

def part_trunk_skin(xneck, depth=0.22):
    body = bpy.data.objects["P_body"]
    bm = pr.bm_from(body)
    wl = bm.verts.layers.float.get("w_arm")
    p0, n = neck_plane(xneck)
    pr.bisect(bm, p0, n, lambda f: abs((f.calc_center_median() - p0).dot(n)) < 0.05 and all(v[wl] < 0.5 for v in f.verts))
    pr.keep_faces(bm, lambda v: (v.co - p0).dot(n) >= -1e-6 and v.co.x <= xneck + depth and v[wl] < 0.5)
    return pr.obj_from_bm("X_trunkskin", bm, [M("skin")])

def neck_field_factory(shx, Jl):
    def f(co):
        az = abs(co.z)
        x0 = shx + 0.022 - 0.085 * min(1.0, (az / 0.16)) ** 2
        return -(co.x - x0) + 0.65 * (co.y - 0.15)
    return f

def gown_uvs(o, tile=0.12):
    me = o.data
    if not me.uv_layers: me.uv_layers.new(name="UVMap")
    uv = me.uv_layers.active.data
    for p in me.polygons:
        n = p.normal
        ax = max(range(3), key=lambda i: abs(n[i]))
        for li in p.loop_indices:
            c = me.vertices[me.loops[li].vertex_index].co
            if ax == 1: u, v = c.x, c.z
            elif ax == 2: u, v = c.x, c.y
            else: u, v = c.z, c.y
            uv[li].uv = (u / tile, v / tile)

def tuck_thigh_end(o, J, side, band=0.03, depth=0.004):
    """Pull the last few cm of the thigh (and its knee cap) radially inward so it sits inside the shin's
    overlapping top instead of z-fighting with it (visible ring at the knee)."""
    H, K, d = pr.leg_axis(J, side)
    for v in o.data.vertices:
        c = v.co; u = (c - K).dot(d)
        if u > -band:
            w = min(1.0, (u + band) / band) ** 1.5
            r = (c - K) - d * u
            if r.length > 1e-6: v.co = c - r.normalized() * min(depth * w, r.length * 0.5)
    o.data.update()

def fix_cap_uvs(o):
    """Faces added by the dome caps have (0,0) UVs (bright texel) -> copy the UV of the nearest original vertex."""
    me = o.data
    if not me.uv_layers: return
    uv = me.uv_layers.active.data
    good = {}; bad = []
    for p in me.polygons:
        lis = list(p.loop_indices)
        if all(uv[li].uv.length < 1e-7 for li in lis): bad.append(p)
        else:
            for li in lis: good[me.loops[li].vertex_index] = uv[li].uv.copy()
    if not bad or not good: return
    kd = KDTree(len(good)); keys = list(good.keys())
    for i, vi in enumerate(keys): kd.insert(me.vertices[vi].co, i)
    kd.balance()
    for p in bad:
        for li in p.loop_indices:
            _, i, _ = kd.find(me.vertices[me.loops[li].vertex_index].co)
            uv[li].uv = good[keys[i]]

def eyelids_obj(open_me, closed_me, closed_lashes):
    """Faces of the closed-eye body that moved (upper/lower lids), as a skin shell slightly proud of the open face."""
    Po = pr.get_co(open_me); Pc = pr.get_co(closed_me)
    d = np.linalg.norm(Pc - Po, axis=1)
    moved = d > 0.0004
    bm = bmesh.new(); bm.from_mesh(closed_me)
    bm.verts.ensure_lookup_table()
    oid = bm.verts.layers.int.new("oid")
    for v in bm.verts: v[oid] = v.index
    sel = {v for v in bm.verts if moved[v.index]}
    for _ in range(2):
        sel |= {e.other_vert(v) for v in list(sel) for e in v.link_edges}
    pr.keep_faces(bm, lambda v: v in sel)
    bm.normal_update()
    for v in bm.verts:
        v.co += v.normal * (0.00035 + 0.0003 * min(1.0, float(d[v[oid]]) / 0.002))
    o = pr.obj_from_bm("X_eyelids", bm, [M("skin")])
    lo = obj("X_eyelashes_c", closed_lashes, None)
    return o, lo

def build_all(J_st, meshes, Jl, xneck):
    pr._KD.clear()
    pr.X_NECK = xneck
    # materials may have been purged/re-created since the helper module was loaded -> refresh its handles
    pr.SKIN = M("skin")
    pr.MATT = pr.mat("mattress", (0.93, 0.95, 0.97), 0.2); pr.PILLOW = pr.mat("pillow", (0.99, 0.99, 1.0), 0.15)
    obj("P_body", meshes["body"], None)
    obj("P_eyes", meshes["eyes"], None)
    obj("P_brows", meshes["brows"], None)
    obj("P_lashes", meshes["lashes"], None)
    obj("P_hair", meshes["hair"], None)
    J = {}
    for s in "LR":
        for k in ("shoulder", "elbow", "wrist", "hip", "knee", "ankle"):
            J[k + s] = [float(c) for c in Jl[k + s]]
    part_head(xneck)
    part_trunk_skin(xneck, 0.22)
    for s in "LR":
        pr.part_arm(s, J); pr.part_thigh(s, J); pr.part_shin(s, J)
        tuck_thigh_end(bpy.data.objects["X_thigh" + s], J, s)
    for n in ("X_head", "X_thighL", "X_thighR", "X_shinL", "X_shinR", "X_armL", "X_armR"):
        fix_cap_uvs(bpy.data.objects[n])
    # gown (shell built by the old helper, then textured with a planar UV)
    hipx, kneex = J["hipR"][0], J["kneeR"][0]
    pr.HEM_X = hipx + 0.57 * (kneex - hipx)
    pr.SLEEVE_LEN = 0.15
    pr.neck_field = neck_field_factory(J["shoulderR"][0], Jl)
    pr.GOWN = M("gown"); pr.GOWN_D = M("gown_trim")
    g = pr.build_gown(J)
    parts = pr.split_gown(J, g, {"trunk": 7000, "L": 900, "R": 900})
    for o in parts.values(): gown_uvs(o)
    g.hide_render = True; g.hide_viewport = True
    bpy.data.objects["P_body"].hide_render = True; bpy.data.objects["P_body"].hide_viewport = True
    return J

# ---------------------------------------------------------------- export MGM2
def export2(asset, objs, origin):
    subs = {}; order = []
    org = Vector(origin)
    for o in objs:
        me = o.data
        me.calc_loop_triangles()
        mw = o.matrix_world
        nm = mw.to_3x3().inverted().transposed()
        cn = me.corner_normals
        uvl = me.uv_layers.active.data if me.uv_layers.active else None
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
                uv = tuple(uvl[li].uv) if uvl else (0.0, 0.0)
                k = (round(p.x, 5), round(p.y, 5), round(p.z, 5), round(nrm.x, 3), round(nrm.y, 3), round(nrm.z, 3),
                     round(uv[0], 5), round(uv[1], 5))
                i = s["map"].get(k)
                if i is None:
                    i = len(s["verts"]); s["map"][k] = i
                    s["verts"].append((p.x, p.y, p.z, nrm.x, nrm.y, nrm.z, uv[0], uv[1]))
                idx.append(i)
            s["tris"].append(idx)
    order.sort(key=lambda k: 0 if k == "skin" else 1)
    data = bytearray(b"MGM2"); data += struct.pack("<i", len(order)); total = 0
    for key in order:
        s = subs[key]
        rgba, sm, tex = MATSPEC.get(key, ((1, 1, 1, 1), 0.3, ""))
        name = key.encode("utf8"); tb = tex.encode("utf8")
        data += struct.pack("<i", len(name)) + name
        data += struct.pack("<5f", rgba[0], rgba[1], rgba[2], rgba[3], sm)
        data += struct.pack("<i", len(tb)) + tb
        data += struct.pack("<i", len(s["verts"]))
        data += np.asarray(s["verts"], dtype="<f4").tobytes()
        data += struct.pack("<i", len(s["tris"]) * 3)
        data += np.asarray(s["tris"], dtype="<i4").tobytes()
        total += len(s["verts"])
    with open(os.path.join(OUT, asset + ".bytes"), "wb") as f:
        f.write(data)
    return {"verts": total, "tris": sum(len(subs[k]["tris"]) for k in order), "submeshes": order, "kb": len(data) // 1024}

def parse_mgm2(path):
    with open(path, "rb") as f: b = f.read()
    assert b[:4] == b"MGM2"
    o = 4; n, = struct.unpack_from("<i", b, o); o += 4
    res = []
    for _ in range(n):
        l, = struct.unpack_from("<i", b, o); o += 4; name = b[o:o + l].decode(); o += l
        col = struct.unpack_from("<5f", b, o); o += 20
        l, = struct.unpack_from("<i", b, o); o += 4; tex = b[o:o + l].decode(); o += l
        vc, = struct.unpack_from("<i", b, o); o += 4
        V = np.frombuffer(b, "<f4", vc * 8, o).reshape(-1, 8); o += vc * 32
        ic, = struct.unpack_from("<i", b, o); o += 4
        I = np.frombuffer(b, "<i4", ic, o); o += ic * 4
        assert ic % 3 == 0 and (ic == 0 or (I.min() >= 0 and I.max() < vc))
        assert np.isfinite(V).all()
        res.append({"name": name, "tex": tex, "verts": vc, "tris": ic // 3,
                    "uv_range": [round(float(V[:, 6].min()), 3), round(float(V[:, 6].max()), 3),
                                 round(float(V[:, 7].min()), 3), round(float(V[:, 7].max()), 3)]})
    assert o == len(b), "trailing bytes"
    return res

# ---------------------------------------------------------------- finalize
def face_points(head_obj, hc):
    """Nose tip and mouth (patient space) from the head mesh."""
    P = pr.get_co(head_obj.data)
    mid = np.abs(P[:, 2]) < 0.004
    Q = P[mid]
    nose = Q[np.argmax(Q[:, 1])]
    # mouth: frontmost mid-line point 2-4 cm below the nose tip (toward the chin = +x)
    m = (Q[:, 0] > nose[0] + 0.02) & (Q[:, 0] < nose[0] + 0.04)
    mouth = Q[m][np.argmax(Q[m][:, 1])] if m.any() else nose + np.array([0.03, -0.01, 0])
    return nose, mouth

def finalize(J, Jl, xneck, closed):
    hc = [float(c) for c in Jl["head"]]
    trunk_pivot = (round(J["shoulderR"][0] + 0.104, 4), 0.0, 0.0)
    groups = {
        "head": (["X_head", "P_eyes", "P_brows", "P_lashes", "P_hair"], hc),
        "eyelids": (["X_eyelids", "X_eyelashes_c"], hc),
        "trunk": (["X_trunkskin", "G_trunk", "G_trim"], trunk_pivot),
        "armL": (["X_armL", "G_L"], J["shoulderL"]), "armR": (["X_armR", "G_R"], J["shoulderR"]),
        "thighL": (["X_thighL"], J["hipL"]), "thighR": (["X_thighR"], J["hipR"]),
        "shinL": (["X_shinL"], J["kneeL"]), "shinR": (["X_shinR"], J["kneeR"]),
    }
    stats = {}
    for a, (names, piv) in groups.items():
        objs = [bpy.data.objects[n] for n in names if n in bpy.data.objects]
        stats[a] = export2(a, objs, piv)
        stats[a]["check"] = parse_mgm2(os.path.join(OUT, a + ".bytes"))
    head = bpy.data.objects["X_head"]
    nose, mouth = face_points(head, hc)
    hcv = np.array(hc)
    mask = np.array([(nose[0] + mouth[0]) / 2 - 0.003, nose[1] + 0.012, 0.0]) - hcv
    eyesP = pr.get_co(bpy.data.objects["P_eyes"].data)
    eR = eyesP[eyesP[:, 2] > 0]
    lidc = (eR.min(0) + eR.max(0)) / 2 - hcv; lidc[1] += 0.004
    ext = eR.max(0) - eR.min(0)
    lid_size = np.array([ext[0] * 0.55, 0.009, ext[2] * 1.05])
    R = lambda v: [round(float(x), 4) for x in v]
    knee_rel = np.array(J["kneeR"]) - np.array(J["hipR"])
    # IV cannula spot: top of the left forearm 5 cm above the wrist, relative to the left shoulder pivot (armL local)
    PA = pr.get_co(bpy.data.objects["X_armL"].data)
    xa = J["wristL"][0] - 0.05
    ma = np.abs(PA[:, 0] - xa) < 0.004
    top = PA[ma][np.argmax(PA[ma][:, 1])] + np.array([0, 0.004, 0])
    cannula = top - np.array(J["shoulderL"])
    piv = {
        "head_centre": R(hc), "trunk_pivot": R(trunk_pivot), "shoulderR": R(J["shoulderR"]), "hipR": R(J["hipR"]),
        "kneeR_rel_hip": R(knee_rel), "mask_on_face": R(mask), "lidR_centre": R(lidc), "lid_size": R(lid_size),
        "cannula_on_armL": R(cannula),
    }
    lines = ["# patient pivots (patient space unless noted; right side = +z). Written by BuildLogs/patient_textured.py"]
    for k, v in piv.items(): lines.append("%s %.4f %.4f %.4f" % (k, v[0], v[1], v[2]))
    with open(os.path.join(OUT, "patient_pivots.txt"), "w", newline="\n") as f: f.write("\n".join(lines) + "\n")
    allv = np.concatenate([pr.get_co(bpy.data.objects[n].data) for n in
                           ("X_head", "P_hair", "X_trunkskin", "G_trunk", "X_armL", "X_armR", "X_thighL", "X_thighR", "X_shinL", "X_shinR")])
    shin = np.concatenate([pr.get_co(bpy.data.objects[n].data) for n in ("X_shinL", "X_shinR")])
    hd = np.concatenate([pr.get_co(bpy.data.objects[n].data) for n in ("X_head", "P_hair")])
    full = dict(piv)
    full.update({"space": "patient space (Unity local): X along bed (head -X), Y up, Z across (patient right = +Z)",
                 "joints": {k: R(v) for k, v in J.items()}, "neck_cut_x": round(xneck, 4), "gown_hem_x": round(pr.HEM_X, 4),
                 "nose_tip": R(nose), "mouth": R(mouth),
                 "extents": {"head_top_x": round(float(hd[:, 0].min()), 4), "toe_tip_x": round(float(shin[:, 0].max()), 4),
                             "mattress_contact_y": round(float(allv[:, 1].min()), 4),
                             "head_back_y": round(float(hd[:, 1].min()), 4),
                             "max_y_belly": round(float(pr.get_co(bpy.data.objects["G_trunk"].data)[:, 1].max()), 4),
                             "half_width_z": round(float(np.abs(allv[:, 2]).max()), 4)},
                 "vertex_counts": {k: v["verts"] for k, v in stats.items()}})
    full["vertex_counts"]["total"] = sum(v["verts"] for v in stats.values())
    with open(os.path.join(LOGS, "patient_pivots.json"), "w") as f: json.dump(full, f, indent=2)
    return full, stats

# ---------------------------------------------------------------- preview renders
def bed_pillow_mesh():
    """The pillow submesh of the app's bed.bytes (MGM1), moved into patient space (patient group sits at y 0.64)."""
    with open(os.path.join(OUT, "bed.bytes"), "rb") as f: b = f.read()
    o = 4; n, = struct.unpack_from("<i", b, o); o += 4
    for _ in range(n):
        l, = struct.unpack_from("<i", b, o); o += 4; name = b[o:o + l].decode(); o += l + 20
        vc, = struct.unpack_from("<i", b, o); o += 4
        V = np.frombuffer(b, "<f4", vc * 6, o).reshape(-1, 6); o += vc * 24
        ic, = struct.unpack_from("<i", b, o); o += 4
        I = np.frombuffer(b, "<i4", ic, o); o += ic * 4
        if name.startswith("pillow"):
            me = bpy.data.meshes.new("bed_pillow")
            P = V[:, :3].astype(np.float64).copy(); P[:, 1] -= 0.64
            me.from_pydata([tuple(p) for p in P], [], [tuple(t) for t in I.reshape(-1, 3)])
            me.update(); return me
    return None

def pillow_top(x, z):
    """Height (patient space) of the app pillow's top surface at (x, z); None outside it."""
    if "pillow_bvh" not in LY:
        me = bed_pillow_mesh()
        bm = bmesh.new(); bm.from_mesh(me); LY["pillow_bvh"] = BVHTree.FromBMesh(bm); bm.free()
        bpy.data.meshes.remove(me)
    hit = LY["pillow_bvh"].ray_cast(Vector((x, 0.5, z)), Vector((0, -1, 0)))
    return hit[0].y if hit[0] is not None else None

def preview_scene():
    pr.preview_props()
    me = bed_pillow_mesh()
    if me is not None:
        pv = bpy.data.objects["V_pillow"]; old = pv.data; pv.data = me
        me.materials.append(pr.PILLOW)
        for p in me.polygons: p.use_smooth = True
        bpy.data.meshes.remove(old)
        mt = bpy.data.objects["V_mattress"]          # app mattress top is y = 0 in patient space
        for v in mt.data.vertices: v.co.y = min(v.co.y, 0.0)
    sc = pr.get_scene()
    sc.render.engine = 'BLENDER_EEVEE'
    sc.render.resolution_x = 1200; sc.render.resolution_y = 900
    sc.view_settings.view_transform = 'Standard'
    sc.world.use_nodes = True
    bg = sc.world.node_tree.nodes.get("Background")
    bg.inputs[0].default_value = (0.78, 0.80, 0.83, 1); bg.inputs[1].default_value = 0.9
    sun = bpy.data.objects.get("pv_sun")
    if sun is None:
        sun = bpy.data.objects.new("pv_sun", bpy.data.lights.new("pv_sun", 'SUN')); sc.collection.objects.link(sun)
    sun.data.energy = 2.6; sun.data.angle = math.radians(8)
    sun.rotation_euler = (math.radians(-50), math.radians(25), math.radians(10))
    return sc

def shot(name, eye, target, lens=35, size=(1200, 900)):
    sc = bpy.data.scenes["PAT"]
    sc.render.resolution_x, sc.render.resolution_y = size
    pr.look(sc.camera, eye, target, lens)
    sc.render.filepath = os.path.join(SHOTS, name + ".png")
    with InScene("PAT"):
        bpy.ops.render.render(write_still=True)
    return sc.render.filepath

def renders(tag, lids=False):
    for n in ("X_eyelids", "X_eyelashes_c"):
        if n in bpy.data.objects: bpy.data.objects[n].hide_render = not lids
    out = []
    out.append(shot("tex_%s_overview" % tag, (0.75, 1.25, 1.05), (-0.12, 0.12, 0), 32))
    out.append(shot("tex_%s_face" % tag, (-0.70, 0.55, 0.12), (-0.80, 0.16, 0.0), 55, (1000, 1000)))
    out.append(shot("tex_%s_side" % tag, (-0.1, 0.30, 2.4), (-0.1, 0.15, 0), 30))
    return out

# ---------------------------------------------------------------- main
def main(rebuild_mh=True, tag="v1", do_renders=True):
    if rebuild_mh or "MH" not in bpy.data.scenes: build_mh()
    tex = make_textures()
    pr.clear_scene()
    for n in [o.name for o in bpy.data.objects if o.name.startswith(("X_", "G_", "P_"))]:
        bpy.data.objects.remove(bpy.data.objects[n], do_unlink=True)
    meshes, J_st = bake(False)
    calibrate(meshes)
    Jl, xneck = stage_posed(meshes, J_st)
    J = build_all(J_st, meshes, Jl, xneck)
    closed, _ = bake(True)
    single_material(closed["body"], M("skin")); single_material(closed["lashes"], M("cutout_lashes"))
    lay(closed["body"], xneck); lay(closed["lashes"], xneck)
    eyelids_obj(meshes["body"], closed["body"], closed["lashes"])
    piv, stats = finalize(J, Jl, xneck, closed)
    pose_rig(False)
    shots = []
    if do_renders:
        setup_preview_materials(); preview_scene()
        shots = renders(tag)
    return {"tex": tex, "pivots": piv, "stats": {k: {kk: vv for kk, vv in v.items() if kk != "check"} for k, v in stats.items()},
            "checks": {k: v["check"] for k, v in stats.items()}, "shots": shots}
