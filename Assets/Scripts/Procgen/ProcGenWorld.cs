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
    public bool makeWater   = true;  // NEW
    public bool makeTrees   = true;
    public bool makeRocks   = true;
    public bool makeGrass   = true;

    [Header("World seed")]
    public int seed = 1337;

    [Header("Chunking")]
    public bool useChunkGrid = true;
    public Vector2Int gridDims = new Vector2Int(8, 8);

    [Header("Water Settings")]  // NEW
    public float waterLevel = 5f;
    public int minWaterBodySize = 10;

    [Header("Counts (high level)")]
    public int trees = 600;
    public int rocks = 1200;
    public int grass = 12000;

    [Header("Collider LOD radius (m)")]
    public float colliderRadius = 28f;

    [Header("Biome (rules)")]
    public BiomeProfile biome;

    [Header("Auto-run in Edit Mode")]
    public bool autorunInEditor = true;

    [Header("References (auto-created as children)")]
    public ProceduralTerrain terrain;
    public ProceduralWater water;                     // NEW
    public InstancedTreeField treeField;
    public InstancedRockField rockField;
    public GrassField grassField;
    public ProcgenCullingHub cullingHub;

#if UNITY_EDITOR
    bool _queued;
#endif

    void Start() => RebuildWorld();

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

        // Terrain
        if (makeTerrain && terrain)
        {
            terrain.seed = seed;
            terrain.Rebuild();
        }

        // Water (NEW - build after terrain)
        if (makeWater && water && terrain)
        {
            water.terrainSource = terrain;
            water.waterLevel = waterLevel;
            water.minWaterBodySize = minWaterBodySize;
            water.Rebuild();
        }

        // Terrain bounds → center & size
        Vector3 tPos  = terrain ? terrain.terrain.transform.position : transform.position;
        Vector3 tSize = terrain ? terrain.terrain.terrainData.size : new Vector3(200, 25, 200);
        Vector3 center = tPos + new Vector3(tSize.x * 0.5f, 0f, tSize.z * 0.5f);
        Vector2 areaXZ = new Vector2(tSize.x, tSize.z);
        var worldBounds = new Bounds(tPos + new Vector3(tSize.x/2f, tSize.y/2f, tSize.z/2f), tSize);

        ChunkGrid grid = null;
        if (useChunkGrid)
        {
            var origin = new Vector3(tPos.x, 0f, tPos.z);
            grid = new ChunkGrid(origin, new Vector2(tSize.x, tSize.z), gridDims, worldBounds);
        }

        // Build context
        var ctx = new WorldContext {
            Root = transform,
            Terrain = terrain ? terrain.terrain : null,
            Masks = terrain as IWorldMaskProvider,
            Biome = biome,
            Seed = seed,
            WorldBounds = worldBounds,
            Grid = grid  
        };

        // Trees
        if (makeTrees && treeField)
        {
            treeField.seed = seed + 11;
            treeField.transform.position = center;
            treeField.areaSize = areaXZ;
            treeField.colliderRadius = colliderRadius;
            treeField.terrainSource = terrain;
            treeField.totalTrees = Mathf.RoundToInt(trees * (biome ? biome.globalDensity : 1f));
            treeField.Configure(ctx);
            treeField.Rebuild();

            if (biome)
            {
                treeField.maxSlope01   = biome.treeMaxSlope01;
                treeField.moistureBias = biome.treeMoistureBias;
            }

            ForceInstancing(treeField.woodMaterial, treeField.leafMaterial);
        }

        // Rocks
        if (makeRocks && rockField)
        {
            rockField.seed = seed + 22;
            rockField.transform.position = center;
            rockField.areaSize = areaXZ;
            rockField.colliderRadius = colliderRadius;
            rockField.terrainSource = terrain;
            rockField.totalRocks = Mathf.RoundToInt(rocks * (biome ? biome.globalDensity : 1f));
            rockField.Configure(ctx);
            rockField.Rebuild();

            if (biome)
            {
                rockField.minSlope01  = biome.rockMinSlope01;
                rockField.drynessBias = biome.rockDrynessBias;
            }

            ForceInstancing(rockField.rockMaterial);
        }

        // Grass
        if (makeGrass && grassField)
        {
            grassField.seed = seed + 33;
            grassField.transform.position = center;
            grassField.areaSize = areaXZ;
            grassField.terrainSource = terrain;
            grassField.bladeCount = Mathf.RoundToInt(grass * (biome ? biome.globalDensity : 1f));
            grassField.Configure(ctx);
            
            if (biome)
            {
                grassField.maxSlope01    = biome.grassMaxSlope01;
                grassField.moistureBias  = biome.grassMoistureBias;
                grassField.minSpawnProb  = biome.grassMinSpawnProb;
            }
            grassField.Build();

            ForceInstancing(grassField.grassMaterial);
        }

        if (cullingHub) cullingHub.RefreshAll();
    }

    void EnsureChildren()
    {
        // Hub ON THIS OBJECT
        if (!cullingHub) cullingHub = GetComponent<ProcgenCullingHub>();
        if (!cullingHub) cullingHub = gameObject.AddComponent<ProcgenCullingHub>();

        // Terrain holder
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

        // Water (NEW)
        if (!water && makeWater)
        {
            var w = transform.Find("Water")?.GetComponent<ProceduralWater>();
            if (!w)
            {
                var go = new GameObject("Water");
                go.transform.SetParent(transform, false);
                water = go.AddComponent<ProceduralWater>();
            }
            else water = w;
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
            var rf = transform.Find("RockField")?.GetComponent<InstancedRockField>();
            if (!rf)
            {
                var go = new GameObject("RockField");
                go.transform.SetParent(transform, false);
                rockField = go.AddComponent<InstancedRockField>();
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
        foreach (var m in mats) if (m) m.enableInstancing = true;
    }
}

