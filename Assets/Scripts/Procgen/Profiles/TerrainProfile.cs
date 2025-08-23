// Assets/Scripts/Procgen/Profiles/TerrainProfile.cs
using UnityEngine;

[CreateAssetMenu(menuName = "Procgen/Profiles/Terrain Profile")]
public class TerrainProfile : ScriptableObject
{
    [Header("Optional terrain data asset")]
    [Tooltip("If your ProceduralTerrain component has a 'data' field, this will be assigned to it (if types match). If not, this is ignored.")]
    public ScriptableObject terrainDataAsset;

    [Header("Numeric overrides (applied if enabled)")]
    public bool  applyNumericOverrides = true;
    public float heightScale  = 35f;
    public float baseScale    = 0.01f;
    public float warpStrength = 0.4f;
    [Range(1,12)] public int octaves = 6;
}
