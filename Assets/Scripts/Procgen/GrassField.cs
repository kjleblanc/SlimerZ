// Assets/Scripts/Procgen/GrassField.cs
using UnityEngine;
using System.Collections.Generic;

[ExecuteAlways]
[DisallowMultipleComponent]
public class GrassField : ProcgenSpawnerBase
{
    public ProceduralTerrain terrainSource;
    public Material grassMaterial;

    [Header("Area & Density")]
    public Vector2 areaSize = new Vector2(180, 180);
    [Range(0, 100000)] public int bladeCount = 12000;
    public float minSpacing = 0.6f;
    public int seed = 99;

    [Header("Placement rules")]
    [Range(0f,1f)] public float maxSlope01 = 0.55f;
    [Range(0f,1f)] public float moistureBias = 0.8f;
    [Range(0f,1f)] public float minSpawnProb = 0.12f;

    [Header("Blade size (meters)")]
    public Vector2 heightRange = new Vector2(0.6f, 1.2f);
    public Vector2 widthRange  = new Vector2(0.04f, 0.07f);

    [Header("Batching")]
    public bool chunkedBatches = true;

    // instancing data
    readonly List<Matrix4x4> _instances = new();               // accepted transforms
    readonly List<Matrix4x4[]> _batches = new();               // sliced arrays for DrawMeshInstanced
    readonly List<ProcgenCullingHub.Batch> _exposedBatches = new(); // hub-facing
    const int kMaxPerBatch = 1023;

    Mesh _bladeMesh;
    System.Random _rng;

    void Start()
    {
        if (!terrainSource) terrainSource = Object.FindObjectOfType<ProceduralTerrain>();
        EnsureDefaults();
        Build();
    }

    void OnDisable()
    {
        _instances.Clear();
        _batches.Clear();
        _exposedBatches.Clear();
    }

    // ProcgenSpawnerBase requires this
    public override void Rebuild() { Build(); }

    [ContextMenu("Rebuild Now")]
    public void Build()
    {
        EnsureDefaults();

        // Biome-driven rules (optional)
        if (Ctx != null && Ctx.Biome != null)
        {
            maxSlope01   = Ctx.Biome.grassMaxSlope01;
            moistureBias = Ctx.Biome.grassMoistureBias;
            minSpawnProb = Ctx.Biome.grassMinSpawnProb;
        }
        // Ensure terrain reference from context
        if (!terrainSource && Ctx != null)
            terrainSource = Ctx.Root.GetComponentInChildren<ProceduralTerrain>();

        Scatter();
        PrepareBatches();
        NotifyHub();
    }

    void EnsureDefaults()
    {
        if (!_bladeMesh) _bladeMesh = BuildBladeMesh();
        if (!grassMaterial)
        {
            var sh = Shader.Find("URP/GrassWindUnlit");
            if (!sh) sh = Shader.Find("Universal Render Pipeline/Unlit");
            grassMaterial = new Material(sh) { name = "M_Grass (runtime)" };
        }
        grassMaterial.enableInstancing = true;
        if (!terrainSource) terrainSource = Object.FindObjectOfType<ProceduralTerrain>();
    }

    void Scatter()
    {
        _instances.Clear();
        _rng = new System.Random(seed);

        var accepted = new List<Vector2>();

        var t = terrainSource ? terrainSource.terrain : null;
        bool hasTerrain = t && t.terrainData;

        int attempts = 0, maxAttempts = Mathf.Max(1, bladeCount) * 25;

        while (accepted.Count < bladeCount && attempts < maxAttempts)
        {
            attempts++;

            Vector2 pLocal;
            pLocal.x = (float)(_rng.NextDouble() - 0.5) * areaSize.x;
            pLocal.y = (float)(_rng.NextDouble() - 0.5) * areaSize.y;

            // Poisson-ish spacing
            bool ok = true;
            for (int i = 0; i < accepted.Count; i++)
                if ((accepted[i] - pLocal).sqrMagnitude < minSpacing * minSpacing) { ok = false; break; }
            if (!ok) continue;

            Vector3 pos = transform.position + new Vector3(pLocal.x, 0f, pLocal.y);

            float slope01 = 0f, moist01 = 1f;

            if (hasTerrain)
            {
                var td = t.terrainData;
                Vector3 tLocal = pos - t.transform.position;
                if (tLocal.x < 0f || tLocal.z < 0f || tLocal.x > td.size.x || tLocal.z > td.size.z)
                    continue;

                pos.y = t.SampleHeight(pos) + t.transform.position.y;

                if (!terrainSource.TrySampleMasks(pos, out slope01, out moist01))
                { slope01 = 0f; moist01 = 1f; }
            }

            if (slope01 > maxSlope01) continue;

            float spawnProb = Mathf.Max(minSpawnProb, Mathf.Lerp(1f, moist01, moistureBias));
            if (_rng.NextDouble() > spawnProb) continue;

            accepted.Add(pLocal);

            float h = Mathf.Lerp(heightRange.x, heightRange.y, (float)_rng.NextDouble());
            float w = Mathf.Lerp(widthRange.x,  widthRange.y,  (float)_rng.NextDouble());
            float yRot = (float)_rng.NextDouble() * 360f;

            _instances.Add(Matrix4x4.TRS(pos, Quaternion.Euler(0, yRot, 0), new Vector3(w, h, w)));
        }
    }

