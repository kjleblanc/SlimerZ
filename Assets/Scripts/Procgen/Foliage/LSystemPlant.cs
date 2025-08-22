using UnityEngine;
using System.Collections.Generic;
using System.Text;

[System.Serializable]
public class LSystemRule
{
    public char symbol = 'F';
    public string replacement = "F[+F]F[-F]F";
    [Range(0f, 1f)] public float probability = 1f; // For stochastic L-Systems
}

[System.Serializable]
public class LSystemSettings
{
    [Header("L-System Grammar")]
    public string axiom = "X";
    public LSystemRule[] rules = new LSystemRule[] {
        new LSystemRule { symbol = 'X', replacement = "F[+X]F[-X]+X" },
        new LSystemRule { symbol = 'F', replacement = "FF" }
    };
    [Range(1, 6)] public int iterations = 4;
    
    [Header("Interpretation")]
    [Range(0.05f, 2f)] public float segmentLength = 0.3f;
    [Range(5f, 90f)] public float angleIncrement = 25f;
    [Range(0f, 30f)] public float angleVariation = 5f; // Random variation
    
    [Header("Branch Appearance")]
    [Range(0.01f, 0.5f)] public float startRadius = 0.08f;
    [Range(0.5f, 1f)] public float radiusTaper = 0.85f; // Per branch level
    [Range(0.8f, 1f)] public float lengthScale = 0.9f; // Per branch level
    
    [Header("Leaves")]
    public bool generateLeaves = true;
    [Range(0.05f, 0.5f)] public float leafSize = 0.15f;
    [Range(0.5f, 2f)] public float leafSizeVariation = 1.2f;
    public int leafSegments = 4; // Quad by default
    
    [Header("Colors")]
    public Gradient branchGradient;
    public Gradient leafGradient;
    
    [Header("Wind")]
    public bool enableWind = true;
    public float windStrength = 0.1f;
    public float windFrequency = 1f;
}

