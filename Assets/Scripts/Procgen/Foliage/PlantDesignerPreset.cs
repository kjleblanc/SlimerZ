using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[CreateAssetMenu(menuName = "Procgen/Plant Designer Preset", fileName = "PlantDesign")]
public class PlantDesignerPreset : ScriptableObject
{
    [Header("Plant Info")]
    public string plantName = "Custom Plant";
    [TextArea(3, 5)]
    public string description = "Describe what your plant looks like and where it grows";
    public Texture2D previewImage;
    
    [Header("The Design")]
    public PlantTemplate template = new PlantTemplate();
    
    [Header("Environment Preferences")]
    [Range(0f, 1f)] public float preferredMoisture = 0.5f;
    [Range(0f, 1f)] public float moistureTolerance = 0.3f;
    [Range(0f, 1f)] public float maxSlope = 0.7f;
    
    [Header("Spawn Settings")]
    [Range(0.1f, 3f)] public float averageSize = 1f;
    [Range(0f, 50f)] public float sizeVariation = 20f; // Percentage
    
    // Apply to a designer
    public void ApplyToDesigner(LSystemPlantDesigner designer)
    {
        if (designer == null) return;
        
        // Deep copy the template
        designer.template = CopyTemplate(template);
        designer.Generate();
    }
    
    // Copy from designer
    public void CopyFromDesigner(LSystemPlantDesigner designer)
    {
        if (designer == null) return;
        
        template = CopyTemplate(designer.template);
        
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }
    
    static PlantTemplate CopyTemplate(PlantTemplate source)
    {
        var copy = new PlantTemplate
        {
            name = source.name,
            description = source.description,
            icon = source.icon,
            startingStructure = (PlantAction[])source.startingStructure.Clone(),
            growthCycles = source.growthCycles,
            segmentLength = source.segmentLength,
            thickness = source.thickness,
            thicknessReduction = source.thicknessReduction,
            hasLeaves = source.hasLeaves,
            leafSize = source.leafSize,
            leafColorMin = source.leafColorMin,
            leafColorMax = source.leafColorMax,
            leavesOnlyAtTips = source.leavesOnlyAtTips,
            stemColorBottom = source.stemColorBottom,
            stemColorTop = source.stemColorTop,
            angleRandomness = source.angleRandomness,
            sizeRandomness = source.sizeRandomness
        };
        
        // Deep copy rules
        if (source.rules != null)
        {
            copy.rules = new SimpleRule[source.rules.Length];
            for (int i = 0; i < source.rules.Length; i++)
            {
                copy.rules[i] = new SimpleRule
                {
                    partName = source.rules[i].partName,
                    partColor = source.rules[i].partColor,
                    actions = (PlantAction[])source.rules[i].actions.Clone(),
                    chanceToGrow = source.rules[i].chanceToGrow,
                    bendAngle = source.rules[i].bendAngle,
                    symbol = source.rules[i].symbol
                };
            }
        }
        
        return copy;
    }
    
    // Create common plant presets with understandable settings
    public static PlantDesignerPreset CreateFernPreset()
    {
        var preset = CreateInstance<PlantDesignerPreset>();
        preset.plantName = "Forest Fern";
        preset.description = "A delicate fern that grows in shaded, moist areas. Each frond splits into smaller fronds creating a fractal pattern.";
        
        preset.template.name = "Fern";
        preset.template.startingStructure = new[] { PlantAction.GrowForward };
        preset.template.rules = new[] {
            new SimpleRule {
                partName = "Main Stem",
                partColor = Color.green,
                actions = new[] { 
                    PlantAction.GrowForward,
                    PlantAction.SplitSymmetric,  // Creates the characteristic fern shape
                    PlantAction.GrowForward
                },
                chanceToGrow = 100f,
                bendAngle = 30f
            }
        };
        preset.template.growthCycles = 5;
        preset.template.segmentLength = 0.15f;
        preset.template.hasLeaves = true;
        preset.template.leafSize = 0.08f;
        
        preset.preferredMoisture = 0.7f;
        preset.moistureTolerance = 0.2f;
        preset.maxSlope = 0.6f;
        
        return preset;
    }
    
    public static PlantDesignerPreset CreateBushPreset()
    {
        var preset = CreateInstance<PlantDesignerPreset>();
        preset.plantName = "Dense Bush";
        preset.description = "A bushy plant that branches heavily, creating a round, full appearance. Good for filling spaces.";
        
        preset.template.name = "Bush";
        preset.template.startingStructure = new[] { PlantAction.GrowForward };
        preset.template.rules = new[] {
            new SimpleRule {
                partName = "Branch",
                partColor = new Color(0.4f, 0.3f, 0.2f),
                actions = new[] { 
                    PlantAction.GrowForward,
                    PlantAction.DoubleBranch,  // Creates bushy appearance
                    PlantAction.GrowForward
                },
                chanceToGrow = 85f,
                bendAngle = 25f
            }
        };
        preset.template.growthCycles = 4;
        preset.template.thickness = 0.06f;
        preset.template.hasLeaves = true;
        preset.template.leafSize = 0.12f;
        
        preset.preferredMoisture = 0.5f;
        preset.moistureTolerance = 0.4f;
        
        return preset;
    }
    
