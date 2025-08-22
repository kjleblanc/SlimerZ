using UnityEngine;
using System.Collections.Generic;

[ExecuteAlways]
[DisallowMultipleComponent]
public class InstancedFoliageField : ProcgenSpawnerBase
{
    [Header("Area (X-Z)")]
    public Vector2 areaSize = new Vector2(160, 160);
    
    [Header("Counts & LOD")]
    public float colliderRadius = 15f;
    [Range(1, 5000)] public int totalPlants = 800;
    public float minSpacing = 0.8f;
    public int seed = 2468;
    
    [Header("Placement (Terrain Masks)")]
    public ProceduralTerrain terrainSource;
    [Range(0f, 1f)] public float maxSlope01 = 0.7f;
    [Range(0f, 1f)] public float moistureRange = 0.5f; // Center point
    [Range(0f, 1f)] public float moistureWidth = 0.3f; // How wide the moisture range is
    public bool snapToGround = true;
    public LayerMask groundMask = ~0;
    public float rayStartHeight = 50f;
    public float rayMaxDistance = 200f;
    
    [Header("Plant Presets")]
    public LSystemPlantPreset[] plantPresets;
    [Range(0f, 1f)] public float presetMixing = 0.5f; // How much to mix presets vs use single type
    
    [Header("Per-plant Randomization")]
    public Vector2 uniformScaleRange = new Vector2(0.7f, 1.3f);
    public Vector2 yRotationRange = new Vector2(0f, 360f);
    [Range(0f, 30f)] public float randomTilt = 5f; // Random tilt from vertical
    
    [Header("Plant Generation Settings")]
    [Range(1, 16)] public int variantsPerPreset = 4; // Mesh variants per preset
    public bool shareVariantsBetweenInstances = true;
    
    [Header("Rendering")]
    public Material branchMaterial;
    public Material leafMaterial;
    
    [Header("Batching")]
    public bool chunkedBatches = true;
    
    // Internal
    readonly Dictionary<LSystemPlantPreset, List<Mesh>> _presetMeshes = new();
    readonly Dictionary<Mesh, List<Matrix4x4>> _instancedBranches = new();
    readonly Dictionary<Mesh, List<Matrix4x4>> _instancedLeaves = new();
    readonly List<ProcgenCullingHub.Batch> _exposedBatches = new();
    const int kMaxPerBatch = 1023;
    System.Random _rng;
    
    void Start()
    {
        if (!terrainSource) terrainSource = Object.FindFirstObjectByType<ProceduralTerrain>();
        EnsureDefaults();
        Rebuild();
    }
    