[ExecuteAlways]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class LSystemPlant : MonoBehaviour
{
    [Header("Settings")]
    public LSystemSettings settings = new LSystemSettings();
    public int seed = 12345;
    
    [Header("Mesh Generation")]
    [Range(3, 12)] public int branchSides = 6;
    public bool flatShadedLeaves = false;
    public bool combineMeshes = true;
    
    [Header("Materials")]
    public Material branchMaterial;
    public Material leafMaterial;
    
    [Header("Debug")]
    public bool showGizmos = false;
    public bool regenerateOnChange = true;
    
    // Internal
    System.Random rng;
    string currentString;
    Mesh mesh;
    List<Vector3> vertices = new List<Vector3>();
    List<Vector3> normals = new List<Vector3>();
    List<Vector2> uvs = new List<Vector2>();
    List<Color> colors = new List<Color>();
    List<int> branchTriangles = new List<int>();
    List<int> leafTriangles = new List<int>();
    
    struct TurtleState
    {
        public Vector3 position;
        public Quaternion rotation;
        public float radius;
        public float length;
        public int depth;
    }
    
    void Start()
    {
        EnsureDefaults();
        Generate();
    }
    
    void OnValidate()
    {
        if (regenerateOnChange && Application.isPlaying)
            Generate();
    }
    
    void EnsureDefaults()
    {
        if (settings.branchGradient == null || settings.branchGradient.colorKeys.Length == 0)
        {
            settings.branchGradient = new Gradient();
            settings.branchGradient.SetKeys(
                new GradientColorKey[] { 
                    new GradientColorKey(new Color(0.4f, 0.3f, 0.2f), 0f),
                    new GradientColorKey(new Color(0.5f, 0.4f, 0.3f), 1f)
                },
                new GradientAlphaKey[] { 
                    new GradientAlphaKey(1f, 0f), 
                    new GradientAlphaKey(1f, 1f) 
                }
            );
        }
        
        if (settings.leafGradient == null || settings.leafGradient.colorKeys.Length == 0)
        {
            settings.leafGradient = new Gradient();
            settings.leafGradient.SetKeys(
                new GradientColorKey[] { 
                    new GradientColorKey(new Color(0.3f, 0.6f, 0.2f), 0f),
                    new GradientColorKey(new Color(0.4f, 0.7f, 0.3f), 1f)
                },
                new GradientAlphaKey[] { 
                    new GradientAlphaKey(1f, 0f), 
                    new GradientAlphaKey(1f, 1f) 
                }
            );
        }
        
        if (!branchMaterial)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            branchMaterial = new Material(shader) { name = "M_PlantBranch" };
        }
        
        if (!leafMaterial)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            leafMaterial = new Material(shader) { name = "M_PlantLeaf" };
            leafMaterial.doubleSidedGI = true;
            leafMaterial.SetFloat("_Cull", 0); // Disable culling for leaves
        }
    }
    
    [ContextMenu("Generate Plant")]
    public void Generate()
    {
        EnsureDefaults();
        rng = new System.Random(seed);
        
        // Generate L-System string
        currentString = GenerateLSystemString();
        
        // Build mesh from string
        BuildMesh();
    }
    
    string GenerateLSystemString()
    {
        string current = settings.axiom;
        
        for (int iteration = 0; iteration < settings.iterations; iteration++)
        {
            StringBuilder next = new StringBuilder();
            
            for (int i = 0; i < current.Length; i++)
            {
                char c = current[i];
                bool replaced = false;
                
                // Check all rules for this symbol
                List<LSystemRule> applicableRules = new List<LSystemRule>();
                foreach (var rule in settings.rules)
                {
                    if (rule.symbol == c && rng.NextDouble() <= rule.probability)
                        applicableRules.Add(rule);
                }
                
                if (applicableRules.Count > 0)
                {
                    // Pick a random applicable rule (for stochastic L-Systems)
                    var chosenRule = applicableRules[rng.Next(applicableRules.Count)];
                    next.Append(chosenRule.replacement);
                    replaced = true;
                }
                
                if (!replaced)
                    next.Append(c);
            }
            
            current = next.ToString();
        }
        
        return current;
    }
    
    void BuildMesh()
    {
        // Clear mesh data
        vertices.Clear();
        normals.Clear();
        uvs.Clear();
        colors.Clear();
        branchTriangles.Clear();
        leafTriangles.Clear();
        
        // Turtle graphics interpretation
        Stack<TurtleState> stateStack = new Stack<TurtleState>();
        TurtleState turtle = new TurtleState
        {
            position = Vector3.zero,
            rotation = Quaternion.identity,
            radius = settings.startRadius,
            length = settings.segmentLength,
            depth = 0
        };
        
        List<Vector3> currentBranchPath = new List<Vector3>();
        currentBranchPath.Add(turtle.position);
        
        for (int i = 0; i < currentString.Length; i++)
        {
            char c = currentString[i];
            
            switch (c)
            {
                case 'F': // Draw forward
                case 'X': // Also draw for X in some L-Systems
                case 'Y':
                    Vector3 newPos = turtle.position + turtle.rotation * Vector3.up * turtle.length;
                    
                    // Add branch segment
                    if (c == 'F' || (c == 'X' && i == currentString.Length - 1))
                    {
                        AddBranchSegment(turtle.position, newPos, turtle.radius, turtle.depth);
                        currentBranchPath.Add(newPos);
                    }
                    
                    turtle.position = newPos;
                    break;
                    
                case '+': // Turn right
                    float rightAngle = settings.angleIncrement + (float)(rng.NextDouble() - 0.5) * settings.angleVariation;
                    turtle.rotation *= Quaternion.Euler(0, 0, -rightAngle);
                    break;
                    
                case '-': // Turn left
                    float leftAngle = settings.angleIncrement + (float)(rng.NextDouble() - 0.5) * settings.angleVariation;
                    turtle.rotation *= Quaternion.Euler(0, 0, leftAngle);
                    break;
                    
                case '&': // Pitch down
                    float pitchDown = settings.angleIncrement + (float)(rng.NextDouble() - 0.5) * settings.angleVariation;
                    turtle.rotation *= Quaternion.Euler(pitchDown, 0, 0);
                    break;
                    
                case '^': // Pitch up
                    float pitchUp = settings.angleIncrement + (float)(rng.NextDouble() - 0.5) * settings.angleVariation;
                    turtle.rotation *= Quaternion.Euler(-pitchUp, 0, 0);
                    break;
                    
                case '\\': // Roll left
                    float rollLeft = settings.angleIncrement + (float)(rng.NextDouble() - 0.5) * settings.angleVariation;
                    turtle.rotation *= Quaternion.Euler(0, rollLeft, 0);
                    break;
                    
                case '/': // Roll right
                    float rollRight = settings.angleIncrement + (float)(rng.NextDouble() - 0.5) * settings.angleVariation;
                    turtle.rotation *= Quaternion.Euler(0, -rollRight, 0);
                    break;
                    
                case '[': // Push state
                    stateStack.Push(turtle);
                    turtle.depth++;
                    turtle.radius *= settings.radiusTaper;
                    turtle.length *= settings.lengthScale;
                    currentBranchPath.Clear();
                    currentBranchPath.Add(turtle.position);
                    break;
                    
                case ']': // Pop state
                    if (stateStack.Count > 0)
                    {
                        // Add leaf at branch tip if enabled
                        if (settings.generateLeaves && currentBranchPath.Count > 1)
                        {
                            AddLeaf(turtle.position, turtle.rotation, turtle.depth);
                        }
                        
                        turtle = stateStack.Pop();
                        currentBranchPath.Clear();
                        currentBranchPath.Add(turtle.position);
                    }
                    break;
                    
                case 'L': // Explicit leaf
                    if (settings.generateLeaves)
                    {
                        AddLeaf(turtle.position, turtle.rotation, turtle.depth);
                    }
                    break;
            }
        }
        
        // Build final mesh
        ApplyMesh();
    }
    
    void AddBranchSegment(Vector3 start, Vector3 end, float radius, int depth)
    {
        Vector3 direction = (end - start).normalized;
        if (direction.magnitude < 0.001f) return;
        
        // Create ring vertices
        int startIndex = vertices.Count;
        Quaternion rotation = Quaternion.LookRotation(direction);
        
        float depthT = Mathf.Clamp01(depth / 5f);
        Color branchColor = settings.branchGradient.Evaluate(depthT);
        
        // Add vertices for cylinder segment
        for (int i = 0; i <= branchSides; i++)
        {
            float angle = (float)i / branchSides * Mathf.PI * 2f;
            Vector3 offset = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * radius;
            
            // Start ring
            vertices.Add(start + rotation * offset);
            normals.Add((rotation * offset).normalized);
            uvs.Add(new Vector2((float)i / branchSides, 0));
            colors.Add(branchColor);
            
            // End ring
            vertices.Add(end + rotation * offset);
            normals.Add((rotation * offset).normalized);
            uvs.Add(new Vector2((float)i / branchSides, 1));
            colors.Add(branchColor);
        }
        
        // Add triangles
        for (int i = 0; i < branchSides; i++)
        {
            int idx = startIndex + i * 2;
            
            branchTriangles.Add(idx);
            branchTriangles.Add(idx + 2);
            branchTriangles.Add(idx + 1);
            
            branchTriangles.Add(idx + 1);
            branchTriangles.Add(idx + 2);
            branchTriangles.Add(idx + 3);
        }
    }
    
    void AddLeaf(Vector3 position, Quaternion rotation, int depth)
    {
        float sizeMultiplier = (float)(rng.NextDouble() * 0.5 + 0.75) * settings.leafSizeVariation;
        float size = settings.leafSize * sizeMultiplier;
        
        float depthT = Mathf.Clamp01(depth / 5f);
        Color leafColor = settings.leafGradient.Evaluate((float)rng.NextDouble());
        
        int startIndex = vertices.Count;
        
        if (settings.leafSegments <= 4)
        {
            // Simple quad leaf
            vertices.Add(position + rotation * new Vector3(-size/2, 0, 0));
            vertices.Add(position + rotation * new Vector3(size/2, 0, 0));
            vertices.Add(position + rotation * new Vector3(-size/2, size, 0));
            vertices.Add(position + rotation * new Vector3(size/2, size, 0));
            
            Vector3 normal = rotation * Vector3.forward;
            normals.Add(normal);
            normals.Add(normal);
            normals.Add(normal);
            normals.Add(normal);
            
            uvs.Add(new Vector2(0, 0));
            uvs.Add(new Vector2(1, 0));
            uvs.Add(new Vector2(0, 1));
            uvs.Add(new Vector2(1, 1));
            
            colors.Add(leafColor);
            colors.Add(leafColor);
            colors.Add(leafColor);
            colors.Add(leafColor);
            
            leafTriangles.Add(startIndex);
            leafTriangles.Add(startIndex + 2);
            leafTriangles.Add(startIndex + 1);
            
            leafTriangles.Add(startIndex + 1);
            leafTriangles.Add(startIndex + 2);
            leafTriangles.Add(startIndex + 3);
        }
        else
        {
            // More complex leaf shape (diamond/star)
            Vector3 center = position + rotation * new Vector3(0, size/2, 0);
            vertices.Add(center);
            normals.Add(rotation * Vector3.forward);
            uvs.Add(new Vector2(0.5f, 0.5f));
            colors.Add(leafColor);
            
            for (int i = 0; i < settings.leafSegments; i++)
            {
                float angle = (float)i / settings.leafSegments * Mathf.PI * 2f;
                float nextAngle = (float)(i + 1) / settings.leafSegments * Mathf.PI * 2f;
                
                float radiusModifier = (i % 2 == 0) ? 1f : 0.6f; // Star shape
                
                Vector3 offset1 = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0) * size/2 * radiusModifier;
                Vector3 offset2 = new Vector3(Mathf.Cos(nextAngle), Mathf.Sin(nextAngle), 0) * size/2 * ((i + 1) % 2 == 0 ? 1f : 0.6f);
                
                vertices.Add(center + rotation * offset1);
                vertices.Add(center + rotation * offset2);
                
                normals.Add(rotation * Vector3.forward);
                normals.Add(rotation * Vector3.forward);
                
                uvs.Add(new Vector2(Mathf.Cos(angle) * 0.5f + 0.5f, Mathf.Sin(angle) * 0.5f + 0.5f));
                uvs.Add(new Vector2(Mathf.Cos(nextAngle) * 0.5f + 0.5f, Mathf.Sin(nextAngle) * 0.5f + 0.5f));
                
                colors.Add(leafColor);
                colors.Add(leafColor);
                
                leafTriangles.Add(startIndex);
                leafTriangles.Add(startIndex + 1 + i * 2);
                leafTriangles.Add(startIndex + 2 + i * 2);
            }
        }
    }
    
    void ApplyMesh()
    {
        if (mesh == null)
        {
            mesh = new Mesh();
            mesh.name = "LSystemPlant";
        }
        else
        {
            mesh.Clear();
        }
        
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetColors(colors);
        
        if (combineMeshes)
        {
            // Single mesh with submeshes
            mesh.subMeshCount = 2;
            mesh.SetTriangles(branchTriangles, 0);
            mesh.SetTriangles(leafTriangles, 1);
            
            var mf = GetComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            
            var mr = GetComponent<MeshRenderer>();
            mr.sharedMaterials = new Material[] { branchMaterial, leafMaterial };
        }
        else
        {
            // Branches only in main mesh
            mesh.SetTriangles(branchTriangles, 0);
            
            var mf = GetComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            
            var mr = GetComponent<MeshRenderer>();
            mr.sharedMaterial = branchMaterial;
        }
        
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
    }
    
    void OnDrawGizmosSelected()
    {
        if (!showGizmos) return;
        
        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(transform.position + Vector3.up * settings.segmentLength * 2, 
                           Vector3.one * settings.segmentLength * 4);
    }
    
    public Mesh GetMesh() => mesh;
    public Bounds GetBounds() => mesh ? mesh.bounds : new Bounds();
}