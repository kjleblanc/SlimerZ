// Assets/Scripts/Procgen/World/ProcgenWorld.Preset.cs
using UnityEngine;
using System.Reflection;

public partial class ProcgenWorld : MonoBehaviour
{
    [Header("Preset")]
    public bool usePreset = true;
    public WorldPreset preset;

    public void ApplyPresetToScene()
    {
        if (!usePreset || !preset) return;

        // toggles
        makeTerrain = preset.terrain;
        makeWater   = preset.water;
        makeTrees   = preset.trees;
        makeRocks   = preset.rocks;
        makeGrass   = preset.grass;
        if (foliageField) makeFoliage = preset.foliage;

        // seed & grid
        seed = preset.seed;
        gridDims = preset.gridDims;

        // biome (safe cast only if types match)
        if (preset.biome != null)
        {
            // If your project has BiomeProfile, this cast will succeed; if not, it's ignored.
            var bp = preset.biome as BiomeProfile;
            if (bp != null) biome = bp;
        }

        // --- Terrain ---
        if (terrain && preset.terrainProfile)
        {
            var dataAsset = preset.terrainProfile.terrainDataAsset;
            if (dataAsset)
            {
                var trType = terrain.GetType();
                var f = trType.GetField("data", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null && f.FieldType.IsAssignableFrom(dataAsset.GetType()))
                    f.SetValue(terrain, dataAsset);

                var p = trType.GetProperty("data", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null && p.CanWrite && p.PropertyType.IsAssignableFrom(dataAsset.GetType()))
                    p.SetValue(terrain, dataAsset, null);
            }

            if (preset.terrainProfile.applyNumericOverrides)
            {
                terrain.heightScale  = preset.terrainProfile.heightScale;
                terrain.baseScale    = preset.terrainProfile.baseScale;
                terrain.warpStrength = preset.terrainProfile.warpStrength;
                terrain.octaves      = preset.terrainProfile.octaves;
            }
        }

        // --- Hydrology / Water ---
        if (water && preset.hydrologyProfile)
        {
            water.waterLevel       = preset.hydrologyProfile.waterLevel;
            water.minWaterBodySize = preset.hydrologyProfile.minWaterBodySize;
            water.shoreBlend       = preset.hydrologyProfile.shoreBlend;
            water.waveScale        = preset.hydrologyProfile.waveScale;
            water.waveSpeed        = preset.hydrologyProfile.waveSpeed;
        }

        // --- Scatter counts & collider radius ---
        if (preset.scatterProfile)
        {
            trees = preset.scatterProfile.trees;
            rocks = preset.scatterProfile.rocks;
            grass = preset.scatterProfile.grass;
            if (foliageField) SetMemberIfExists(foliageField, "instanceCount", preset.scatterProfile.foliage);

            colliderRadius = preset.scatterProfile.colliderRadius;

            // Push down to spawners, accounting for different field names across versions
            if (treeField)
            {
                SetMemberIfExists(treeField, "nearColliderRadius", colliderRadius);
                SetMemberIfExists(treeField, "colliderRadius",     colliderRadius);
            }
            if (rockField)
            {
                SetMemberIfExists(rockField, "nearColliderRadius", colliderRadius);
                SetMemberIfExists(rockField, "colliderRadius",     colliderRadius);
            }
            if (grassField)
            {
                SetMemberIfExists(grassField, "instanceCount", grass); // if present
                SetMemberIfExists(grassField, "bladeCount",    grass); // older/newer name
            }
        }
    }

    public void RebuildFromPreset()
    {
        EnsureChildren();
        ApplyPresetToScene();
        RebuildWorld();
    }

    // ---------- helpers ----------
    static void SetMemberIfExists(object target, string name, object value)
    {
        if (target == null) return;
        var t = target.GetType();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        var f = t.GetField(name, flags);
        if (f != null)
        {
            if (TryConvert(value, f.FieldType, out var cv)) f.SetValue(target, cv);
            return;
        }
        var p = t.GetProperty(name, flags);
        if (p != null && p.CanWrite)
        {
            if (TryConvert(value, p.PropertyType, out var cv)) p.SetValue(target, cv, null);
        }
    }

    static bool TryConvert(object value, System.Type targetType, out object converted)
    {
        try
        {
            if (value == null) { converted = null; return !targetType.IsValueType; }
            if (targetType.IsInstanceOfType(value)) { converted = value; return true; }
            converted = System.Convert.ChangeType(value, targetType);
            return true;
        }
        catch { converted = null; return false; }
    }
}
