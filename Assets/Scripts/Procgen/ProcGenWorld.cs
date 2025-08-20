using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
[DisallowMultipleComponent]
public class ProcgenWorld : MonoBehaviour
{
    [Header("What to generate")]
    public bool makeTerrain = true;
    public bool makeTrees   = true;
    public bool makeRocks   = true;
    public bool makeGrass   = true;

    [Header("World seed (propagated to children with offsets)")]
    public int seed = 1337;

    [Header("High-level counts (overrides children)")]
    public int trees  = 100;
    public int rocks  = 200;
    public int grass  = 1000;

    [Header("Collider LOD radius (m) for trees/rocks near origin")]
    public float colliderRadius = 28f;

    [Header("Auto-run in Edit Mode on changes")]
    public bool autorunInEditor = true;

    [Header("References (auto-created under this GameObject)")]
    public ProceduralTerrain terrain;                 // child "GeneratedTerrain"
    public InstancedTreeField treeField;              // child "TreeField"
    public InstancedRockField_Terrain rockField;      // child "RockField"
    public GrassField grassField;                     // child "GrassField"

#if UNITY_EDITOR
    bool _queued;
#endif

    void Start()
    {
        // Safe in play mode
        RebuildWorld();
    }

    void OnValidate()
    {
#if UNITY_EDITOR
        if (!autorunInEditor || Application.isPlaying) return;
        if (_queued) return;
        _queued = true;
        EditorApplication.delayCall += () =>
        {
            if (this == null) return;
            _queued = false;
            RebuildWorld();
        };
#endif
    }

    [ContextMenu("Rebuild World Now")]
    public void RebuildWorld()
    {
        EnsureChildren();

        // --- TERRAIN ---
        if (makeTerrain && terrain)
        {
            // Propagate seed
            terrain.seed = seed;
            terrain.Rebuild();
        }

        // Terrain bounds for spawners
        Vector3 tPos = terrain ? terrain.terrain.transform.position : transform.position;
        Vector3 tSize = terrain ? terrain.terrain.terrainData.size : new Vector3(200, 25, 200);
        Vector3 center = tPos + new Vector3(tSize.x * 0.5f, 0f, tSize.z * 0.5f);
        Vector2 areaXZ = new Vector2(tSize.x, tSize.z);

        // --- TREES ---
        if (makeTrees && treeField)
        {
            treeField.seed = seed + 11;
            treeField.transform.SetPositionAndRotation(center, Quaternion.identity);
            treeField.areaSize = areaXZ;
            treeField.colliderRadius = colliderRadius;
            treeField.terrainSource = terrain;
            treeField.snapToGround = true;
            treeField.groundMask = ~0;
            treeField.totalTrees = trees;
            treeField.Rebuild();
            ForceInstancing(treeField.woodMaterial, treeField.leafMaterial);
        }

        // --- ROCKS ---
        if (makeRocks && rockField)
        {
            rockField.seed = seed + 22;
            rockField.transform.SetPositionAndRotation(center, Quaternion.identity);
            rockField.areaSize = areaXZ;
            rockField.colliderRadius = colliderRadius;
            rockField.terrainSource = terrain;
            rockField.snapToGround = true;
            rockField.groundMask = ~0;
            rockField.totalRocks = rocks;
            rockField.Rebuild();
            ForceInstancing(rockField.rockMaterial);
        }

        // --- GRASS ---
        if (makeGrass && grassField)
        {
            grassField.seed = seed + 33;
            grassField.transform.SetPositionAndRotation(center, Quaternion.identity);
            grassField.areaSize = areaXZ;
            grassField.terrainSource = terrain;
            grassField.bladeCount = grass;
            grassField.Build();
            ForceInstancing(grassField.grassMaterial);
        }
    }

    void EnsureChildren()
    {
        // Terrain root (child created by ProceduralTerrain itself)
        if (!terrain)
        {
            var tObj = transform.Find("ProcTerrain")?.GetComponent<ProceduralTerrain>();
            if (!tObj)
            {
                var go = new GameObject("ProcTerrain");
                go.transform.SetParent(transform, false);
                terrain = go.AddComponent<ProceduralTerrain>();
            }
            else terrain = tObj;
        }

        // TreeField
        if (!treeField && makeTrees)
        {
            var tf = transform.Find("TreeField")?.GetComponent<InstancedTreeField>();
            if (!tf)
            {
                var go = new GameObject("TreeField");
                go.transform.SetParent(transform, false);
                treeField = go.AddComponent<InstancedTreeField>();
            }
            else treeField = tf;
        }

        // RockField
        if (!rockField && makeRocks)
        {
            var rf = transform.Find("RockField")?.GetComponent<InstancedRockField_Terrain>();
            if (!rf)
            {
                var go = new GameObject("RockField");
                go.transform.SetParent(transform, false);
                rockField = go.AddComponent<InstancedRockField_Terrain>();
            }
            else rockField = rf;
        }

        // GrassField
        if (!grassField && makeGrass)
        {
            var gf = transform.Find("GrassField")?.GetComponent<GrassField>();
            if (!gf)
            {
                var go = new GameObject("GrassField");
                go.transform.SetParent(transform, false);
                grassField = go.AddComponent<GrassField>();
            }
            else grassField = gf;
        }
    }

    static void ForceInstancing(params Material[] mats)
    {
        if (mats == null) return;
        foreach (var m in mats)
            if (m) m.enableInstancing = true;
    }
}
