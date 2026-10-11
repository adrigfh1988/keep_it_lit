using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>F109: generated sign meshes (bevelled plates with metric UVs, curled paper, domed screws, a unit quad), saved as assets under Assets/SourceFiles/Meshes/Signs/.</summary>
public static partial class WorldSignBuilder
{
    private const string MeshFolder = "Assets/SourceFiles/Meshes/Signs/";

    private static Mesh SaveMesh(string name, System.Func<Mesh> build)
    {
        string path = MeshFolder + name + ".asset";
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null) return existing;
        Directory.CreateDirectory(MeshFolder);
        Mesh m = build();
        m.name = name;
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    private sealed class MeshBuf
    {
        public readonly List<Vector3> v = new List<Vector3>();
        public readonly List<Vector2> uv = new List<Vector2>();
        public readonly List<int> t0 = new List<int>();
        public readonly List<int> t1 = new List<int>();
        public Vector2 uvOffset;

        // Quad given in any order of a loop (p0..p3 around the rim); flipped so the face points along `outward`.
        public void Quad(List<int> tris, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 outward, Vector3 uvScaleAxisU, Vector3 uvScaleAxisV)
        {
            int b = v.Count;
            v.Add(p0); v.Add(p1); v.Add(p2); v.Add(p3);
            for (int i = 0; i < 4; i++)
            {
                Vector3 p = v[b + i];
                // Metric UV: project onto two axes so a side face is not stretched.
                uv.Add(new Vector2(Vector3.Dot(p, uvScaleAxisU), Vector3.Dot(p, uvScaleAxisV)) * 2f + uvOffset);
            }
            Tri(tris, b, b + 1, b + 2, outward);
            Tri(tris, b, b + 2, b + 3, outward);
        }

        private void Tri(List<int> tris, int a, int bI, int c, Vector3 outward)
        {
            Vector3 n = Vector3.Cross(v[bI] - v[a], v[c] - v[a]);
            if (Vector3.Dot(n, outward) < 0f) { int tmp = bI; bI = c; c = tmp; }
            tris.Add(a); tris.Add(bI); tris.Add(c);
        }

        public Mesh ToMesh(bool twoSubmeshes)
        {
            Mesh m = new Mesh();
            m.SetVertices(v);
            m.SetUVs(0, uv);
            if (twoSubmeshes)
            {
                m.subMeshCount = 2;
                m.SetTriangles(t0, 0);
                m.SetTriangles(t1, 1);
            }
            else
            {
                var all = new List<int>(t0); all.AddRange(t1);
                m.SetTriangles(all, 0);
            }
            m.RecalculateNormals();
            m.RecalculateTangents();
            m.RecalculateBounds();
            return m;
        }
    }

    /// <summary>
    /// A bevelled slab facing -Z (the viewer's side) with its back toward +Z. Submesh 0 = the flat front, submesh 1 = chamfer + sides (a separate "edge" material).
    /// Centred on the origin in XY; the front face is at z = 0, the back at z = depth.
    /// </summary>
    public static Mesh BevelPlate(float w, float h, float depth, float bevel)
    {
        w = Mathf.Round(w * 100f) / 100f; h = Mathf.Round(h * 100f) / 100f; depth = Mathf.Round(depth * 1000f) / 1000f; bevel = Mathf.Round(bevel * 1000f) / 1000f;
        string name = $"Plate_{w:0.00}x{h:0.00}x{depth:0.000}_{bevel:0.000}";
        return SaveMesh(name, () =>
        {
            var b = new MeshBuf { uvOffset = new Vector2(Hash((int)(w * 100), (int)(h * 100), 5), Hash((int)(h * 100), (int)(w * 100), 6)) };
            float hw = w * 0.5f, hh = h * 0.5f, bv = Mathf.Min(bevel, Mathf.Min(hw, hh) * 0.5f), bz = Mathf.Min(bv, depth * 0.5f);
            Vector3 axU = new Vector3(1f, 0f, 0f), axV = new Vector3(0f, 1f, 0f);
            // front face
            b.Quad(b.t0, new Vector3(-hw + bv, -hh + bv, 0), new Vector3(hw - bv, -hh + bv, 0), new Vector3(hw - bv, hh - bv, 0), new Vector3(-hw + bv, hh - bv, 0), Vector3.back, axU, axV);
            // chamfer ring
            Vector3 fbl = new Vector3(-hw + bv, -hh + bv, 0), fbr = new Vector3(hw - bv, -hh + bv, 0), ftr = new Vector3(hw - bv, hh - bv, 0), ftl = new Vector3(-hw + bv, hh - bv, 0);
            Vector3 obl = new Vector3(-hw, -hh, bz), obr = new Vector3(hw, -hh, bz), otr = new Vector3(hw, hh, bz), otl = new Vector3(-hw, hh, bz);
            b.Quad(b.t1, fbl, fbr, obr, obl, new Vector3(0, -1, -1), axU, axV);
            b.Quad(b.t1, fbr, ftr, otr, obr, new Vector3(1, 0, -1), axU, axV);
            b.Quad(b.t1, ftr, ftl, otl, otr, new Vector3(0, 1, -1), axU, axV);
            b.Quad(b.t1, ftl, fbl, obl, otl, new Vector3(-1, 0, -1), axU, axV);
            // sides
            Vector3 bbl = new Vector3(-hw, -hh, depth), bbr = new Vector3(hw, -hh, depth), btr = new Vector3(hw, hh, depth), btl = new Vector3(-hw, hh, depth);
            b.Quad(b.t1, obl, obr, bbr, bbl, Vector3.down, axU, new Vector3(0, 0, 1));
            b.Quad(b.t1, obr, otr, btr, bbr, Vector3.right, new Vector3(0, 0, 1), axV);
            b.Quad(b.t1, otr, otl, btl, btr, Vector3.up, axU, new Vector3(0, 0, 1));
            b.Quad(b.t1, otl, obl, bbl, btl, Vector3.left, new Vector3(0, 0, 1), axV);
            return b.ToMesh(true);
        });
    }

