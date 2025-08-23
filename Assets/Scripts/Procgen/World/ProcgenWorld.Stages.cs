using UnityEngine;

public partial class ProcgenWorld : MonoBehaviour
{
    /// <summary>
    /// Build via staged pipeline (uses your existing components & WorldContext).
    /// </summary>
    [ContextMenu("Build World (Stages)")]
    public void BuildViaStages()
    {
        EnsureChildren(); // your existing helper

        // Terrain bounds → center & size (same as your RebuildWorld)  :contentReference[oaicite:12]{index=12}
        Vector3 tPos  = terrain ? terrain.terrain.transform.position : transform.position;
        Vector3 tSize = terrain ? terrain.terrain.terrainData.size    : new Vector3(200, 25, 200);
        Vector3 center = tPos + new Vector3(tSize.x * 0.5f, 0f, tSize.z * 0.5f);
        Vector2 areaXZ = new Vector2(tSize.x, tSize.z);
        var worldBounds = new Bounds(tPos + new Vector3(tSize.x/2f, tSize.y/2f, tSize.z/2f), tSize);

        // Optional grid  :contentReference[oaicite:13]{index=13}
        ChunkGrid grid = null;
        if (useChunkGrid)
        {
            var origin = new Vector3(tPos.x, 0f, tPos.z);
            grid = new ChunkGrid(origin, new Vector2(tSize.x, tSize.z), gridDims, worldBounds);
        }

        // Context (your existing WorldContext is reused)  :contentReference[oaicite:14]{index=14}
        var ctx = new WorldContext {
            Root = transform,
            Terrain = terrain ? terrain.terrain : null,
            Masks = terrain as IWorldMaskProvider,
            Biome = biome,
            Seed = seed,
            WorldBounds = worldBounds,
            Grid = grid
        };

        // Run stages
        new ProcgenStageRunner()
            .Add(new TerrainStage())
            .Add(new ScatterStage(center, areaXZ))
            .RunAll(ctx, this);

        // Notify hub once (optional; your spawners already call NotifyHub after batching)
        if (cullingHub) cullingHub.NotifyDirty();
    }
}

