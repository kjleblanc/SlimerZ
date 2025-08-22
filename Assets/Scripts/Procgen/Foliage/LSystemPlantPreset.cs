using UnityEngine;

[CreateAssetMenu(menuName = "Procgen/L-System Plant Preset", fileName = "PlantPreset")]
public class LSystemPlantPreset : ScriptableObject
{
    [Header("Plant Type")]
    public string plantName = "Custom Plant";
    [TextArea(2, 4)]
    public string description = "A procedural plant generated using L-Systems";
    
    [Header("L-System Settings")]
    public LSystemSettings settings = new LSystemSettings();
    
    [Header("Mesh Settings")]
    [Range(3, 12)] public int branchSides = 6;
    public bool flatShadedLeaves = false;
    public bool combineMeshes = true;
    
    [Header("Preview")]
    public Texture2D previewIcon;
    
    // Apply preset to a plant
    public void ApplyToPlant(LSystemPlant plant)
    {
        if (plant == null) return;
        
        // Deep copy settings
        plant.settings = CopySettings(settings);
        plant.branchSides = branchSides;
        plant.flatShadedLeaves = flatShadedLeaves;
        plant.combineMeshes = combineMeshes;
        
        // Regenerate with new settings
        plant.Generate();
    }
    
    // Copy from plant to preset
    public void CopyFromPlant(LSystemPlant plant)
    {
        if (plant == null) return;
        
        settings = CopySettings(plant.settings);
        branchSides = plant.branchSides;
        flatShadedLeaves = plant.flatShadedLeaves;
        combineMeshes = plant.combineMeshes;
        
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }
    
    static LSystemSettings CopySettings(LSystemSettings source)
    {
        var copy = new LSystemSettings
        {
            axiom = source.axiom,
            iterations = source.iterations,
            segmentLength = source.segmentLength,
            angleIncrement = source.angleIncrement,
            angleVariation = source.angleVariation,
            startRadius = source.startRadius,
            radiusTaper = source.radiusTaper,
            lengthScale = source.lengthScale,
            generateLeaves = source.generateLeaves,
            leafSize = source.leafSize,
            leafSizeVariation = source.leafSizeVariation,
            leafSegments = source.leafSegments,
            enableWind = source.enableWind,
            windStrength = source.windStrength,
            windFrequency = source.windFrequency
        };
        
        // Copy rules
        if (source.rules != null)
        {
            copy.rules = new LSystemRule[source.rules.Length];
            for (int i = 0; i < source.rules.Length; i++)
            {
                copy.rules[i] = new LSystemRule
                {
                    symbol = source.rules[i].symbol,
                    replacement = source.rules[i].replacement,
                    probability = source.rules[i].probability
                };
            }
        }
        
        // Copy gradients
        if (source.branchGradient != null)
        {
            copy.branchGradient = new Gradient();
            copy.branchGradient.SetKeys(source.branchGradient.colorKeys, source.branchGradient.alphaKeys);
        }
        
        if (source.leafGradient != null)
        {
            copy.leafGradient = new Gradient();
            copy.leafGradient.SetKeys(source.leafGradient.colorKeys, source.leafGradient.alphaKeys);
        }
        
        return copy;
    }
    
    // Preset library with common plant types
    public static LSystemPlantPreset CreateFernPreset()
    {
        var preset = CreateInstance<LSystemPlantPreset>();
        preset.plantName = "Fern";
        preset.description = "A fractal fern using L-Systems";
        preset.settings.axiom = "X";
        preset.settings.rules = new LSystemRule[] {
            new LSystemRule { symbol = 'X', replacement = "F[+X][-X]FX" },
            new LSystemRule { symbol = 'F', replacement = "FF" }
        };
        preset.settings.iterations = 5;
        preset.settings.angleIncrement = 25f;
        preset.settings.segmentLength = 0.15f;
        preset.settings.generateLeaves = true;
        preset.settings.leafSize = 0.08f;
        return preset;
    }
    
    public static LSystemPlantPreset CreateBushPreset()
    {
        var preset = CreateInstance<LSystemPlantPreset>();
        preset.plantName = "Bush";
        preset.description = "A bushy plant with many branches";
        preset.settings.axiom = "F";
        preset.settings.rules = new LSystemRule[] {
            new LSystemRule { symbol = 'F', replacement = "F[+F]F[-F][F]" }
        };
        preset.settings.iterations = 4;
        preset.settings.angleIncrement = 20f;
        preset.settings.segmentLength = 0.25f;
        preset.settings.startRadius = 0.06f;
        preset.settings.generateLeaves = true;
        preset.settings.leafSize = 0.12f;
        return preset;
    }
    
    public static LSystemPlantPreset CreateWeedPreset()
    {
        var preset = CreateInstance<LSystemPlantPreset>();
        preset.plantName = "Weed";
        preset.description = "Simple weed or grass-like plant";
        preset.settings.axiom = "F";
        preset.settings.rules = new LSystemRule[] {
            new LSystemRule { symbol = 'F', replacement = "F[+F]F[-F]F", probability = 0.8f },
            new LSystemRule { symbol = 'F', replacement = "F[+F]F", probability = 0.2f }
        };
        preset.settings.iterations = 3;
        preset.settings.angleIncrement = 30f;
        preset.settings.angleVariation = 10f;
        preset.settings.segmentLength = 0.2f;
        preset.settings.startRadius = 0.03f;
        preset.settings.generateLeaves = true;
        preset.settings.leafSize = 0.06f;
        return preset;
    }
    
    public static LSystemPlantPreset CreateSaplingPreset()
    {
        var preset = CreateInstance<LSystemPlantPreset>();
        preset.plantName = "Sapling";
        preset.description = "Young tree sapling";
        preset.settings.axiom = "X";
        preset.settings.rules = new LSystemRule[] {
            new LSystemRule { symbol = 'X', replacement = "F[+X]F[-X]+X" },
            new LSystemRule { symbol = 'F', replacement = "FF" }
        };
        preset.settings.iterations = 5;
        preset.settings.angleIncrement = 22.5f;
        preset.settings.segmentLength = 0.3f;
        preset.settings.startRadius = 0.05f;
        preset.settings.radiusTaper = 0.9f;
        preset.settings.generateLeaves = true;
        preset.settings.leafSize = 0.1f;
        return preset;
    }
    
    public static LSystemPlantPreset CreateFlowerPreset()
    {
        var preset = CreateInstance<LSystemPlantPreset>();
        preset.plantName = "Flower";
        preset.description = "Flowering plant with radial symmetry";
        preset.settings.axiom = "F";
        preset.settings.rules = new LSystemRule[] {
            new LSystemRule { symbol = 'F', replacement = "FF+[+F-F-F]-[-F+F+F]" }
        };
        preset.settings.iterations = 3;
        preset.settings.angleIncrement = 22.5f;
        preset.settings.segmentLength = 0.15f;
        preset.settings.startRadius = 0.02f;
        preset.settings.generateLeaves = true;
        preset.settings.leafSize = 0.15f;
        preset.settings.leafSegments = 8; // Flower-like leaves
        return preset;
    }
}