using UnityEngine;

public interface IWorldMaskProvider
{
    // Sample your world masks in 0..1 space.
    bool TrySample(Vector3 worldPos, out float slope01, out float moisture01);
}

public sealed class WorldContext
{
    public Transform Root;
    public Terrain Terrain;
    public IWorldMaskProvider Masks;
    public BiomeProfile Biome;
    public int Seed;
    public Bounds WorldBounds;
    public ChunkGrid Grid; 
}


// Spawners implement this to receive the context (no base class required yet).
public interface IProcgenConfigurable
{
    void Configure(WorldContext ctx);
}
