using UnityEngine;
using System.Collections.Generic;

public interface IProcgenStage
{
    string Name { get; }
    void Run(WorldContext ctx, ProcgenWorld world);
}

public sealed class ProcgenStageRunner
{
    readonly List<IProcgenStage> _stages = new();
    public ProcgenStageRunner Add(IProcgenStage stage) { if (stage != null) _stages.Add(stage); return this; }

    public void RunAll(WorldContext ctx, ProcgenWorld world)
    {
        for (int i = 0; i < _stages.Count; i++)
        {
            var s = _stages[i];
            if (s == null) continue;
            s.Run(ctx, world);
        }
    }
}

