using UnityEngine;
using System.Collections.Generic;

[ExecuteAlways]
[DisallowMultipleComponent]
public class InstancedTreeField : ProcgenSpawnerBase
{
    [Header("Area (X-Z)")]
    public Vector2 areaSize = new Vector2(160, 160);

    [Header("Counts & LOD")]
    public float colliderRadius = 28f;
    [Range(1, 8000)] public int totalTrees = 600;
    public float minSpacing = 3.2f;
    public int seed = 1337;

    [Header("Placement (Terrain Masks)")]
    public ProceduralTerrain terrainSource;
    [Range(0f, 1f)] public float maxSlope01 = 0.6f;
    [Range(0f, 1f)] public float moistureBias = 0.65f;
    public bool snapToGround = true;
    public LayerMask groundMask = ~0;
    public float rayStartHeight = 50f;
    public float rayMaxDistance = 200f;

    [Header("Variants")]
    [Range(1, 16)] public int variants = 6;

    // ProceduralTree-style controls
    [Header("Trunk")]
    [Range(2, 64)] public int trunkSegments = 18;
    [Range(6, 24)] public int trunkSides = 10;
    public float trunkHeight = 6f;
    public float trunkBaseRadius = 0.35f;
    public float trunkTopRadius = 0.12f;
    public float trunkBend = 0.6f;

    [Header("Branches")]
    [Range(0, 128)] public int branchCount = 24;
    [Range(3, 24)] public int branchSegments = 8;
    [Range(6, 24)] public int branchSides = 8;
    public Vector2 branchLengthRange = new Vector2(1.5f, 3.0f);
    public float branchRadiusScale = 0.35f;
    public float branchCurve = 0.6f;
    public float branchStart = 0.2f;
    public float branchEnd = 0.9f;

    [Header("Leaves")]
    public int leavesPerBranch = 2;
    public float leafBlobRadius = 0.5f;
    [Range(1, 5)] public int leafBlobSubdiv = 2;
    public Vector2 leafBlobScaleJitter = new Vector2(0.8f, 1.4f);

    [Header("Per-tree Randomization")]
    public Vector2 uniformScaleRange = new Vector2(0.9f, 1.35f);
    public Vector2 yRotationRange   = new Vector2(0f, 360f);

    [Header("Rendering")]
    public Material woodMaterial;
    public Material leafMaterial;

    [Header("Batching")]
    public bool chunkedBatches = true;


    // internals
    readonly List<Mesh> _variantMeshes = new();
    readonly Dictionary<Mesh, List<Matrix4x4>> _instanced = new();
    readonly List<ProcgenCullingHub.Batch> _exposedBatches = new();
    const int kMaxPerBatch = 1023;
    System.Random _rng;

    void Start()
    {
        if (!terrainSource) terrainSource = Object.FindFirstObjectByType<ProceduralTerrain>();
        EnsureDefaults();
        Rebuild();
    }