    void EnsureDefaults()
    {
        if (!branchMaterial)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            branchMaterial = new Material(shader) { name = "M_FoliageBranch (runtime)" };
        }
        if (!leafMaterial)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            leafMaterial = new Material(shader) { name = "M_FoliageLeaf (runtime)" };
            leafMaterial.doubleSidedGI = true;
            leafMaterial.SetFloat("_Cull", 0); // Double-sided for leaves
        }
        
        branchMaterial.enableInstancing = true;
        leafMaterial.enableInstancing = true;
        
        // Create default presets if none assigned
        if (plantPresets == null || plantPresets.Length == 0)
        {
            var fern = LSystemPlantPreset.CreateFernPreset();
            var bush = LSystemPlantPreset.CreateBushPreset();
            var weed = LSystemPlantPreset.CreateWeedPreset();
            plantPresets = new[] { fern, bush, weed };
        }
    }
    
    public override void Configure(WorldContext ctx)
    {
        base.Configure(ctx);
        if (!terrainSource && ctx != null)
            terrainSource = ctx.Root.GetComponentInChildren<ProceduralTerrain>();
    }
    
    [ContextMenu("Rebuild Now")]
    public override void Rebuild()
    {
        EnsureDefaults();
        ClearAll();
        
        // Apply biome if present
        if (Ctx != null && Ctx.Biome != null)
        {
            // Could add foliage-specific biome settings here
            maxSlope01 = Mathf.Min(maxSlope01, Ctx.Biome.grassMaxSlope01 * 1.2f);
        }
        
        BuildVariants();
        ScatterAndBuild();
        PrepareBatches();
        NotifyHub();
    }
    
    void ClearAll()
    {
        DestroyAllChildren();
        
        // Clean up generated meshes
        foreach (var kv in _presetMeshes)
        {
            foreach (var mesh in kv.Value)
            {
#if UNITY_EDITOR
                if (mesh) DestroyImmediate(mesh);
#else
                if (mesh) Destroy(mesh);
#endif
            }
        }
        
        _presetMeshes.Clear();
        _instancedBranches.Clear();
        _instancedLeaves.Clear();
        _exposedBatches.Clear();
    }
    
    void BuildVariants()
    {
        _rng = new System.Random(seed);
        
        foreach (var preset in plantPresets)
        {
            if (preset == null) continue;
            
            var meshList = new List<Mesh>();
            
            for (int v = 0; v < variantsPerPreset; v++)
            {
                // Create temporary plant to generate mesh
                var tempGO = new GameObject("TempPlant");
                tempGO.hideFlags = HideFlags.HideAndDontSave;
                var plant = tempGO.AddComponent<LSystemPlant>();
                
                // Apply preset settings
                preset.ApplyToPlant(plant);
                plant.seed = seed + v + preset.GetHashCode();
                plant.branchMaterial = branchMaterial;
                plant.leafMaterial = leafMaterial;
                plant.Generate();
                
                // Copy the generated mesh
                var mesh = plant.GetMesh();
                if (mesh != null)
                {
                    var meshCopy = Instantiate(mesh);
                    meshCopy.name = $"{preset.plantName}_Variant_{v}";
                    meshList.Add(meshCopy);
                }
                
                // Clean up temp object
#if UNITY_EDITOR
                DestroyImmediate(tempGO);
#else
                Destroy(tempGO);
#endif
            }
            
            _presetMeshes[preset] = meshList;
        }
    }
    
    void ScatterAndBuild()
    {
        List<Vector2> accepted = new();
        int attempts = 0, maxAttempts = totalPlants * 30;
        
        while (accepted.Count < totalPlants && attempts < maxAttempts)
        {
            attempts++;
            
            Vector2 pLocal;
            pLocal.x = (float)(_rng.NextDouble() - 0.5f) * areaSize.x;
            pLocal.y = (float)(_rng.NextDouble() - 0.5f) * areaSize.y;
            
            // Spacing check
            bool ok = true;
            for (int i = 0; i < accepted.Count; i++)
            {
                if ((accepted[i] - pLocal).sqrMagnitude < minSpacing * minSpacing)
                {
                    ok = false;
                    break;
                }
            }
            if (!ok) continue;
            
            Vector3 pos = transform.position + new Vector3(pLocal.x, 0f, pLocal.y);
            Vector3 hitNormal = Vector3.up;
            
            if (snapToGround)
            {
                var ray = new Ray(pos + Vector3.up * rayStartHeight, Vector3.down);
                if (Physics.Raycast(ray, out var hit, rayMaxDistance, groundMask, QueryTriggerInteraction.Ignore))
                {
                    pos = hit.point;
                    hitNormal = hit.normal;
                }
            }
            
            // Terrain mask checks
            float slope01 = 0f, moisture01 = 0.5f;
            bool hasMasks = terrainSource && terrainSource.TrySampleMasks(pos, out slope01, out moisture01);
            
            if (!hasMasks)
            {
                float upDot = Mathf.Clamp01(Vector3.Dot(hitNormal, Vector3.up));
                slope01 = 1f - upDot;
                moisture01 = 0.5f;
            }
            
            if (slope01 > maxSlope01) continue;
            
            // Check moisture range (foliage likes specific moisture levels)
            float moistureDist = Mathf.Abs(moisture01 - moistureRange);
            if (moistureDist > moistureWidth * 0.5f) continue;
            
            // Check if underwater
            var water = transform.parent?.GetComponentInChildren<ProceduralWater>();
            if (water && water.IsWater(pos)) continue;
            
            accepted.Add(pLocal);
            
            // Choose preset based on mixing factor
            LSystemPlantPreset chosenPreset = null;
            if (_rng.NextDouble() < presetMixing && plantPresets.Length > 1)
            {
                // Mix presets based on environmental factors
                float moistureWeight = moisture01;
                float slopeWeight = 1f - slope01;
                float combinedWeight = moistureWeight * 0.7f + slopeWeight * 0.3f;
                
                int presetIndex = Mathf.FloorToInt(combinedWeight * (plantPresets.Length - 1));
                chosenPreset = plantPresets[Mathf.Clamp(presetIndex, 0, plantPresets.Length - 1)];
            }
            else
            {
                // Random preset
                chosenPreset = plantPresets[_rng.Next(plantPresets.Length)];
            }
            
            if (!_presetMeshes.ContainsKey(chosenPreset) || _presetMeshes[chosenPreset].Count == 0)
                continue;
            
            // Get mesh variant
            var meshList = _presetMeshes[chosenPreset];
            var mesh = shareVariantsBetweenInstances ? 
                       meshList[_rng.Next(meshList.Count)] : 
                       meshList[accepted.Count % meshList.Count];
            
            // Create transform matrix
            float s = Mathf.Lerp(uniformScaleRange.x, uniformScaleRange.y, (float)_rng.NextDouble());
            float yRot = Mathf.Lerp(yRotationRange.x, yRotationRange.y, (float)_rng.NextDouble());
            
            // Add random tilt
            float tiltX = (float)(_rng.NextDouble() - 0.5) * randomTilt * 2f;
            float tiltZ = (float)(_rng.NextDouble() - 0.5) * randomTilt * 2f;
            
            var rot = Quaternion.Euler(tiltX, yRot, tiltZ);
            var mat = Matrix4x4.TRS(pos, rot, new Vector3(s, s, s));
            
            // Near-field: real GameObject for collision
            if ((pos - transform.position).magnitude <= colliderRadius)
            {
                var go = new GameObject($"Foliage_{chosenPreset.plantName}");
                go.transform.SetPositionAndRotation(pos, rot);
                go.transform.localScale = new Vector3(s, s, s);
                go.transform.parent = transform;
                
                var mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = new[] { branchMaterial, leafMaterial };
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                
                // Simple box collider for performance
                var bc = go.AddComponent<BoxCollider>();
                bc.size = mesh.bounds.size;
                bc.center = mesh.bounds.center;
                
                go.isStatic = true;
            }
            else
            {
                // Far-field: instanced rendering
                if (!_instancedBranches.TryGetValue(mesh, out var branchList))
                    _instancedBranches[mesh] = branchList = new List<Matrix4x4>();
                branchList.Add(mat);
                
                if (!_instancedLeaves.TryGetValue(mesh, out var leafList))
                    _instancedLeaves[mesh] = leafList = new List<Matrix4x4>();
                leafList.Add(mat);
            }
        }
    }
    
    void PrepareBatches()
    {
        _exposedBatches.Clear();
        bool useChunks = chunkedBatches && Ctx != null && Ctx.Grid != null;
        
        // Process branches
        ProcessBatchesForSubmesh(_instancedBranches, branchMaterial, 0, useChunks);
        
        // Process leaves  
        ProcessBatchesForSubmesh(_instancedLeaves, leafMaterial, 1, useChunks);
    }
    
    void ProcessBatchesForSubmesh(Dictionary<Mesh, List<Matrix4x4>> instances, Material material, int submesh, bool useChunks)
    {
        foreach (var kv in instances)
        {
            var mats = kv.Value;
            if (!useChunks)
            {
                // Simple batching
                int offset = 0;
                while (offset < mats.Count)
                {
                    int count = Mathf.Min(kMaxPerBatch, mats.Count - offset);
                    var arr = new Matrix4x4[count];
                    mats.CopyTo(offset, arr, 0, count);
                    offset += count;
                    
                    var b = ProcgenBoundsUtil.FromInstances(arr, kv.Key, 0.5f);
                    
                    _exposedBatches.Add(new ProcgenCullingHub.Batch
                    {
                        mesh = kv.Key,
                        submeshIndex = submesh,
                        material = material,
                        matrices = arr,
                        bounds = b,
                        layer = gameObject.layer,
                        shadowCasting = submesh == 0 ? // Branches cast shadows, leaves optional
                            UnityEngine.Rendering.ShadowCastingMode.On :
                            UnityEngine.Rendering.ShadowCastingMode.Off,
                        receiveShadows = true
                    });
                }
            }
            else
            {
                // Chunked batching for better culling
                var grid = Ctx.Grid;
                var byChunk = new Dictionary<ChunkId, List<Matrix4x4>>(64);
                
                for (int i = 0; i < mats.Count; i++)
                {
                    Vector3 p = mats[i].GetColumn(3);
                    var id = grid.IdAt(p);
                    if (!byChunk.TryGetValue(id, out var list))
                        byChunk[id] = list = new List<Matrix4x4>();
                    list.Add(mats[i]);
                }
                
                foreach (var ck in byChunk)
                {
                    var list = ck.Value;
                    int offset = 0;
                    while (offset < list.Count)
                    {
                        int count = Mathf.Min(kMaxPerBatch, list.Count - offset);
                        var arr = new Matrix4x4[count];
                        list.CopyTo(offset, arr, 0, count);
                        offset += count;
                        
                        var b = grid.BoundsOf(ck.Key);
                        b.Expand(1.0f);
                        
                        _exposedBatches.Add(new ProcgenCullingHub.Batch
                        {
                            mesh = kv.Key,
                            submeshIndex = submesh,
                            material = material,
                            matrices = arr,
                            bounds = b,
                            layer = gameObject.layer,
                            shadowCasting = submesh == 0 ?
                                UnityEngine.Rendering.ShadowCastingMode.On :
                                UnityEngine.Rendering.ShadowCastingMode.Off,
                            receiveShadows = true
                        });
                    }
                }
            }
        }
    }
    
    public override List<ProcgenCullingHub.Batch> GetInstancedBatches() => _exposedBatches;
    
    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 0.8f, 0.3f, 0.15f);
        Gizmos.DrawCube(transform.position, new Vector3(areaSize.x, 0.1f, areaSize.y));
    }
}
