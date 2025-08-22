using UnityEngine;
using System.Collections.Generic;
using System.Linq;

#if UNITY_EDITOR
using UnityEditor;
#endif

// Human-readable rule actions
public enum PlantAction
{
    GrowForward,        // F - Grow a segment forward
    Branch,             // [F] - Create a branch
    DoubleBranch,       // [F][F] - Create two branches
    TripleBranch,       // [F][F][F] - Create three branches
    SplitSymmetric,     // [+F][-F] - Split into two symmetric branches
    SplitThreeWay,      // [+F]F[-F] - Split into three directions
    TurnLeft,           // + - Bend left
    TurnRight,          // - - Bend right
    AddLeaf,            // L - Add a leaf
    Nothing,            // Do nothing (for probability)
}

[System.Serializable]
public class SimpleRule
{
    [Header("When this part...")]
    public string partName = "Stem";
    public Color partColor = Color.green;
    
    [Header("...grows, it becomes:")]
    public PlantAction[] actions = new PlantAction[] { PlantAction.GrowForward, PlantAction.Branch };
    
    [Header("Growth Settings")]
    [Range(0f, 100f)] public float chanceToGrow = 100f; // Percentage
    [Range(0f, 50f)] public float bendAngle = 25f; // How much branches bend
    
    [HideInInspector] public char symbol = 'F'; // Internal L-System symbol
}

[System.Serializable]
public class PlantTemplate
{
    public string name = "Custom Plant";
    public string description = "Describe what your plant looks like";
    public Texture2D icon;
    
    [Header("Starting Shape")]
    public PlantAction[] startingStructure = new PlantAction[] { PlantAction.GrowForward };
    
    [Header("Growth Rules")]
    public SimpleRule[] rules = new SimpleRule[] {
        new SimpleRule { partName = "Stem", actions = new PlantAction[] { PlantAction.GrowForward, PlantAction.Branch } }
    };
    
    [Header("How Many Times to Apply Rules")]
    [Range(1, 6)] public int growthCycles = 4;
    
    [Header("Visual Settings")]
    [Range(0.05f, 2f)] public float segmentLength = 0.3f;
    [Range(0.01f, 0.5f)] public float thickness = 0.08f;
    [Range(0.5f, 1f)] public float thicknessReduction = 0.85f; // How much thinner branches get
    
    [Header("Leaves")]
    public bool hasLeaves = true;
    [Range(0.05f, 0.5f)] public float leafSize = 0.15f;
    public Color leafColorMin = new Color(0.3f, 0.6f, 0.2f);
    public Color leafColorMax = new Color(0.4f, 0.7f, 0.3f);
    public bool leavesOnlyAtTips = true;
    
    [Header("Colors")]
    public Color stemColorBottom = new Color(0.4f, 0.3f, 0.2f);
    public Color stemColorTop = new Color(0.5f, 0.4f, 0.3f);
    
    [Header("Randomness")]
    [Range(0f, 30f)] public float angleRandomness = 5f;
    [Range(0f, 50f)] public float sizeRandomness = 20f; // Percentage
}

