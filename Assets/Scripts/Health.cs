using System;
using UnityEngine;

public class Health : MonoBehaviour
{
    [Min(1f)] public float maxHealth = 10f;
    [NonSerialized] public float current;

    public bool invulnerable = false;

    public event Action<float, GameObject> Damaged; // (amount, source)
    public event Action<GameObject> Died;           // (source)

    void Awake() { current = maxHealth; }

    public void ApplyDamage(float amount, GameObject source = null)
    {
        if (invulnerable || amount <= 0f) return;
        current -= amount;
        Damaged?.Invoke(amount, source);
        if (current <= 0f)
        {
            current = 0f;
            Died?.Invoke(source);
        }
    }

    public void Heal(float amount)
    {
        if (amount <= 0f) return;
        current = Mathf.Min(maxHealth, current + amount);
    }
}
