// Assets/Scripts/Procgen/InstancedTreeField.cs
using UnityEngine;
using System.Collections.Generic;

[DisallowMultipleComponent]
public class InstancedTreeField : MonoBehaviour
{
    [Header("Area (X-Z)")]
    public Vector2 areaSize = new Vector2(160, 160);

    [Header("Counts & LOD")]
    [Tooltip("Trees with real GOs + MeshColliders within this radius (meters).")]
    public float colliderRadius = 28f;
    [Range(1, 8000)] public int totalTrees = 600;
    public float minSpacing = 3.2f;
    public int seed = 1337;

    [Header("Placement (Terrain Masks)")]
    public ProceduralTerrain terrainSource;         // Drag your ProcTerrain here
    [Range(0f, 1f)] public float maxSlope01 = 0.6f; // reject if slope > this
    [Range(0f, 1f)] public float moistureBias = 0.65f; // 0=no bias, 1=spawn prob = moisture
    public bool snapToGround = true;
    public LayerMask groundMask = ~0;
    public float rayStartHeight = 50f;
    public float rayMaxDistance = 200f;

    [Header("Variants (mesh reuse for batching)")]
    [Range(1, 16)] public int variants = 6;

    [Header("Trunk")]
    [Range(6, 64)] public int trunkSegments = 20;
    [Range(6, 24)] public int trunkSides = 10;
    public float trunkHeight = 7.5f;
    public float trunkBaseRadius = 0.32f;
    public float trunkTopRadius = 0.12f;
    public float trunkBend = 0.55f;

    [Header("Branches")]
    [Range(0, 128)] public int branchCount = 28;
    [Range(3, 24)] public int branchSegments = 8;
    [Range(6, 24)] public int branchSides = 8;
    public Vector2 branchLengthRange = new Vector2(1.6f, 3.2f);
    public float branchRadiusScale = 0.35f;
    public float branchCurve = 0.6f;
    [Range(0f, 1f)] public float branchStart = 0.2f;
    [Range(0f, 1f)] public float branchEnd = 0.9f;

    [Header("Leaves (submesh 1)")]
    public int leavesPerBranch = 2;
    public float leafBlobRadius = 0.55f;
    [Range(1, 5)] public int leafBlobSubdiv = 2;
    public Vector2 leafBlobScaleJitter = new Vector2(0.85f, 1.35f);

    [Header("Per-tree Randomization")]
    public Vector2 uniformScaleRange = new Vector2(0.9f, 1.35f);
    public Vector2 yRotationRange = new Vector2(0f, 360f);

    [Header("Rendering")]
    public Material woodMaterial;  // submesh 0
    public Material leafMaterial;  // submesh 1 (LeafWindToon)

    // ---- internals ----
    readonly List<Mesh> _variantMeshes = new();
    readonly Dictionary<Mesh, List<Matrix4x4>> _instanced = new();
    readonly Dictionary<Mesh, List<Matrix4x4[]>> _batches = new();
    const int kMaxPerBatch = 1023;
    System.Random _rng;

    void Start() { if (!terrainSource) terrainSource = Object.FindFirstObjectByType<ProceduralTerrain>(); Rebuild(); }
    void OnDisable() { ClearChildren(); }
    void OnValidate() { colliderRadius = Mathf.Max(0f, colliderRadius); }

    [ContextMenu("Rebuild Now")]
    public void Rebuild()
    {
        ClearChildren();
        BuildVariants();
        ScatterAndBuild();
        PrepareBatches();
    }

