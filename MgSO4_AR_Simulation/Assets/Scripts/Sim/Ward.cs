#if MGSO4_READY
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MgSO4AR
{
    /// <summary>
    /// Procedurally built labour-ward bed space: eclamptic patient, bed, IV pole with MgSO4 bag and
    /// infusion pump, vital-signs monitor, medication tray and urine bag. Units are metres (real size).
    /// The viewer is expected on the +Z side of the bed.
    /// </summary>
    public class Ward : MonoBehaviour
    {
        // live state driven by the simulation
        public float RespRate = 18f;
        public bool Seizing;
        public bool Somnolent;
        public float Flush;          // 0..1 facial flushing
        public bool OxygenOn;

        public readonly Dictionary<string, Interactable> Items = new Dictionary<string, Interactable>();
        public Text MonitorText, PumpText, AlarmText;
        public readonly List<GameObject> Labels = new List<GameObject>();

        Transform torso, belly, head, armL, armR, thighL, thighR, shinL, shinR, eyeL, eyeR, syringeFill, urineFill, o2Mask;
        Renderer headRenderer, alarmLamp;
        Vector3 torsoScale, bellyScale, o2Home, o2OnFace;
        Quaternion armLRot, armRRot, thighLRot, thighRRot, headRot;
        float breathPhase, kickAngle, seizeT, clonicPhase;
        bool alarm;
        Color skin = new Color(0.80f, 0.60f, 0.47f);
        Material headMat;
        readonly List<Material> flushExtra = new List<Material>();

        // ------------------------------------------------------------------
        public static Ward Build(Transform parent)
        {
            var go = new GameObject("Ward");
            go.transform.SetParent(parent, false);
            var w = go.AddComponent<Ward>();
            w.BuildAll();
            return w;
        }

        // Eye placement on the Blender head (head-local: face = +Y, top of head = -X, chin = +X).
        // Centre of each eye = (x, y, +/-z). Tweak here if the head model changes.
        static readonly Vector3 EyeOffset = new Vector3(0.0f, 0.080f, 0.032f);
        const float EyeOpen = 0.012f, EyeClosed = 0.0015f, EyeWidth = 0.026f, EyeDepth = 0.01f;
        // Realistic head (Blender base mesh) has real eyeballs; skin-coloured lids cover them when somnolent.
        static Vector3 LidOffset = new Vector3(-0.0176f, 0.0925f, 0.0333f);
        static Vector3 LidScale = new Vector3(0.0135f, 0.009f, 0.0255f);
        bool eyeLids;

        // Joint pivots of the realistic patient (patient space, from BuildLogs/patient_pivots.json)
        static Vector3 HeadCentre = new Vector3(-0.796f, 0.132f, 0f);
        static Vector3 TrunkPivot = new Vector3(-0.5f, 0f, 0f);
        static Vector3 Shoulder = new Vector3(-0.604f, 0.087f, 0.165f);   // z mirrored for left
        static Vector3 Hip = new Vector3(-0.129f, 0.105f, 0.088f);
        static Vector3 KneeFromHip = new Vector3(0.374f, -0.015f, 0.0313f);
        // knee -> ankle of the realistic leg (patient space), used to keep the heel on the mattress
        static Vector2 AnkleFromKnee = new Vector2(0.38f, -0.046f);
        const float KneeLiftDeg = 35f;
        bool realLegs;

        /// <summary>
        /// Shin angle (about Z, relative to the thigh) that keeps the heel resting on the mattress while the
        /// knee is lifted by 'lift' degrees - like a nurse lifting the knee with a hand underneath it.
        /// </summary>
        float HangAngle(float lift)
        {
            Vector2 knee = realLegs ? new Vector2(KneeFromHip.x, KneeFromHip.y) : new Vector2(0.4f, 0f);
            Vector2 ank = realLegs ? AnkleFromKnee : new Vector2(0.42f, 0f);
            float th = lift * Mathf.Deg2Rad;
            float kneeY = knee.x * Mathf.Sin(th) + knee.y * Mathf.Cos(th);   // knee height above the resting hip line
            float restAnkleY = knee.y + ank.y;                                // ankle height at rest (heel on mattress)
            float L = ank.magnitude;
            float beta = Mathf.Atan2(ank.y, ank.x) * Mathf.Rad2Deg;          // shin's resting slope
            float alpha = Mathf.Asin(Mathf.Clamp((restAnkleY + 0.006f - kneeY) / L, -1f, 1f)) * Mathf.Rad2Deg;
            return alpha - beta - lift;
        }
        static Vector3 MaskOnFace = new Vector3(0.036f, 0.118f, 0f);   // over nose + mouth, clear of the eyes
        static Vector3 CannulaOnArmL = new Vector3(0.3703f, -0.0185f, -0.0875f);   // top of the left forearm
        bool lidsInModel;          // optional "eyelids" model (closed lids, head-local) shown when somnolent
        GameObject lidsModel;

        /// <summary>
        /// Optional Resources/Models/patient_pivots.txt written by the Blender exporter: one "key x y z" per line
        /// (patient space; right side = +z). Keeps the app in sync with whatever body model was exported.
        /// </summary>
        static void LoadPivots()
        {
            var ta = Resources.Load<TextAsset>("Models/patient_pivots");
            if (ta == null) return;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            foreach (var raw in ta.text.Split('\n'))
            {
                var p = raw.Trim().Split(new[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);
                if (p.Length < 2 || p[0].StartsWith("#")) continue;
                if (p.Length < 4) continue;
                float x, y, z;
                if (!float.TryParse(p[1], System.Globalization.NumberStyles.Float, ci, out x) ||
                    !float.TryParse(p[2], System.Globalization.NumberStyles.Float, ci, out y) ||
                    !float.TryParse(p[3], System.Globalization.NumberStyles.Float, ci, out z)) continue;
                var v = new Vector3(x, y, z);
                switch (p[0])
                {
                    case "head_centre": HeadCentre = v; break;
                    case "trunk_pivot": TrunkPivot = v; break;
                    case "shoulderR": Shoulder = v; break;
                    case "hipR": Hip = v; break;
                    case "kneeR_rel_hip": KneeFromHip = v; break;
                    case "lidR_centre": LidOffset = v; break;
                    case "lid_size": LidScale = v; break;
                    case "cannula_on_armL": CannulaOnArmL = v; break;
                }
            }
        }

        bool trunkModel;
        Vector3 cannulaLocal; Transform cannulaArm;

        void BuildAll()
        {
            var t = transform;
            var gown = new Color(0.55f, 0.74f, 0.90f);
            var metal = new Color(0.72f, 0.75f, 0.79f);
            var dark = new Color(0.16f, 0.18f, 0.21f);

            // floor tile
            if (ModelLib.Spawn("floor", t) == null)
                Prim.Make(PrimitiveType.Cube, t, new Vector3(0.1f, -0.01f, 0.1f), new Vector3(2.9f, 0.02f, 2.1f), new Color(0.80f, 0.85f, 0.89f), "Floor");

            // ---------------- bed ----------------
            var bed = Prim.Group("Bed", t, Vector3.zero);
            if (ModelLib.Spawn("bed", bed) == null)
            {
                Prim.Make(PrimitiveType.Cube, bed, new Vector3(0, 0.45f, 0), new Vector3(2.0f, 0.1f, 0.92f), metal, "Frame");
                foreach (var x in new[] { -0.92f, 0.92f })
                    foreach (var z in new[] { -0.4f, 0.4f })
                        Prim.Make(PrimitiveType.Cylinder, bed, new Vector3(x, 0.2f, z), new Vector3(0.05f, 0.2f, 0.05f), dark, "Leg");
                Prim.Make(PrimitiveType.Cube, bed, new Vector3(0, 0.57f, 0), new Vector3(1.96f, 0.14f, 0.88f), new Color(0.95f, 0.96f, 0.97f), "Mattress");
                Prim.Make(PrimitiveType.Cube, bed, new Vector3(-1.0f, 0.78f, 0), new Vector3(0.06f, 0.6f, 0.92f), new Color(0.45f, 0.58f, 0.72f), "Headboard");
                Prim.Make(PrimitiveType.Cube, bed, new Vector3(1.0f, 0.66f, 0), new Vector3(0.06f, 0.36f, 0.92f), new Color(0.45f, 0.58f, 0.72f), "Footboard");
                Prim.Make(PrimitiveType.Cube, bed, new Vector3(-0.2f, 0.82f, -0.46f), new Vector3(1.1f, 0.04f, 0.03f), metal, "SideRail");
                Prim.Make(PrimitiveType.Cube, bed, new Vector3(-0.2f, 0.82f, 0.46f), new Vector3(1.1f, 0.04f, 0.03f), new Color(0.85f, 0.4f, 0.4f), "PaddedRail");
                Prim.Make(PrimitiveType.Cube, bed, new Vector3(-0.8f, 0.685f, 0), new Vector3(0.34f, 0.09f, 0.56f), Color.white, "Pillow");
                // sheet over left leg / lower body edge
                Prim.Make(PrimitiveType.Cube, bed, new Vector3(0.62f, 0.66f, -0.2f), new Vector3(0.72f, 0.05f, 0.42f), new Color(0.88f, 0.92f, 0.96f), "Sheet");
            }

            // ---------------- patient ----------------
            var patient = Prim.Group("Patient", t, new Vector3(0, 0.64f, 0));
            bool realBody = ModelLib.Has("head") && ModelLib.Has("trunk");
            if (realBody) LoadPivots();
            var headG = Prim.Group("HeadGroup", patient, realBody ? HeadCentre : new Vector3(-0.8f, 0.14f, 0));
            head = headG; headRot = headG.localRotation;
            var eyeCol = new Color(0.08f, 0.06f, 0.05f);
            var headModel = ModelLib.Spawn("head", headG);
            if (headModel != null)
            {
                headRenderer = headModel.GetComponent<Renderer>();
                var hm = headRenderer.materials;          // per-renderer instances; skin = submesh 0
                headMat = hm[0];
                skin = Prim.GetColor(headMat);           // flushing tints from the model's own skin tone
                eyeLids = true;
                eyeL = Prim.Make(PrimitiveType.Sphere, headG, new Vector3(LidOffset.x, LidOffset.y, -LidOffset.z), LidScale, skin, "LidL").transform;
                eyeR = Prim.Make(PrimitiveType.Sphere, headG, new Vector3(LidOffset.x, LidOffset.y, LidOffset.z), LidScale, skin, "LidR").transform;
                eyeL.GetComponent<Renderer>().sharedMaterial = headMat;   // lids share the face material (same tone + flush)
                eyeR.GetComponent<Renderer>().sharedMaterial = headMat;
                lidsModel = ModelLib.Spawn("eyelids", headG);
                if (lidsModel != null)
                {
                    lidsInModel = true; eyeLids = false;
                    var lr = lidsModel.GetComponent<Renderer>();          // closed lids flush with the face
                    var lm = lr.sharedMaterials; lm[0] = headMat; lr.sharedMaterials = lm;
                    eyeL.gameObject.SetActive(false); eyeR.gameObject.SetActive(false);
                    lidsModel.SetActive(false);
                }
            }
            else
            {
                var h = Prim.Make(PrimitiveType.Sphere, headG, Vector3.zero, new Vector3(0.22f, 0.22f, 0.21f), skin, "Head");
                headRenderer = h.GetComponent<Renderer>();
                headMat = new Material(headRenderer.sharedMaterial);
                headRenderer.material = headMat;
                Prim.Make(PrimitiveType.Sphere, headG, new Vector3(-0.035f, -0.03f, 0), new Vector3(0.235f, 0.2f, 0.235f), new Color(0.13f, 0.09f, 0.07f), "Hair");
                eyeL = Prim.Make(PrimitiveType.Sphere, headG, new Vector3(0.035f, 0.095f, -0.045f), new Vector3(EyeOpen, EyeDepth, EyeWidth), eyeCol, "EyeL").transform;
                eyeR = Prim.Make(PrimitiveType.Sphere, headG, new Vector3(0.035f, 0.095f, 0.045f), new Vector3(EyeOpen, EyeDepth, EyeWidth), eyeCol, "EyeR").transform;
                Prim.Make(PrimitiveType.Sphere, headG, new Vector3(0.0f, 0.112f, 0), new Vector3(0.03f, 0.04f, 0.03f), skin, "Nose");
            }

            var chest = Prim.Group("ChestGroup", patient, Vector3.zero);
            var trunk = ModelLib.Spawn("trunk", chest, realBody ? TrunkPivot : new Vector3(-0.38f, 0, 0));
            if (trunk != null)
            {
                trunkModel = true;
                torso = trunk.transform;
                var tr = trunk.GetComponent<Renderer>();
                var tm = tr.materials;                                     // neck/upper-chest skin flushes too
                if (tm.Length > 0 && headMat != null) flushExtra.Add(tm[0]);
                belly = null;
            }
            else
            {
                Prim.Make(PrimitiveType.Cylinder, patient, new Vector3(-0.66f, 0.1f, 0), new Vector3(0.09f, 0.05f, 0.09f), skin, "Neck", new Vector3(0, 0, 90));
                torso = Prim.Make(PrimitiveType.Capsule, chest, new Vector3(-0.38f, 0.11f, 0), new Vector3(0.32f, 0.3f, 0.36f), gown, "Torso", new Vector3(0, 0, 90)).transform;
                belly = Prim.Make(PrimitiveType.Sphere, chest, new Vector3(-0.12f, 0.17f, 0), new Vector3(0.38f, 0.3f, 0.36f), gown, "PregnantAbdomen").transform;
                Prim.Make(PrimitiveType.Sphere, chest, new Vector3(0.05f, 0.1f, 0), new Vector3(0.3f, 0.22f, 0.36f), gown, "Pelvis");
            }
            torsoScale = torso.localScale;
            if (belly != null) bellyScale = belly.localScale;

            armL = Prim.Group("ArmL", patient, realBody ? new Vector3(Shoulder.x, Shoulder.y, -Shoulder.z) : new Vector3(-0.6f, 0.05f, -0.25f));
            if (ModelLib.Spawn("armL", armL) == null)
                Prim.Make(PrimitiveType.Capsule, armL, new Vector3(0.24f, 0, 0), new Vector3(0.1f, 0.24f, 0.1f), skin, "Arm", new Vector3(0, 0, 90));
            armR = Prim.Group("ArmR", patient, realBody ? Shoulder : new Vector3(-0.6f, 0.05f, 0.25f));
            if (ModelLib.Spawn("armR", armR) == null)
                Prim.Make(PrimitiveType.Capsule, armR, new Vector3(0.24f, 0, 0), new Vector3(0.1f, 0.24f, 0.1f), skin, "Arm", new Vector3(0, 0, 90));
            cannulaLocal = realBody ? CannulaOnArmL : new Vector3(0.40f, 0.035f, 0);
            Prim.Make(PrimitiveType.Cube, realBody ? armL : armR, cannulaLocal, new Vector3(0.035f, 0.008f, 0.025f), Color.white, "IVCannula");
            cannulaArm = realBody ? armL : armR;
            armLRot = armL.localRotation; armRRot = armR.localRotation;

            realLegs = realBody;
            thighL = BuildLeg(patient, "LeftLeg", "L", -1f, realBody, skin, out shinL);
            var kneeGroup = BuildLeg(patient, "RightLeg", "R", 1f, realBody, skin, out shinR);
            thighR = kneeGroup;
            thighLRot = thighL.localRotation; thighRRot = thighR.localRotation;

            Register(chest, "chest", "Chest (count respirations)");
            Register(kneeGroup, "knee", "Right knee (patellar reflex)");

            var patientLabel = UIKit.WorldText(t, "Mrs. Amal  |  24 y  |  G2 P1  |  34 weeks", new Vector3(0.25f, 1.12f, 0.05f),
                new Vector2(0.8f, 0.09f), 0.045f, Color.white, true, new Color(0.08f, 0.16f, 0.25f, 0.85f));
            Labels.Add(patientLabel.canvas.gameObject);

            // ---------------- IV pole, bag, pump ----------------
            var pole = Prim.Group("IVPole", t, new Vector3(-1.12f, 0, -0.55f));
            if (ModelLib.Spawn("ivpole", pole) == null)
            {
                Prim.Make(PrimitiveType.Cylinder, pole, new Vector3(0, 0.95f, 0), new Vector3(0.03f, 0.95f, 0.03f), metal, "Pole");
                Prim.Make(PrimitiveType.Cube, pole, new Vector3(0, 0.02f, 0), new Vector3(0.45f, 0.03f, 0.05f), dark, "Base1");
                Prim.Make(PrimitiveType.Cube, pole, new Vector3(0, 0.02f, 0), new Vector3(0.05f, 0.03f, 0.45f), dark, "Base2");
                Prim.Make(PrimitiveType.Cube, pole, new Vector3(0, 1.88f, 0), new Vector3(0.3f, 0.02f, 0.02f), metal, "Hook");
            }
            var bag = Prim.Group("Bag", pole, new Vector3(0.1f, 1.68f, 0));
            bool bagModel = ModelLib.Spawn("ivbag", bag) != null;
            if (!bagModel)
            {
                Prim.Make(PrimitiveType.Cube, bag, Vector3.zero, new Vector3(0.16f, 0.26f, 0.05f), new Color(0.9f, 0.95f, 1f, 0.45f), "BagShell", null, true);
                Prim.Make(PrimitiveType.Cube, bag, new Vector3(0, -0.03f, 0), new Vector3(0.14f, 0.18f, 0.04f), new Color(0.75f, 0.88f, 1f, 0.7f), "Fluid", null, true);
                Prim.Make(PrimitiveType.Cube, bag, new Vector3(0, 0.02f, 0.027f), new Vector3(0.1f, 0.06f, 0.002f), new Color(0.95f, 0.45f, 0.2f), "HighAlertTag");
            }
            var bagLabel = UIKit.WorldText(bag, "HIGH ALERT\nMgSO4 20 g / 500 mL", new Vector3(0.27f, 0.02f, 0), new Vector2(0.38f, 0.11f), 0.032f,
                Color.white, true, new Color(0.75f, 0.25f, 0.1f, 0.9f));
            Labels.Add(bagLabel.canvas.gameObject);

            var pump = Prim.Group("Pump", pole, new Vector3(0.0f, 1.15f, 0.07f));
            if (ModelLib.Spawn("pump", pump) == null)
            {
                Prim.Make(PrimitiveType.Cube, pump, Vector3.zero, new Vector3(0.26f, 0.22f, 0.12f), new Color(0.88f, 0.9f, 0.93f), "PumpBody");
                Prim.Make(PrimitiveType.Cube, pump, new Vector3(0, 0.03f, 0.061f), new Vector3(0.2f, 0.1f, 0.004f), new Color(0.05f, 0.09f, 0.12f), "Screen");
                Prim.Make(PrimitiveType.Cube, pump, new Vector3(-0.05f, -0.065f, 0.062f), new Vector3(0.04f, 0.03f, 0.01f), new Color(0.2f, 0.7f, 0.3f), "StartBtn");
                Prim.Make(PrimitiveType.Cube, pump, new Vector3(0.05f, -0.065f, 0.062f), new Vector3(0.04f, 0.03f, 0.01f), new Color(0.85f, 0.25f, 0.25f), "StopBtn");
            }
            Register(pump, "pump", "Infusion pump");
            // model screen: y -0.02..0.08, front face z 0.060
            PumpText = UIKit.WorldText(pump, "", new Vector3(0, 0.03f, 0.065f), new Vector2(0.2f, 0.1f), 0.022f,
                new Color(0.55f, 1f, 0.82f), false, null, Quaternion.Euler(0, 180, 0));

            // ---------------- monitor ----------------
            var mon = Prim.Group("Monitor", t, new Vector3(-1.15f, 0, 0.62f), new Vector3(0, 30, 0));
            bool monModel = ModelLib.Spawn("monitor", mon) != null;
            if (!monModel)
            {
                Prim.Make(PrimitiveType.Cylinder, mon, new Vector3(0, 0.68f, 0), new Vector3(0.04f, 0.68f, 0.04f), metal, "Stand");
                Prim.Make(PrimitiveType.Cylinder, mon, new Vector3(0, 0.02f, 0), new Vector3(0.36f, 0.02f, 0.36f), dark, "Base");
                Prim.Make(PrimitiveType.Cube, mon, new Vector3(0, 1.52f, 0), new Vector3(0.58f, 0.42f, 0.07f), new Color(0.2f, 0.22f, 0.25f), "Housing");
                Prim.Make(PrimitiveType.Cube, mon, new Vector3(0, 1.52f, 0.036f), new Vector3(0.52f, 0.36f, 0.004f), new Color(0.02f, 0.03f, 0.04f), "Display");
            }
            alarmLamp = Prim.Make(PrimitiveType.Sphere, mon, new Vector3(0.24f, 1.76f, monModel ? 0.02f : 0f), new Vector3(0.05f, 0.05f, 0.05f), new Color(0.3f, 0.1f, 0.1f), "AlarmLamp").GetComponent<Renderer>();
            alarmLamp.material = new Material(alarmLamp.sharedMaterial);
            Register(mon, "monitor", "Vital signs monitor");
            // model display face at z 0.037..0.040 -> text a hair in front
            MonitorText = UIKit.WorldText(mon, "", new Vector3(0, 1.5f, 0.043f), new Vector2(0.5f, 0.3f), 0.036f, Color.white,
                false, null, Quaternion.Euler(0, 180, 0), TextAnchor.UpperLeft);
            MonitorText.alignment = TextAnchor.UpperLeft;
            AlarmText = UIKit.WorldText(mon, "", new Vector3(0, 1.79f, monModel ? 0.065f : 0.04f), new Vector2(0.5f, 0.06f), 0.034f, new Color(1f, 0.35f, 0.35f),
                false, null, Quaternion.Euler(0, 180, 0));

            // ---------------- tray / medications ----------------
            var tray = Prim.Group("Tray", t, new Vector3(1.12f, 0, 0.78f), new Vector3(0, -10, 0));
            if (ModelLib.Spawn("tray", tray) == null)
            {
                Prim.Make(PrimitiveType.Cube, tray, new Vector3(0, 0.8f, 0), new Vector3(0.62f, 0.03f, 0.42f), new Color(0.82f, 0.85f, 0.88f), "Top");
                Prim.Make(PrimitiveType.Cylinder, tray, new Vector3(0, 0.4f, 0), new Vector3(0.035f, 0.4f, 0.035f), metal, "Leg");
                Prim.Make(PrimitiveType.Cube, tray, new Vector3(0, 0.02f, 0), new Vector3(0.4f, 0.03f, 0.3f), dark, "Foot");
            }

            float top = 0.815f;
            var mg = Prim.Group("MgSO4Vials", tray, new Vector3(-0.22f, top, -0.1f));
            if (ModelLib.Spawn("mgvials", mg) == null)
                for (int i = 0; i < 3; i++)
                {
                    Prim.Make(PrimitiveType.Cylinder, mg, new Vector3(i * 0.05f - 0.05f, 0.04f, 0), new Vector3(0.04f, 0.04f, 0.04f), new Color(0.9f, 0.95f, 1f, 0.55f), "Vial", null, true);
                    Prim.Make(PrimitiveType.Cylinder, mg, new Vector3(i * 0.05f - 0.05f, 0.085f, 0), new Vector3(0.042f, 0.006f, 0.042f), new Color(0.2f, 0.4f, 0.9f), "Cap");
                }
            Register(mg, "mgso4", "MgSO4 50% vials (1 g/2 mL)");
            AddLabel(mg, "MgSO4 50%\n1 g / 2 mL", new Vector3(0, 0.15f, 0), new Color(0.15f, 0.3f, 0.7f, 0.9f));

            var water = Prim.Group("Water", tray, new Vector3(-0.04f, top, -0.1f));
            if (ModelLib.Spawn("water", water) == null)
            {
                Prim.Make(PrimitiveType.Cylinder, water, new Vector3(0, 0.05f, 0), new Vector3(0.035f, 0.05f, 0.035f), new Color(0.95f, 0.97f, 1f, 0.6f), "Ampoule", null, true);
                Prim.Make(PrimitiveType.Cylinder, water, new Vector3(0, 0.11f, 0), new Vector3(0.018f, 0.012f, 0.018f), new Color(0.9f, 0.95f, 1f, 0.6f), "Neck", null, true);
            }
            Register(water, "water", "Sterile water for injection");
            AddLabel(water, "Water for\ninjection", new Vector3(0, 0.25f, 0), new Color(0.2f, 0.45f, 0.55f, 0.9f));

            var lig = Prim.Group("Lignocaine", tray, new Vector3(0.1f, top, -0.1f));
            if (ModelLib.Spawn("lignocaine", lig) == null)
            {
                Prim.Make(PrimitiveType.Cylinder, lig, new Vector3(0, 0.035f, 0), new Vector3(0.035f, 0.035f, 0.035f), new Color(0.95f, 0.95f, 0.9f, 0.6f), "Vial", null, true);
                Prim.Make(PrimitiveType.Cylinder, lig, new Vector3(0, 0.075f, 0), new Vector3(0.037f, 0.006f, 0.037f), new Color(0.95f, 0.6f, 0.1f), "Cap");
            }
            Register(lig, "lignocaine", "Lignocaine (lidocaine) 2%");
            AddLabel(lig, "Lignocaine\n2%", new Vector3(0, 0.15f, 0), new Color(0.65f, 0.4f, 0.05f, 0.9f));

            var ca = Prim.Group("CaGluconate", tray, new Vector3(0.23f, top, -0.1f));
            if (ModelLib.Spawn("cagluc", ca) == null)
            {
                Prim.Make(PrimitiveType.Cylinder, ca, new Vector3(0, 0.05f, 0), new Vector3(0.035f, 0.05f, 0.035f), new Color(1f, 0.95f, 0.95f, 0.6f), "Ampoule", null, true);
                Prim.Make(PrimitiveType.Cylinder, ca, new Vector3(0, 0.105f, 0), new Vector3(0.037f, 0.006f, 0.037f), new Color(0.85f, 0.15f, 0.2f), "Cap");
            }
            Register(ca, "cagluc", "Calcium gluconate 10% (antidote)");
            AddLabel(ca, "ANTIDOTE\nCa gluconate 10%", new Vector3(0, 0.25f, 0), new Color(0.75f, 0.12f, 0.18f, 0.92f));

            // syringe: the model lies on its side (finger flange flat on the tray), barrel radius 0.014
            bool syrModel = ModelLib.Has("syringe");
            var syr = Prim.Group("Syringe", tray, new Vector3(-0.15f, top + (syrModel ? 0.016f : 0.025f), 0.09f));
            if (syrModel)
            {
                ModelLib.Spawn("syringe", syr, Vector3.zero, new Vector3(90, 0, 0));
                var fillPivot = Prim.Group("FillPivot", syr, new Vector3(0.07f, 0, 0));
                syringeFill = Prim.Make(PrimitiveType.Cylinder, fillPivot, new Vector3(-0.07f, 0, 0), new Vector3(0.024f, 0.07f, 0.024f), new Color(0.65f, 0.85f, 1f), "Fill", new Vector3(0, 0, 90)).transform.parent;
            }
            else
            {
                Prim.Make(PrimitiveType.Cylinder, syr, Vector3.zero, new Vector3(0.035f, 0.09f, 0.035f), new Color(0.95f, 0.97f, 1f, 0.45f), "Barrel", new Vector3(0, 0, 90), true);
                var fillPivot = Prim.Group("FillPivot", syr, new Vector3(0.09f, 0, 0));
                syringeFill = Prim.Make(PrimitiveType.Cylinder, fillPivot, new Vector3(-0.09f, 0, 0), new Vector3(0.03f, 0.09f, 0.03f), new Color(0.65f, 0.85f, 1f), "Fill", new Vector3(0, 0, 90)).transform.parent;
                Prim.Make(PrimitiveType.Cylinder, syr, new Vector3(-0.12f, 0, 0), new Vector3(0.012f, 0.04f, 0.012f), Color.white, "Plunger", new Vector3(0, 0, 90));
                Prim.Make(PrimitiveType.Cylinder, syr, new Vector3(0.13f, 0, 0), new Vector3(0.004f, 0.04f, 0.004f), metal, "Needle", new Vector3(0, 0, 90));
            }
            Register(syr, "syringe", "20 mL syringe");
            AddLabel(syr, "20 mL syringe", new Vector3(-0.06f, 0.09f, 0.05f), new Color(0.25f, 0.3f, 0.38f, 0.9f));
            SetSyringeFill(0);

            bool hamModel = ModelLib.Has("hammer");
            var ham = Prim.Group("Hammer", tray, new Vector3(0.10f, top + (hamModel ? 0.008f : 0.015f), 0.0f));
            if (ModelLib.Spawn("hammer", ham) == null)
            {
                Prim.Make(PrimitiveType.Cylinder, ham, Vector3.zero, new Vector3(0.012f, 0.08f, 0.012f), metal, "Handle", new Vector3(0, 0, 90));
                Prim.Make(PrimitiveType.Cylinder, ham, new Vector3(-0.085f, 0, 0), new Vector3(0.035f, 0.025f, 0.035f), new Color(0.85f, 0.2f, 0.2f), "Head", new Vector3(90, 0, 0));
            }
            Register(ham, "hammer", "Reflex hammer");
            AddLabel(ham, "Reflex hammer", new Vector3(0, 0.33f, 0), new Color(0.25f, 0.3f, 0.38f, 0.9f));

            var o2 = Prim.Group("O2Mask", tray, new Vector3(0.24f, top + 0.03f, 0.1f));
            o2Mask = Prim.Group("Mask", o2, Vector3.zero);
            if (ModelLib.Spawn("o2mask", o2Mask) == null)
            {
                Prim.Make(PrimitiveType.Sphere, o2Mask, Vector3.zero, new Vector3(0.08f, 0.045f, 0.065f), new Color(0.55f, 0.95f, 0.6f, 0.6f), "Mask", null, true);
                Prim.Make(PrimitiveType.Cylinder, o2Mask, new Vector3(0.0f, -0.02f, 0.05f), new Vector3(0.01f, 0.05f, 0.01f), new Color(0.55f, 0.95f, 0.6f, 0.8f), "Tube", new Vector3(90, 0, 0), true);
            }
            Register(o2, "o2", "Oxygen face mask");
            AddLabel(o2, "O2 mask", new Vector3(0, 0.08f, 0.03f), new Color(0.15f, 0.5f, 0.25f, 0.9f));
            o2Home = o2Mask.localPosition;

            // ---------------- urine bag ----------------
            var ub = Prim.Group("UrineBag", t, new Vector3(0.45f, 0.3f, 0.5f));
            bool ubModel = ModelLib.Spawn("urinebag", ub) != null;
            if (!ubModel)
            {
                Prim.Make(PrimitiveType.Cube, ub, new Vector3(0, 0.13f, -0.01f), new Vector3(0.02f, 0.06f, 0.02f), metal, "Hook");
                Prim.Make(PrimitiveType.Cube, ub, Vector3.zero, new Vector3(0.17f, 0.22f, 0.02f), new Color(0.95f, 0.95f, 0.95f, 0.5f), "Bag", null, true);
            }
            // fill sits inside the bag (bag 0.17 x 0.22, ~0.022 thick): slightly narrower and thinner
            var ufPivot = Prim.Group("FillPivot", ub, new Vector3(0, ubModel ? -0.104f : -0.1f, 0));
            urineFill = ufPivot;
            Prim.Make(PrimitiveType.Cube, ufPivot, new Vector3(0, 0.5f, 0), ubModel ? new Vector3(0.155f, 1f, 0.014f) : new Vector3(0.15f, 1f, 0.018f),
                new Color(0.95f, 0.8f, 0.2f), "Urine");
            SetUrineLevel(0.35f);
            Register(ub, "urine", "Urine bag (Foley catheter)");
            AddLabel(ub, "Urine bag", new Vector3(0, 0.2f, 0.02f), new Color(0.25f, 0.3f, 0.38f, 0.9f));

            // ---------------- tubing ----------------
            // IV line: bag -> pump -> down beside the bed (patient's left, where the pole stands) -> cannula.
            // Foley: from between the legs over the mattress edge down to the urine bag. Nothing crosses the face.
            var tube = new Color(0.85f, 0.92f, 1f, 0.8f);
            Vector3 bagBottom = bagModel ? new Vector3(-0.995f, 1.51f, -0.55f) : new Vector3(-1.02f, 1.54f, -0.55f),
                pumpTop = new Vector3(-1.12f, 1.27f, -0.48f),
                pumpBottom = new Vector3(-1.12f, 1.03f, -0.48f),
                cannula = t.InverseTransformPoint(cannulaArm.TransformPoint(cannulaLocal));
            Polyline(t, new[] { bagBottom, pumpTop }, 0.007f, tube);
            Polyline(t, new[] { pumpBottom, new Vector3(-1.0f, 0.86f, -0.52f), new Vector3(-0.55f, 0.74f, -0.50f),
                new Vector3(cannula.x + 0.06f, cannula.y + 0.01f, cannula.z - 0.08f), cannula }, 0.007f, tube);
            var urineCol = new Color(0.95f, 0.85f, 0.5f, 0.85f);
            Vector3 crotch = realBody ? new Vector3(-0.07f, 0.70f, 0.0f) : new Vector3(0.1f, 0.68f, 0.12f);
            Polyline(t, new[] { crotch, new Vector3(0.12f, 0.665f, 0.30f), new Vector3(0.30f, 0.655f, 0.445f),
                new Vector3(0.38f, 0.56f, 0.47f), new Vector3(0.45f, 0.44f, 0.5f) }, 0.008f, urineCol);

            // mask over nose and mouth (head-local; face = +Y, chin = +X)
            o2OnFace = transform.InverseTransformPoint(head.TransformPoint(realBody ? MaskOnFace : new Vector3(0.05f, 0.11f, 0)));
        }

        Transform BuildLeg(Transform patient, string name, string side, float sgn, bool realBody, Color skin, out Transform shin)
        {
            var g = Prim.Group(name, patient, realBody ? new Vector3(Hip.x, Hip.y, sgn * Hip.z) : new Vector3(0.1f, 0.08f, sgn * 0.105f));
            if (ModelLib.Spawn("thigh" + side, g) == null)
            {
                Prim.Make(PrimitiveType.Capsule, g, new Vector3(0.2f, 0, 0), new Vector3(0.16f, 0.2f, 0.16f), skin, "Thigh", new Vector3(0, 0, 90));
                Prim.Make(PrimitiveType.Sphere, g, new Vector3(0.4f, 0.02f, 0), new Vector3(0.12f, 0.12f, 0.12f), skin, "Knee");
            }
            shin = Prim.Group("Shin" + side, g, realBody ? new Vector3(KneeFromHip.x, KneeFromHip.y, sgn * KneeFromHip.z) : new Vector3(0.4f, 0, 0));
            if (ModelLib.Spawn("shin" + side, shin) == null)
            {
                Prim.Make(PrimitiveType.Capsule, shin, new Vector3(0.2f, 0, 0), new Vector3(0.12f, 0.2f, 0.12f), skin, "Shin", new Vector3(0, 0, 90));
                Prim.Make(PrimitiveType.Cube, shin, new Vector3(0.42f, 0.05f, 0), new Vector3(0.06f, 0.12f, 0.09f), skin, "Foot");
            }
            return g;
        }

        static void Polyline(Transform parent, Vector3[] pts, float d, Color c)
        {
            for (int i = 0; i < pts.Length - 1; i++) Prim.Tube(parent, pts[i], pts[i + 1], d, c, true);
        }

        void Register(Transform g, string id, string display)
        {
            var it = g.gameObject.AddComponent<Interactable>();
            it.Init(id, display);
            Items[id] = it;
        }

        void AddLabel(Transform parent, string text, Vector3 pos, Color bg)
        {
            var l = UIKit.WorldText(parent, text, pos, new Vector2(0.2f, 0.066f), 0.02f, Color.white, true, bg);
            Labels.Add(l.canvas.gameObject);
        }

        // ------------------------------------------------------------------
        public void SetLabelsVisible(bool on)
        {
            foreach (var l in Labels) if (l) l.SetActive(on);
        }

        public void SetVisible(bool on)
        {
            foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = on;
            foreach (var c in GetComponentsInChildren<Canvas>(true)) c.enabled = on;
            foreach (var c in GetComponentsInChildren<Collider>(true)) c.enabled = on;
        }

        public void Highlight(IEnumerable<string> ids)
        {
            var set = new HashSet<string>(ids ?? new string[0]);
            foreach (var kv in Items) kv.Value.SetHighlight(set.Contains(kv.Key));
        }

        public void SetSyringeFill(float f)
        {
            syringeFill.localScale = new Vector3(Mathf.Clamp01(Mathf.Max(f, 0.001f)), 1, 1);
            syringeFill.gameObject.SetActive(f > 0.001f);
        }

        public void SetUrineLevel(float f)
        {
            urineFill.localScale = new Vector3(1, Mathf.Clamp(f, 0.01f, 1f) * 0.2f, 1);
        }

        public void SetPump(float rate, bool running, string drug = "MgSO4 0.04 g/mL")
        {
            if (PumpText == null) return;
            PumpText.text = running
                ? $"{drug}\n<size=34>{rate:0.0} mL/h</size>\n<color=#7CFF9E>RUNNING</color>"
                : $"{drug}\n<size=34>{rate:0.0} mL/h</size>\n<color=#FF7C7C>STOPPED</color>";
        }

        public void SetOxygen(bool on)
        {
            OxygenOn = on;
            o2Mask.localPosition = o2Home;
            o2Mask.localScale = on ? new Vector3(0.8f, 1f, 0.85f) : Vector3.one;   // adult mask: bridge of nose to chin
            if (on) o2Mask.position = transform.TransformPoint(o2OnFace);
        }

        public void SetAlarm(bool on, string text)
        {
            alarm = on;
            if (AlarmText) AlarmText.text = on ? text : "";
        }

        public void Kick(int reflexGrade)
        {
            if (reflexGrade > 0) Haptics.Click();
            StopCoroutine("KickRoutine");
            StartCoroutine(KickRoutine(reflexGrade));
        }

        float kneeLift, shinBase;

        /// <summary>
        /// Patellar reflex test as done at the bedside: the nurse lifts the knee (hand under the knee,
        /// lower leg hanging), taps the tendon below the kneecap, the lower leg extends briefly
        /// according to the reflex grade, then the leg is lowered again.
        /// </summary>
        IEnumerator KickRoutine(int grade)
        {
            float amp = grade <= 0 ? 0f : grade == 1 ? 7f : grade == 2 ? 16f : 26f;
            // 1a. lift at the hip first: the thigh rises with the leg still straight (heel leaves the bed)
            float t = 0f;
            while (t < 0.4f)
            {
                t += Time.deltaTime;
                kneeLift = KneeLiftDeg * Ease.InOutSine(t / 0.4f); shinBase = 0f;
                yield return null;
            }
            // 1b. the lower leg relaxes and hangs from the knee until the heel rests on the mattress again
            t = 0f;
            float hang = HangAngle(KneeLiftDeg) - HangAngle(0f);
            while (t < 0.35f)
            {
                t += Time.deltaTime;
                shinBase = hang * Ease.InOutSine(t / 0.35f);
                yield return null;
            }
            // 2. hammer tap
            yield return new WaitForSeconds(0.2f);
            // 3. reflex: quick extension of the lower leg, slower return
            if (amp > 0f)
            {
                t = 0f;
                while (t < 0.12f) { t += Time.deltaTime; kickAngle = amp * Ease.OutCubic(t / 0.12f); yield return null; }
                t = 0f;
                while (t < 0.35f) { t += Time.deltaTime; kickAngle = amp * (1f - Ease.InOutSine(t / 0.35f)); yield return null; }
                kickAngle = 0f;
                if (grade >= 4)
                    for (int i = 0; i < 4; i++)
                    {
                        t = 0f;
                        while (t < 0.2f) { t += Time.deltaTime; kickAngle = 8f * Mathf.Sin(t / 0.2f * Mathf.PI); yield return null; }
                    }
                kickAngle = 0f;
            }
            yield return new WaitForSeconds(0.45f);
            // 4. lower the leg back onto the bed
            t = 0f;
            while (t < 0.5f)
            {
                t += Time.deltaTime;
                float k = 1f - Ease.InOutSine(t / 0.5f);
                kneeLift = KneeLiftDeg * k; shinBase = HangAngle(kneeLift) - HangAngle(0f);
                yield return null;
            }
            kneeLift = 0f; shinBase = 0f;
        }

        void Update()
        {
            // breathing
            if (RespRate > 0.5f)
            {
                breathPhase += Time.deltaTime * RespRate / 60f * Mathf.PI * 2f;
                float b = Mathf.Sin(breathPhase);
                if (trunkModel)   // Blender trunk: chest/belly rise (pivot on the mattress)
                    torso.localScale = new Vector3(torsoScale.x, torsoScale.y * (1f + 0.035f * b), torsoScale.z);
                else
                    torso.localScale = new Vector3(torsoScale.x * (1f + 0.06f * b), torsoScale.y, torsoScale.z * (1f + 0.02f * b));
                if (belly != null) belly.localScale = new Vector3(bellyScale.x, bellyScale.y * (1f + 0.025f * b), bellyScale.z);
            }
            else
            {
                torso.localScale = torsoScale;
                if (belly != null) belly.localScale = bellyScale;
            }

            // eclamptic seizure, modelled on a generalised tonic-clonic seizure:
            //  tonic phase (~1.2 s): body stiffens - head extends slightly, arms held rigid with a fine tremor;
            //  clonic phase: rhythmic flexion jerks of arms/legs/head, ~3 Hz slowing to ~1.6 Hz, small amplitude.
            if (Seizing)
            {
                seizeT += Time.deltaTime;
                float tremor = Mathf.Sin(Time.time * 2f * Mathf.PI * 9f);
                if (seizeT < 1.2f)
                {
                    float k = Mathf.Clamp01(seizeT / 0.3f);
                    head.localRotation = headRot * Quaternion.Euler(0, 0, 4f * k + 0.3f * tremor);
                    armL.localRotation = armLRot * Quaternion.Euler(0, -3f * k, 2f * k + 0.4f * tremor);
                    armR.localRotation = armRRot * Quaternion.Euler(0, 3f * k, 2f * k + 0.4f * tremor);
                    thighL.localRotation = thighLRot * Quaternion.Euler(0, 0, 0.3f * tremor);
                    thighR.localRotation = thighRRot * Quaternion.Euler(0, 0, kneeLift + 0.3f * tremor);
                }
                else
                {
                    float u = Mathf.Clamp01((seizeT - 1.2f) / 10f);
                    clonicPhase += Time.deltaTime * Mathf.Lerp(3.0f, 1.6f, u);
                    float j = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(clonicPhase * 2f * Mathf.PI)), 6f);   // sharp jerk, then relax
                    float amp = Mathf.Lerp(1f, 0.6f, u);
                    float side = Mathf.Sin(clonicPhase * Mathf.PI) > 0 ? 1f : -1f;                  // head alternates slightly
                    head.localRotation = headRot * Quaternion.Euler(3f * j * amp * side, 0, 2f + 3f * j * amp);
                    armL.localRotation = armLRot * Quaternion.Euler(0, -2f * j * amp, 7f * j * amp);
                    armR.localRotation = armRRot * Quaternion.Euler(0, 2f * j * amp, 7f * j * amp);
                    thighL.localRotation = thighLRot * Quaternion.Euler(0, 0, 3f * j * amp);
                    thighR.localRotation = thighRRot * Quaternion.Euler(0, 0, kneeLift + 3f * j * amp);
                }
            }
            else
            {
                seizeT = 0f; clonicPhase = 0f;
                head.localRotation = Quaternion.Slerp(head.localRotation, headRot, Time.deltaTime * 6f);
                armL.localRotation = Quaternion.Slerp(armL.localRotation, armLRot, Time.deltaTime * 6f);
                armR.localRotation = Quaternion.Slerp(armR.localRotation, armRRot, Time.deltaTime * 6f);
                thighL.localRotation = Quaternion.Slerp(thighL.localRotation, thighLRot, Time.deltaTime * 6f);
                thighR.localRotation = thighRRot * Quaternion.Euler(0, 0, kneeLift);
            }
            float shinMin = HangAngle(kneeLift) - HangAngle(0f);         // below this the heel would enter the mattress
            shinR.localRotation = Quaternion.Euler(0, 0, Mathf.Max(shinBase, shinMin) + kickAngle);

            // eyes & flushing
            // almond eyes: X = opening along the head length (face up, chin = +X)
            bool closed = Somnolent || Seizing;
            if (eyeLids)
            {
                // lids slide down over the eyeballs when somnolent / seizing
                float k = Mathf.MoveTowards(eyeL.localScale.x / LidScale.x, closed ? 1f : 0f, Time.deltaTime * 4f);
                var sc = new Vector3(LidScale.x * k, LidScale.y, LidScale.z);
                eyeL.localScale = sc; eyeR.localScale = sc;
                eyeL.gameObject.SetActive(k > 0.02f); eyeR.gameObject.SetActive(k > 0.02f);
            }
            else if (lidsInModel)
            {
                if (lidsModel.activeSelf != closed) lidsModel.SetActive(closed);
            }
            else
            {
                float eyeX = closed ? EyeClosed : EyeOpen;
                eyeL.localScale = new Vector3(eyeX, EyeDepth, EyeWidth);
                eyeR.localScale = new Vector3(eyeX, EyeDepth, EyeWidth);
            }
            if (headMat != null)
            {
                // textured skin (white base colour): multiply towards a warm red; flat-colour fallback: lerp to red
                bool textured = skin.r > 0.97f && skin.g > 0.97f && skin.b > 0.97f;
                var tint = textured ? Color.Lerp(Color.white, new Color(1f, 0.80f, 0.76f), Mathf.Clamp01(Flush))
                                    : Color.Lerp(skin, new Color(0.92f, 0.45f, 0.42f), Mathf.Clamp01(Flush) * 0.7f);
                Prim.SetColor(headMat, tint);
                foreach (var m in flushExtra) if (m) Prim.SetColor(m, tint);
            }

            // alarm lamp
            var lampColor = alarm && Mathf.Repeat(Time.time, 0.8f) < 0.4f ? new Color(1f, 0.15f, 0.15f) : new Color(0.3f, 0.1f, 0.1f);
            Prim.SetColor(alarmLamp.material, lampColor);
        }
    }
}
#endif
