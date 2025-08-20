using UnityEngine;
using System.Collections.Generic;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

[ExecuteAlways]
[DisallowMultipleComponent]
public class ProcgenCullingHub : MonoBehaviour
{
    [System.Serializable]
    public struct Batch
    {
    public Mesh mesh;
    public int submeshIndex; // NEW
    public Material material;
    public Matrix4x4[] matrices;
    public Bounds bounds;
    public int layer;
    public UnityEngine.Rendering.ShadowCastingMode shadowCasting;
    public bool receiveShadows;
    }

    [Header("Culling")]
    public bool enableCulling = true;
    [Range(-1f, 1f)] public float facingDotMin = 0.0f;
    public float maxViewDistance = 300f;

    [Header("Draw")]
    public bool drawInEditAndPlay = true;

    Camera _boundCamera;
    CullingGroup _group;
    BoundingSphere[] _spheres = System.Array.Empty<BoundingSphere>();

    readonly List<IProcgenInstancedSource> _sources = new();
    readonly List<Batch> _batches = new();

    void OnEnable() { BindToBestCamera(); RefreshAll(); }
    void OnDisable() { ReleaseGroup(); _sources.Clear(); _batches.Clear(); _spheres = System.Array.Empty<BoundingSphere>(); }
    void Update()
    {
        if (!drawInEditAndPlay) return;
        if (!_boundCamera || (Application.isPlaying && _boundCamera != Camera.main)) BindToBestCamera();
    }

    public void NotifyDirty() => RefreshAll();

    public void RefreshAll()
    {
        _sources.Clear();
        GetComponentsInChildren(true, _sources);

        _batches.Clear();
        foreach (var src in _sources)
        {
            var list = src.GetInstancedBatches();
            if (list == null) continue;
            foreach (var b in list)
                if (b.mesh && b.material && b.matrices != null && b.matrices.Length > 0)
                    _batches.Add(b);
        }

        if (_batches.Count == 0) { _spheres = System.Array.Empty<BoundingSphere>(); RebindGroup(); return; }

        if (_spheres.Length != _batches.Count)
            _spheres = new BoundingSphere[_batches.Count];

        for (int i = 0; i < _batches.Count; i++)
        {
            var b = _batches[i];
            float r = b.bounds.extents.magnitude;
            _spheres[i] = new BoundingSphere(b.bounds.center, r);
        }
        RebindGroup();
    }

    void OnRenderObject()
    {
        if (!drawInEditAndPlay || _batches.Count == 0) return;

        var cam = Camera.current; if (!cam) return;
        if (cam != _boundCamera) { BindToCamera(cam); RebindGroup(); }

        Vector3 camPos = cam.transform.position, camFwd = cam.transform.forward;
        float maxDistSqr = (maxViewDistance > 0f) ? maxViewDistance * maxViewDistance : float.PositiveInfinity;

        for (int i = 0; i < _batches.Count; i++)
        {
            var b = _batches[i];
            if (enableCulling && _group != null)
            {
                if (!_group.IsVisible(i)) continue;
                if (maxDistSqr < float.PositiveInfinity)
                {
                    var to = b.bounds.center - camPos;
                    if (to.sqrMagnitude > maxDistSqr) continue;
                    if (facingDotMin > -0.999f && Vector3.Dot(camFwd, to.normalized) < facingDotMin) continue;
                }
            }

            Graphics.DrawMeshInstanced(
                b.mesh, b.submeshIndex, b.material, b.matrices, b.matrices.Length, null,
                b.shadowCasting, b.receiveShadows, b.layer);
        }
    }

    void BindToBestCamera()
    {
#if UNITY_EDITOR
        var sceneCam = SceneView.lastActiveSceneView ? SceneView.lastActiveSceneView.camera : null;
        var target = Application.isPlaying ? Camera.main : (sceneCam ? sceneCam : Camera.main);
#else
        var target = Camera.main;
#endif
        BindToCamera(target);
        RebindGroup();
    }

    void BindToCamera(Camera cam)
    {
        _boundCamera = cam;
        if (_group == null) _group = new CullingGroup();
        _group.targetCamera = _boundCamera;
        _group.onStateChanged = null;
    }

    void RebindGroup()
    {
        if (_group == null) return;
        _group.SetBoundingSpheres(_spheres);
        _group.SetBoundingSphereCount(_spheres.Length);
        if (maxViewDistance > 0f) _group.SetBoundingDistances(new float[] { maxViewDistance });
        else _group.SetBoundingDistances(System.Array.Empty<float>());
    }

    void ReleaseGroup() { if (_group != null) { _group.Dispose(); _group = null; } }
}