    void ClearChildren()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var c = transform.GetChild(i);
#if UNITY_EDITOR
            if (!Application.isPlaying) DestroyImmediate(c.gameObject);
            else Destroy(c.gameObject);
#else
            Destroy(c.gameObject);
#endif
        }
        foreach (var m in _variantMeshes) if (m) DestroyImmediate(m);
        _variantMeshes.Clear();
        _instanced.Clear();
        _batches.Clear();
    }

    void BuildVariants()
    {
        _rng = new System.Random(seed);
        for (int v = 0; v < variants; v++)
        {
            var mesh = GenerateTreeMesh(
                trunkSegments, trunkSides, trunkHeight, trunkBaseRadius, trunkTopRadius, trunkBend,
                branchCount, branchSegments, branchSides, branchLengthRange, branchRadiusScale, branchCurve, branchStart, branchEnd,
                leavesPerBranch, leafBlobRadius, leafBlobSubdiv, leafBlobScaleJitter,
                _rng.Next());
            mesh.name = $"TreeVariant_{v}";
            _variantMeshes.Add(mesh);
        }
    }

    void ScatterAndBuild()
    {
        if (!woodMaterial) woodMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        if (!leafMaterial) leafMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));

        List<Vector2> accepted = new();
        int attempts = 0, maxAttempts = totalTrees * 40;

        while (accepted.Count < totalTrees && attempts < maxAttempts)
        {
            attempts++;

            // proposal
            Vector2 p;
            p.x = (float)(_rng.NextDouble() - 0.5) * areaSize.x;
            p.y = (float)(_rng.NextDouble() - 0.5) * areaSize.y;

            // spacing
            bool ok = true;
            for (int i = 0; i < accepted.Count; i++)
                if ((accepted[i] - p).sqrMagnitude < minSpacing * minSpacing) { ok = false; break; }
            if (!ok) continue;

            // world pos (and ground snap / hit normal)
            Vector3 pos = new Vector3(p.x, 0f, p.y);
            Vector3 hitNormal = Vector3.up;
            if (snapToGround)
            {
                var ray = new Ray(new Vector3(p.x, rayStartHeight, p.y), Vector3.down);
                if (Physics.Raycast(ray, out var hit, rayMaxDistance, groundMask, QueryTriggerInteraction.Ignore))
                {
                    pos.y = hit.point.y;
                    hitNormal = hit.normal;
                }
            }

            // --- mask checks ---
            float slope01 = 0f, moisture01 = 1f;
            bool hasMasks = terrainSource && terrainSource.TrySampleMasks(pos, out slope01, out moisture01);
            if (!hasMasks)
            {
                // fallback: estimate slope from hit normal
                float upDot = Mathf.Clamp01(Vector3.Dot(hitNormal, Vector3.up));
                slope01 = 1f - upDot; // 0=flat, 1=vertical
                moisture01 = 1f;      // neutral
            }

            if (slope01 > maxSlope01) continue; // too steep

            // bias density by moisture (moist sites more likely)
            float spawnProb = Mathf.Lerp(1f, moisture01, moistureBias);
            if (_rng.NextDouble() > spawnProb) continue;

            // accept
            accepted.Add(p);

            float s = Mathf.Lerp(uniformScaleRange.x, uniformScaleRange.y, (float)_rng.NextDouble());
            float yRot = Mathf.Lerp(yRotationRange.x, yRotationRange.y, (float)_rng.NextDouble());
            var rot = Quaternion.Euler(0f, yRot, 0f);
            var mat = Matrix4x4.TRS(pos, rot, new Vector3(s, s, s));

            var mesh = _variantMeshes[_rng.Next(_variantMeshes.Count)];

            if ((pos - transform.position).magnitude <= colliderRadius)
            {
                var go = new GameObject("Tree (Collider LOD)");
                go.transform.SetPositionAndRotation(pos, rot);
                go.transform.localScale = new Vector3(s, s, s);
                go.transform.parent = transform;

                var mf = go.AddComponent<MeshFilter>(); mf.sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterials = new[] { woodMaterial, leafMaterial };
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                var mc = go.AddComponent<MeshCollider>(); mc.sharedMesh = mesh;
                go.isStatic = true;
            }
            else
            {
                if (!_instanced.TryGetValue(mesh, out var list))
                    _instanced[mesh] = list = new List<Matrix4x4>();
                list.Add(mat);
            }
        }
    }

    void PrepareBatches()
    {
        _batches.Clear();
        foreach (var kv in _instanced)
        {
            var mats = kv.Value;
            var batches = new List<Matrix4x4[]>();
            int offset = 0;
            while (offset < mats.Count)
            {
                int count = Mathf.Min(kMaxPerBatch, mats.Count - offset);
                var arr = new Matrix4x4[count];
                mats.CopyTo(offset, arr, 0, count);
                batches.Add(arr);
                offset += count;
            }
            _batches[kv.Key] = batches;
        }
    }

    void LateUpdate()
    {
        if (_batches.Count == 0 || !woodMaterial || !leafMaterial) return;
        foreach (var kv in _batches)
        {
            var mesh = kv.Key;
            foreach (var arr in kv.Value)
            {
                Graphics.DrawMeshInstanced(mesh, 0, woodMaterial, arr, arr.Length, null,
                    UnityEngine.Rendering.ShadowCastingMode.On, true, gameObject.layer);
                Graphics.DrawMeshInstanced(mesh, 1, leafMaterial, arr, arr.Length, null,
                    UnityEngine.Rendering.ShadowCastingMode.On, true, gameObject.layer);
            }
        }
    }

    // ===== mesh generation (same as before) =====
    Mesh GenerateTreeMesh(
        int trunkSegs, int trunkSides, float trunkH, float trunkR0, float trunkR1, float bend,
        int brCount, int brSegs, int brSides, Vector2 brLenRange, float brRScale, float brCurve, float brStart, float brEnd,
        int leavesPerBr, float leafR, int leafSubdiv, Vector2 leafScaleJitter, int localSeed)
    {
        var rng = new System.Random(localSeed);

        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var uvs = new List<Vector2>();
        var trisWood = new List<int>();
        var trisLeaves = new List<int>();

        var trunkPath = new List<Vector3>(trunkSegs + 1);
        for (int i = 0; i <= trunkSegs; i++)
        {
            float t = (float)i / trunkSegs;
            float y = t * trunkH;
            float ang = (float)(i * 0.7f + rng.NextDouble() * 0.5);
            float r = bend * Mathf.Sin(t * Mathf.PI * 0.75f);
            Vector2 off = new Vector2(Mathf.Sin(ang), Mathf.Cos(ang)) * r;
            trunkPath.Add(new Vector3(off.x, y, off.y));
        }

        var trunkR = new List<float>(trunkSegs + 1);
        for (int i = 0; i <= trunkSegs; i++)
        {
            float t = (float)i / trunkSegs;
            trunkR.Add(Mathf.Lerp(trunkR0, trunkR1, t));
        }
        AddTube(trunkPath, trunkR, trunkSides, ref verts, ref norms, ref uvs, ref trisWood);

        int iStart = Mathf.CeilToInt(Mathf.Clamp01(brStart) * trunkSegs);
        int iEnd = Mathf.FloorToInt(Mathf.Clamp01(brEnd) * trunkSegs);
        iStart = Mathf.Clamp(iStart, 1, trunkSegs - 2);
        iEnd = Mathf.Clamp(iEnd, iStart + 1, trunkSegs - 1);

        var anchors = new List<int>();
        for (int b = 0; b < brCount; b++) anchors.Add(rng.Next(iStart, iEnd + 1));

        foreach (int idx in anchors)
        {
            Vector3 basePos = trunkPath[idx];
            Vector3 nextPos = trunkPath[Mathf.Min(idx + 1, trunkPath.Count - 1)];
            Vector3 prevPos = trunkPath[Mathf.Max(idx - 1, 0)];
            Vector3 trunkTan = (nextPos - prevPos).normalized;

            float az = (float)(rng.NextDouble() * Mathf.PI * 2f);
            OrthonormalBasis(trunkTan, out Vector3 right, out Vector3 binorm);
            Vector3 outward = (Mathf.Cos(az) * right + Mathf.Sin(az) * binorm).normalized;

            float len = Mathf.Lerp(brLenRange.x, brLenRange.y, (float)rng.NextDouble());
            float baseR = Mathf.Lerp(trunkR0, trunkR1, (float)idx / trunkSegs) * brRScale;
            int segs = brSegs;

            var path = new List<Vector3>(segs + 1);
            for (int i = 0; i <= segs; i++)
            {
                float t = (float)i / segs;
                Vector3 p = basePos + outward * (len * t) + Vector3.up * (brCurve * len * t * t);
                path.Add(p);
            }
            var radii = new List<float>(segs + 1);
            for (int i = 0; i <= segs; i++)
            {
                float t = (float)i / segs;
                radii.Add(Mathf.Lerp(baseR, baseR * 0.2f, t));
            }
            AddTube(path, radii, brSides, ref verts, ref norms, ref uvs, ref trisWood);

            Vector3 tip = path[path.Count - 1];
            for (int k = 0; k < Mathf.Max(1, leavesPerBr); k++)
            {
                float s = Mathf.Lerp(leafScaleJitter.x, leafScaleJitter.y, (float)rng.NextDouble());
                Vector3 jitter = outward * (0.15f * k) + RandomInUnitSphere(rng) * 0.22f;
                AddLeafBlob(tip + jitter, leafR * s, leafSubdiv, ref verts, ref norms, ref uvs, ref trisLeaves);
            }
        }

        var mesh = new Mesh();
        mesh.subMeshCount = 2;
        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(trisWood, 0, true);
        mesh.SetTriangles(trisLeaves, 1, true);
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        return mesh;
    }

    // ---------- geometry helpers ----------
    static void OrthonormalBasis(Vector3 t, out Vector3 right, out Vector3 binorm)
    {
        t = t.normalized;
        Vector3 up = Mathf.Abs(t.y) < 0.99f ? Vector3.up : Vector3.right;
        right = Vector3.Normalize(Vector3.Cross(up, t));
        binorm = Vector3.Normalize(Vector3.Cross(t, right));
    }

    static Vector3 RandomInUnitSphere(System.Random rng)
    {
        float u = (float)(rng.NextDouble() * 2.0 - 1.0);
        float theta = (float)(rng.NextDouble() * Mathf.PI * 2.0);
        float r = Mathf.Sqrt(1 - u * u);
        return new Vector3(r * Mathf.Cos(theta), u, r * Mathf.Sin(theta)) * 0.5f;
    }

    static void AddTube(List<Vector3> path, List<float> radii, int sides,
                        ref List<Vector3> verts, ref List<Vector3> norms, ref List<Vector2> uvs, ref List<int> tris)
    {
        if (path.Count != radii.Count) { Debug.LogError("Path/radii mismatch"); return; }
        int ringStart = verts.Count;
        Vector3 prevR = Vector3.right, prevB = Vector3.forward;

        for (int i = 0; i < path.Count; i++)
        {
            Vector3 p = path[i];
            Vector3 t;
            if (i == 0) t = (path[i + 1] - path[i]).normalized;
            else if (i == path.Count - 1) t = (path[i] - path[i - 1]).normalized;
            else t = (path[i + 1] - path[i - 1]).normalized;

            OrthonormalBasis(t, out Vector3 R, out Vector3 B);
            if (i > 0) { R = Vector3.Slerp(prevR, R, 0.5f).normalized; B = Vector3.Slerp(prevB, B, 0.5f).normalized; }
            prevR = R; prevB = B;

            float r = Mathf.Max(0.001f, radii[i]);
            for (int s = 0; s < sides; s++)
            {
                float ang = (s / (float)sides) * Mathf.PI * 2f;
                Vector3 dir = Mathf.Cos(ang) * R + Mathf.Sin(ang) * B;
                Vector3 v = p + dir * r;
                verts.Add(v);
                norms.Add(dir);
                uvs.Add(new Vector2(s / (float)sides, i / (float)(path.Count - 1)));
            }
        }

        int ringCount = path.Count;
        for (int i = 0; i < ringCount - 1; i++)
        {
            int i0 = ringStart + i * sides;
            int i1 = ringStart + (i + 1) * sides;
            for (int s = 0; s < sides; s++)
            {
                int a = i0 + s;
                int b = i0 + (s + 1) % sides;
                int c = i1 + s;
                int d = i1 + (s + 1) % sides;
                tris.Add(a); tris.Add(c); tris.Add(b);
                tris.Add(b); tris.Add(c); tris.Add(d);
            }
        }
    }

    static void AddLeafBlob(Vector3 center, float radius, int subdiv,
                            ref List<Vector3> verts, ref List<Vector3> norms, ref List<Vector2> uvs, ref List<int> trisLeaves)
    {
        var lverts = new List<Vector3>();
        var ltris = new List<int>();
        BuildCubeSphere(subdiv, radius, lverts, ltris);

        int baseIndex = verts.Count;
        for (int i = 0; i < lverts.Count; i++)
        {
            var v = center + lverts[i];
            verts.Add(v);
            norms.Add((v - center).normalized);
            uvs.Add(Vector2.zero);
        }
        for (int i = 0; i < ltris.Count; i++) trisLeaves.Add(baseIndex + ltris[i]);
    }

    static void BuildCubeSphere(int sub, float radius, List<Vector3> verts, List<int> tris)
    {
        Vector3[] faceNormals = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
        Vector3[] faceTangents = { Vector3.forward, Vector3.forward, Vector3.right, Vector3.right, Vector3.right, Vector3.right };

        for (int f = 0; f < 6; f++)
        {
            Vector3 n = faceNormals[f], t = faceTangents[f], b = Vector3.Cross(n, t);
            int startIndex = verts.Count;

            for (int y = 0; y <= sub; y++)
            for (int x = 0; x <= sub; x++)
            {
                float u = (float)x / sub, v = (float)y / sub;
                Vector2 uv = new Vector2(u * 2f - 1f, v * 2f - 1f);
                Vector3 p = (n + uv.x * t + uv.y * b).normalized * radius;
                verts.Add(p);
            }

            for (int y = 0; y < sub; y++)
            for (int x = 0; x < sub; x++)
            {
                int i0 = startIndex + x + y * (sub + 1);
                int i1 = i0 + 1;
                int i2 = i0 + (sub + 1);
                int i3 = i2 + 1;
                tris.Add(i0); tris.Add(i2); tris.Add(i1);
                tris.Add(i1); tris.Add(i2); tris.Add(i3);
            }
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.1f, 0.8f, 0.2f, 0.15f);
        Gizmos.DrawWireSphere(transform.position, colliderRadius);
        Gizmos.DrawCube(transform.position, new Vector3(areaSize.x, 0.1f, areaSize.y));
    }
}