    /// <summary>A unit quad (1 x 1, facing -Z, UV 0..1) for overlays; scale it.</summary>
    public static Mesh UnitQuad()
    {
        return SaveMesh("SignQuad", () =>
        {
            Mesh m = new Mesh();
            m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            m.triangles = new[] { 0, 3, 1, 3, 2, 1 };
            m.RecalculateNormals();
            m.RecalculateTangents();
            m.RecalculateBounds();
            return m;
        });
    }

    /// <summary>A sheet of paper facing -Z whose upper-right corner curls toward the viewer.</summary>
    public static Mesh PaperSheet(float w, float h, bool curlRight)
    {
        w = Mathf.Round(w * 100f) / 100f; h = Mathf.Round(h * 100f) / 100f;
        return SaveMesh($"Paper_{w:0.00}x{h:0.00}_{(curlRight ? "R" : "L")}", () =>
        {
            const int n = 12;
            Mesh m = new Mesh();
            var vs = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            for (int j = 0; j <= n; j++)
                for (int i = 0; i <= n; i++)
                {
                    float fx = i / (float)n, fy = j / (float)n;
                    float cx = curlRight ? fx : 1f - fx;
                    float lift = Mathf.Pow(Mathf.Clamp01((cx + fy - 1.25f) / 0.75f), 2f) * 0.07f;
                    float shrink = lift * 0.7f;
                    vs.Add(new Vector3((fx - 0.5f) * w - (curlRight ? 1 : -1) * shrink * fy, (fy - 0.5f) * h - shrink * cx, -lift));
                    uvs.Add(new Vector2(fx * w, fy * h));
                }
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    int a = j * (n + 1) + i, bI = a + 1, c = a + n + 1, d = c + 1;
                    tris.Add(a); tris.Add(c); tris.Add(bI);
                    tris.Add(c); tris.Add(d); tris.Add(bI);
                }
            m.SetVertices(vs); m.SetUVs(0, uvs); m.SetTriangles(tris, 0);
            m.RecalculateNormals(); m.RecalculateTangents(); m.RecalculateBounds();
            return m;
        });
    }

    /// <summary>A shallow domed screw / nail head facing -Z, radius r, 12 segments.</summary>
    public static Mesh ScrewHead(float r = 0.011f)
    {
        return SaveMesh("ScrewHead_" + r.ToString("0.000"), () =>
        {
            const int seg = 12;
            float hgt = r * 0.55f;
            var vs = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            // rings: centre top, mid dome, rim, back rim
            vs.Add(new Vector3(0, 0, -hgt)); uvs.Add(new Vector2(0.5f, 0.5f));
            float[] ringR = { 0.55f * r, r, r };
            float[] ringZ = { -hgt * 0.85f, -hgt * 0.3f, 0f };
            for (int k = 0; k < 3; k++)
                for (int i = 0; i < seg; i++)
                {
                    float a = i / (float)seg * Mathf.PI * 2f;
                    vs.Add(new Vector3(Mathf.Cos(a) * ringR[k], Mathf.Sin(a) * ringR[k], ringZ[k]));
                    uvs.Add(new Vector2(0.5f + Mathf.Cos(a) * 0.5f * ringR[k] / r, 0.5f + Mathf.Sin(a) * 0.5f * ringR[k] / r));
                }
            for (int i = 0; i < seg; i++)
            {
                int i2 = (i + 1) % seg;
                tris.Add(0); tris.Add(1 + i2); tris.Add(1 + i);
                for (int k = 0; k < 2; k++)
                {
                    int a = 1 + k * seg + i, bI = 1 + k * seg + i2, c = 1 + (k + 1) * seg + i, d = 1 + (k + 1) * seg + i2;
                    tris.Add(a); tris.Add(bI); tris.Add(c);
                    tris.Add(bI); tris.Add(d); tris.Add(c);
                }
            }
            Mesh m = new Mesh();
            m.SetVertices(vs); m.SetUVs(0, uvs); m.SetTriangles(tris, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            // make sure the dome faces -Z
            if (Vector3.Dot(m.normals[0], Vector3.back) < 0f) { m.triangles = FlipTris(m.triangles); m.RecalculateNormals(); }
            return m;
        });
    }

    private static int[] FlipTris(int[] t)
    {
        for (int i = 0; i < t.Length; i += 3) { int tmp = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = tmp; }
        return t;
    }
}
