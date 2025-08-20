using UnityEngine;
using System.Collections.Generic;

#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
[DisallowMultipleComponent]
public class ProceduralWater : MonoBehaviour, IProcgenConfigurable
{
    [Header("Water Detection")]
    [Tooltip("Height threshold (meters) below which water appears")]
    public float waterLevel = 5f;
    [Tooltip("Blend distance for shore transitions")]
    public float shoreBlend = 2f;
    
    [Header("Water Bodies")]
    [Tooltip("Minimum connected cells to form a water body")]
    public int minWaterBodySize = 10;
    [Tooltip("Resolution for water detection grid")]
    public int waterGridResolution = 128;
    
    [Header("Visual")]
    public Material waterMaterial;
    public Color shallowColor = new Color(0.2f, 0.5f, 0.7f, 0.8f);
    public Color deepColor = new Color(0.1f, 0.3f, 0.5f, 0.95f);
    [Range(0f, 1f)] public float waterOpacity = 0.85f;
    public float waveSpeed = 0.5f;
    public float waveScale = 0.1f;
    
    [Header("Mesh")]
    [Range(8, 256)] public int meshResolution = 64;
    public float meshBorderExtend = 5f;
    
    [Header("Masks (output)")]
    public Texture2D waterMask;  // 1 = water, 0 = land
    public Texture2D depthMask;  // gradient based on depth
    
    [Header("References")]
    public ProceduralTerrain terrainSource;
    
    // Internal
    WorldContext _ctx;
    List<WaterBody> _waterBodies = new();
    MeshFilter _meshFilter;
    MeshRenderer _meshRenderer;
    
    struct WaterBody
    {
        public List<Vector2Int> cells;
        public Bounds bounds;
        public float averageDepth;
        public GameObject meshObject;
    }
    
    void Start()
    {
        if (!terrainSource) terrainSource = FindFirstObjectByType<ProceduralTerrain>();
        EnsureDefaults();
        Rebuild();
    }
    
    public void Configure(WorldContext ctx)
    {
        _ctx = ctx;
        if (!terrainSource && ctx != null) 
            terrainSource = ctx.Root.GetComponentInChildren<ProceduralTerrain>();
    }
    
    [ContextMenu("Rebuild Water")]
    public void Rebuild()
    {
        EnsureDefaults();
        ClearWater();
        
        if (!terrainSource || !terrainSource.terrain) return;
        
        DetectWaterBodies();
        BuildWaterMeshes();
        BuildMasks();
    }
    
    void EnsureDefaults()
    {
        if (!waterMaterial)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            waterMaterial = new Material(shader) { name = "M_Water (runtime)" };
            waterMaterial.SetFloat("_Smoothness", 0.95f);
            waterMaterial.SetFloat("_Metallic", 0.1f);
        }
        
        // Set transparent mode
        waterMaterial.SetInt("_Surface", 1); // Transparent
        waterMaterial.SetInt("_Blend", 0);   // Alpha
        waterMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        waterMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        waterMaterial.SetInt("_ZWrite", 0);
        waterMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        waterMaterial.renderQueue = 3000;
    }
    
    void ClearWater()
    {
        // Destroy existing water mesh objects
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i).gameObject;
#if UNITY_EDITOR
            if (!Application.isPlaying) DestroyImmediate(child);
            else Destroy(child);
#else
            Destroy(child);
