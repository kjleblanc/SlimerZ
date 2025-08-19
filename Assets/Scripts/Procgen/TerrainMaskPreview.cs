// Assets/Scripts/Procgen/TerrainMaskPreview.cs
using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class TerrainMaskPreview : MonoBehaviour
{
    public ProceduralTerrain source;
    public enum Mode { Slope, Moisture }
    public Mode mode = Mode.Slope;
    public float yOffset = 0.2f;
    [Range(0,1)] public float opacity = 0.8f;

    MeshRenderer mr; MeshFilter mf; Material mat;

    void Reset()     { Ensure(true); }
    void OnEnable()  { Ensure(true); }
    void OnValidate(){ Ensure(false); }

    void Ensure(bool buildMesh)
    {
        if (!mf) mf = GetComponent<MeshFilter>();
        if (!mr) mr = GetComponent<MeshRenderer>();
        if (!mat || mr.sharedMaterial == null || mr.sharedMaterial.shader == null ||
            mr.sharedMaterial.shader.name != "URP/MaskOverlay")
        {
            mat = new Material(Shader.Find("URP/MaskOverlay"));
            mr.sharedMaterial = mat;
        }
        else mat = mr.sharedMaterial;

        if (buildMesh && mf.sharedMesh == null) mf.sharedMesh = BuildQuadXZ();
        if (!source) source = Object.FindFirstObjectByType<ProceduralTerrain>();
    }

    void Update()
    {
        if (!source || !source.terrain || !mr || !mf) return;

        var td = source.terrain.terrainData;

        // Center over terrain, slightly above it
        var t = source.terrain.transform;
        transform.position = t.position + new Vector3(td.size.x * 0.5f, yOffset, td.size.z * 0.5f);

        // Quad already lies in XZ → keep identity rotation
        transform.rotation = Quaternion.identity;

        // Scale to terrain dimensions (X,Z); Y stays 1
        transform.localScale = new Vector3(td.size.x, 1f, td.size.z);

        var tex = (mode == Mode.Slope) ? source.slopeMask : source.moistureMask;
        if (!tex) { mr.enabled = false; return; }
        mr.enabled = true;
        mat.SetTexture("_MainTex", tex);
        mat.SetColor("_Color", new Color(1, 1, 1, opacity));
    }

    static Mesh BuildQuadXZ()
    {
        // Unit quad centered at origin on XZ plane
        var m = new Mesh { name = "MaskQuadXZ" };
        m.SetVertices(new[]
        {
            new Vector3(-0.5f, 0f, -0.5f),
            new Vector3( 0.5f, 0f, -0.5f),
            new Vector3(-0.5f, 0f,  0.5f),
            new Vector3( 0.5f, 0f,  0.5f)
        });
        m.SetUVs(0, new[] { new Vector2(0,0), new Vector2(1,0), new Vector2(0,1), new Vector2(1,1) });
        m.SetTriangles(new[] { 0,2,1, 1,2,3 }, 0);
        m.RecalculateBounds();
        return m;
    }
}