    public static PlantDesignerPreset CreateGrassPreset()
    {
        var preset = CreateInstance<PlantDesignerPreset>();
        preset.plantName = "Wild Grass";
        preset.description = "Tall grass blades that grow straight up with slight curves. Sways naturally in wind.";
        
        preset.template.name = "Grass";
        preset.template.startingStructure = new[] { PlantAction.GrowForward };
        preset.template.rules = new[] {
            new SimpleRule {
                partName = "Blade",
                partColor = new Color(0.3f, 0.6f, 0.2f),
                actions = new[] { 
                    PlantAction.GrowForward,
                    PlantAction.GrowForward,
                    PlantAction.TurnLeft  // Slight bend
                },
                chanceToGrow = 100f,
                bendAngle = 5f
            }
        };
        preset.template.growthCycles = 3;
        preset.template.segmentLength = 0.4f;
        preset.template.thickness = 0.02f;
        preset.template.hasLeaves = false;
        
        preset.preferredMoisture = 0.6f;
        preset.maxSlope = 0.8f;
        
        return preset;
    }
    
    public static PlantDesignerPreset CreateFloweringPlantPreset()
    {
        var preset = CreateInstance<PlantDesignerPreset>();
        preset.plantName = "Wildflower";
        preset.description = "A flowering plant with colorful petals at the tips. Grows in open meadows.";
        
        preset.template.name = "Flower";
        preset.template.startingStructure = new[] { PlantAction.GrowForward };
        preset.template.rules = new[] {
            new SimpleRule {
                partName = "Stem",
                partColor = Color.green,
                actions = new[] { 
                    PlantAction.GrowForward,
                    PlantAction.GrowForward,
                    PlantAction.TripleBranch  // Flower head
                },
                chanceToGrow = 90f,
                bendAngle = 15f
            }
        };
        preset.template.growthCycles = 3;
        preset.template.hasLeaves = true;
        preset.template.leafSize = 0.15f;
        preset.template.leafColorMin = new Color(0.8f, 0.3f, 0.5f); // Pink petals
        preset.template.leafColorMax = new Color(0.9f, 0.4f, 0.6f);
        
        preset.preferredMoisture = 0.4f;
        preset.maxSlope = 0.5f;
        
        return preset;
    }
    
    public static PlantDesignerPreset CreateMushroomPreset()
    {
        var preset = CreateInstance<PlantDesignerPreset>();
        preset.plantName = "Forest Mushroom";
        preset.description = "Not technically a plant, but grows in damp, shaded areas. Simple structure with a cap on top.";
        
        preset.template.name = "Mushroom";
        preset.template.startingStructure = new[] { PlantAction.GrowForward };
        preset.template.rules = new[] {
            new SimpleRule {
                partName = "Stalk",
                partColor = new Color(0.9f, 0.9f, 0.8f),
                actions = new[] { 
                    PlantAction.GrowForward,
                    PlantAction.GrowForward,
                    PlantAction.AddLeaf  // Cap
                },
                chanceToGrow = 100f,
                bendAngle = 0f
            }
        };
        preset.template.growthCycles = 2;
        preset.template.segmentLength = 0.2f;
        preset.template.thickness = 0.1f;
        preset.template.hasLeaves = true;
        preset.template.leafSize = 0.3f;
        preset.template.leafColorMin = new Color(0.6f, 0.4f, 0.3f); // Brown cap
        preset.template.leafColorMax = new Color(0.7f, 0.5f, 0.4f);
        
        preset.preferredMoisture = 0.8f;
        preset.moistureTolerance = 0.1f;
        preset.maxSlope = 0.4f;
        
        return preset;
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(PlantDesignerPreset))]
public class PlantDesignerPresetEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var preset = (PlantDesignerPreset)target;
        
        EditorGUILayout.LabelField("🌿 Plant Design Preset", EditorStyles.boldLabel);
        EditorGUILayout.Space(5);
        
        // Info section
        EditorGUILayout.PropertyField(serializedObject.FindProperty("plantName"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("description"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("previewImage"));
        
        EditorGUILayout.Space(10);
        
        // Template section with help
        EditorGUILayout.LabelField("Plant Structure", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Design how your plant grows:\n" +
            "• Starting Structure: The initial shape\n" +
            "• Rules: How each part transforms as it grows\n" +
            "• Growth Cycles: How many times to apply the rules",
            MessageType.None
        );
        EditorGUILayout.PropertyField(serializedObject.FindProperty("template"), true);
        
        EditorGUILayout.Space(10);
        
        // Environment section
        EditorGUILayout.LabelField("Environment Preferences", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("preferredMoisture"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("moistureTolerance"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("maxSlope"));
        
        EditorGUILayout.Space(10);
        
        // Spawn settings
        EditorGUILayout.LabelField("Spawn Settings", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("averageSize"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("sizeVariation"));
        
        serializedObject.ApplyModifiedProperties();
        
        EditorGUILayout.Space(10);
        
        // Preview button
        if (GUILayout.Button("Preview This Plant", GUILayout.Height(30)))
        {
            PreviewPlant(preset);
        }
    }
    
    void PreviewPlant(PlantDesignerPreset preset)
    {
        // Create a temporary GameObject with the designer
        var previewGO = new GameObject("Plant Preview");
        var designer = previewGO.AddComponent<LSystemPlantDesigner>();
        preset.ApplyToDesigner(designer);
        
        // Select it so user can see it
        Selection.activeGameObject = previewGO;
        SceneView.FrameLastActiveSceneView();
    }
}
#endif