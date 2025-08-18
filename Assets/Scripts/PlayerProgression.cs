using UnityEngine;

public class PlayerProgression : MonoBehaviour
{
    [Header("Refs")]
    public GooWallet wallet;          // Player
    public Health playerHealth;       // Player
    public SlimePlayer player;        // Player
    public DashAttack dash;           // Player

    [Header("Progression")]
    public int gooPerLevel = 5;
    public float moveSpeedPerLevel = 0.2f;
    public float dashDamagePerLevel = 1f;
    public float maxHealthPerLevel = 1f;

    int appliedLevels;

    void Start()
    {
        if (!wallet) wallet = GetComponent<GooWallet>();
        if (!playerHealth) playerHealth = GetComponent<Health>();
        if (!player) player = GetComponent<SlimePlayer>();
        if (!dash) dash = GetComponent<DashAttack>();

        if (wallet) wallet.Changed += OnGooChanged;
        // Apply once in case starting goo > 0
        if (wallet) OnGooChanged(wallet.goo);
    }

    void OnDestroy()
    {
        if (wallet) wallet.Changed -= OnGooChanged;
    }

    void OnGooChanged(int total)
    {
        int level = Mathf.FloorToInt(total / (float)gooPerLevel);
        int delta = level - appliedLevels;
        if (delta <= 0) return;

        appliedLevels = level;

        if (player) player.moveSpeed += moveSpeedPerLevel * delta;
        if (dash) dash.dashDamage += dashDamagePerLevel * delta;
        if (playerHealth)
        {
            playerHealth.maxHealth += maxHealthPerLevel * delta;
            playerHealth.Heal(maxHealthPerLevel * delta);
        }
    }
}
