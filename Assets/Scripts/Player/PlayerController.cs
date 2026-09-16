using System;
using UnityEngine;
using AdaptiveWorld2D.Director;

namespace AdaptiveWorld2D.Player
{
    [RequireComponent(typeof(Rigidbody2D))]
    public class PlayerController : MonoBehaviour
    {
        [Header("Movement Speeds")]
        public float walkSpeed = 3.5f;
        public float runSpeed = 6.0f;
        public float driveSpeed = 12.0f;

        [Header("State")]
        public bool isDriving = false;
        public bool isSprinting = false;
        public GameObject currentVehicle;

        [Header("Terrain Detection")]
        public LayerMask roadLayer;
        public LayerMask alleyLayer;
        public LayerMask buildingLayer;

        private Rigidbody2D _rb;
        private Vector2 _moveInput;
        private SpriteRenderer _spriteRenderer;

        [Header("Dependencies (Phase 4 — assign in Inspector to skip singleton lookup)")]
        [Tooltip("Assign TelemetryReporter here. When null at Start, falls back to TelemetryReporter.Instance.")]
        [SerializeField] private TelemetryReporter _telemetryReporter;

        // Telemetry tracking variables
        private float _intervalTimer = 0f;
        private Vector2 _lastPosition;
        private float _accumulatedDistance = 0f;
        private string _currentSurface = "ROAD";

        public Vector2 MoveDirection => _moveInput;

        private void Start()
        {
            if (_telemetryReporter == null)
                _telemetryReporter = TelemetryReporter.Instance;
        }

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            _spriteRenderer = GetComponent<SpriteRenderer>();
            if (_spriteRenderer != null)
            {
                _spriteRenderer.sortingLayerName = "Characters";
                _spriteRenderer.sortingOrder = 25;
            }
            _rb.gravityScale = 0f;
            _rb.freezeRotation = true;
            _lastPosition = transform.position;
        }

        private void Update()
        {
            HandleInput();
            TrackTelemetryInterval();
        }

        private void FixedUpdate()
        {
            MoveCharacter();
        }

        private void HandleInput()
        {
            float h = 0f;
            float v = 0f;

            try
            {
                h = Input.GetAxisRaw("Horizontal");
                v = Input.GetAxisRaw("Vertical");
            }
            catch (System.Exception) { /* Axis may not be configured in Input Manager */ }

            // Direct key fallback in case Input Manager axes are not configured
            if (h == 0f && v == 0f)
            {
                try
                {
                    if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) v += 1f;
                    if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) v -= 1f;
                    if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) h -= 1f;
                    if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) h += 1f;
                }
                catch (System.Exception) { }
            }

            bool shift = false;
            try { shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift); } catch (System.Exception) { }
            isSprinting = shift && !isDriving;

            _moveInput = new Vector2(h, v).normalized;

            // Rotate player sprite towards movement direction
            if (PlayerMovementModel.ShouldRotate(_moveInput))
            {
                float angle = PlayerMovementModel.ComputeFacingAngleDegrees(_moveInput);
                transform.rotation = Quaternion.Euler(0f, 0f, angle);
            }
        }

        private void MoveCharacter()
        {
            if (_rb == null) _rb = GetComponent<Rigidbody2D>();
            if (_rb == null) return;

            float speed = PlayerMovementModel.SelectSpeed(isDriving, isSprinting, walkSpeed, runSpeed, driveSpeed);

            // Kinematic bodies use MovePosition; Dynamic bodies use linearVelocity
            if (_rb.bodyType == RigidbodyType2D.Kinematic)
            {
                _rb.MovePosition(PlayerMovementModel.ComputeKinematicDisplacement(_rb.position, _moveInput, speed, Time.fixedDeltaTime));
            }
            else
            {
                _rb.linearVelocity = PlayerMovementModel.ComputeVelocity(_moveInput, speed);
                // Direct positional fallback if velocity doesn't move body
                if (_moveInput.sqrMagnitude > 0.001f && _rb.linearVelocity.sqrMagnitude == 0f)
                {
                    _rb.position = PlayerMovementModel.ComputeKinematicDisplacement(_rb.position, _moveInput, speed, Time.fixedDeltaTime);
                }
            }

            // Track distance
            Vector2 curPos = transform.position;
            _accumulatedDistance += Vector2.Distance(curPos, _lastPosition);
            _lastPosition = curPos;
        }

