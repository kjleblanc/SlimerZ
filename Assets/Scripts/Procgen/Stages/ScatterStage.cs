using UnityEngine;

public sealed class ScatterStage : IProcgenStage
{
    public string Name => "Scatter";

    readonly Vector3 _center;
    readonly Vector2 _areaXZ;

    public ScatterStage(Vector3 center, Vector2 areaXZ)
    {
        _center = center; _areaXZ = areaXZ;
    }

    public void Run(WorldContext ctx, ProcgenWorld world)
    {
        var biome = world.biome; // optional rules

        // TREES
        if (world.makeTrees && world.treeField)
        {
            var tf = world.treeField;
            tf.seed = world.seed + 11;
            tf.transform.position = _center;
            tf.areaSize = _areaXZ;
            tf.colliderRadius = world.colliderRadius;
            tf.terrainSource = world.terrain;
            tf.totalTrees = Mathf.RoundToInt(world.trees * (biome ? biome.globalDensity : 1f)); //  :contentReference[oaicite:3]{index=3}
            tf.Configure(ctx);
            tf.Rebuild();

            if (biome)
            {
                tf.maxSlope01   = biome.treeMaxSlope01;
                tf.moistureBias = biome.treeMoistureBias; //  :contentReference[oaicite:4]{index=4}
            }

            if (tf.woodMaterial) tf.woodMaterial.enableInstancing = true;
            if (tf.leafMaterial) tf.leafMaterial.enableInstancing = true; //  :contentReference[oaicite:5]{index=5}
        }

        // ROCKS
        if (world.makeRocks && world.rockField)
        {
            var rf = world.rockField;
            rf.seed = world.seed + 22;
            rf.transform.position = _center;
            rf.areaSize = _areaXZ;
            rf.colliderRadius = world.colliderRadius;
            rf.terrainSource = world.terrain;
            rf.totalRocks = Mathf.RoundToInt(world.rocks * (biome ? biome.globalDensity : 1f)); //  :contentReference[oaicite:6]{index=6}
            rf.Configure(ctx);
            rf.Rebuild();

            if (biome)
            {
                rf.minSlope01  = biome.rockMinSlope01;
                rf.drynessBias = biome.rockDrynessBias; //  :contentReference[oaicite:7]{index=7}
            }

            if (rf.rockMaterial) rf.rockMaterial.enableInstancing = true; //  :contentReference[oaicite:8]{index=8}
        }

        // GRASS
        if (world.makeGrass && world.grassField)
        {
            var gf = world.grassField;
            gf.seed = world.seed + 33;
            gf.transform.position = _center;
            gf.areaSize = _areaXZ;
            gf.terrainSource = world.terrain;
            gf.bladeCount = Mathf.RoundToInt(world.grass * (biome ? biome.globalDensity : 1f)); //  :contentReference[oaicite:9]{index=9}
            gf.Configure(ctx);

            if (biome)
            {
                gf.maxSlope01   = biome.grassMaxSlope01;
                gf.moistureBias = biome.grassMoistureBias;
                gf.minSpawnProb = biome.grassMinSpawnProb; //  :contentReference[oaicite:10]{index=10}
            }

            gf.Build(); // (grass uses Build())  :contentReference[oaicite:11]{index=11}
            if (gf.grassMaterial) gf.grassMaterial.enableInstancing = true;
        }
    }
}

