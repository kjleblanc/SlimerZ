// Assets/Scripts/Procgen/InstancedRockField_Terrain.cs
using UnityEngine;
using System.Collections.Generic;

[DisallowMultipleComponent]
public class InstancedRockField_Terrain : MonoBehaviour
{
    [Header("Area (X-Z)")]
    public Vector2 areaSize = new Vector2(140, 140);

    [Header("Counts & LOD")]
    public float colliderRadius = 25f;
    [Range(1, 20000)] public int totalRocks = 1200;
    public float minSpacing = 1.8f;
    public int seed = 1234;

    [Header("Terrain masks (drag your ProcTerrain)")]
    public ProceduralTerrain terrainSource;
    [Range(0f, 1f)] public float minSlope01 = 0.25f;
    [Range(0f, 1f)] public float drynessBias = 0.6f;
    public bool snapToGround = true;
    public LayerMask groundMask = ~0;
    public float rayStartHeight = 50f, rayMaxDistance = 200f;

    [Header("Rock Variants (mesh reuse)")]
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

    // internals
    readonly List<Mesh> _variantMeshes = new();
    readonly Dictionary<Mesh, List<Matrix4x4>> _instanced = new();
    readonly Dictionary<Mesh, List<Matrix4x4[]>> _batches = new();
    const int kMaxPerBatch = 1023;
    System.Random _rng;

    void Start(){ if (!terrainSource) terrainSource = Object.FindFirstObjectByType<ProceduralTerrain>(); Rebuild(); }
    void OnDisable(){ ClearChildren(); }

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
#if UNITY_EDITOR
        foreach (var m in _variantMeshes) if (m) DestroyImmediate(m);
#else
        foreach (var m in _variantMeshes) if (m) Destroy(m);
#endif
        _variantMeshes.Clear();
        _instanced.Clear();
        _batches.Clear();
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
        if (!rockMaterial) rockMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));

        List<Vector2> accepted = new();
        int attempts = 0, maxAttempts = totalRocks * 30;

        while (accepted.Count < totalRocks && attempts < maxAttempts)
        {
            attempts++;

            // sample in local XZ, then offset by this spawner's position
            Vector2 pLocal;
            pLocal.x = (float)(_rng.NextDouble() - 0.5) * areaSize.x;
            pLocal.y = (float)(_rng.NextDouble() - 0.5) * areaSize.y;

            // spacing in local space
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
                    pos = hit.point;
                    hitNormal = hit.normal;
                }
            }

            float slope01 = 0f, moisture01 = 0.5f;
            bool hasMasks = terrainSource && terrainSource.TrySampleMasks(pos, out slope01, out moisture01);
            if (!hasMasks)
            {
                slope01 = 1f - Mathf.Clamp01(Vector3.Dot(hitNormal, Vector3.up)); // 0 flat..1 vertical
                moisture01 = 0.5f;
            }

            if (slope01 < minSlope01) continue;
            float spawnProb = Mathf.Lerp(1f, (1f - moisture01), drynessBias);
            if (_rng.NextDouble() > spawnProb) continue;

            accepted.Add(pLocal);

            float s = Mathf.Lerp(uniformScaleRange.x, uniformScaleRange.y, (float)_rng.NextDouble());
            float yRot = Mathf.Lerp(yRotationRange.x, yRotationRange.y, (float)_rng.NextDouble());
            var rot = Quaternion.Euler(0f, yRot, 0f);
            var mat = Matrix4x4.TRS(pos, rot, new Vector3(s, s, s));
            var mesh = _variantMeshes[_rng.Next(_variantMeshes.Count)];

            if ((pos - transform.position).magnitude <= colliderRadius)
            {
                var go = new GameObject("Rock (Collider LOD)");
                go.transform.SetPositionAndRotation(pos, rot);
                go.transform.localScale = new Vector3(s, s, s);
                go.transform.parent = transform;
                var mf = go.AddComponent<MeshFilter>(); mf.sharedMesh = mesh;
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
        // Debug.Log($"Rocks accepted: {accepted.Count}");
    }

    void PrepareBatches()
    {
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
        if (_batches.Count == 0 || !rockMaterial) return;
        foreach (var kv in _batches)
            foreach (var arr in kv.Value)
                Graphics.DrawMeshInstanced(kv.Key, 0, rockMaterial, arr, arr.Length, null,
                    UnityEngine.Rendering.ShadowCastingMode.On, true, gameObject.layer);
    }

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
        Vector3[] faceNormals = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
        Vector3[] faceTangents = { Vector3.forward, Vector3.forward, Vector3.right, Vector3.right, Vector3.right, Vector3.right };

        for (int f = 0; f < 6; f++)
        {
            Vector3 n = faceNormals[f], t = faceTangents[f], b = Vector3.Cross(n, t);
            int start = verts.Count;

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
        float xy = Mathf.PerlinNoise(p.x, p.y);
        float yz = Mathf.PerlinNoise(p.y, p.z);
        float zx = Mathf.PerlinNoise(p.z, p.x);
        return (xy + yz + zx) / 3f;
    }

    static float FBm3D(Vector3 p, int oct, float pers, float lac)
    {
        float total = 0f, amp = 1f, f = 1f, norm = 0f;
        for (int i = 0; i < oct; i++)
        {
            total += Perlin3D(p * f) * amp;
            norm += amp;
            amp *= pers;
            f *= lac;
        }
        return total / Mathf.Max(0.0001f, norm);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.85f, 0.7f, 0.6f, 0.25f);
        Gizmos.DrawCube(transform.position, new Vector3(areaSize.x, 0.1f, areaSize.y));
        Gizmos.color = new Color(0.9f, 0.5f, 0.3f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, colliderRadius);
    }
}
