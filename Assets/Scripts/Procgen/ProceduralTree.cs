using UnityEngine;
using System.Collections.Generic;

[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class ProceduralTree : MonoBehaviour
{
    [Header("Seed")]
    public int seed = 12345;

    [Header("Trunk")]
    [Range(2, 64)] public int trunkSegments = 18;
    [Range(6, 24)] public int trunkSides = 10;
    public float trunkHeight = 6f;
    public float trunkBaseRadius = 0.35f;
    public float trunkTopRadius = 0.12f;
    [Tooltip("How much the trunk meanders sideways")] public float trunkBend = 0.6f;

    [Header("Branches")]
    [Range(0, 128)] public int branchCount = 24;
    [Range(3, 24)] public int branchSegments = 8;
    [Range(6, 24)] public int branchSides = 8;
    public Vector2 branchLengthRange = new Vector2(1.5f, 3.0f);
    public float branchRadiusScale = 0.35f;
    [Tooltip("How much branches curve upward")] public float branchCurve = 0.6f;
    [Tooltip("Y% along trunk to begin placing branches (0..1)")] public float branchStart = 0.2f;
    [Tooltip("Y% along trunk to stop placing branches (0..1)")] public float branchEnd = 0.9f;

    [Header("Leaves (low-poly blobs, submesh 1)")]
    public int leavesPerBranch = 2;
    public float leafBlobRadius = 0.5f;
    [Range(1, 5)] public int leafBlobSubdiv = 2; // 1..5 on a cube-sphere
    public Vector2 leafBlobScaleJitter = new Vector2(0.8f, 1.4f);

    [Header("Gizmos")]
    public bool drawDebug = false;

    Mesh mesh;
    MeshCollider meshCollider;
    System.Random rng;

    void OnValidate() => Generate();
    void Awake() => Generate();

    void Generate()
    {
        rng = new System.Random(seed);
        if (mesh == null)
        {
            mesh = new Mesh();
            mesh.name = "ProceduralTree";
            GetComponent<MeshFilter>().sharedMesh = mesh;
        }
        else mesh.Clear();

        // ------- containers -------
        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var uvs = new List<Vector2>();
        var trisWood = new List<int>();   // submesh 0
        var trisLeaves = new List<int>(); // submesh 1

        // ------- build trunk path -------
        var trunkPath = new List<Vector3>(trunkSegments + 1);
        for (int i = 0; i <= trunkSegments; i++)
        {
            float t = (float)i / trunkSegments;
            float y = t * trunkHeight;

            // sideways wobble using cheap pseudo-noise
            float ang = (float)(i * 0.7f + rng.NextDouble() * 0.5);
            float r = trunkBend * Mathf.Sin(t * Mathf.PI * 0.75f);
            Vector2 off = new Vector2(Mathf.Sin(ang), Mathf.Cos(ang)) * r;

            trunkPath.Add(new Vector3(off.x, y, off.y));
        }

        // ------- trunk tube -------
        var trunkRadii = new List<float>(trunkSegments + 1);
        for (int i = 0; i <= trunkSegments; i++)
        {
            float t = (float)i / trunkSegments;
            trunkRadii.Add(Mathf.Lerp(trunkBaseRadius, trunkTopRadius, t));
        }
        AddTube(trunkPath, trunkRadii, trunkSides, ref verts, ref norms, ref uvs, ref trisWood);

        // ------- choose branch anchors on trunk -------
        var anchorIndices = new List<int>();
        int iStart = Mathf.CeilToInt(branchStart * trunkSegments);
        int iEnd = Mathf.FloorToInt(branchEnd * trunkSegments);
        iStart = Mathf.Clamp(iStart, 1, trunkSegments - 2);
        iEnd = Mathf.Clamp(iEnd, iStart + 1, trunkSegments - 1);

        for (int b = 0; b < branchCount; b++)
        {
            int idx = rng.Next(iStart, iEnd + 1);
            anchorIndices.Add(idx);
        }

        // ------- build branches -------
        foreach (int idx in anchorIndices)
        {
            // base data at trunk
            Vector3 basePos = trunkPath[idx];
            Vector3 nextPos = trunkPath[Mathf.Min(idx + 1, trunkPath.Count - 1)];
            Vector3 prevPos = trunkPath[Mathf.Max(idx - 1, 0)];
            Vector3 trunkTangent = (nextPos - prevPos).normalized;

            // random azimuth around trunk
            float az = (float)(rng.NextDouble() * Mathf.PI * 2f);
            Vector3 right, binorm;
            OrthonormalBasis(trunkTangent, out right, out binorm);
            Vector3 outward = (Mathf.Cos(az) * right + Mathf.Sin(az) * binorm).normalized;

            // branch parameters
            float len = Mathf.Lerp(branchLengthRange.x, branchLengthRange.y, (float)rng.NextDouble());
            float baseRadius = Mathf.Lerp(trunkBaseRadius, trunkTopRadius, (float)idx / trunkSegments) * branchRadiusScale;
            int segs = branchSegments;

            // branch path (slight upward curve)
            var path = new List<Vector3>(segs + 1);
            for (int i = 0; i <= segs; i++)
            {
                float t = (float)i / segs;
                Vector3 p = basePos
                          + outward * (len * t)
                          + Vector3.up * (branchCurve * len * t * t); // curve up
                path.Add(p);
            }

            // radii taper
            var radii = new List<float>(segs + 1);
            for (int i = 0; i <= segs; i++)
            {
                float t = (float)i / segs;
                radii.Add(Mathf.Lerp(baseRadius, baseRadius * 0.2f, t));
            }

            AddTube(path, radii, branchSides, ref verts, ref norms, ref uvs, ref trisWood);

            // leaves: blobs near the branch tip (a couple per branch)
            Vector3 tip = path[path.Count - 1];
            for (int k = 0; k < Mathf.Max(1, leavesPerBranch); k++)
            {
                float s = Mathf.Lerp(leafBlobScaleJitter.x, leafBlobScaleJitter.y, (float)rng.NextDouble());
                Vector3 jitter = outward * (0.15f * k) + RandomInUnitSphere() * 0.2f;
                AddLeafBlob(tip + jitter, leafBlobRadius * s, leafBlobSubdiv, ref verts, ref norms, ref uvs, ref trisLeaves);
            }
        }

        // ------- finalize mesh -------
        mesh.subMeshCount = 2;
        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(trisWood, 0, true);
        mesh.SetTriangles(trisLeaves, 1, true);
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();

        // collider for whole tree
        if (!TryGetComponent(out meshCollider))
            meshCollider = gameObject.AddComponent<MeshCollider>();
        meshCollider.sharedMesh = null;
        meshCollider.sharedMesh = mesh;

        // make sure renderer has two materials assigned
        var mr = GetComponent<MeshRenderer>();
        if (mr.sharedMaterials == null || mr.sharedMaterials.Length < 2)
        {
            var wood = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            wood.color = new Color(0.45f, 0.3f, 0.2f, 1f);
            var leaves = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            leaves.color = new Color(0.36f, 0.65f, 0.34f, 1f);
            mr.sharedMaterials = new[] { wood, leaves };
        }
    }

    // ========= helpers =========

    static void OrthonormalBasis(Vector3 t, out Vector3 right, out Vector3 binorm)
    {
        t = t.normalized;
        Vector3 up = Mathf.Abs(t.y) < 0.99f ? Vector3.up : Vector3.right;
        right = Vector3.Normalize(Vector3.Cross(up, t));
        binorm = Vector3.Normalize(Vector3.Cross(t, right));
    }

    static Vector3 RandomInUnitSphere()
    {
        // simple deterministic-ish fallback (no UnityEngine.Random here)
        float u = Random.value * 2f - 1f;
        float theta = Random.value * Mathf.PI * 2f;
        float r = Mathf.Sqrt(1 - u * u);
        return new Vector3(r * Mathf.Cos(theta), u, r * Mathf.Sin(theta)) * 0.5f;
    }

    // Add a tapered tube along a polyline path
    static void AddTube(List<Vector3> path, List<float> radii, int sides,
                        ref List<Vector3> verts, ref List<Vector3> norms, ref List<Vector2> uvs, ref List<int> tris)
    {
        if (path.Count != radii.Count) { Debug.LogError("Path and radii length mismatch"); return; }
        int ringStart = verts.Count;

        Vector3 prevRight = Vector3.right, prevBinorm = Vector3.forward;

        for (int i = 0; i < path.Count; i++)
        {
            Vector3 p = path[i];
            Vector3 t = Vector3.zero;
            if (i == 0) t = (path[i + 1] - path[i]).normalized;
            else if (i == path.Count - 1) t = (path[i] - path[i - 1]).normalized;
            else t = (path[i + 1] - path[i - 1]).normalized;

            Vector3 right, binorm;
            OrthonormalBasis(t, out right, out binorm);

            // simple frame continuity: nudge towards previous
            if (i > 0)
            {
                right = Vector3.Slerp(prevRight, right, 0.5f).normalized;
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
                norms.Add(dir); // radial normal
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
        // Build a cube-sphere (like rocks) with low subdivisions
        var localVerts = new List<Vector3>();
        var localTris = new List<int>();
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
        Vector3[] faceNormals = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
        Vector3[] faceTangents = { Vector3.forward, Vector3.forward, Vector3.right, Vector3.right, Vector3.right, Vector3.right };

        for (int f = 0; f < 6; f++)
        {
            Vector3 n = faceNormals[f], t = faceTangents[f], b = Vector3.Cross(n, t);
            int startIndex = verts.Count;

            for (int y = 0; y <= sub; y++)
            {
                for (int x = 0; x <= sub; x++)
                {
                    float u = (float)x / sub, v = (float)y / sub;
                    Vector2 uv = new Vector2(u * 2f - 1f, v * 2f - 1f);
                    Vector3 p = (n + uv.x * t + uv.y * b).normalized * radius;
                    verts.Add(p);
                }
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

    void OnDrawGizmos()
    {
        if (!drawDebug) return;
        var mf = GetComponent<MeshFilter>();
        if (!mf || !mf.sharedMesh) return;
        Gizmos.color = new Color(0.1f, 0.5f, 0.1f, 0.2f);
        Gizmos.DrawWireCube(transform.position + Vector3.up * trunkHeight * 0.5f, new Vector3(2f, trunkHeight, 2f));
    }
}
