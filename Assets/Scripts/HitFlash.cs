using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Health))]
public class HitFlash : MonoBehaviour
{
    public Color flashColor = new Color(1f, 1f, 1f, 0f);
    public float flashDuration = 0.08f;    // seconds
    public string colorProperty = "_BaseColor"; // URP/Lit

    Renderer[] rends;
    Health hp;
    bool flashing;

    void Awake()
    {
        hp = GetComponent<Health>();
        hp.Damaged += OnDamaged;
        rends = GetComponentsInChildren<Renderer>();
    }

    void OnDestroy()
    {
        if (hp) hp.Damaged -= OnDamaged;
    }

    void OnDamaged(float amt, GameObject src)
    {
        if (!flashing) StartCoroutine(CoFlash());
    }

    IEnumerator CoFlash()
    {
        flashing = true;
        float t = 0f;
        var mpb = new MaterialPropertyBlock();

        while (t < flashDuration)
        {
            t += Time.deltaTime;
            float a = 1f - (t / flashDuration); // fades out
            foreach (var r in rends)
            {
                r.GetPropertyBlock(mpb);
                if (r.sharedMaterial.HasProperty(colorProperty))
                {
                    Color baseCol = r.sharedMaterial.GetColor(colorProperty);
                    mpb.SetColor(colorProperty, Color.Lerp(baseCol + flashColor, baseCol, 1f - a));
                }
                r.SetPropertyBlock(mpb);
            }
            yield return null;
        }

        foreach (var r in rends)
        {
            r.GetPropertyBlock(mpb);
            if (r.sharedMaterial.HasProperty(colorProperty))
            {
                Color baseCol = r.sharedMaterial.GetColor(colorProperty);
                mpb.SetColor(colorProperty, baseCol);
            }
            r.SetPropertyBlock(mpb);
        }

        flashing = false;
    }
}
