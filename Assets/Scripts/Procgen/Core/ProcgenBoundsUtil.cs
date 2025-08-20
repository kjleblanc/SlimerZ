using UnityEngine;
using System.Collections.Generic;

public static class ProcgenBoundsUtil
{
    public static Bounds FromInstances(Matrix4x4[] arr, Mesh mesh, float extraPadding = 0.5f)
    {
        Vector3 min = new(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        Vector3 max = new(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        float maxScale = 0f;

        for (int i = 0; i < arr.Length; i++)
        {
            var m = arr[i];
            var p = new Vector3(m.m03, m.m13, m.m23);
            if (p.x < min.x) min.x = p.x; if (p.y < min.y) min.y = p.y; if (p.z < min.z) min.z = p.z;
            if (p.x > max.x) max.x = p.x; if (p.y > max.y) max.y = p.y; if (p.z > max.z) max.z = p.z;

            // approximate uniform scale from basis vectors
            float sx = new Vector3(m.m00, m.m10, m.m20).magnitude;
            float sy = new Vector3(m.m01, m.m11, m.m21).magnitude;
            float sz = new Vector3(m.m02, m.m12, m.m22).magnitude;
            float s = Mathf.Max(sx, Mathf.Max(sy, sz));
            if (s > maxScale) maxScale = s;
        }

        var b = new Bounds(); b.SetMinMax(min, max);

        // expand by mesh extents * maxScale + small pad
        if (mesh)
        {
            var e = mesh.bounds.extents;
            float r = Mathf.Max(e.x, Mathf.Max(e.y, e.z)) * Mathf.Max(1f, maxScale);
            b.Expand(r * 2f + extraPadding); // extents on both sides
        }
        else b.Expand(extraPadding);

        return b;
    }
}

