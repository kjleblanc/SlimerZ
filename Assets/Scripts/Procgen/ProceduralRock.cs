using UnityEngine;
using System.Collections.Generic;

[ExecuteAlways]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
[DisallowMultipleComponent]
public class ProceduralRock : MonoBehaviour
{
    [Header("Shape")]
    [Range(0.1f, 10f)] public float radius = 1.2f;
    [Range(1, 32)] public int subdivisions = 8; // per edge of each cube face

    [Header("Noise")]
    public float noiseScale = 1.5f;
    public float noiseAmplitude = 0.25f;
    [Range(1, 8)] public int octaves = 4;
    [Range(0f, 1f)] public float persistence = 0.5f;
    public float lacunarity = 2f;
    public int seed = 12345;

    [Header("Look/Utility")]
    public bool flatShaded = true;
    public bool flattenBottom = true;
    public float flattenHeight = 0f; // y=0 plane
    public float bottomSmoothing = 0.2f;

    Mesh mesh;
    MeshCollider meshCollider;
    System.Random rng;

    void OnValidate() { Generate(); }
    void Awake() { Generate(); }
    void Reset()
    {
        var mr = GetComponent<MeshRenderer>();
        if (mr) mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
    }

    void Generate()
    {
        rng = new System.Random(seed);

        var mf = GetComponent<MeshFilter>();
        if (mesh == null)
        {
            mesh = new Mesh();
            mesh.name = "ProceduralRock";
            mf.sharedMesh = mesh;
        }
        else mesh.Clear();

        List<Vector3> verts;
        List<int> tris;
        BuildCubeSphere(subdivisions, radius, out verts, out tris);

        // Displace vertices along normal using simple 3D fBm noise
        for (int i = 0; i < verts.Count; i++)
        {
            Vector3 v = verts[i];
            Vector3 n = v.normalized;

            float h = FBm3D(v * noiseScale, octaves, persistence, lacunarity);
            float disp = 1f + noiseAmplitude * (h * 2f - 1f); // map 0..1 to -1..1
            v = n * radius * disp;

            // Flatten bottom so it sits nicely on ground
            if (flattenBottom && v.y < flattenHeight)
            {
                float t = Mathf.InverseLerp(flattenHeight - bottomSmoothing, flattenHeight, v.y);
                v.y = Mathf.Lerp(flattenHeight, v.y, t);
            }

            verts[i] = v;
        }

        if (flatShaded)
            MakeFlatShaded(verts, tris, out verts, out tris);

        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0, true);
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();

        // Collider matches the generated mesh (good collisions!)
        if (!TryGetComponent(out meshCollider))
            meshCollider = gameObject.AddComponent<MeshCollider>();

        meshCollider.sharedMesh = null; // force refresh
        meshCollider.sharedMesh = mesh;
    }

    // Build a subdivided cube and project to a sphere (cube-sphere)
    static void BuildCubeSphere(int sub, float radius, out List<Vector3> verts, out List<int> tris)
    {
        verts = new List<Vector3>();
        tris = new List<int>();

        Vector3[] faceNormals = {
            Vector3.right, Vector3.left, Vector3.up,
            Vector3.down, Vector3.forward, Vector3.back
        };
        Vector3[] faceTangents = {
            Vector3.forward, Vector3.forward, Vector3.right,
            Vector3.right, Vector3.right, Vector3.right
        };

        for (int f = 0; f < 6; f++)
        {
            Vector3 n = faceNormals[f];
            Vector3 t = faceTangents[f];
            Vector3 b = Vector3.Cross(n, t);

            int startIndex = verts.Count;

            for (int y = 0; y <= sub; y++)
            {
                for (int x = 0; x <= sub; x++)
                {
                    float u = (float)x / sub;     // 0..1
                    float v = (float)y / sub;     // 0..1
                    Vector2 uv = new Vector2(u * 2f - 1f, v * 2f - 1f); // -1..1

                    Vector3 p = n + uv.x * t + uv.y * b; // cube face point
                    p = p.normalized * radius;           // project to sphere
                    verts.Add(p);
                }
            }

            for (int y = 0; y < sub; y++)
            {
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
    }

    // Duplicate verts per triangle so normals are flat
    static void MakeFlatShaded(List<Vector3> vertsIn, List<int> trisIn, out List<Vector3> vertsOut, out List<int> trisOut)
    {
        vertsOut = new List<Vector3>(trisIn.Count);
        trisOut = new List<int>(trisIn.Count);
        for (int i = 0; i < trisIn.Count; i++)
        {
            vertsOut.Add(vertsIn[trisIn[i]]);
            trisOut.Add(i);
        }
    }

    // Lightweight "3D" Perlin via averaging 2D slices
    static float Perlin3D(Vector3 p)
    {
        float xy = Mathf.PerlinNoise(p.x, p.y);
        float yz = Mathf.PerlinNoise(p.y, p.z);
        float zx = Mathf.PerlinNoise(p.z, p.x);
        return (xy + yz + zx) / 3f;
    }

    static float FBm3D(Vector3 p, int octaves, float persistence, float lacunarity)
    {
        float total = 0f, amplitude = 1f, frequency = 1f, maxValue = 0f;
        for (int i = 0; i < octaves; i++)
        {
            total += Perlin3D(p * frequency) * amplitude;
            maxValue += amplitude;
            amplitude *= persistence;
            frequency *= lacunarity;
        }
        return total / maxValue;
    }
}