[ExecuteAlways]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class LSystemPlantDesigner : MonoBehaviour
{
    [Header("=== PLANT DESIGNER ===")]
    [Space(10)]
    
    [Header("Choose a Starting Template:")]
    public PlantTemplateType templateType = PlantTemplateType.Custom;
    
    [Space(10)]
    [Header("Your Plant Design:")]
    public PlantTemplate template = new PlantTemplate();
    
    [Space(10)]
    [Header("Generation")]
    public int randomSeed = 12345;
    public bool autoRegenerate = true;
    public bool showAdvanced = false;
    
    [Header("Materials")]
    public Material branchMaterial;
    public Material leafMaterial;
    
    // Advanced (hidden by default)
    [HideInInspector] public LSystemSettings advancedSettings;
    [HideInInspector] public string generatedLSystemString;
    [HideInInspector] public int branchSides = 6;
    
    // Internal
    Mesh mesh;
    LSystemPlant internalPlant;
    
    public enum PlantTemplateType
    {
        Custom,
        SimpleFern,
        BushyPlant,
        TallGrass,
        YoungTree,
        Flower,
        Vine,
        Coral
    }
    
    void Start()
    {
        SetupInternalPlant();
        Generate();
    }
    
    void OnValidate()
    {
        if (autoRegenerate && Application.isPlaying)
        {
            Generate();
        }
    }
    
    void SetupInternalPlant()
    {
        if (!internalPlant)
        {
            var go = new GameObject("InternalLSystem");
            go.hideFlags = HideFlags.HideAndDontSave;
            go.transform.SetParent(transform, false);
            internalPlant = go.AddComponent<LSystemPlant>();
        }
    }
    
    [ContextMenu("Generate Plant")]
    public void Generate()
    {
        SetupInternalPlant();
        
        // Convert template to L-System
        var lsystem = ConvertTemplateToLSystem(template);
        
        // Apply to internal plant
        internalPlant.settings = lsystem;
        internalPlant.seed = randomSeed;
        internalPlant.branchSides = branchSides;
        internalPlant.branchMaterial = branchMaterial;
        internalPlant.leafMaterial = leafMaterial;
        internalPlant.regenerateOnChange = false;
        
        // Generate
        internalPlant.Generate();
        
        // Copy mesh
        mesh = internalPlant.GetMesh();
        if (mesh)
        {
            GetComponent<MeshFilter>().sharedMesh = mesh;
            GetComponent<MeshRenderer>().sharedMaterials = new Material[] { branchMaterial, leafMaterial };
        }
        
        // Store for debugging
        advancedSettings = lsystem;
        generatedLSystemString = GetLSystemPreview(lsystem);
    }
    
    LSystemSettings ConvertTemplateToLSystem(PlantTemplate tmpl)
    {
        var settings = new LSystemSettings();
        
        // Convert starting structure to axiom
        settings.axiom = ConvertActionsToSymbols(tmpl.startingStructure, tmpl.rules);
        
        // Convert rules
        var lsystemRules = new List<LSystemRule>();
        var usedSymbols = new HashSet<char> { 'F', '+', '-', '[', ']', 'L' };
        char nextSymbol = 'A';
        
        foreach (var rule in tmpl.rules)
        {
            // Assign unique symbol if needed
            if (rule.symbol == '\0' || rule.symbol == 'F')
            {
                while (usedSymbols.Contains(nextSymbol))
                    nextSymbol++;
                rule.symbol = nextSymbol++;
                usedSymbols.Add(rule.symbol);
            }
            
            var lrule = new LSystemRule
            {
                symbol = rule.symbol,
                replacement = ConvertActionsToSymbols(rule.actions, tmpl.rules),
                probability = rule.chanceToGrow / 100f
            };
            
            // Add angle modifiers based on bendAngle
            if (rule.bendAngle > 0)
            {
                lrule.replacement = lrule.replacement.Replace("F", "F");
                // Angles are handled by the angleIncrement setting
            }
            
            lsystemRules.Add(lrule);
        }
        
        settings.rules = lsystemRules.ToArray();
        settings.iterations = tmpl.growthCycles;
        
        // Visual settings
        settings.segmentLength = tmpl.segmentLength;
        settings.angleIncrement = tmpl.rules.Length > 0 ? tmpl.rules[0].bendAngle : 25f;
        settings.angleVariation = tmpl.angleRandomness;
        settings.startRadius = tmpl.thickness;
        settings.radiusTaper = tmpl.thicknessReduction;
        settings.lengthScale = 0.9f;
        
        // Leaves
        settings.generateLeaves = tmpl.hasLeaves;
        settings.leafSize = tmpl.leafSize;
        settings.leafSizeVariation = 1f + (tmpl.sizeRandomness / 100f);
        
        // Colors
        settings.branchGradient = new Gradient();
        settings.branchGradient.SetKeys(
            new GradientColorKey[] {
                new GradientColorKey(tmpl.stemColorBottom, 0f),
                new GradientColorKey(tmpl.stemColorTop, 1f)
            },
            new GradientAlphaKey[] {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 1f)
            }
        );
        
        settings.leafGradient = new Gradient();
        settings.leafGradient.SetKeys(
            new GradientColorKey[] {
                new GradientColorKey(tmpl.leafColorMin, 0f),
                new GradientColorKey(tmpl.leafColorMax, 1f)
            },
            new GradientAlphaKey[] {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 1f)
            }
        );
        
        return settings;
    }
    
    string ConvertActionsToSymbols(PlantAction[] actions, SimpleRule[] rules)
    {
        var result = "";
        
        foreach (var action in actions)
        {
            switch (action)
            {
                case PlantAction.GrowForward:
                    result += "F";
                    break;
                case PlantAction.Branch:
                    result += "[F]";
                    break;
                case PlantAction.DoubleBranch:
                    result += "[+F][-F]";
                    break;
                case PlantAction.TripleBranch:
                    result += "[+F][F][-F]";
                    break;
                case PlantAction.SplitSymmetric:
                    result += "[+F][-F]";
                    break;
                case PlantAction.SplitThreeWay:
                    result += "[+F]F[-F]";
                    break;
                case PlantAction.TurnLeft:
                    result += "+";
                    break;
                case PlantAction.TurnRight:
                    result += "-";
                    break;
                case PlantAction.AddLeaf:
                    result += "L";
                    break;
                case PlantAction.Nothing:
                    // Used for stochastic rules
                    break;
            }
        }
        
        // Replace F with custom symbols where rules exist
        foreach (var rule in rules)
        {
            if (rule.symbol != '\0' && rule.symbol != 'F')
            {
                // First occurrence becomes the custom symbol
                var index = result.IndexOf('F');
                if (index >= 0)
                {
                    result = result.Remove(index, 1).Insert(index, rule.symbol.ToString());
                    break;
                }
            }
        }
        
        return result;
    }
    
    string GetLSystemPreview(LSystemSettings settings)
    {
        string preview = $"Starting: {settings.axiom}\n";
        preview += "Rules:\n";
        foreach (var rule in settings.rules)
        {
            preview += $"  {rule.symbol} → {rule.replacement} ({rule.probability * 100}%)\n";
        }
        return preview;
    }
    
    [ContextMenu("Load Fern Template")]
    public void LoadFernTemplate()
    {
        template = new PlantTemplate
        {
            name = "Fern",
            description = "A delicate fern with fractal fronds",
            startingStructure = new PlantAction[] { PlantAction.GrowForward },
            rules = new SimpleRule[] {
                new SimpleRule {
                    partName = "Frond",
                    actions = new PlantAction[] {
                        PlantAction.GrowForward,
                        PlantAction.SplitSymmetric,
                        PlantAction.GrowForward
                    },
                    chanceToGrow = 100f,
                    bendAngle = 25f
                }
            },
            growthCycles = 5,
            segmentLength = 0.15f,
            thickness = 0.04f,
            hasLeaves = true,
            leafSize = 0.08f,
            leavesOnlyAtTips = true,
            leafColorMin = new Color(0.2f, 0.5f, 0.1f),
            leafColorMax = new Color(0.3f, 0.6f, 0.2f)
        };
        Generate();
    }
    
    [ContextMenu("Load Bush Template")]
    public void LoadBushTemplate()
    {
        template = new PlantTemplate
        {
            name = "Bush",
            description = "A bushy plant with many branches",
            startingStructure = new PlantAction[] { PlantAction.GrowForward },
            rules = new SimpleRule[] {
                new SimpleRule {
                    partName = "Branch",
                    actions = new PlantAction[] {
                        PlantAction.GrowForward,
                        PlantAction.DoubleBranch,
                        PlantAction.GrowForward
                    },
                    chanceToGrow = 90f,
                    bendAngle = 20f
                }
            },
            growthCycles = 4,
            segmentLength = 0.25f,
            thickness = 0.06f,
            hasLeaves = true,
            leafSize = 0.12f,
            leafColorMin = new Color(0.3f, 0.5f, 0.2f),
            leafColorMax = new Color(0.4f, 0.6f, 0.3f)
        };
        Generate();
    }
    
    [ContextMenu("Load Grass Template")]
    public void LoadGrassTemplate()
    {
        template = new PlantTemplate
        {
            name = "Tall Grass",
            description = "Simple grass that sways in the wind",
            startingStructure = new PlantAction[] { PlantAction.GrowForward },
            rules = new SimpleRule[] {
                new SimpleRule {
                    partName = "Blade",
                    actions = new PlantAction[] {
                        PlantAction.GrowForward,
                        PlantAction.GrowForward,
                        PlantAction.TurnLeft
                    },
                    chanceToGrow = 100f,
                    bendAngle = 5f
                }
            },
            growthCycles = 3,
            segmentLength = 0.4f,
            thickness = 0.02f,
            hasLeaves = false
        };
        Generate();
    }
    
    public Mesh GetMesh() => mesh;
}

