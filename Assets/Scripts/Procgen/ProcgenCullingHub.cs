// ... existing usings ...
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine.Rendering;
#endif

[ExecuteAlways]
[DisallowMultipleComponent]
public class ProcgenCullingHub : MonoBehaviour
{
    // ---------- public API for spawners stays the same ----------
    public enum BatchTag { Generic, TreeWood, TreeLeaves, Grass, Rocks }

    public struct Batch
    {
        public Mesh mesh;
        public int submeshIndex;
        public Material material;
        public Matrix4x4[] matrices;
        public Bounds bounds;
        public int layer;
        public UnityEngine.Rendering.ShadowCastingMode shadowCasting;
        public bool receiveShadows;
        public BatchTag tag;
    }

    // ---------- NEW: lightweight frame stats ----------
    public struct FrameStats
    {
        public int frame;
        public string cameraName;
        public int totalBatches;
        public int totalInstances;
        public int visibleBatches;
        public int drawnInstances;
        public int drawCalls;
        public int culledByFrustum;
        public int culledByDistance;
        public int culledByFacing;
        public Dictionary<BatchTag,int> visibleByTag;
    }
    FrameStats _stats;
    int _statsAccumFrame = -1;

    // ---------- discovery / camera / culling (unchanged options) ----------
    public enum DiscoveryMode { ChildrenOfThis, WholeScene }

    [Header("Discovery")]
    public DiscoveryMode discovery = DiscoveryMode.ChildrenOfThis;
    public bool includeInactive = true;

    [Header("Camera")]
    public Camera targetCameraOverride = null;
    public bool preferMainCameraInEditMode = true;

    [Header("Culling (global)")]
    public bool enableCulling = true;
    public float maxViewDistance = 300f;

    [Header("Facing Cull")]
    public bool enableFacingCulling = false;
    [Range(-1f,1f)] public float facingDotMin = 0.0f;

    [Header("Per-tag max distance (m)  (−1 = unlimited)")]
    public float maxDistTreeLeaves = 80f;
    public float maxDistGrass     = 120f;

    [Header("Shadows")]
    public bool applyFacingInShadows = false;
    public bool applyDistanceCapsInShadows = false;

    [Header("Draw")]
    public bool drawInEditAndPlay = true;

    [Header("Debug")]
    public bool debugDrawBounds = false;
    public Color debugVisible = new Color(0,1,0,0.25f);
    public Color debugCulled  = new Color(1,0,0,0.15f);

    // internals
    readonly List<IProcgenInstancedSource> _sources = new();
    readonly List<Batch> _batches = new();

#if UNITY_EDITOR
    void OnEnable(){ RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering_SRP; Camera.onPreCull += OnPreCull_BuiltIn; RefreshAll(); }
    void OnDisable(){ RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering_SRP; Camera.onPreCull -= OnPreCull_BuiltIn; _sources.Clear(); _batches.Clear(); }
#else
    void OnEnable(){ Camera.onPreCull += OnPreCull_BuiltIn; RefreshAll(); }
    void OnDisable(){ Camera.onPreCull -= OnPreCull_BuiltIn; _sources.Clear(); _batches.Clear(); }
#endif

    public void NotifyDirty() => RefreshAll();

