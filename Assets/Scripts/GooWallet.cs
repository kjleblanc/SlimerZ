using System;
using UnityEngine;

public class GooWallet : MonoBehaviour
{
    public int goo;
    public event Action<int> Changed; // new total

    public void Add(int amount)
    {
        if (amount == 0) return;
        goo = Mathf.Max(0, goo + amount);
        Changed?.Invoke(goo);
    }
}
