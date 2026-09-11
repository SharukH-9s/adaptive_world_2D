using UnityEngine;

namespace AdaptiveWorld2D.World
{
    public class CameraFollow2D : MonoBehaviour
    {
        [Header("Target Tracking")]
        [Tooltip("The Transform the camera will follow (e.g., Player)")]
        public Transform target;

        [Header("Settings")]
        [Range(1f, 20f)]
        public float smoothSpeed = 8f;
        public Vector2 offset = Vector2.zero;
        public float cameraZ = -10f;

        private void Start()
        {
            Camera cam = GetComponent<Camera>();
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.10f, 0.11f, 0.13f);
            }

            if (target == null)
            {
                GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
                if (playerObj != null)
                {
                    target = playerObj.transform;
                }
            }
        }

        private void LateUpdate()
        {
            if (target == null)
            {
                GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
                if (playerObj != null) target = playerObj.transform;
                else return;
            }

            Vector3 targetPos = new Vector3(target.position.x + offset.x, target.position.y + offset.y, cameraZ);
            transform.position = Vector3.Lerp(transform.position, targetPos, smoothSpeed * Time.deltaTime);

            // Keep camera upright - never rotate with player
            transform.rotation = Quaternion.identity;
        }
    }
}