    public void RefreshAll()
    {
        _sources.Clear();
        if (discovery == DiscoveryMode.ChildrenOfThis)
        {
            var monos = GetComponentsInChildren<MonoBehaviour>(includeInactive);
            for (int i = 0; i < monos.Length; i++) if (monos[i] is IProcgenInstancedSource s) _sources.Add(s);
        }
        else
        {
#if UNITY_2022_2_OR_NEWER
            var monos = FindObjectsByType<MonoBehaviour>(includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#else
            var monos = Resources.FindObjectsOfTypeAll<MonoBehaviour>();
#endif
            for (int i = 0; i < monos.Length; i++)
                if (monos[i] is IProcgenInstancedSource s)
                    if (includeInactive || (monos[i].isActiveAndEnabled && monos[i].gameObject.activeInHierarchy))
                        _sources.Add(s);
        }

        _batches.Clear();
        int totalInst = 0;
        for (int i = 0; i < _sources.Count; i++)
        {
            var list = _sources[i].GetInstancedBatches();
            if (list == null) continue;
            for (int j = 0; j < list.Count; j++)
            {
                var b = list[j];
                if (!b.mesh || !b.material || b.matrices == null || b.matrices.Length == 0) continue;
                _batches.Add(b);
                totalInst += b.matrices.Length;
            }
        }

        // reset stats snapshot (will fill on next draw)
        _stats = new FrameStats {
            frame = -1, cameraName = "",
            totalBatches = _batches.Count, totalInstances = totalInst,
            visibleByTag = new Dictionary<BatchTag,int>()
        };
        _statsAccumFrame = -1;
    }

#if UNITY_EDITOR
    void OnBeginCameraRendering_SRP(UnityEngine.Rendering.ScriptableRenderContext ctx, Camera cam)
    {
        if (!Application.isPlaying) { if (!ShouldDrawFor(cam)) return; }
        DrawForCamera(cam);
    }
#endif

    void OnPreCull_BuiltIn(Camera cam)
    {
#if UNITY_EDITOR
        if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null) return;
#endif
        if (!ShouldDrawFor(cam)) return;
        DrawForCamera(cam);
    }

    bool ShouldDrawFor(Camera cam)
    {
        if (!drawInEditAndPlay || !cam) return false;
        if (targetCameraOverride && cam != targetCameraOverride) return false;
#if UNITY_EDITOR
        if (!Application.isPlaying && preferMainCameraInEditMode && Camera.main && cam != Camera.main) return false;
#endif
        return true;
    }

    void DrawForCamera(Camera cam)
    {
        if (_batches.Count == 0) return;

        bool isShadow = IsShadowLike(cam);
        var planes = enableCulling ? GeometryUtility.CalculateFrustumPlanes(cam) : null;
        Vector3 camPos = cam.transform.position, camFwd = cam.transform.forward;

        // NEW: (re)start accumulation once per frame per hub
        if (_statsAccumFrame != Time.frameCount)
        {
            _stats = new FrameStats {
                frame = Time.frameCount,
                cameraName = cam ? cam.name : "",
                totalBatches = _batches.Count,
                totalInstances = 0,
                visibleBatches = 0, drawnInstances = 0, drawCalls = 0,
                culledByFrustum = 0, culledByDistance = 0, culledByFacing = 0,
                visibleByTag = new Dictionary<BatchTag,int>()
            };
            for (int i = 0; i < _batches.Count; i++) _stats.totalInstances += _batches[i].matrices.Length;
            _statsAccumFrame = Time.frameCount;
        }

        for (int i = 0; i < _batches.Count; i++)
        {
            var b = _batches[i];
            bool culled = false;

            if (enableCulling)
            {
                if (planes != null && !GeometryUtility.TestPlanesAABB(planes, b.bounds))
                { _stats.culledByFrustum++; culled = true; }
                else
                {
                    float cap = EffectiveDistanceCap(b.tag);
                    bool applyDist = !isShadow || applyDistanceCapsInShadows;
                    if (!culled && applyDist && cap > 0f)
                    {
                        var to = b.bounds.center - camPos;
                        if (to.sqrMagnitude > cap * cap)
                        { _stats.culledByDistance++; culled = true; }
                    }

                    bool applyFacing = enableFacingCulling && (!isShadow || applyFacingInShadows);
                    if (!culled && applyFacing)
                    {
                        var to = b.bounds.center - camPos;
                        var dir = to.sqrMagnitude > 1e-6f ? to.normalized : Vector3.forward;
                        if (Vector3.Dot(camFwd, dir) < facingDotMin)
                        { _stats.culledByFacing++; culled = true; }
                    }
                }
            }

            if (culled) continue;

            Graphics.DrawMeshInstanced(b.mesh, b.submeshIndex, b.material, b.matrices, b.matrices.Length, null,
                                       b.shadowCasting, b.receiveShadows, b.layer);

            _stats.visibleBatches++;
            _stats.drawCalls++;
            _stats.drawnInstances += b.matrices.Length;

            if (!_stats.visibleByTag.TryGetValue(b.tag, out var cnt)) cnt = 0;
            _stats.visibleByTag[b.tag] = cnt + 1;
        }
    }

