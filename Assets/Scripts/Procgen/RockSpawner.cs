using UnityEngine;
using System.Collections.Generic;

[DisallowMultipleComponent]
public class RockSpawner : MonoBehaviour
{
    [Header("Scatter Area (X-Z)")]
    public Vector2 areaSize = new Vector2(40, 40);

    [Header("Spawn")]
    [Range(1, 500)] public int count = 60;
    [Tooltip("Min spacing between rock centers (meters)")]
    public float minSpacing = 1.8f;
    public int seed = 1234;

    [Header("Rock Variants (mesh re-use for performance)")]
    [Range(1, 16)] public int variants = 6;

    [Header("Rock Shape")]
    [Range(0.4f, 3f)] public float radius = 1.2f;
    [Range(4, 16)] public int subdivisions = 10;
    public bool flatShaded = true;
    public bool flattenBottom = true;
    public float flattenHeight = 0f;
    public float bottomSmoothing = 0.2f;

    [Header("Rock Noise")]
    public float noiseScale = 1.5f;
    public float noiseAmplitude = 0.25f;
    [Range(1, 8)] public int octaves = 4;
    [Range(0f, 1f)] public float persistence = 0.5f;
    public float lacunarity = 2f;

    [Header("Randomize per-instance")]
    public Vector2 uniformScaleRange = new Vector2(0.8f, 1.6f);

    [Header("Rendering")]
    public Material rockMaterial;

    readonly List<Mesh> _variantMeshes = new();
    System.Random _rng;

    [ContextMenu("Rebuild Now")]
    public void Rebuild()
    {
        ClearChildren();
        BuildVariants();
        Scatter();
    }

    void Start() { Rebuild(); }

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
        foreach (var m in _variantMeshes) DestroyImmediate(m);
        _variantMeshes.Clear();
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
                flatShaded,
                _rng.Next()); // different seed per variant
            mesh.name = $"RockVariant_{v}";
            _variantMeshes.Add(mesh);
        }
    }

    void Scatter()
    {
        if (_variantMeshes.Count == 0) return;
        if (!rockMaterial) rockMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));

        List<Vector2> accepted = new();
        int attempts = 0, maxAttempts = count * 15;

        while (accepted.Count < count && attempts < maxAttempts)
        {
            attempts++;
            Vector2 p;
            p.x = (float)(_rng.NextDouble() - 0.5) * areaSize.x;
            p.y = (float)(_rng.NextDouble() - 0.5) * areaSize.y;

            bool ok = true;
            for (int i = 0; i < accepted.Count; i++)
                if ((accepted[i] - p).sqrMagnitude < minSpacing * minSpacing) { ok = false; break; }

            if (!ok) continue;
            accepted.Add(p);

            var go = new GameObject("Rock");
            go.transform.parent = transform;
            go.transform.localPosition = new Vector3(p.x, 0f, p.y);
            go.transform.localRotation = Quaternion.Euler(0f, (float)(_rng.NextDouble() * 360.0), 0f);
            float s = Mathf.Lerp(uniformScaleRange.x, uniformScaleRange.y, (float)_rng.NextDouble());
            go.transform.localScale = new Vector3(s, s, s);

            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            var mc = go.AddComponent<MeshCollider>();

            // pick a mesh variant; reuse meshes for better batching
            var mesh = _variantMeshes[_rng.Next(_variantMeshes.Count)];
            mf.sharedMesh = mesh;
            mc.sharedMesh = mesh;
            mr.sharedMaterial = rockMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

            // optional: mark static for lightmapping/static batching in editor
            go.isStatic = true;
        }
    }

    // ==== Mesh Generation (same core math as before) ====
    Mesh GenerateRockMesh(
        int sub, float rad, float nScale, float nAmp, int oct, float pers, float lac,
        bool doFlatten, float flatH, float flatSmooth, bool makeFlat, int localSeed)
    {
        var verts = new List<Vector3>();
        var tris = new List<int>();
        BuildCubeSphere(sub, rad, verts, tris);

        var rand = new System.Random(localSeed);
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

        Mesh mesh = new Mesh();
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
            Vector3 n = faceNormals[f];
            Vector3 t = faceTangents[f];
            Vector3 b = Vector3.Cross(n, t);

            int startIndex = verts.Count;

            for (int y = 0; y <= sub; y++)
                for (int x = 0; x <= sub; x++)
                {
                    float u = (float)x / sub, v = (float)y / sub;
                    Vector2 uv = new Vector2(u * 2f - 1f, v * 2f - 1f);
                    Vector3 p = n + uv.x * t + uv.y * b;
                    p = p.normalized * radius;
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
        float total = 0f, amp = 1f, freq = 1f, maxV = 0f;
        for (int i = 0; i < oct; i++)
        {
            total += Perlin3D(p * freq) * amp;
            maxV += amp;
            amp *= pers;
            freq *= lac;
        }
        return total / maxV;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.15f);
        Gizmos.DrawCube(transform.position, new Vector3(areaSize.x, 0.1f, areaSize.y));
    }
}
