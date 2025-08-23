using UnityEngine;

public sealed class TerrainStage : IProcgenStage
{
    public string Name => "Terrain & Water";

    public void Run(WorldContext ctx, ProcgenWorld world)
    {
        // Terrain
        if (world.makeTerrain && world.terrain)
        {
            world.terrain.seed = world.seed;
            world.terrain.Rebuild(); // your existing component call  :contentReference[oaicite:0]{index=0}
            // feed context (height/masks provider)
            ctx.Terrain = world.terrain.terrain;
            ctx.Masks   = world.terrain as IWorldMaskProvider;  // your ProceduralTerrain implements this  :contentReference[oaicite:1]{index=1}
        }

        // Water (after terrain)
        if (world.makeWater && world.water && world.terrain)
        {
            world.water.terrainSource   = world.terrain;
            world.water.waterLevel      = world.waterLevel;
            world.water.minWaterBodySize= world.minWaterBodySize;
            world.water.Rebuild(); // already in your flow  :contentReference[oaicite:2]{index=2}
        }
    }
}

