using UnityEngine;
using System.Collections.Generic;

public abstract class ProcgenSpawnerBase : MonoBehaviour, IProcgenConfigurable, IProcgenInstancedSource
{
    protected WorldContext Ctx { get; private set; }

    public virtual void Configure(WorldContext ctx) { Ctx = ctx; OnConfigured(); }
    protected virtual void OnConfigured() {}

    // NEW: subclasses (Trees, Rocks, Grass) override this
    public abstract void Rebuild();

    // Notify the culling hub after rebuild/batching
    protected void NotifyHub()
    {
        var hub = FindFirstObjectByType<ProcgenCullingHub>();
        if (hub) hub.NotifyDirty();
    }

    // Utility: destroy all child GOs (safe edit & play)
    protected void DestroyAllChildren()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var go = transform.GetChild(i).gameObject;
#if UNITY_EDITOR
            if (!Application.isPlaying) DestroyImmediate(go);
            else Destroy(go);
#else
            Destroy(go);
#endif
        }
    }

    public abstract List<ProcgenCullingHub.Batch> GetInstancedBatches();
}