    void EnsureDefaults()
    {
        if (!woodMaterial) woodMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "M_Wood (runtime)" };
        if (!leafMaterial) leafMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "M_Leaves (runtime)" };
        woodMaterial.enableInstancing = true; leafMaterial.enableInstancing = true;
    }

    public override void Configure(WorldContext ctx)
    {
        base.Configure(ctx);
        if (!terrainSource && ctx != null) terrainSource = ctx.Root.GetComponentInChildren<ProceduralTerrain>();
    }

    [ContextMenu("Rebuild Now")]
    public override void Rebuild()
    {
        EnsureDefaults();
        ClearAll();

        // Apply biome if present
        if (Ctx != null && Ctx.Biome != null)
        {
            maxSlope01   = Ctx.Biome.treeMaxSlope01;
            moistureBias = Ctx.Biome.treeMoistureBias;
        }

        BuildVariants();
        ScatterAndBuild();
        PrepareBatches();
        NotifyHub();
    }

    void ClearAll()
    {
        DestroyAllChildren();
#if UNITY_EDITOR
        foreach (var m in _variantMeshes) if (m) DestroyImmediate(m);
#else
        foreach (var m in _variantMeshes) if (m) Destroy(m);
#endif
        _variantMeshes.Clear();
        _instanced.Clear();
        _exposedBatches.Clear();
    }

    void BuildVariants()
    {
        _rng = new System.Random(seed);
        for (int v = 0; v < variants; v++)
        {
            var mesh = GenerateTreeMesh(_rng.Next());
            mesh.name = $"TreeVariant_{v}";
            _variantMeshes.Add(mesh);
        }
    }

    void ScatterAndBuild()
    {
        List<Vector2> accepted = new();
        int attempts = 0, maxAttempts = totalTrees * 40;

        while (accepted.Count < totalTrees && attempts < maxAttempts)
        {
            attempts++;

            Vector2 pLocal;
            pLocal.x = (float)(_rng.NextDouble() - 0.5f) * areaSize.x;
            pLocal.y = (float)(_rng.NextDouble() - 0.5f) * areaSize.y;

            bool ok = true;
            for (int i = 0; i < accepted.Count; i++)
                if ((accepted[i] - pLocal).sqrMagnitude < minSpacing * minSpacing) { ok = false; break; }
            if (!ok) continue;

            Vector3 pos = transform.position + new Vector3(pLocal.x, 0f, pLocal.y);
            Vector3 hitNormal = Vector3.up;

            if (snapToGround)
            {
                var ray = new Ray(pos + Vector3.up * rayStartHeight, Vector3.down);
                if (Physics.Raycast(ray, out var hit, rayMaxDistance, groundMask, QueryTriggerInteraction.Ignore))
                {
                    pos = hit.point - hit.normal * 0.015f; // tiny push-down to avoid hovering
                    hitNormal = hit.normal;
                }
            }

            float slope01 = 0f, moisture01 = 1f;
            bool hasMasks = terrainSource && terrainSource.TrySampleMasks(pos, out slope01, out moisture01);
            if (!hasMasks)
            {
                float upDot = Mathf.Clamp01(Vector3.Dot(hitNormal, Vector3.up));
                slope01 = 1f - upDot; moisture01 = 1f;
            }
            if (slope01 > maxSlope01) continue;

            float spawnProb = Mathf.Lerp(1f, moisture01, moistureBias);
            if (_rng.NextDouble() > spawnProb) continue;

            accepted.Add(pLocal);

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
                if (!_instanced.TryGetValue(mesh, out var list)) _instanced[mesh] = list = new List<Matrix4x4>();
                list.Add(mat);
            }
        }
    }

    void PrepareBatches()
    {
        _exposedBatches.Clear();

        // Group per mesh → per chunk → split into ≤1023
        bool useChunks = chunkedBatches && Ctx != null && Ctx.Grid != null;

        foreach (var kv in _instanced) // kv.Key = Mesh, kv.Value = List<Matrix4x4>
        {
            var mats = kv.Value;
            if (!useChunks)
            {
                // old path: single big bounds from positions
                int offset = 0;
                while (offset < mats.Count)
                {
                    int count = Mathf.Min(kMaxPerBatch, mats.Count - offset);
                    var arr = new Matrix4x4[count];
                    mats.CopyTo(offset, arr, 0, count);
                    offset += count;

                    Vector3 min = new(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
                    Vector3 max = new(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
                    for (int i = 0; i < arr.Length; i++)
                    {
                        var p = arr[i].GetColumn(3);
                        if (p.x < min.x) min.x = p.x; if (p.y < min.y) min.y = p.y; if (p.z < min.z) min.z = p.z;
                        if (p.x > max.x) max.x = p.x; if (p.y > max.y) max.y = p.y; if (p.z > max.z) max.z = p.z;
                    }
                    var b = new Bounds(); b.SetMinMax(min, max); b.Expand(1.0f);

                    _exposedBatches.Add(new ProcgenCullingHub.Batch { mesh = kv.Key, submeshIndex = 0, material = woodMaterial, matrices = arr, bounds = b, layer = gameObject.layer, shadowCasting = UnityEngine.Rendering.ShadowCastingMode.On, receiveShadows = true });
                    _exposedBatches.Add(new ProcgenCullingHub.Batch { mesh = kv.Key, submeshIndex = 1, material = leafMaterial, matrices = arr, bounds = b, layer = gameObject.layer, shadowCasting = UnityEngine.Rendering.ShadowCastingMode.On, receiveShadows = true });
                }
                continue;
            }

            // chunked path
            var grid = Ctx.Grid;
            var byChunk = new Dictionary<ChunkId, List<Matrix4x4>>(64);
            for (int i = 0; i < mats.Count; i++)
            {
                Vector3 p = mats[i].GetColumn(3);
                var id = grid.IdAt(p);
                if (!byChunk.TryGetValue(id, out var list)) byChunk[id] = list = new List<Matrix4x4>();
                list.Add(mats[i]);
            }

            foreach (var ck in byChunk)
            {
                var list = ck.Value;
                int offset = 0;
                while (offset < list.Count)
                {
                    int count = Mathf.Min(kMaxPerBatch, list.Count - offset);
                    var arr = new Matrix4x4[count];
                    list.CopyTo(offset, arr, 0, count);
                    offset += count;

                    var b = grid.BoundsOf(ck.Key);
                    b.Expand(1.0f);

                    _exposedBatches.Add(new ProcgenCullingHub.Batch { mesh = kv.Key, submeshIndex = 0, material = woodMaterial, matrices = arr, bounds = b, layer = gameObject.layer, shadowCasting = UnityEngine.Rendering.ShadowCastingMode.On, receiveShadows = true });
                    _exposedBatches.Add(new ProcgenCullingHub.Batch { mesh = kv.Key, submeshIndex = 1, material = leafMaterial, matrices = arr, bounds = b, layer = gameObject.layer, shadowCasting = UnityEngine.Rendering.ShadowCastingMode.On, receiveShadows = true });
                }
            }
        }
    }


    public override List<ProcgenCullingHub.Batch> GetInstancedBatches() => _exposedBatches;

    // ===== mesh gen (trunk, branches, leaves) =====
    Mesh GenerateTreeMesh(int localSeed)
    {
        var rng = new System.Random(localSeed);

        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var uvs   = new List<Vector2>();
        var trisWood   = new List<int>();
        var trisLeaves = new List<int>();

        // trunk path with bend
        var trunkPath = new List<Vector3>(trunkSegments + 1);
        for (int i = 0; i <= trunkSegments; i++)
        {
            float t = (float)i / trunkSegments;
            float y = t * trunkHeight;
            float ang = (float)(i * 0.7f + rng.NextDouble() * 0.5);
            float r = trunkBend * Mathf.Sin(t * Mathf.PI * 0.75f);
            Vector2 off = new Vector2(Mathf.Sin(ang), Mathf.Cos(ang)) * r;
            trunkPath.Add(new Vector3(off.x, y, off.y));
        }

        var trunkRadii = new List<float>(trunkSegments + 1);
        for (int i = 0; i <= trunkSegments; i++)
        {
            float t = (float)i / trunkSegments;
            trunkRadii.Add(Mathf.Lerp(trunkBaseRadius, trunkTopRadius, t));
        }
        AddTube(trunkPath, trunkRadii, trunkSides, ref verts, ref norms, ref uvs, ref trisWood);

        // branch anchors
        int iStart = Mathf.CeilToInt(Mathf.Clamp01(branchStart) * trunkSegments);
        int iEnd   = Mathf.FloorToInt(Mathf.Clamp01(branchEnd) * trunkSegments);
        iStart = Mathf.Clamp(iStart, 1, trunkSegments - 2);
        iEnd   = Mathf.Clamp(iEnd, iStart + 1, trunkSegments - 1);

        for (int b = 0; b < branchCount; b++)
        {
            int idx = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(iStart, iEnd, (float)rng.NextDouble())), iStart, iEnd);

            Vector3 basePos = trunkPath[idx];
            Vector3 nextPos = trunkPath[Mathf.Min(idx + 1, trunkPath.Count - 1)];
            Vector3 prevPos = trunkPath[Mathf.Max(idx - 1, 0)];
            Vector3 trunkTangent = (nextPos - prevPos).normalized;

            OrthonormalBasis(trunkTangent, out var right, out var binorm);
            float az = (float)(rng.NextDouble() * Mathf.PI * 2f);
            Vector3 outward = (Mathf.Cos(az) * right + Mathf.Sin(az) * binorm).normalized;

            float len = Mathf.Lerp(branchLengthRange.x, branchLengthRange.y, (float)rng.NextDouble());
            float baseRadius = Mathf.Lerp(trunkBaseRadius, trunkTopRadius, (float)idx / trunkSegments) * branchRadiusScale;
            int segs = branchSegments;

            var path = new List<Vector3>(segs + 1);
            for (int i = 0; i <= segs; i++)
            {
                float t = (float)i / segs;
                Vector3 p = basePos
                          + outward * (len * t)
                          + Vector3.up * (branchCurve * len * t * t);
                path.Add(p);
            }

            var radii = new List<float>(segs + 1);
            for (int i = 0; i <= segs; i++)
            {
                float t = (float)i / segs;
                radii.Add(Mathf.Lerp(baseRadius, baseRadius * 0.2f, t));
            }

            AddTube(path, radii, branchSides, ref verts, ref norms, ref uvs, ref trisWood);

            Vector3 tip = path[path.Count - 1];
            for (int k = 0; k < Mathf.Max(1, leavesPerBranch); k++)
            {
                float s = Mathf.Lerp(leafBlobScaleJitter.x, leafBlobScaleJitter.y, (float)rng.NextDouble());
                Vector3 jitter = outward * (0.15f * k) + RandomInUnitSphere() * 0.2f;
                AddLeafBlob(tip + jitter, leafBlobRadius * s, leafBlobSubdiv, ref verts, ref norms, ref uvs, ref trisLeaves);
            }
        }

        var mesh = new Mesh { name = "TreeMesh" };
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

    // ===== helpers =====
    static void OrthonormalBasis(Vector3 t, out Vector3 right, out Vector3 binorm)
    {
        t = t.normalized;
        Vector3 up = Mathf.Abs(t.y) < 0.99f ? Vector3.up : Vector3.right;
        right = Vector3.Normalize(Vector3.Cross(up, t));
        binorm = Vector3.Normalize(Vector3.Cross(t, right));
    }

    static Vector3 RandomInUnitSphere()
    {
        float u = Random.value * 2f - 1f;
        float theta = Random.value * Mathf.PI * 2f;
        float r = Mathf.Sqrt(1 - u * u);
        return new Vector3(r * Mathf.Cos(theta), u, r * Mathf.Sin(theta)) * 0.5f;
    }

    static void AddTube(List<Vector3> path, List<float> radii, int sides,
                        ref List<Vector3> verts, ref List<Vector3> norms, ref List<Vector2> uvs, ref List<int> tris)
    {
        if (path.Count != radii.Count) { Debug.LogError("Path/radii mismatch"); return; }
        int ringStart = verts.Count;

        Vector3 prevRight = Vector3.right, prevBinorm = Vector3.forward;

        for (int i = 0; i < path.Count; i++)
        {
            Vector3 p = path[i];
            Vector3 t = (i == 0) ? (path[i + 1] - path[i]).normalized
                        : (i == path.Count - 1) ? (path[i] - path[i - 1]).normalized
                        : (path[i + 1] - path[i - 1]).normalized;

            OrthonormalBasis(t, out var right, out var binorm);

            if (i > 0)
            {
                right  = Vector3.Slerp(prevRight,  right,  0.5f).normalized;
                binorm = Vector3.Slerp(prevBinorm, binorm, 0.5f).normalized;
            }
            prevRight = right; prevBinorm = binorm;

            float r = Mathf.Max(0.001f, radii[i]);
            for (int s = 0; s < sides; s++)
            {
                float ang = (s / (float)sides) * Mathf.PI * 2f;
                Vector3 dir = Mathf.Cos(ang) * right + Mathf.Sin(ang) * binorm;
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
        var localVerts = new List<Vector3>();
        var localTris  = new List<int>();
        BuildCubeSphere(subdiv, radius, localVerts, localTris);

        int baseIndex = verts.Count;
        for (int i = 0; i < localVerts.Count; i++)
        {
            var v = center + localVerts[i];
            verts.Add(v);
            norms.Add((v - center).normalized);
            uvs.Add(Vector2.zero);
        }
        for (int i = 0; i < localTris.Count; i++) trisLeaves.Add(baseIndex + localTris[i]);
    }

    static void BuildCubeSphere(int sub, float radius, List<Vector3> verts, List<int> tris)
    {
        Vector3[] faceNormals  = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
        Vector3[] faceTangents = { Vector3.forward, Vector3.forward, Vector3.right, Vector3.right, Vector3.right, Vector3.right };

        for (int f = 0; f < 6; f++)
        {
            Vector3 n = faceNormals[f], t = faceTangents[f], b = Vector3.Cross(n, t);
            int start = verts.Count;

            for (int y = 0; y <= sub; y++)
                for (int x = 0; x <= sub; x++)
                {
                    float u = (float)x / sub, v = (float)y / sub;
                    Vector2 uv = new(u * 2f - 1f, v * 2f - 1f);
                    Vector3 p = (n + uv.x * t + uv.y * b).normalized * radius;
                    verts.Add(p);
                }

            for (int y = 0; y < sub; y++)
                for (int x = 0; x < sub; x++)
                {
                    int i0 = start + x + y * (sub + 1);
                    int i1 = i0 + 1;
                    int i2 = i0 + (sub + 1);
                    int i3 = i2 + 1;
                    tris.Add(i0); tris.Add(i2); tris.Add(i1);
                    tris.Add(i1); tris.Add(i2); tris.Add(i3);
                }
        }
    }
}


