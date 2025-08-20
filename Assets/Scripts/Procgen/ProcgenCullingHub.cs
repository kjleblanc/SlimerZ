// Assets/Scripts/Procgen/ProcgenCullingHub.cs
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine.Rendering;
#endif

/// <summary>
/// One-stop renderer for all instanced procgen batches.
/// - Discovers spawners (ChildrenOfThis or WholeScene) that implement IProcgenInstancedSource
/// - Pulls batches (mesh/material/matrices/bounds) and draws them once per camera
/// - CPU frustum culling + optional facing and distance caps (global + per-tag)
/// - Works in Edit and Play modes; debug gizmos match runtime culling
/// </summary>

[ExecuteAlways]
[DisallowMultipleComponent]
public class ProcgenCullingHub : MonoBehaviour
{
    // -------- Public batch API --------
    public enum BatchTag { Generic, TreeWood, TreeLeaves, Grass, Rocks }

    public struct Batch
    {
        public Mesh mesh;
        public int submeshIndex;
        public Material material;
        public Matrix4x4[] matrices;  // ≤1023 per array
        public Bounds bounds;         // our culling/debug bounds
        public int layer;
        public UnityEngine.Rendering.ShadowCastingMode shadowCasting;
        public bool receiveShadows;
        public BatchTag tag;
    }

    // -------- Discovery --------
    public enum DiscoveryMode { ChildrenOfThis, WholeScene }

    [Header("Discovery")]
    public DiscoveryMode discovery = DiscoveryMode.ChildrenOfThis;
    public bool includeInactive = true;

    // -------- Cameras --------
    [Header("Camera")]
    public Camera targetCameraOverride = null;
    public bool preferMainCameraInEditMode = true;

    // -------- Culling --------
    [Header("Culling (global)")]
    public bool enableCulling = true;
    [Tooltip("Global max draw distance in meters (−1 = unlimited).")]
    public float maxViewDistance = 300f;

    [Tooltip("Enable facing cull (compare camera forward to batch center).")]
    public bool enableFacingCulling = false;
    [Range(-1f, 1f)] public float facingDotMin = 0.0f; // used only if enableFacingCulling=true

    [Header("Per-tag max distance (m)  (−1 = unlimited)")]
    public float maxDistTreeLeaves = 80f;
    public float maxDistGrass     = 120f;

    [Header("Shadows")]
    [Tooltip("Apply facing culling during shadow rendering too (usually OFF to avoid shadow popping).")]
    public bool applyFacingInShadows = false;
    [Tooltip("Apply per-tag distance caps during shadow rendering too (usually OFF to keep stable shadows).")]
    public bool applyDistanceCapsInShadows = false;

    // -------- Draw / Debug --------
    [Header("Draw")]
    public bool drawInEditAndPlay = true;

    [Header("Debug")]
    public bool debugDrawBounds = false;
    public Color debugVisible = new Color(0f, 1f, 0f, 0.25f);
    public Color debugCulled  = new Color(1f, 0f, 0f, 0.15f);

    // -------- Internals --------
    readonly List<IProcgenInstancedSource> _sources = new();
    readonly List<Batch> _batches = new();

    void OnEnable()
    {
#if UNITY_EDITOR
        RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering_SRP;
#endif
        Camera.onPreCull += OnPreCull_BuiltIn;
        RefreshAll();
    }

    void OnDisable()
    {
#if UNITY_EDITOR
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering_SRP;
#endif
        Camera.onPreCull -= OnPreCull_BuiltIn;
        _sources.Clear();
        _batches.Clear();
    }

    public void NotifyDirty() => RefreshAll();

