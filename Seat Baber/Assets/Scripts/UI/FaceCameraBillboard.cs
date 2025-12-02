using UnityEngine;

public class FaceCameraBillboard : MonoBehaviour
{
	public Camera mainCamera;
	public bool onlyRotateAroundY = false;

	void LateUpdate()
	{
		if (mainCamera == null) mainCamera = Camera.main;
		if (mainCamera == null) return;

		// 让物体正面朝向相机：将 forward 指向“从相机到物体”的方向（避免文字镜像）
		if (onlyRotateAroundY)
		{
			Vector3 dir = transform.position - mainCamera.transform.position;
			dir.y = 0f;
			if (dir.sqrMagnitude > 0.0001f)
			{
				transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
			}
			return;
		}

		Vector3 forward = (transform.position - mainCamera.transform.position);
		if (forward.sqrMagnitude > 0.0001f)
		{
			transform.rotation = Quaternion.LookRotation(forward.normalized, mainCamera.transform.up);
		}
	}
}

