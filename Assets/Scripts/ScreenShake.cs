using UnityEngine;

public class ScreenShake : MonoBehaviour
{
    [Tooltip("Max positional shake (meters) at amplitude=1.")]
    public float posAmplitude = 0.2f;
    [Tooltip("Max rotational shake (deg) at amplitude=1.")]
    public float rotAmplitude = 1.2f;

    float trauma;          // 0..1
    float decay;           // per-second decay when added
    Vector3 seed;

    void Awake(){ seed = new Vector3(Random.value, Random.value, Random.value) * 100f; }

    public void AddShake(float amount, float decayPerSec)
    {
        trauma = Mathf.Clamp01(trauma + amount);
        decay = Mathf.Max(decay, decayPerSec);
    }

    void LateUpdate()
    {
        if (trauma > 0f)
        {
            float t = trauma * trauma; // bias to stronger high-end
            float time = Time.unscaledTime * 25f;
            Vector3 p = new Vector3(
                (Mathf.PerlinNoise(seed.x, time) - 0.5f),
                (Mathf.PerlinNoise(seed.y, time) - 0.5f),
                0f
            ) * (posAmplitude * t);

            Vector3 r = new Vector3(
                0f,
                0f,
                (Mathf.PerlinNoise(seed.z, time) - 0.5f) * rotAmplitude * t
            );

            transform.localPosition = p;
            transform.localRotation = Quaternion.Euler(r);

            trauma = Mathf.Max(0f, trauma - decay * Time.unscaledDeltaTime);
        }
        else
        {
            // return to rest
            transform.localPosition = Vector3.Lerp(transform.localPosition, Vector3.zero, 0.25f);
            transform.localRotation = Quaternion.Slerp(transform.localRotation, Quaternion.identity, 0.25f);
        }
    }
}
