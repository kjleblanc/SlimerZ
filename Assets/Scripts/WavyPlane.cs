using UnityEngine;

[RequireComponent(typeof(MeshFilter))]
public class WavyPlane : MonoBehaviour
{
    [Tooltip("World-space noise scale (bigger = broader hills).")]
    public float noiseScale = 0.06f;
    [Tooltip("Vertical amplitude of the waves (meters).")]
    public float amplitude = 0.6f;
    [Tooltip("Random offset to change the pattern.")]
    public Vector2 noiseOffset = new Vector2(12.3f, 47.8f);

    Mesh _mesh;
    Vector3[] _base, _deformed;

    void Awake()
    {
        var mf = GetComponent<MeshFilter>();
        _mesh = Instantiate(mf.sharedMesh);
        _mesh.name = mf.sharedMesh.name + " (Wavy)";
        mf.sharedMesh = _mesh;
        _base = _mesh.vertices;
        _deformed = new Vector3[_base.Length];

        // Apply once (static field). If you want animated wind in the ground, move this to Update.
        for (int i = 0; i < _base.Length; i++)
        {
            var v = _base[i];
            Vector3 wpos = transform.TransformPoint(v);
            float n = Mathf.PerlinNoise(wpos.x * noiseScale + noiseOffset.x,
                                        wpos.z * noiseScale + noiseOffset.y);
            v.y += (n - 0.5f) * 2f * amplitude;
            _deformed[i] = v;
        }
        _mesh.vertices = _deformed;
        _mesh.RecalculateBounds();
        _mesh.RecalculateNormals();
    }
}
