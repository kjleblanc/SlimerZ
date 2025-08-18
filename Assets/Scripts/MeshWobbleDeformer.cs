using UnityEngine;

[RequireComponent(typeof(MeshFilter))]
public class MeshWobbleDeformer : MonoBehaviour
{
    [Header("Surface Wobble (Local, Visual Only)")]
    [Tooltip("Base surface displacement amount (meters). Higher = jellier skin.")]
    [Min(0f)] public float amplitude = 0.06f;
    [Tooltip("Spatial scale of the noise (bigger = larger ripples).")]
    [Min(0.01f)] public float noiseScale = 1.6f;
    [Tooltip("How fast the ripples move (1/s).")]
    [Min(0f)] public float noiseSpeed = 0.8f;
    [Tooltip("0 = slide vertices tangentially, 1 = push along normals (puffier).")]
    [Range(0f, 1f)] public float normalPush = 0.6f;
    [Tooltip("Velocity damping of the vertex spring. Higher = settles faster.")]
    [Min(0f)] public float damping = 8f;
    [Tooltip("Center resists deformation (0 = even, 1 = center rigid). Good for blobs.")]
    [Range(0f,1f)] public float centerStiffness = 0.6f;

    [HideInInspector] public float ExternalWobble; // from SlimePlayer (oscillator + impulses)
    [HideInInspector] public float SpeedFactor;    // 0..1 (HorizontalSpeed / moveSpeed)

    Mesh deformMesh;
    Vector3[] baseVerts, workVerts, baseNormals, velocities;
    float time;

    void Awake()
    {
        var mf = GetComponent<MeshFilter>();
        deformMesh = Instantiate(mf.sharedMesh);
        deformMesh.name = mf.sharedMesh.name + " (Deformed)";
        mf.sharedMesh = deformMesh;

        baseVerts = deformMesh.vertices;
        baseNormals = deformMesh.normals;
        workVerts = new Vector3[baseVerts.Length];
        velocities = new Vector3[baseVerts.Length];
        System.Array.Copy(baseVerts, workVerts, baseVerts.Length);
    }

    void OnDisable()
    {
        if (deformMesh)
        {
            deformMesh.vertices = baseVerts;
            deformMesh.RecalculateBounds();
        }
    }

    void LateUpdate()
    {
        time += Time.deltaTime * noiseSpeed;

        // Slightly scale amplitude by motion + injected wobble (landing/dash)
        float amp = amplitude * (1f + SpeedFactor * 0.8f) + Mathf.Abs(ExternalWobble) * 0.5f;

        for (int i = 0; i < workVerts.Length; i++)
        {
            Vector3 basePos = baseVerts[i];
            float centerWeight = Mathf.Clamp01(basePos.magnitude); // ~1 near shell
            float w = Mathf.Lerp(1f, 1f - centerStiffness, 1f - centerWeight);

            Vector3 bn = baseNormals[i];
            float n = Perlin3D(basePos * noiseScale + new Vector3(0f, time, 0f));
            float offset = (n - 0.5f) * 2f * amp * w;

            Vector3 target = basePos + bn * (offset * normalPush);

            velocities[i] += (target - workVerts[i]) * (10f * Time.deltaTime);
            velocities[i] *= Mathf.Exp(-damping * Time.deltaTime);
            workVerts[i] += velocities[i];
        }

        deformMesh.vertices = workVerts;
        deformMesh.RecalculateBounds();
        // deformMesh.RecalculateNormals(); // enable if you push normals a lot
    }

    // Cheap 3D Perlin from 2D samples
    static float Perlin3D(Vector3 p)
    {
        float ab = Mathf.PerlinNoise(p.x, p.y);
        float bc = Mathf.PerlinNoise(p.y, p.z);
        float ca = Mathf.PerlinNoise(p.z, p.x);
        return (ab + bc + ca) / 3f;
    }
}
