using UnityEngine;
using System.Collections.Generic;

[ExecuteAlways]
[DisallowMultipleComponent]
public class InstancedRockField : ProcgenSpawnerBase
{
    [Header("Area (X-Z)")]
    public Vector2 areaSize = new Vector2(140, 140);

    [Header("Counts & LOD")]
    public float colliderRadius = 25f;
    [Range(1, 20000)] public int totalRocks = 1200;
    public float minSpacing = 1.8f;
    public int seed = 1234;

    [Header("Terrain masks")]
    public ProceduralTerrain terrainSource;
    [Range(0f, 1f)] public float minSlope01 = 0.25f;
    [Range(0f, 1f)] public float drynessBias = 0.6f;
    public bool snapToGround = true;
    public LayerMask groundMask = ~0;
    public float rayStartHeight = 50f, rayMaxDistance = 200f;

    [Header("Rock Variants")]
    [Range(1, 16)] public int variants = 6;

    [Header("Rock Shape")]
    [Range(0.4f, 3f)] public float radius = 1.2f;
    [Range(4, 16)] public int subdivisions = 10;
    public bool flatShaded = true;
    public bool flattenBottom = true;
    public float flattenHeight = 0f;
    public float bottomSmoothing = 0.2f;

    [Header("Noise")]
    public float noiseScale = 1.5f;
    public float noiseAmplitude = 0.25f;
    [Range(1, 8)] public int octaves = 4;
    [Range(0f, 1f)] public float persistence = 0.5f;
    public float lacunarity = 2f;

    [Header("Per-instance Randomization")]
    public Vector2 uniformScaleRange = new Vector2(0.8f, 1.6f);
    public Vector2 yRotationRange = new Vector2(0f, 360f);

