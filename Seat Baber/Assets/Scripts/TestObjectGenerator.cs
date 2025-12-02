using UnityEngine;

public class TestObjectGenerator : MonoBehaviour
{
    public NoteGenerator launcher;
    public float interval = 1f; // 每几秒发射一次

    private float timer = 0f;

    void Update()
    {
        if (launcher == null) return;

        timer += Time.deltaTime;
        if (timer >= interval)
        {
            timer = 0f;
        }
    }
}
