using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public Transform target;
    public Vector3 offset = new Vector3(0, 6, -8);
    public float followDamp = 6f;
    public float lookDamp = 10f;

    void LateUpdate()
    {
        if (!target) return;
        Vector3 desired = target.position + offset;
        transform.position = Vector3.Lerp(transform.position, desired, 1f - Mathf.Exp(-followDamp * Time.deltaTime));

        Vector3 lookDir = Vector3.Lerp(transform.forward, (target.position - transform.position).normalized, 1f - Mathf.Exp(-lookDamp * Time.deltaTime));
        transform.rotation = Quaternion.LookRotation(lookDir, Vector3.up);
    }
}