    float EffectiveDistanceCap(BatchTag tag)
    {
        float tagCap = -1f;
        switch (tag)
        {
            case BatchTag.TreeLeaves: tagCap = maxDistTreeLeaves; break;
            case BatchTag.Grass:      tagCap = maxDistGrass;      break;
            default:                  tagCap = -1f;               break;
        }
        bool hasGlobal = maxViewDistance > 0f, hasTag = tagCap > 0f;
        if (hasGlobal && hasTag) return Mathf.Min(maxViewDistance, tagCap);
        if (hasGlobal) return maxViewDistance;
        if (hasTag)    return tagCap;
        return -1f;
    }

    static bool IsShadowLike(Camera cam)
    {
        if (!cam) return false;
        var n = cam.name;
        return !string.IsNullOrEmpty(n) && n.IndexOf("shadow", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    // ---------- NEW: expose latest stats to HUD ----------
    public FrameStats GetLatestStats() => _stats;

    // ---------- debug gizmos (unchanged logic) ----------
    void OnDrawGizmosSelected()
    {
        if (!debugDrawBounds || _batches == null || _batches.Count == 0) return;
        var cam = GetDebugCamera();
        bool isShadow = IsShadowLike(cam);
        Vector3 camPos = cam ? cam.transform.position : Vector3.zero;
        Vector3 camFwd = cam ? cam.transform.forward : Vector3.forward;
        var planes = (enableCulling && cam) ? GeometryUtility.CalculateFrustumPlanes(cam) : null;

        for (int i = 0; i < _batches.Count; i++)
        {
            var b = _batches[i];
            bool vis = true;

            if (enableCulling)
            {
                if (planes != null && !GeometryUtility.TestPlanesAABB(planes, b.bounds)) vis = false;

                if (vis)
                {
                    float cap = EffectiveDistanceCap(b.tag);
                    bool applyDist = !isShadow || applyDistanceCapsInShadows;
                    if (applyDist && cap > 0f && cam)
                    {
                        var to = b.bounds.center - camPos;
                        if (to.sqrMagnitude > cap * cap) vis = false;
                    }
                }
                if (vis)
                {
                    bool applyFacing = enableFacingCulling && (!isShadow || applyFacingInShadows) && cam;
                    if (applyFacing)
                    {
                        var to = b.bounds.center - camPos;
                        var dir = to.sqrMagnitude > 1e-6f ? to.normalized : Vector3.forward;
                        if (Vector3.Dot(camFwd, dir) < facingDotMin) vis = false;
                    }
                }
            }

            Gizmos.color = vis ? debugVisible : debugCulled;
            Gizmos.DrawCube(b.bounds.center, b.bounds.size);
            Gizmos.color = Color.black; Gizmos.DrawWireCube(b.bounds.center, b.bounds.size);
        }
    }

    Camera GetDebugCamera()
    {
        if (targetCameraOverride) return targetCameraOverride;
#if UNITY_EDITOR
        if (!Application.isPlaying && preferMainCameraInEditMode && Camera.main) return Camera.main;
        var sv = SceneView.lastActiveSceneView;
        if (!Application.isPlaying && sv && sv.camera) return sv.camera;
#endif
        return Camera.main;
    }
}

