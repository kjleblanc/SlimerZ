using UnityEngine;

[CreateAssetMenu(menuName="Procgen/Biome Profile")]
public class BiomeProfile : ScriptableObject
{
    [Header("Global Density")]
    [Range(0.1f, 3f)] public float globalDensity = 1f;

    [Header("Trees")]
    [Range(0f,1f)] public float treeMaxSlope01 = 0.6f;
    [Range(0f,1f)] public float treeMoistureBias = 0.65f;

    [Header("Rocks")]
    [Range(0f,1f)] public float rockMinSlope01 = 0.25f;
    [Range(0f,1f)] public float rockDrynessBias = 0.6f;

    [Header("Grass")]
    [Range(0f,1f)] public float grassMaxSlope01 = 0.55f;
    [Range(0f,1f)] public float grassMoistureBias = 0.8f;
    [Range(0f,1f)] public float grassMinSpawnProb = 0.12f;
}
