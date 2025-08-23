using UnityEngine;

[CreateAssetMenu(menuName = "Procgen/Profiles/Hydrology Profile")]
public class HydrologyProfile : ScriptableObject
{
    [Header("Level & Bodies")]
    public float waterLevel = 2.0f;
    public int   minWaterBodySize = 60; // grid cells

    [Header("Shore & Waves (for shader/material)")]
    [Range(0,0.2f)] public float shoreBlend = 0.04f;
    public float waveScale = 0.15f;
    public float waveSpeed = 0.8f;
}