#endif
        }
        _waterBodies.Clear();
    }
    
    void DetectWaterBodies()
    {
        var terrain = terrainSource.terrain;
        var td = terrain.terrainData;
        
        int gridSize = waterGridResolution;
        bool[,] waterGrid = new bool[gridSize, gridSize];
        float[,] depths = new float[gridSize, gridSize];
        
        // Mark cells below water level
        for (int z = 0; z < gridSize; z++)
        {
            for (int x = 0; x < gridSize; x++)
            {
                float u = (float)x / (gridSize - 1);
                float v = (float)z / (gridSize - 1);
                
                Vector3 worldPos = terrain.transform.position + 
                    new Vector3(u * td.size.x, 0, v * td.size.z);
                
                float height = terrain.SampleHeight(worldPos) + terrain.transform.position.y;
                
                if (height < waterLevel)
                {
                    waterGrid[x, z] = true;
                    depths[x, z] = waterLevel - height;
                }
            }
        }
        
        // Flood fill to find connected water bodies
        bool[,] visited = new bool[gridSize, gridSize];
        
        for (int z = 0; z < gridSize; z++)
        {
            for (int x = 0; x < gridSize; x++)
            {
                if (waterGrid[x, z] && !visited[x, z])
                {
                    var body = FloodFill(x, z, waterGrid, visited, depths, gridSize);
                    
                    if (body.cells.Count >= minWaterBodySize)
                    {
                        // Calculate bounds and average depth
                        Vector3 min = new Vector3(float.MaxValue, waterLevel, float.MaxValue);
                        Vector3 max = new Vector3(float.MinValue, waterLevel, float.MinValue);
                        float totalDepth = 0;
                        
                        foreach (var cell in body.cells)
                        {
                            float u = (float)cell.x / (gridSize - 1);
                            float v = (float)cell.y / (gridSize - 1);
                            
                            Vector3 worldPos = terrain.transform.position + 
                                new Vector3(u * td.size.x, 0, v * td.size.z);
                            
                            min.x = Mathf.Min(min.x, worldPos.x);
                            min.z = Mathf.Min(min.z, worldPos.z);
                            max.x = Mathf.Max(max.x, worldPos.x);
                            max.z = Mathf.Max(max.z, worldPos.z);
                            
                            totalDepth += depths[cell.x, cell.y];
                        }
                        
                        body.bounds = new Bounds();
                        body.bounds.SetMinMax(min, max);
                        body.averageDepth = totalDepth / body.cells.Count;
                        
                        _waterBodies.Add(body);
                    }
                }
            }
        }
    }
    
    WaterBody FloodFill(int startX, int startZ, bool[,] waterGrid, bool[,] visited, 
                        float[,] depths, int gridSize)
    {
        var body = new WaterBody { cells = new List<Vector2Int>() };
        var queue = new Queue<Vector2Int>();
        
        queue.Enqueue(new Vector2Int(startX, startZ));
        visited[startX, startZ] = true;
        
        int[] dx = { 0, 1, 0, -1 };
        int[] dz = { 1, 0, -1, 0 };
        
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            body.cells.Add(current);
            
            for (int i = 0; i < 4; i++)
            {
                int nx = current.x + dx[i];
                int nz = current.y + dz[i];
                
                if (nx >= 0 && nx < gridSize && nz >= 0 && nz < gridSize &&
                    waterGrid[nx, nz] && !visited[nx, nz])
                {
                    visited[nx, nz] = true;
                    queue.Enqueue(new Vector2Int(nx, nz));
                }
            }
        }
        
        return body;
    }
    
    void BuildWaterMeshes()
    {
        for (int i = 0; i < _waterBodies.Count; i++)
        {
            var body = _waterBodies[i];
            
            var waterObj = new GameObject($"WaterBody_{i}");
            waterObj.transform.SetParent(transform, false);
            
            var mf = waterObj.AddComponent<MeshFilter>();
            var mr = waterObj.AddComponent<MeshRenderer>();
            
            // Build mesh for this water body
            var mesh = BuildWaterMesh(body);
            mf.sharedMesh = mesh;
            
            // Setup material with depth-based color
            var mat = new Material(waterMaterial);
            float depthFactor = Mathf.Clamp01(body.averageDepth / 10f);
            mat.color = Color.Lerp(shallowColor, deepColor, depthFactor);
            mat.color = new Color(mat.color.r, mat.color.g, mat.color.b, waterOpacity);
            mr.sharedMaterial = mat;
            
            // Position at water level
            waterObj.transform.position = new Vector3(0, waterLevel, 0);
            
            body.meshObject = waterObj;
            _waterBodies[i] = body;
        }
    }
    
    Mesh BuildWaterMesh(WaterBody body)
    {
        var bounds = body.bounds;
        bounds.Expand(meshBorderExtend);
        
        int res = meshResolution;
        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();
        var colors = new List<Color>();
        
        float sizeX = bounds.size.x;
        float sizeZ = bounds.size.z;
        
        var terrain = terrainSource.terrain;
        
        // Generate grid vertices
        for (int z = 0; z <= res; z++)
        {
            for (int x = 0; x <= res; x++)
            {
                float u = (float)x / res;
                float v = (float)z / res;
                
                Vector3 localPos = new Vector3(
                    bounds.min.x + u * sizeX - terrain.transform.position.x,
                    0,
                    bounds.min.z + v * sizeZ - terrain.transform.position.z
                );
                
                Vector3 worldPos = terrain.transform.position + localPos;
                float terrainHeight = terrain.SampleHeight(worldPos) + terrain.transform.position.y;
                float depth = waterLevel - terrainHeight;
                
                // Add slight wave displacement
                float wave = Mathf.Sin(localPos.x * waveScale + Time.time * waveSpeed) * 
                           Mathf.Cos(localPos.z * waveScale + Time.time * waveSpeed * 0.7f) * 0.05f;
                
                localPos.y = wave;
                
                verts.Add(localPos);
                uvs.Add(new Vector2(u, v));
                
                // Color based on depth
                float depthNorm = Mathf.Clamp01(depth / 10f);
                colors.Add(new Color(1, 1, 1, depthNorm));
            }
        }
        
        // Generate triangles
        for (int z = 0; z < res; z++)
        {
            for (int x = 0; x < res; x++)
            {
                int i0 = x + z * (res + 1);
                int i1 = i0 + 1;
                int i2 = i0 + (res + 1);
                int i3 = i2 + 1;
                
                tris.Add(i0); tris.Add(i2); tris.Add(i1);
                tris.Add(i1); tris.Add(i2); tris.Add(i3);
            }
        }
        
        var mesh = new Mesh { name = "WaterMesh" };
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetColors(colors);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        
        return mesh;
    }
    
    void BuildMasks()
    {
        if (!terrainSource || !terrainSource.terrain) return;
        
        var td = terrainSource.terrain.terrainData;
        int res = td.heightmapResolution;
        
        waterMask = NewOrResize(waterMask, res, res, TextureFormat.R8);
        depthMask = NewOrResize(depthMask, res, res, TextureFormat.R8);
        
        Color[] waterPixels = new Color[res * res];
        Color[] depthPixels = new Color[res * res];
        
        var terrain = terrainSource.terrain;
        
        for (int y = 0; y < res; y++)
        {
            for (int x = 0; x < res; x++)
            {
                float u = (float)x / (res - 1);
                float v = (float)y / (res - 1);
                
                Vector3 worldPos = terrain.transform.position + 
                    new Vector3(u * td.size.x, 0, v * td.size.z);
                
                float height = terrain.SampleHeight(worldPos) + terrain.transform.position.y;
                
                float waterValue = 0;
                float depthValue = 0;
                
                if (height < waterLevel)
                {
                    float depth = waterLevel - height;
                    waterValue = Mathf.SmoothStep(0, 1, depth / shoreBlend);
                    depthValue = Mathf.Clamp01(depth / 10f);
                }
                
                int i = y * res + x;
                waterPixels[i] = new Color(waterValue, waterValue, waterValue, 1);
                depthPixels[i] = new Color(depthValue, depthValue, depthValue, 1);
            }
        }
        
        waterMask.SetPixels(waterPixels);
        waterMask.Apply(false, false);
        
        depthMask.SetPixels(depthPixels);
        depthMask.Apply(false, false);
    }
    
    Texture2D NewOrResize(Texture2D tex, int w, int h, TextureFormat fmt)
    {
        if (tex == null || tex.width != w || tex.height != h || tex.format != fmt)
        {
            if (tex != null) Destroy(tex);
            tex = new Texture2D(w, h, fmt, false, true)
            {
                name = "WaterMask",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
        }
        return tex;
    }
    
    // Implement mask sampling for spawners to avoid placing objects in water
    public bool IsWater(Vector3 worldPos)
    {
        if (!waterMask || !terrainSource || !terrainSource.terrain) return false;
        
        var terrain = terrainSource.terrain;
        var td = terrain.terrainData;
        
        Vector3 local = worldPos - terrain.transform.position;
        float u = Mathf.InverseLerp(0f, td.size.x, local.x);
        float v = Mathf.InverseLerp(0f, td.size.z, local.z);
        
        if (u < 0f || u > 1f || v < 0f || v > 1f) return false;
        
        return waterMask.GetPixelBilinear(u, v).r > 0.5f;
    }
    
    public float GetWaterDepth(Vector3 worldPos)
    {
        if (!depthMask || !terrainSource || !terrainSource.terrain) return 0;
        
        var terrain = terrainSource.terrain;
        var td = terrain.terrainData;
        
        Vector3 local = worldPos - terrain.transform.position;
        float u = Mathf.InverseLerp(0f, td.size.x, local.x);
        float v = Mathf.InverseLerp(0f, td.size.z, local.z);
        
        if (u < 0f || u > 1f || v < 0f || v > 1f) return 0;
        
        return depthMask.GetPixelBilinear(u, v).r * 10f; // Convert back to meters
    }
    
#if UNITY_EDITOR
    void OnValidate()
    {
        if (!Application.isPlaying)
        {
            EditorApplication.delayCall += () =>
            {
                if (this != null) Rebuild();
            };
        }
    }
#endif
    
    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0, 0.5f, 1f, 0.3f);
        foreach (var body in _waterBodies)
        {
            Gizmos.DrawCube(body.bounds.center, body.bounds.size);
        }
    }
}
