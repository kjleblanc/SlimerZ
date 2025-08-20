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

    [Header("Debug")]
    public bool debugDrawBounds = false;
    public Color debugVisible = new Color(0, 1, 0, 0.25f);
    public Color debugCulled = new Color(1, 0, 0, 0.15f);

    [Header("Camera")]
    public Camera targetCameraOverride;                 // assign to force a camera
    public bool preferMainCameraInEditMode = true;      // use Main Camera even in edit mode

    public enum CullingMode { CullingGroup, CPUFrustum }
    
    [Header("Culling")]
    public CullingMode cullingMode = CullingMode.CullingGroup;  



    Camera _boundCamera;
    CullingGroup _group;
    BoundingSphere[] _spheres = System.Array.Empty<BoundingSphere>();

    readonly List<IProcgenInstancedSource> _sources = new();
    readonly List<Batch> _batches = new();

    Camera GetActiveCameraForDebug()
    {
        if (targetCameraOverride) return targetCameraOverride;
    #if UNITY_EDITOR
        if (!Application.isPlaying && preferMainCameraInEditMode && Camera.main) return Camera.main;
        var sv = UnityEditor.SceneView.lastActiveSceneView;
        if (!Application.isPlaying && sv && sv.camera) return sv.camera;
    #endif
        return _boundCamera ? _boundCamera : Camera.main;
    }


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

        Camera cam = targetCameraOverride ? targetCameraOverride : Camera.current;
        if (!cam) cam = _boundCamera ? _boundCamera : Camera.main;
        if (!cam) return;

        if (cullingMode == CullingMode.CullingGroup)
        {
            if (cam != _boundCamera) { BindToCamera(cam); RebindGroup(); }
            Vector3 camPos = cam.transform.position, camFwd = cam.transform.forward;
            float maxDistSqr = (maxViewDistance > 0f) ? maxViewDistance * maxViewDistance : float.PositiveInfinity;

            for (int i = 0; i < _batches.Count; i++)
            {
                var b = _batches[i];
                if (enableCulling && _group != null)
                {
                    if (!_group.IsVisible(i)) continue;
                    var to = b.bounds.center - camPos;
                    if (to.sqrMagnitude > maxDistSqr) continue;
                    if (facingDotMin > -0.999f && Vector3.Dot(camFwd, to.normalized) < facingDotMin) continue;
                }
                Graphics.DrawMeshInstanced(b.mesh, b.submeshIndex, b.material, b.matrices, b.matrices.Length, null,
                    b.shadowCasting, b.receiveShadows, b.layer);
            }
            return;
        }

        // CPU frustum mode (per-camera, matches what you see)
        Plane[] planes = GeometryUtility.CalculateFrustumPlanes(cam);
        Vector3 cPos = cam.transform.position, cFwd = cam.transform.forward;
        float max2 = (maxViewDistance > 0f) ? maxViewDistance * maxViewDistance : float.PositiveInfinity;

        for (int i = 0; i < _batches.Count; i++)
        {
            var b = _batches[i];
            bool vis = true;
            if (enableCulling)
            {
                if (!GeometryUtility.TestPlanesAABB(planes, b.bounds)) vis = false;
                if (vis && maxViewDistance > 0f)
                {
                    var to = b.bounds.center - cPos;
                    if (to.sqrMagnitude > max2) vis = false;
                    else if (facingDotMin > -0.999f && Vector3.Dot(cFwd, to.normalized) < facingDotMin) vis = false;
                }
            }
            if (!vis) continue;
            Graphics.DrawMeshInstanced(b.mesh, b.submeshIndex, b.material, b.matrices, b.matrices.Length, null,
                b.shadowCasting, b.receiveShadows, b.layer);
        }
    }


    void BindToBestCamera()
    {
        Camera target = targetCameraOverride;
    #if UNITY_EDITOR
        if (!Application.isPlaying && preferMainCameraInEditMode && target == null)
            target = Camera.main;
        if (!Application.isPlaying && target == null && UnityEditor.SceneView.lastActiveSceneView)
            target = UnityEditor.SceneView.lastActiveSceneView.camera;
    #endif
        if (target == null) target = Camera.main;
        BindToCamera(target);
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
    
    void OnDrawGizmosSelected()
    {
        if (!debugDrawBounds || _batches == null || _batches.Count == 0) return;

        var cam = GetActiveCameraForDebug();
        Vector3 camPos = cam ? cam.transform.position : Vector3.zero;
        Vector3 camFwd = cam ? cam.transform.forward : Vector3.forward;
        float maxDistSqr = (maxViewDistance > 0f) ? maxViewDistance * maxViewDistance : float.PositiveInfinity;

        Plane[] planes = (enableCulling && cam)
            ? GeometryUtility.CalculateFrustumPlanes(cam)
            : null;

        for (int i = 0; i < _batches.Count; i++)
        {
            var b = _batches[i];
            bool vis = true;

            if (enableCulling)
            {
                // 1) Frustum (fallback to true if no camera)
                if (planes != null && !GeometryUtility.TestPlanesAABB(planes, b.bounds))
                    vis = false;

                // 2) Distance
                if (vis && maxViewDistance > 0f)
                {
                    var to = b.bounds.center - camPos;
                    if (to.sqrMagnitude > maxDistSqr) vis = false;
                }

                // 3) Facing
                if (vis && facingDotMin > -0.999f && cam)
                {
                    var to = (b.bounds.center - camPos).normalized;
                    if (Vector3.Dot(camFwd, to) < facingDotMin) vis = false;
                }
            }

            Gizmos.color = vis ? debugVisible : debugCulled;
            Gizmos.DrawCube(b.bounds.center, b.bounds.size);
            Gizmos.color = Color.black; Gizmos.DrawWireCube(b.bounds.center, b.bounds.size);
        }
    }


}
