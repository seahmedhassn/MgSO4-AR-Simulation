#if MGSO4_READY
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace MgSO4AR
{
    /// <summary>
    /// Loads the ward models made in Blender (Resources/Models/*.bytes). Two formats:
    /// "MGM1": per material a submesh with colour, smoothness, positions + normals (Unity local coords) and indices.
    /// "MGM2": as MGM1 plus a texture name per submesh (Resources/Models/Tex/&lt;name&gt;) and UVs per vertex.
    ///   Submeshes whose name starts with "cutout_" (hair cards, lashes, brows) are rendered alpha-tested and
    ///   double-sided (their triangles are duplicated with flipped winding/normals at load time).
    /// Returns null when a model is missing so the caller can fall back to simple primitives.
    /// </summary>
    public static class ModelLib
    {
        public const string CutoutPrefix = "cutout_";

        class Data { public Mesh mesh; public Color[] colors; public float[] smooth; public string[] names; public string[] tex; }
        static readonly Dictionary<string, Data> cache = new Dictionary<string, Data>();

        public static bool Has(string name) => Load(name) != null;

        /// <summary>Index of the first submesh whose (lower-case) name contains key, or -1.</summary>
        public static int FindSubmesh(string model, string key)
        {
            var d = Load(model);
            if (d == null) return -1;
            for (int i = 0; i < d.names.Length; i++)
                if (d.names[i] != null && d.names[i].ToLowerInvariant().Contains(key)) return i;
            return -1;
        }

        static Data Load(string name)
        {
            if (cache.TryGetValue(name, out var d)) return d;
            d = null;
            var ta = Resources.Load<TextAsset>("Models/" + name);
            if (ta != null)
            {
                try { d = Parse(ta.bytes, name); }
                catch (System.Exception e) { Debug.LogWarning("[MgSO4] model " + name + ": " + e.Message); d = null; }
            }
            cache[name] = d;
            return d;
        }

        static string ReadStr(BinaryReader br)
        {
            int n = br.ReadInt32();
            if (n < 0 || n > 4096) throw new IOException("bad string length " + n);
            return n == 0 ? "" : Encoding.UTF8.GetString(br.ReadBytes(n));
        }

        static Data Parse(byte[] bytes, string name)
        {
            using (var br = new BinaryReader(new MemoryStream(bytes)))
            {
                string magic = Encoding.ASCII.GetString(br.ReadBytes(4));
                bool v2 = magic == "MGM2";
                if (!v2 && magic != "MGM1") throw new IOException("bad header " + magic);
                int subs = br.ReadInt32();
                var verts = new List<Vector3>();
                var norms = new List<Vector3>();
                var uvs = v2 ? new List<Vector2>() : null;
                var tris = new List<int[]>();
                var cols = new Color[subs];
                var sm = new float[subs];
                var names = new string[subs];
                var tex = new string[subs];
                for (int s = 0; s < subs; s++)
                {
                    names[s] = ReadStr(br);
                    cols[s] = new Color(br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                    sm[s] = br.ReadSingle();
                    tex[s] = v2 ? ReadStr(br) : "";
                    int vc = br.ReadInt32();
                    int baseIdx = verts.Count;
                    for (int i = 0; i < vc; i++)
                    {
                        verts.Add(new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle()));
                        norms.Add(new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle()));
                        if (v2) uvs.Add(new Vector2(br.ReadSingle(), br.ReadSingle()));
                    }
                    int ic = br.ReadInt32();
                    var idx = new int[ic];
                    for (int i = 0; i < ic; i++) idx[i] = br.ReadInt32() + baseIdx;

                    if (names[s].StartsWith(CutoutPrefix) && vc > 0)
                    {
                        // double-sided: duplicate vertices with flipped normals + reversed winding
                        int dupBase = verts.Count;
                        for (int i = 0; i < vc; i++)
                        {
                            verts.Add(verts[baseIdx + i]);
                            norms.Add(-norms[baseIdx + i]);
                            if (v2) uvs.Add(uvs[baseIdx + i]);
                        }
                        var both = new int[ic * 2];
                        System.Array.Copy(idx, both, ic);
                        for (int t = 0; t + 2 < ic; t += 3)
                        {
                            both[ic + t] = idx[t] - baseIdx + dupBase;
                            both[ic + t + 1] = idx[t + 2] - baseIdx + dupBase;
                            both[ic + t + 2] = idx[t + 1] - baseIdx + dupBase;
                        }
                        idx = both;
                    }
                    tris.Add(idx);
                }
                var mesh = new Mesh { name = name };
                if (verts.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(verts);
                mesh.SetNormals(norms);
                if (v2) mesh.SetUVs(0, uvs);
                mesh.subMeshCount = subs;
                for (int s = 0; s < subs; s++) mesh.SetTriangles(tris[s], s, false);
                mesh.RecalculateBounds();
                if (v2 && Prim.TextureExists(tex)) { try { mesh.RecalculateTangents(); } catch { } }
                mesh.UploadMeshData(true);
                return new Data { mesh = mesh, colors = cols, smooth = sm, names = names, tex = tex };
            }
        }

        /// <summary>Creates the model as a child of parent (model origin = parent origin + localPos). Null if missing.</summary>
        public static GameObject Spawn(string name, Transform parent, Vector3 localPos = default, Vector3? euler = null)
        {
            var d = Load(name);
            if (d == null) return null;
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(euler ?? Vector3.zero);
            go.AddComponent<MeshFilter>().sharedMesh = d.mesh;
            var r = go.AddComponent<MeshRenderer>();
            var mats = new Material[d.colors.Length];
            for (int i = 0; i < mats.Length; i++)
            {
                var c = d.colors[i];
                var mode = d.names[i].StartsWith(CutoutPrefix) ? Prim.Surface.Cutout
                         : c.a < 0.99f ? Prim.Surface.Transparent : Prim.Surface.Opaque;
                // hair cards: lower alpha threshold so thin strands read as a full ponytail on a white pillow
                float cut = d.names[i].Contains("hair") ? 0.22f : 0.45f;
                mats[i] = Prim.MatTex(c, d.tex[i], mode, d.names[i].Contains("hair") ? 0.05f : d.smooth[i], cut);   // matte hair: no silver sheen
            }
            r.sharedMaterials = mats;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }
    }
}
#endif
