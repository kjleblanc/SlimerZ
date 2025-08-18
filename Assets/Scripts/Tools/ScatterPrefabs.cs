using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
public class ScatterPrefabs : MonoBehaviour
{
    [Header("Area")]
    public Vector2 size = new Vector2(55, 55);  // XZ area centered on this object
    public LayerMask groundMask = ~0;

    [Header("Placement")]
    public GameObject prefab;
    public int count = 800;
    public Vector2 scaleRange = new Vector2(0.9f, 1.3f);
    public bool randomYRotation = true;
    public float surfaceOffset = 0.0f; // raise tiny bit if needed
    public int seed = 12345;

    [Header("Avoid")]
    public Transform avoidCenter; // e.g., player start
    public float avoidRadius = 2f;

    [ContextMenu("Regenerate")]
    public void Regenerate()
    {
        if (!prefab) { Debug.LogWarning("ScatterPrefabs: assign a prefab."); return; }

        // clear existing children
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            #if UNITY_EDITOR
            if (!Application.isPlaying) DestroyImmediate(transform.GetChild(i).gameObject);
            else Destroy(transform.GetChild(i).gameObject);
            #else
            DestroyImmediate(transform.GetChild(i).gameObject);
            #endif
        }

        var rnd = new System.Random(seed);
        Vector3 origin = transform.position;

        int placed = 0;
        int safety = count * 3;
        while (placed < count && safety-- > 0)
        {
            float rx = (float)rnd.NextDouble() * size.x - size.x * 0.5f;
            float rz = (float)rnd.NextDouble() * size.y - size.y * 0.5f;
            Vector3 pos = origin + new Vector3(rx, 20f, rz); // raycast down from above

            if (avoidCenter && Vector3.Distance(new Vector3(pos.x, avoidCenter.position.y, pos.z),
                                                new Vector3(avoidCenter.position.x, avoidCenter.position.y, avoidCenter.position.z)) < avoidRadius)
                continue;

            if (Physics.Raycast(pos, Vector3.down, out var hit, 100f, groundMask, QueryTriggerInteraction.Ignore))
            {
                var go = (GameObject)Instantiate(prefab, hit.point + Vector3.up * surfaceOffset, Quaternion.identity, transform);
                float s = Mathf.Lerp(scaleRange.x, scaleRange.y, (float)rnd.NextDouble());
                go.transform.localScale *= s;
                if (randomYRotation) go.transform.Rotate(0f, (float)rnd.NextDouble() * 360f, 0f);
                placed++;
            }
        }
        #if UNITY_EDITOR
        if (!Application.isPlaying) EditorUtility.SetDirty(this);
        #endif
    }
}
