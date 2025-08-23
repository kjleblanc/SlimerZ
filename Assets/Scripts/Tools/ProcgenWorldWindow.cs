// Assets/Editor/ProcgenWorldWindow.cs
// A unified control panel for your procedural world.
// Open via: Tools/Procgen World Window

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System;

public class ProcgenWorldWindow : EditorWindow
{
    // --- State ---
    ProcgenWorld world;
    ProcgenCullingHub hub;
    Vector2 scroll;
    bool foldScene, foldWorld = true, foldTerrain = true, foldWater = true, foldSpawners = true, foldCulling = true, foldActions = true;

    [MenuItem("Tools/Procgen World Window")]
    public static void ShowWindow()
    {
        var w = GetWindow<ProcgenWorldWindow>("Procgen World");
        w.minSize = new Vector2(360, 420);
        w.FindRefs();
    }

    void OnFocus()  => FindRefs();
    void OnHierarchyChange() => Repaint();
    void OnProjectChange() => Repaint();

    void FindRefs()
    {
        if (!world) world = FindFirstObjectByType<ProcgenWorld>();
        if (!hub)   hub   = FindFirstObjectByType<ProcgenCullingHub>();
        Repaint();
    }

    public override void SaveChanges() => base.SaveChanges();

    void OnGUI()
    {
        using (new EditorGUILayout.VerticalScope())
        {
            // Header / Scene
            foldScene = EditorGUILayout.BeginFoldoutHeaderGroup(foldScene, "Scene");
            if (foldScene)
            {
                EditorGUILayout.ObjectField("World", world, typeof(ProcgenWorld), true);
                EditorGUILayout.ObjectField("Culling Hub", hub, typeof(ProcgenCullingHub), true);

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(world ? "Select World" : "Create World", GUILayout.Height(22)))
                {
                    if (!world)
                    {
                        var go = new GameObject("ProcgenWorld");
                        Undo.RegisterCreatedObjectUndo(go, "Create ProcgenWorld");
                        world = go.AddComponent<ProcgenWorld>();
                    }
                    Selection.activeObject = world;
                }
                if (GUILayout.Button(hub ? "Select Hub" : "Create Hub", GUILayout.Height(22)))
                {
                    if (!hub)
                    {
                        var go = new GameObject("ProcgenCullingHub");
                        Undo.RegisterCreatedObjectUndo(go, "Create ProcgenCullingHub");
                        hub = go.AddComponent<ProcgenCullingHub>();
                    }
                    Selection.activeObject = hub;
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(4);
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            if (!world)
            {
                EditorGUILayout.HelpBox("No ProcgenWorld in the scene. Click “Create World”.", MessageType.Info);
                return;
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);

            // PRESET
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Preset", EditorStyles.boldLabel);

            world.usePreset = EditorGUILayout.Toggle("Use Preset", world.usePreset);
            world.preset = (WorldPreset)EditorGUILayout.ObjectField("World Preset", world.preset, typeof(WorldPreset), false);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.enabled = world && world.preset && world.usePreset;
                if (GUILayout.Button("Apply Preset"))
                {
                    world.ApplyPresetToScene();
                    EditorUtility.SetDirty(world);
                    SceneView.RepaintAll();
                }
                if (GUILayout.Button("Apply + Rebuild"))
                {
                    world.RebuildFromPreset();
                    hub?.NotifyDirty();
                    SceneView.RepaintAll();
                }
                GUI.enabled = true;
            }
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);


            // WORLD
            foldWorld = EditorGUILayout.BeginFoldoutHeaderGroup(foldWorld, "World");
            if (foldWorld)
            {
                Undo.RecordObject(world, "World Settings");
                world.autorunInEditor = EditorGUILayout.Toggle(new GUIContent("Autorun in Editor", "Auto rebuild when values change in Edit Mode"), world.autorunInEditor);
                world.seed = EditorGUILayout.IntField("Seed", world.seed);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Randomize", GUILayout.Width(100)))
                    {
                        world.seed = UnityEngine.Random.Range(0, int.MaxValue);
                        EditorUtility.SetDirty(world);
                    }
                    if (GUILayout.Button("Rebuild World Now"))
                    {
                        world.RebuildWorld();
                        hub?.NotifyDirty();
                        SceneView.RepaintAll();
                    }
                    if (GUILayout.Button("Build (Stages)", GUILayout.Height(22)))
                    {
                        world.BuildViaStages();
                        hub?.NotifyDirty();
                        SceneView.RepaintAll();
                    }
                    if (GUILayout.Button("Select World", GUILayout.Width(110)))
                        Selection.activeObject = world;
                }

                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("What to Generate", EditorStyles.boldLabel);
                world.makeTerrain = EditorGUILayout.Toggle("Terrain", world.makeTerrain);
                // Only show Water if you’ve added it in your ProcgenWorld
                SerializedObject soW = new SerializedObject(world);
                var propMakeWater = soW.FindProperty("makeWater");
                if (propMakeWater != null)
                {
                    soW.Update();
                    propMakeWater.boolValue = EditorGUILayout.Toggle("Water", propMakeWater.boolValue);
                    soW.ApplyModifiedProperties();
                }
                world.makeTrees  = EditorGUILayout.Toggle("Trees",  world.makeTrees);
                world.makeRocks  = EditorGUILayout.Toggle("Rocks",  world.makeRocks);
                world.makeGrass  = EditorGUILayout.Toggle("Grass",  world.makeGrass);

                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("High-level Counts", EditorStyles.boldLabel);
                world.trees = EditorGUILayout.IntSlider("Trees", world.trees, 0, 5000);
                world.rocks = EditorGUILayout.IntSlider("Rocks", world.rocks, 0, 10000);
                world.grass = EditorGUILayout.IntSlider("Grass Blades", world.grass, 0, 100000);

                EditorGUILayout.Space(4);
                world.colliderRadius = EditorGUILayout.Slider(new GUIContent("Collider Radius (m)", "Near-LOD radius for tree/rock colliders"), world.colliderRadius, 0f, 120f);

                // Optional biome field (exists in your repo)
                var biomeProp = new SerializedObject(world).FindProperty("biome");
                if (biomeProp != null)
                {
                    var so = new SerializedObject(world);
                    so.Update();
                    EditorGUILayout.PropertyField(so.FindProperty("biome"));
                    so.ApplyModifiedProperties();
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            // TERRAIN
            foldTerrain = EditorGUILayout.BeginFoldoutHeaderGroup(foldTerrain, "Terrain");
            if (foldTerrain && world.terrain)
            {
                var so = new SerializedObject(world.terrain);
                so.Update();
                EditorGUILayout.PropertyField(so.FindProperty("data"), new GUIContent("Profile (ScriptableObject)"));
                EditorGUILayout.PropertyField(so.FindProperty("seed"));
                EditorGUILayout.PropertyField(so.FindProperty("drawMaskOverlay"));
                if (GUILayout.Button("Rebuild Terrain"))
                {
                    world.terrain.Rebuild();
                    SceneView.RepaintAll();
                }
                so.ApplyModifiedProperties();
            }
            else if (foldTerrain)
            {
                EditorGUILayout.HelpBox("No ProceduralTerrain found (it is created automatically on rebuild).", MessageType.None);
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            // WATER (if present in your ProcgenWorld)
            foldWater = EditorGUILayout.BeginFoldoutHeaderGroup(foldWater, "Water");
            if (foldWater)
            {
                var soW = new SerializedObject(world);
                var pMakeWater = soW.FindProperty("makeWater");
                var pWaterLevel = soW.FindProperty("waterLevel");
                var pMinBody = soW.FindProperty("minWaterBodySize");
                var hasWater = pMakeWater != null && pWaterLevel != null && pMinBody != null;

                if (!hasWater)
                {
                    EditorGUILayout.HelpBox("Water controls will appear once your ProcgenWorld exposes Water fields.", MessageType.Info);
                }
                else
                {
                    soW.Update();
                    pMakeWater.boolValue = EditorGUILayout.Toggle("Enable Water", pMakeWater.boolValue);
                    pWaterLevel.floatValue = EditorGUILayout.Slider("Water Level", pWaterLevel.floatValue, -10f, 50f);
                    pMinBody.intValue = EditorGUILayout.IntSlider("Min Body Size (cells)", pMinBody.intValue, 1, 500);
                    soW.ApplyModifiedProperties();

                    if (world.water && GUILayout.Button("Rebuild Water"))
                    {
                        world.water.Rebuild();
                        SceneView.RepaintAll();
                    }
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            // SPAWNERS (summary; detailed tuning stays on components)
            foldSpawners = EditorGUILayout.BeginFoldoutHeaderGroup(foldSpawners, "Spawners (summary)");
            if (foldSpawners)
            {
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.ObjectField("TreeField",  world.treeField,  typeof(InstancedTreeField),  true);
                EditorGUILayout.ObjectField("RockField",  world.rockField,  typeof(InstancedRockField),  true);
                EditorGUILayout.ObjectField("GrassField", world.grassField, typeof(GrassField),          true);
                EditorGUI.EndDisabledGroup();

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Rebuild Trees"))  { if (world.treeField)  world.treeField.Rebuild();  }
                    if (GUILayout.Button("Rebuild Rocks"))  { if (world.rockField)  world.rockField.Rebuild();  }
                    if (GUILayout.Button("Rebuild Grass"))  { if (world.grassField) world.grassField.Build();    }
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            // CULLING
            foldCulling = EditorGUILayout.BeginFoldoutHeaderGroup(foldCulling, "Culling");
            if (foldCulling)
            {
                if (!hub) EditorGUILayout.HelpBox("No ProcgenCullingHub found. Click “Create Hub” above.", MessageType.Warning);
                if (hub)
                {
                    Undo.RecordObject(hub, "Culling Settings");

                    EditorGUILayout.LabelField("Discovery", EditorStyles.boldLabel);
                    hub.discovery = (ProcgenCullingHub.DiscoveryMode)EditorGUILayout.EnumPopup("Mode", hub.discovery);
                    hub.includeInactive = EditorGUILayout.Toggle("Include Inactive", hub.includeInactive);
                    hub.targetCameraOverride = (Camera)EditorGUILayout.ObjectField("Target Camera Override", hub.targetCameraOverride, typeof(Camera), true);
                    hub.preferMainCameraInEditMode = EditorGUILayout.Toggle("Prefer MainCam In Edit", hub.preferMainCameraInEditMode);

                    EditorGUILayout.Space(4);
                    EditorGUILayout.LabelField("Global", EditorStyles.boldLabel);
                    hub.enableCulling = EditorGUILayout.Toggle("Enable Culling", hub.enableCulling);
                    hub.maxViewDistance = EditorGUILayout.FloatField(new GUIContent("Max View Distance (m)", "−1 = unlimited"), hub.maxViewDistance);

                    EditorGUILayout.Space(2);
                    hub.enableFacingCulling = EditorGUILayout.Toggle(new GUIContent("Facing Cull", "Cull when facing away"), hub.enableFacingCulling);
                    if (hub.enableFacingCulling)
                        hub.facingDotMin = EditorGUILayout.Slider("Facing Dot Min", hub.facingDotMin, -1f, 1f);

                    EditorGUILayout.Space(4);
                    EditorGUILayout.LabelField("Per-Tag Caps (meters, −1 = off)", EditorStyles.boldLabel);
                    hub.maxDistTreeLeaves = EditorGUILayout.FloatField("Leaves", hub.maxDistTreeLeaves);
                    hub.maxDistGrass      = EditorGUILayout.FloatField("Grass",  hub.maxDistGrass);

                    EditorGUILayout.Space(4);
                    EditorGUILayout.LabelField("Shadows", EditorStyles.boldLabel);
                    hub.applyFacingInShadows       = EditorGUILayout.Toggle("Apply Facing in Shadows", hub.applyFacingInShadows);
                    hub.applyDistanceCapsInShadows = EditorGUILayout.Toggle("Apply Distance Caps in Shadows", hub.applyDistanceCapsInShadows);

                    EditorGUILayout.Space(4);
                    EditorGUILayout.LabelField("Debug / Draw", EditorStyles.boldLabel);
                    hub.drawInEditAndPlay = EditorGUILayout.Toggle("Draw In Edit And Play", hub.drawInEditAndPlay);
                    hub.debugDrawBounds   = EditorGUILayout.Toggle("Debug Draw Bounds", hub.debugDrawBounds);

                    if (GUILayout.Button("Refresh Batches (Pull From Sources)"))
                    {
                        hub.NotifyDirty();
                        SceneView.RepaintAll();
                    }
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            // ACTIONS
            foldActions = EditorGUILayout.BeginFoldoutHeaderGroup(foldActions, "Quick Actions");
            if (foldActions)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Center & Size Spawners To Terrain"))
                    {
                        Undo.RecordObject(world, "Align Spawners");
                        world.RebuildWorld(); // your Rebuild sets center/area for all fields
                        hub?.NotifyDirty();
                        SceneView.RepaintAll();
                    }
                    if (GUILayout.Button("Enable Instancing On All Mats"))
                    {
                        ForceInstancingAll(world);
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Rebuild EVERYTHING", GUILayout.Height(28)))
                    {
                        world.RebuildWorld();
                        hub?.NotifyDirty();
                        SceneView.RepaintAll();
                    }
                    if (GUILayout.Button("Select Generated Children"))
                    {
                        var list = Array.Empty<UnityEngine.Object>();
                        var v = new System.Collections.Generic.List<UnityEngine.Object>();
                        if (world.terrain)   v.Add(world.terrain.gameObject);
                        if (world.treeField) v.Add(world.treeField.gameObject);
                        if (world.rockField) v.Add(world.rockField.gameObject);
                        if (world.grassField)v.Add(world.grassField.gameObject);
                        if (world.water)     v.Add(world.water.gameObject);
                        if (hub)             v.Add(hub.gameObject);
                        Selection.objects = v.ToArray();
                    }
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            EditorGUILayout.EndScrollView();

            // Dirty/Save
            if (GUI.changed)
            {
                EditorUtility.SetDirty(world);
                if (hub) EditorUtility.SetDirty(hub);
            }
        }
    }

    static void ForceInstancingAll(ProcgenWorld w)
    {
        if (!w) return;
        void EnableMat(Material m) { if (m) m.enableInstancing = true; }
        if (w.treeField) { EnableMat(w.treeField.woodMaterial); EnableMat(w.treeField.leafMaterial); }
        if (w.rockField) { EnableMat(w.rockField.rockMaterial); }
        if (w.grassField){ EnableMat(w.grassField.grassMaterial); }
        EditorUtility.DisplayDialog("Procgen", "Enabled instancing on known materials.", "OK");
    }
}
#endif