#if UNITY_EDITOR
[CustomEditor(typeof(LSystemPlantDesigner))]
public class LSystemPlantDesignerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var designer = (LSystemPlantDesigner)target;
        
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("🌿 PLANT DESIGNER", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Design your plant using simple, understandable rules!\n" +
            "No L-System knowledge required.", 
            MessageType.Info
        );
        
        EditorGUILayout.Space(10);
        
        // Template selector
        EditorGUILayout.LabelField("Quick Templates", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("🌿 Fern")) designer.LoadFernTemplate();
        if (GUILayout.Button("🌳 Bush")) designer.LoadBushTemplate();
        if (GUILayout.Button("🌾 Grass")) designer.LoadGrassTemplate();
        EditorGUILayout.EndHorizontal();
        
        EditorGUILayout.Space(10);
        
        // Main template editing
        SerializedProperty templateProp = serializedObject.FindProperty("template");
        EditorGUILayout.PropertyField(templateProp, new GUIContent("Plant Design"), true);
        
        EditorGUILayout.Space(10);
        
        // Generation settings
        EditorGUILayout.PropertyField(serializedObject.FindProperty("randomSeed"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("autoRegenerate"));
        
        EditorGUILayout.Space(10);
        
        // Materials
        EditorGUILayout.PropertyField(serializedObject.FindProperty("branchMaterial"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("leafMaterial"));
        
        EditorGUILayout.Space(10);
        
        // Generate button
        if (GUILayout.Button("🌱 Generate Plant", GUILayout.Height(30)))
        {
            designer.Generate();
        }
        
        // Advanced section
        designer.showAdvanced = EditorGUILayout.Foldout(designer.showAdvanced, "Advanced (L-System Details)");
        if (designer.showAdvanced)
        {
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.TextArea(designer.generatedLSystemString, GUILayout.Height(80));
            EditorGUI.EndDisabledGroup();
            
            EditorGUILayout.HelpBox(
                "This shows the L-System rules generated from your design.\n" +
                "You don't need to understand this - it's just for debugging!",
                MessageType.None
            );
        }
        
        serializedObject.ApplyModifiedProperties();
    }
}

// Property drawer for PlantAction arrays to make them more visual
[CustomPropertyDrawer(typeof(PlantAction[]))]
public class PlantActionArrayDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.PropertyField(position, property, label, true);
    }
}
#endif