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

        [Header("Impulse / Shake")]
        [Range(1f, 15f)]
        public float shakeDecay = 5.0f;

        private Vector2 _shakeOffset;
        private float _shakeIntensity;
        private Vector3 _basePosition;

        public static CameraFollow2D Instance { get; private set; }

        private void Awake()
        {
            Instance = this;
            _basePosition = transform.position;
        }

        public void AddImpulse(float intensity)
        {
            _shakeIntensity = Mathf.Clamp(_shakeIntensity + intensity, 0f, 1.2f);
        }

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
                    _basePosition = new Vector3(target.position.x + offset.x, target.position.y + offset.y, cameraZ);
                    transform.position = _basePosition;
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

            // 1. Follow from unshaken base position (never lerp from transform.position to avoid jitter)
            Vector3 targetPos = new Vector3(target.position.x + offset.x, target.position.y + offset.y, cameraZ);
            _basePosition = Vector3.Lerp(_basePosition, targetPos, smoothSpeed * Time.deltaTime);

            // 2. Compute multi-frequency Perlin noise shake offset
            if (_shakeIntensity > 0.005f)
            {
                float rx = (Mathf.PerlinNoise(Time.time * 25f, 0f) - 0.5f) * 2f * _shakeIntensity;
                float ry = (Mathf.PerlinNoise(0f, Time.time * 25f) - 0.5f) * 2f * _shakeIntensity;
                _shakeOffset = new Vector2(rx, ry);
                _shakeIntensity = Mathf.MoveTowards(_shakeIntensity, 0f, shakeDecay * Time.deltaTime);
            }
            else
            {
                _shakeOffset = Vector2.zero;
            }

            transform.position = new Vector3(_basePosition.x + _shakeOffset.x, _basePosition.y + _shakeOffset.y, cameraZ);
            transform.rotation = Quaternion.identity;
        }
    }
}
