using UnityEngine;

public struct ChunkId {
    public int x, z;
    public ChunkId(int x, int z){ this.x=x; this.z=z; }
    public override int GetHashCode() => (x*73856093) ^ (z*19349663);
    public override bool Equals(object o) => o is ChunkId c && c.x==x && c.z==z;
}

public sealed class ChunkGrid {
    public readonly Vector2Int dims;   // e.g. 8x8
    public readonly Vector2 cellSize;  // world meters per cell (x,z)
    public readonly Vector3 origin;    // world min (x,*,z) of grid
    public readonly Bounds worldBounds;// overall AABB (y-center/height from terrain)

    public ChunkGrid(Vector3 origin, Vector2 sizeXZ, Vector2Int dims, Bounds worldBounds){
        this.origin = origin;
        this.dims   = new Vector2Int(Mathf.Max(1,dims.x), Mathf.Max(1,dims.y));
        this.cellSize = new Vector2(sizeXZ.x/this.dims.x, sizeXZ.y/this.dims.y);
        this.worldBounds = worldBounds;
    }

    public ChunkId IdAt(Vector3 worldPos){
        int ix = Mathf.FloorToInt((worldPos.x - origin.x) / cellSize.x);
        int iz = Mathf.FloorToInt((worldPos.z - origin.z) / cellSize.y);
        ix = Mathf.Clamp(ix, 0, dims.x-1); iz = Mathf.Clamp(iz, 0, dims.y-1);
        return new ChunkId(ix, iz);
    }

    public Bounds BoundsOf(ChunkId id){
        float minX = origin.x + id.x * cellSize.x;
        float minZ = origin.z + id.z * cellSize.y;
        var center = new Vector3(minX + cellSize.x*0.5f, worldBounds.center.y, minZ + cellSize.y*0.5f);
        var size   = new Vector3(cellSize.x, worldBounds.size.y, cellSize.y);
        return new Bounds(center, size);
    }
}
