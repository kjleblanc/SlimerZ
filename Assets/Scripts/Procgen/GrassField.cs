using UnityEngine;
using System.Collections.Generic;

[DisallowMultipleComponent]
public class GrassField : MonoBehaviour
{
    public ProceduralTerrain terrainSource;
    public Material grassMaterial;

    [Header("Area & Density")]
    public Vector2 areaSize = new Vector2(180, 180);
    [Range(0, 50000)] public int bladeCount = 12000;
    public float minSpacing = 0.6f;
    public int seed = 99;

    [Header("Placement rules")]
    [Range(0f,1f)] public float maxSlope01 = 0.55f; // avoid steep
    [Range(0f,1f)] public float moistureBias = 0.8f; // prefer wet

    [Header("Blade size (meters)")]
    public Vector2 heightRange = new Vector2(0.6f, 1.2f);
    public Vector2 widthRange  = new Vector2(0.04f, 0.07f);

    readonly List<Matrix4x4[]> _batches = new();
    const int kMaxPerBatch = 1023;

    Mesh _bladeMesh;
    System.Random _rng;

    void Start(){ if (!terrainSource) terrainSource = Object.FindFirstObjectByType<ProceduralTerrain>(); Build(); }
    [ContextMenu("Rebuild Now")] public void Build(){ Scatter(); PrepareBatches(); }
    void OnDisable(){ _batches.Clear(); }

    void Scatter()
    {
        if (!_bladeMesh) _bladeMesh = BuildBladeMesh();
        _batches.Clear();
        _rng = new System.Random(seed);

        List<Vector2> accepted = new();
        int attempts = 0, maxAttempts = bladeCount * 25;
        var mats = new List<Matrix4x4>();

        while (accepted.Count < bladeCount && attempts < maxAttempts)
        {
            attempts++;
            Vector2 p;
            p.x = (float)(_rng.NextDouble() - 0.5) * areaSize.x;
            p.y = (float)(_rng.NextDouble() - 0.5) * areaSize.y;

            bool ok = true;
            for (int i = 0; i < accepted.Count; i++)
                if ((accepted[i] - p).sqrMagnitude < minSpacing * minSpacing) { ok = false; break; }
            if (!ok) continue;

            Vector3 pos = new Vector3(p.x, 0f, p.y);
            float slope01 = 0f, moist01 = 1f;
            if (terrainSource && terrainSource.terrain)
            {
                // snap to ground
                var td = terrainSource.terrain.terrainData;
                pos.y = terrainSource.terrain.SampleHeight(pos) + terrainSource.terrain.transform.position.y;
                if (!terrainSource.TrySampleMasks(pos, out slope01, out moist01)) { slope01 = 0f; moist01 = 1f; }
            }

            if (slope01 > maxSlope01) continue;
            float spawnProb = Mathf.Lerp(1f, moist01, moistureBias);
            if (_rng.NextDouble() > spawnProb) continue;

            accepted.Add(p);

            float h = Mathf.Lerp(heightRange.x, heightRange.y, (float)_rng.NextDouble());
            float w = Mathf.Lerp(widthRange.x,  widthRange.y,  (float)_rng.NextDouble());
            float yRot = (float)_rng.NextDouble() * 360f;

            var mat = Matrix4x4.TRS(pos, Quaternion.Euler(0, yRot, 0), new Vector3(w, h, w));
            mats.Add(mat);
        }

        // split to batches
        int offset = 0;
        while (offset < mats.Count)
        {
            int count = Mathf.Min(kMaxPerBatch, mats.Count - offset);
            var arr = new Matrix4x4[count];
            mats.CopyTo(offset, arr, 0, count);
            _batches.Add(arr);
            offset += count;
        }
    }

    void PrepareBatches() { /* already prepared in Scatter */ }

    void LateUpdate()
    {
        if (_batches.Count == 0 || !grassMaterial || !_bladeMesh) return;
        foreach (var arr in _batches)
            Graphics.DrawMeshInstanced(_bladeMesh, 0, grassMaterial, arr, arr.Length, null,
                UnityEngine.Rendering.ShadowCastingMode.Off, true, gameObject.layer);
    }

    static Mesh BuildBladeMesh()
    {
        // Vertical quad, base at y=0, top at y=1, centered on x.
        var m = new Mesh { name = "GrassBlade" };
        var v = new Vector3[] { new(-0.5f,0,0), new(0.5f,0,0), new(-0.5f,1,0), new(0.5f,1,0) };
        var n = new Vector3[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
        var uv= new Vector2[] { new(0,0), new(1,0), new(0,1), new(1,1) };
        var t = new int[] { 0,2,1, 1,2,3 };
        m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0);
        m.RecalculateBounds();
        return m;
    }
}
