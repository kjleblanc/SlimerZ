using UnityEngine;

[CreateAssetMenu(menuName = "Procgen/World Preset")]
public class WorldPreset : ScriptableObject
{
    [Header("Seed & Grid")]
    public int seed = 12345;
    public Vector2Int gridDims = new Vector2Int(12, 12);

    [Header("What to generate")]
    public bool terrain = true;
    public bool water   = true;
    public bool trees   = true;
    public bool rocks   = true;
    public bool grass   = true;
    public bool foliage = true;

    [Header("Profiles")]
    public TerrainProfile  terrainProfile;
    public HydrologyProfile hydrologyProfile;
    public ScatterProfile  scatterProfile;

    [Header("Biome (optional)")]
    public ScriptableObject biome; // keep loose type to avoid compile breaks if your BiomeProfile class moves
}
