using UnityEngine;

public class Note : MonoBehaviour
{
    public Rigidbody rb;
    private float _speed;
    public bool judged = false;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    public void SetSpeed(float speed)
    {
        this._speed = speed;
    }
    public void Launch(Vector3 direction)
    {
        rb.linearVelocity = direction * _speed;
    }
}