    void PrepareBatches()
    {
        _batches.Clear();
        _exposedBatches.Clear();

        bool useChunks = chunkedBatches && Ctx != null && Ctx.Grid != null;

        if (!useChunks)
        {
            // Slice into ≤1023 with per-batch bounds from positions
            int offset = 0;
            while (offset < _instances.Count)
            {
                int count = Mathf.Min(kMaxPerBatch, _instances.Count - offset);
                var arr = new Matrix4x4[count];
                _instances.CopyTo(offset, arr, 0, count);
                offset += count;

                Bounds b = ComputeBoundsFromMatrices(arr, expand: 0.5f);

                _batches.Add(arr);
                _exposedBatches.Add(new ProcgenCullingHub.Batch {
                    mesh = _bladeMesh, submeshIndex = 0, material = grassMaterial, matrices = arr, bounds = b,
                    layer = gameObject.layer,
                    shadowCasting = UnityEngine.Rendering.ShadowCastingMode.Off,
                    receiveShadows = false
                });
            }
            return;
        }

        // Chunked path: group by grid cell for tighter culling
        var grid = Ctx.Grid;
        var byChunk = new Dictionary<ChunkId, List<Matrix4x4>>(64);
        for (int i = 0; i < _instances.Count; i++)
        {
            Vector3 p = ExtractPosition(_instances[i]);
            var id = grid.IdAt(p);
            if (!byChunk.TryGetValue(id, out var list)) byChunk[id] = list = new List<Matrix4x4>();
            list.Add(_instances[i]);
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
                b.Expand(0.5f);

                _batches.Add(arr);
                _exposedBatches.Add(new ProcgenCullingHub.Batch {
                    mesh = _bladeMesh, submeshIndex = 0, material = grassMaterial, matrices = arr, bounds = b,
                    layer = gameObject.layer,
                    shadowCasting = UnityEngine.Rendering.ShadowCastingMode.Off,
                    receiveShadows = false
                });
            }
        }
    }

    public override List<ProcgenCullingHub.Batch> GetInstancedBatches() => _exposedBatches;

    static Mesh BuildBladeMesh()
    {
        var m = new Mesh { name = "GrassBlade" };
        var v = new Vector3[] { new(-0.5f,0,0), new(0.5f,0,0), new(-0.5f,1,0), new(0.5f,1,0) };
        var n = new Vector3[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
        var uv= new Vector2[] { new(0,0), new(1,0), new(0,1), new(1,1) };
        var t = new int[] { 0,2,1, 1,2,3 };
        m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0);
        m.RecalculateBounds();
        return m;
    }

    static Vector3 ExtractPosition(Matrix4x4 m) => new Vector3(m.m03, m.m13, m.m23);

    static Bounds ComputeBoundsFromMatrices(Matrix4x4[] arr, float expand)
    {
        Vector3 min = new(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        Vector3 max = new(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        for (int i = 0; i < arr.Length; i++)
        {
            Vector3 p = ExtractPosition(arr[i]);
            if (p.x < min.x) min.x = p.x; if (p.y < min.y) min.y = p.y; if (p.z < min.z) min.z = p.z;
            if (p.x > max.x) max.x = p.x; if (p.y > max.y) max.y = p.y; if (p.z > max.z) max.z = p.z;
        }
        var b = new Bounds(); b.SetMinMax(min, max); b.Expand(expand);
        return b;
    }
}