    public void RefreshAll()
    {
        // 1) discover
        _sources.Clear();
        if (discovery == DiscoveryMode.ChildrenOfThis)
        {
            var monos = GetComponentsInChildren<MonoBehaviour>(includeInactive);
            for (int i = 0; i < monos.Length; i++)
                if (monos[i] is IProcgenInstancedSource s) _sources.Add(s);
        }
        else
        {
#if UNITY_2022_2_OR_NEWER
            var monos = FindObjectsByType<MonoBehaviour>(
                includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
#else
            var monos = Resources.FindObjectsOfTypeAll<MonoBehaviour>();
#endif
            for (int i = 0; i < monos.Length; i++)
                if (monos[i] is IProcgenInstancedSource s)
                    if (includeInactive || (monos[i].isActiveAndEnabled && monos[i].gameObject.activeInHierarchy))
                        _sources.Add(s);
        }

        // 2) pull batches
        _batches.Clear();
        for (int i = 0; i < _sources.Count; i++)
        {
            var list = _sources[i].GetInstancedBatches();
            if (list == null) continue;
            for (int j = 0; j < list.Count; j++)
            {
                var b = list[j];
                if (!b.mesh || !b.material || b.matrices == null || b.matrices.Length == 0) continue;
                _batches.Add(b);
            }
        }
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
        if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null) return; // SRP active
#endif
        if (!ShouldDrawFor(cam)) return;
        DrawForCamera(cam);
    }

    bool ShouldDrawFor(Camera cam)
    {
        if (!drawInEditAndPlay || cam == null) return false;

        // explicit override wins
        if (targetCameraOverride && cam != targetCameraOverride) return false;

#if UNITY_EDITOR
        // edit mode sanity: prefer MainCamera if requested
        if (!Application.isPlaying && preferMainCameraInEditMode && Camera.main && cam != Camera.main)
            return false;
#endif
        return true;
    }

    // ------- Core draw (CPU frustum; no culling-group) -------
    void DrawForCamera(Camera cam)
    {
        if (_batches.Count == 0) return;

        bool isShadow = IsShadowLike(cam);
        var planes = enableCulling ? GeometryUtility.CalculateFrustumPlanes(cam) : null;
        Vector3 camPos = cam.transform.position, camFwd = cam.transform.forward;

        for (int i = 0; i < _batches.Count; i++)
        {
            var b = _batches[i];

            if (enableCulling)
            {
                // 1) Frustum
                if (planes != null && !GeometryUtility.TestPlanesAABB(planes, b.bounds))
                    continue;

                // 2) Distance (global + per-tag; usually skipped for shadow cams)
                float cap = EffectiveDistanceCap(b.tag);
                bool applyDist = !isShadow || applyDistanceCapsInShadows;
                if (applyDist && cap > 0f)
                {
                    var to = b.bounds.center - camPos;
                    if (to.sqrMagnitude > cap * cap) continue;
                }

                // 3) Facing (usually skipped for shadow cams)
                bool applyFacing = enableFacingCulling && (!isShadow || applyFacingInShadows);
                if (applyFacing)
                {
                    var to = b.bounds.center - camPos;
                    var dir = to.sqrMagnitude > 1e-6f ? to.normalized : Vector3.forward;
                    if (Vector3.Dot(camFwd, dir) < facingDotMin) continue;
                }
            }

            Graphics.DrawMeshInstanced(
                b.mesh, b.submeshIndex, b.material, b.matrices, b.matrices.Length, null,
                b.shadowCasting, b.receiveShadows, b.layer);
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

        bool hasGlobal = maxViewDistance > 0f;
        bool hasTag    = tagCap > 0f;

        if (hasGlobal && hasTag) return Mathf.Min(maxViewDistance, tagCap);
        if (hasGlobal) return maxViewDistance;
        if (hasTag)    return tagCap;
        return -1f;
    }

    static bool IsShadowLike(Camera cam)
    {
        if (!cam) return false;
        // Heuristic: URP/BIRP shadow cams typically contain "Shadow" in the name.
        var n = cam.name;
        return !string.IsNullOrEmpty(n) && n.IndexOf("shadow", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    // ------- Debug gizmos (mirrors runtime rules) -------
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
                if (planes != null && !GeometryUtility.TestPlanesAABB(planes, b.bounds))
                    vis = false;

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
                        var to = (b.bounds.center - camPos);
                        var dir = to.sqrMagnitude > 1e-6f ? to.normalized : Vector3.forward;
                        if (Vector3.Dot(camFwd, dir) < facingDotMin) vis = false;
                    }
                }
            }

            Gizmos.color = vis ? debugVisible : debugCulled;
            Gizmos.DrawCube(b.bounds.center, b.bounds.size);
            Gizmos.color = Color.black;
            Gizmos.DrawWireCube(b.bounds.center, b.bounds.size);
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