    [Header("Rendering")]
    public Material rockMaterial;

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
        if (!rockMaterial)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            rockMaterial = new Material(sh) { name = "M_Rock (runtime)" };
        }
        rockMaterial.enableInstancing = true;
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

        // Pull biome rules if provided
        if (Ctx != null && Ctx.Biome != null)
        {
            minSlope01  = Ctx.Biome.rockMinSlope01;
            drynessBias = Ctx.Biome.rockDrynessBias;
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
            var mesh = GenerateRockMesh(
                subdivisions, radius,
                noiseScale, noiseAmplitude, octaves, persistence, lacunarity,
                flattenBottom, flattenHeight, bottomSmoothing,
                flatShaded, _rng.Next());
            mesh.name = $"RockVariant_{v}";
            _variantMeshes.Add(mesh);
        }
    }

    void ScatterAndBuild()
    {
        List<Vector2> accepted = new();
        int attempts = 0, maxAttempts = Mathf.Max(1, totalRocks) * 30;

        while (accepted.Count < totalRocks && attempts < maxAttempts)
        {
            attempts++;

            // sample in local XZ around this spawner
            Vector2 pLocal;
            pLocal.x = (float)(_rng.NextDouble() - 0.5f) * areaSize.x;
            pLocal.y = (float)(_rng.NextDouble() - 0.5f) * areaSize.y;

            // spacing
            bool ok = true;
            for (int i = 0; i < accepted.Count; i++)
                if ((accepted[i] - pLocal).sqrMagnitude < minSpacing * minSpacing) { ok = false; break; }
            if (!ok) continue;

            // to world
            Vector3 pos = transform.position + new Vector3(pLocal.x, 0f, pLocal.y);
            Vector3 hitNormal = Vector3.up;

            if (snapToGround)
            {
                var ray = new Ray(pos + Vector3.up * rayStartHeight, Vector3.down);
                if (Physics.Raycast(ray, out var hit, rayMaxDistance, groundMask, QueryTriggerInteraction.Ignore))
                { pos = hit.point; hitNormal = hit.normal; }
            }

            float slope01 = 0f, moisture01 = 0.5f;
            bool hasMasks = terrainSource && terrainSource.TrySampleMasks(pos, out slope01, out moisture01);
            if (!hasMasks)
            {
                slope01 = 1f - Mathf.Clamp01(Vector3.Dot(hitNormal, Vector3.up));
                moisture01 = 0.5f;
            }

            if (slope01 < minSlope01) continue;

            var water = transform.parent?.GetComponentInChildren<ProceduralWater>();
            if (water && water.IsWater(pos)) continue;

            float spawnProb = Mathf.Lerp(1f, (1f - moisture01), drynessBias);
            if (_rng.NextDouble() > spawnProb) continue;

            accepted.Add(pLocal);

            float s = Mathf.Lerp(uniformScaleRange.x, uniformScaleRange.y, (float)_rng.NextDouble());
            float yRot = Mathf.Lerp(yRotationRange.x, yRotationRange.y, (float)_rng.NextDouble());
            var rot = Quaternion.Euler(0f, yRot, 0f);
            var mat = Matrix4x4.TRS(pos, rot, new Vector3(s, s, s));
            var mesh = _variantMeshes[_rng.Next(_variantMeshes.Count)];

            // near-field: real GO for collisions & shadows
            if ((pos - transform.position).magnitude <= colliderRadius)
            {
                var go = new GameObject("Rock (Collider LOD)");
                go.transform.SetPositionAndRotation(pos, rot);
                go.transform.localScale = new Vector3(s, s, s);
                go.transform.parent = transform;

                var mf = go.AddComponent<MeshFilter>();   mf.sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = rockMaterial;
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
        bool useChunks = chunkedBatches && Ctx != null && Ctx.Grid != null;

        foreach (var kv in _instanced)
        {
            var mats = kv.Value;
            if (!useChunks)
            {
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
                    var b = ProcgenBoundsUtil.FromInstances(arr, kv.Key, /*extraPadding*/ 0.5f);

                    _exposedBatches.Add(new ProcgenCullingHub.Batch { mesh = kv.Key, submeshIndex = 0, material = rockMaterial, matrices = arr, bounds = b, layer = gameObject.layer, shadowCasting = UnityEngine.Rendering.ShadowCastingMode.On, receiveShadows = true });
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
                    b.Expand(radius * 1.5f);

                    _exposedBatches.Add(new ProcgenCullingHub.Batch { mesh = kv.Key, submeshIndex = 0, material = rockMaterial, matrices = arr, bounds = b, layer = gameObject.layer, shadowCasting = UnityEngine.Rendering.ShadowCastingMode.On, receiveShadows = true });
                }
            }
        }
    }


    public override List<ProcgenCullingHub.Batch> GetInstancedBatches() => _exposedBatches;

    // ---------- mesh generation ----------
    Mesh GenerateRockMesh(int sub, float rad, float nScale, float nAmp, int oct, float pers, float lac,
        bool doFlatten, float flatH, float flatSmooth, bool makeFlat, int localSeed)
    {
        var verts = new List<Vector3>();
        var tris = new List<int>();
        BuildCubeSphere(sub, rad, verts, tris);

        for (int i = 0; i < verts.Count; i++)
        {
            Vector3 v = verts[i];
            Vector3 n = v.normalized;
            float h = FBm3D(v * nScale, oct, pers, lac);
            float disp = 1f + nAmp * (h * 2f - 1f);
            v = n * rad * disp;

            if (doFlatten && v.y < flatH)
            {
                float t = Mathf.InverseLerp(flatH - flatSmooth, flatH, v.y);
                v.y = Mathf.Lerp(flatH, v.y, t);
            }
            verts[i] = v;
        }

        if (makeFlat) MakeFlatShaded(verts, tris, out verts, out tris);

        var mesh = new Mesh();
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0, true);
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        return mesh;
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

    static void MakeFlatShaded(List<Vector3> vin, List<int> tin, out List<Vector3> vout, out List<int> tout)
    {
        vout = new List<Vector3>(tin.Count);
        tout = new List<int>(tin.Count);
        for (int i = 0; i < tin.Count; i++) { vout.Add(vin[tin[i]]); tout.Add(i); }
    }

    static float Perlin3D(Vector3 p)
    {
        float xy = Mathf.PerlinNoise(p.x, p.y), yz = Mathf.PerlinNoise(p.y, p.z), zx = Mathf.PerlinNoise(p.z, p.x);
        return (xy + yz + zx) / 3f;
    }

    static float FBm3D(Vector3 p, int oct, float pers, float lac)
    {
        float total = 0f, amp = 1f, f = 1f, norm = 0f;
        for (int i = 0; i < oct; i++) { total += Perlin3D(p * f) * amp; norm += amp; amp *= pers; f *= lac; }
        return total / Mathf.Max(0.0001f, norm);
    }
}