#if UNITY_EDITOR || DEBUG
        private void OnGUI()
        {
            GUIStyle boxStyle = new GUIStyle(GUI.skin.box);
            boxStyle.fontSize = 13;
            boxStyle.alignment = TextAnchor.UpperLeft;
            boxStyle.normal.textColor = Color.white;

            Vector2 pos = transform.position;
            string inputStr = $"({_moveInput.x:F1}, {_moveInput.y:F1})";
            string text = $"<b>[PLAYER DEBUG]</b>\n" +
                          $"Position: ({pos.x:F2}, {pos.y:F2})\n" +
                          $"Input: {inputStr}\n" +
                          $"Speed: {PlayerMovementModel.SelectSpeed(isDriving, isSprinting, walkSpeed, runSpeed, driveSpeed)} m/s\n" +
                          $"Distance Moved: {_accumulatedDistance:F1}m\n" +
                          $"<i>Click Game view to ensure focus!</i>";

            GUI.Box(new Rect(10, 10, 260, 105), text, boxStyle);
        }
#endif

        private void TrackTelemetryInterval()
        {
            _intervalTimer += Time.deltaTime;

            // Detect surface underneath
            DetectSurface();

            // Flush movement event every 3 seconds if player is moving
            if (_intervalTimer >= 3.0f)
            {
                if (_accumulatedDistance > 0.5f && _telemetryReporter != null)
                {
                    string moveType = PlayerMovementModel.DetermineMovementType(isDriving, isSprinting);
                    float avgSpeed = PlayerMovementModel.ComputeAverageSpeed(_accumulatedDistance, _intervalTimer);

                    _telemetryReporter.ReportMovement(
                        moveType,
                        _currentSurface,
                        _intervalTimer,
                        avgSpeed,
                        _accumulatedDistance
                    );
                }

                _intervalTimer = 0f;
                _accumulatedDistance = 0f;
            }
        }

        private void DetectSurface()
        {
            Vector2 pos = transform.position;
            if (Physics2D.OverlapPoint(pos, buildingLayer))
            {
                _currentSurface = "BUILDING_INTERIOR";
            }
            else if (Physics2D.OverlapPoint(pos, alleyLayer))
            {
                _currentSurface = "ALLEY";
            }
            else
            {
                _currentSurface = "ROAD";
            }
        }

        public void EnterVehicle(GameObject vehicle)
        {
            isDriving = true;
            currentVehicle = vehicle;
            if (_spriteRenderer != null) _spriteRenderer.enabled = false;

            transform.position = vehicle.transform.position;
            vehicle.transform.SetParent(transform);
            vehicle.transform.localPosition = Vector3.zero;
            vehicle.transform.localRotation = Quaternion.identity;

            Collider2D vCol = vehicle.GetComponent<Collider2D>();
            if (vCol != null) vCol.enabled = false;

            Debug.Log("[Player] Entered vehicle!");
        }

        public void ExitVehicle()
        {
            if (!isDriving) return;
            isDriving = false;
            if (_spriteRenderer != null) _spriteRenderer.enabled = true;
            if (currentVehicle != null)
            {
                currentVehicle.transform.SetParent(null);
                Collider2D vCol = currentVehicle.GetComponent<Collider2D>();
                if (vCol != null) vCol.enabled = true;
                transform.position = currentVehicle.transform.position + new Vector3(1.2f, 0f, 0f);
            }
            currentVehicle = null;
            Debug.Log("[Player] Exited vehicle!");
        }
    }
}
