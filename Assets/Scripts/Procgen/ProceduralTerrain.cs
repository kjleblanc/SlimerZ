// Assets/Scripts/Procgen/ProceduralTerrain.cs
using UnityEngine;
using System.Collections.Generic;

#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
[DisallowMultipleComponent]
public class ProceduralTerrain : MonoBehaviour
{
    [Header("Resolution & Size")]
    [Tooltip("(2^n)+1 in [33..4097]. Common: 257, 513, 1025.")]
    public int heightmapResolution = 513;
    public Vector2 sizeXZ = new Vector2(200, 200);
    [Tooltip("World height scale (meters).")]
    public float heightScale = 25f;

    [Header("Noise (FBM + light warp)")]
    public int seed = 1234;
    public float baseScale = 0.008f;
    [Range(1, 8)] public int octaves = 5;
    [Range(0.2f, 1f)] public float persistence = 0.5f;
    [Range(1.5f, 3.0f)] public float lacunarity = 2.0f;
    [Tooltip("Domain warp strength (meters).")]
    public float warpStrength = 8f;
    public float warpScale = 0.02f;

    [Header("Masks")]
    [Tooltip("0..1, steeper = higher slope.")]
    [Range(0f, 1f)] public float slopeSteepnessBoost = 1.0f;
    [Tooltip("Blend factor between inverse-slope and low-frequency noise.")]
    [Range(0f, 1f)] public float moistureNoiseBlend = 0.4f;
    public float moistureNoiseScale = 0.006f;

    [Header("Output (read-only)")]
    public Terrain terrain;
    public Texture2D slopeMask;    // grayscale
    public Texture2D moistureMask; // grayscale

    System.Random rng;

#if UNITY_EDITOR
    bool _queuedEditorRebuild;
#endif

    [ContextMenu("Rebuild Now")]
    public void Rebuild()
    {
        ClampResolution();
        rng = new System.Random(seed);

        // Ensure a Terrain exists (as a child). Avoid doing this inside OnValidate.
        if (terrain == null)
        {
            var go = new GameObject("GeneratedTerrain");
            go.transform.SetParent(transform, false);
            terrain = go.AddComponent<Terrain>();
            go.AddComponent<TerrainCollider>();
        }

        // Build TerrainData
        var td = new TerrainData
        {
            heightmapResolution = heightmapResolution,
            size = new Vector3(sizeXZ.x, heightScale, sizeXZ.y)
        };

        float[,] heights = new float[heightmapResolution, heightmapResolution];

        // Precompute random offsets
        Vector2 offBase = new Vector2((float)rng.NextDouble() * 10000f, (float)rng.NextDouble() * 10000f);
        Vector2 offWarp = new Vector2((float)rng.NextDouble() * 10000f, (float)rng.NextDouble() * 10000f);

        int res = heightmapResolution;
        for (int y = 0; y < res; y++)
        {
            for (int x = 0; x < res; x++)
            {
                float u = (float)x / (res - 1);
                float v = (float)y / (res - 1);

                float wx = Mathf.Lerp(0f, sizeXZ.x, u);
                float wz = Mathf.Lerp(0f, sizeXZ.y, v);

                // Domain warp
                float wxw = wx + (Perlin(wx * warpScale + offWarp.x, wz * warpScale + offWarp.y) - 0.5f) * 2f * warpStrength;
                float wzw = wz + (Perlin((wx + 17.3f) * warpScale + offWarp.x, (wz - 9.1f) * warpScale + offWarp.y) - 0.5f) * 2f * warpStrength;

                float h = FBm(wxw, wzw, baseScale, octaves, persistence, lacunarity, offBase);
                heights[y, x] = Mathf.Clamp01(h);
            }
        }

        td.SetHeights(0, 0, heights);

        // Assign data and collider
#if UNITY_EDITOR
        td.name = "ProceduralTerrainData";
        string path = AssetDatabase.GenerateUniqueAssetPath("Assets/ProceduralTerrainData.asset");
        AssetDatabase.CreateAsset(td, path);
        AssetDatabase.SaveAssets();
        terrain.terrainData = td;
        var tc = terrain.GetComponent<TerrainCollider>();
        tc.terrainData = td;
        EnsureMaterialAndLayer(td);
#else
        terrain.terrainData = td;
        var tc = terrain.GetComponent<TerrainCollider>();
        tc.terrainData = td;
        EnsureMaterialAndLayer(td);
#endif

        // Build masks
        BuildMasks(td, heights);
    }

    void EnsureMaterialAndLayer(TerrainData td)
    {
        // URP Terrain material
        var urpTerrain = Shader.Find("Universal Render Pipeline/Terrain/Lit");
        if (terrain.materialTemplate == null || terrain.materialTemplate.shader != urpTerrain)
            terrain.materialTemplate = new Material(urpTerrain) { name = "M_TerrainURP (runtime)" };

        // Ensure at least one TerrainLayer (flat color)
        if (td.terrainLayers == null || td.terrainLayers.Length == 0)
        {
            var layer = new TerrainLayer();
            layer.diffuseTexture = MakeSolidColorTex(new Color(0.55f, 0.53f, 0.50f, 1f));
            layer.tileSize = new Vector2(10, 10);
            td.terrainLayers = new[] { layer };
        }
    }

    Texture2D MakeSolidColorTex(Color c)
    {
        var tex = new Texture2D(2, 2, TextureFormat.RGB24, false, true);
        tex.SetPixels(new[] { c, c, c, c }); tex.Apply(false, true);
        tex.wrapMode = TextureWrapMode.Repeat; tex.filterMode = FilterMode.Bilinear;
        tex.name = "SolidColor";
        return tex;
    }

