using UnityEngine;

[CreateAssetMenu(menuName = "Procgen/Profiles/Scatter Profile")]
public class ScatterProfile : ScriptableObject
{
    [Header("Counts")]
    public int trees   = 1500;
    public int rocks   = 1200;
    public int grass   = 60000;   // blades
    public int foliage = 2500;

    [Header("Near-LOD Collider Radius (m)")]
    [Range(0,120)] public float colliderRadius = 40f;

    [Header("Global placement biases")]
    [Range(0,1)] public float moistureBias = 0.6f; // higher -> prefer wetter
}
