using System.Collections.Generic;

public interface IProcgenInstancedSource
{
    // Expose instancing batches to the hub.
    List<ProcgenCullingHub.Batch> GetInstancedBatches();
}