    void OnValidate()
    {
        ClampResolution();
#if UNITY_EDITOR
        // Do NOT build immediately inside OnValidate; schedule a delayed rebuild.
        if (!Application.isPlaying)
        {
            if (_queuedEditorRebuild) return;
            _queuedEditorRebuild = true;
            EditorApplication.delayCall += () =>
            {
                _queuedEditorRebuild = false;
                if (this != null) Rebuild();
            };
        }
#endif
    }

    void ClampResolution()
    {
        // Force (2^n)+1 in range
        heightmapResolution = Mathf.Clamp(NearestPow2Plus1(heightmapResolution), 33, 4097);
        sizeXZ.x = Mathf.Max(10f, sizeXZ.x);
        sizeXZ.y = Mathf.Max(10f, sizeXZ.y);
        heightScale = Mathf.Max(1f, heightScale);
    }

    static int NearestPow2Plus1(int v)
    {
        v = Mathf.Clamp(v, 33, 4097);
        int n = 5; // 2^5 + 1 = 33
        while ((1 << n) + 1 < v && n < 12) n++; // up to 4097
        int a = (1 << n) + 1;
        int b = (1 << Mathf.Min(n + 1, 12)) + 1;
        return (Mathf.Abs(v - a) <= Mathf.Abs(v - b)) ? a : b;
    }

    // ===== Masks =====
    void BuildMasks(TerrainData td, float[,] heights)
    {
        int res = td.heightmapResolution;
        slopeMask    = NewOrResize(slopeMask,    res, res, TextureFormat.R8);
        moistureMask = NewOrResize(moistureMask, res, res, TextureFormat.R8);

        Color[] slopePixels = new Color[res * res];
        Color[] moistPixels = new Color[res * res];

        float sx = td.size.x / (res - 1);
        float sz = td.size.z / (res - 1);
        float sy = td.size.y;

        for (int y = 0; y < res; y++)
        {
            int ym = Mathf.Max(0, y - 1), yp = Mathf.Min(res - 1, y + 1);
            for (int x = 0; x < res; x++)
            {
                int xm = Mathf.Max(0, x - 1), xp = Mathf.Min(res - 1, x + 1);

                // heights in meters
                float hL = heights[y, xm] * sy;
                float hR = heights[y, xp] * sy;
                float hD = heights[ym, x] * sy;
                float hU = heights[yp, x] * sy;

                float dHx = (hR - hL) / (2f * sx);
                float dHz = (hU - hD) / (2f * sz);
                float slope = Mathf.Atan(Mathf.Sqrt(dHx * dHx + dHz * dHz)); // rad
                float slope01 = Mathf.Clamp01(slope / (Mathf.PI * 0.5f)) * slopeSteepnessBoost;
                slope01 = Mathf.Clamp01(slope01);

                float u = (float)x / (res - 1);
                float v = (float)y / (res - 1);
                float wx = u * td.size.x;
                float wz = v * td.size.z;
                float mNoise = Mathf.PerlinNoise(wx * moistureNoiseScale + 123.4f, wz * moistureNoiseScale - 77.7f);
                float moist = Mathf.Clamp01(Mathf.Lerp(1f - slope01, mNoise, moistureNoiseBlend));

                int i = y * res + x;
                slopePixels[i] = new Color(slope01, slope01, slope01, 1);
                moistPixels[i] = new Color(moist,    moist,    moist,    1);
            }
        }

        slopeMask.SetPixels(slopePixels);    slopeMask.Apply(false, false);
        moistureMask.SetPixels(moistPixels); moistureMask.Apply(false, false);
    }

    Texture2D NewOrResize(Texture2D tex, int w, int h, TextureFormat fmt)
    {
        if (tex == null || tex.width != w || tex.height != h || tex.format != fmt)
        {
            if (tex != null)
            {
                // Avoid DestroyImmediate during OnValidate; deferred destroy is editor-safe.
                Destroy(tex);
            }
            tex = new Texture2D(w, h, fmt, false, true)
            {
                name = "Mask",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
        }
        return tex;
    }

    // ===== Sampling helpers (for placement rules) =====
    public bool TrySampleMasks(Vector3 worldPos, out float slope01, out float moisture01)
    {
        slope01 = moisture01 = 0f;
        if (!terrain || !slopeMask || !moistureMask) return false;

        Vector3 local = worldPos - terrain.transform.position;
        float u = Mathf.InverseLerp(0f, terrain.terrainData.size.x, local.x);
        float v = Mathf.InverseLerp(0f, terrain.terrainData.size.z, local.z);
        if (u < 0f || u > 1f || v < 0f || v > 1f) return false;

        slope01    = slopeMask.GetPixelBilinear(u, v).r;
        moisture01 = moistureMask.GetPixelBilinear(u, v).r;
        return true;
    }

    // ===== Noise =====
    float Perlin(float x, float y) => Mathf.PerlinNoise(x, y);

    float FBm(float wx, float wz, float scale, int oct, float pers, float lac, Vector2 offs)
    {
        float amp = 1f, freq = 1f, total = 0f, norm = 0f;
        for (int i = 0; i < oct; i++)
        {
            total += Mathf.PerlinNoise(wx * scale * freq + offs.x, wz * scale * freq + offs.y) * amp;
            norm += amp;
            amp *= pers;
            freq *= lac;
        }
        total /= Mathf.Max(0.0001f, norm);

        // Gentle terrace for variation
        total = Mathf.Pow(total, 1.35f);
        return total;
    }
}
