using UnityEngine;
using UnityEngine.UI;

namespace AdaptiveWorld2D.Director
{
    /// <summary>
    /// Attached to the ExtractionCompass UI GameObject spawned by WorldManifestApplier
    /// after Viktor is eliminated (§4.3E — HUD Extraction Compass Indicator).
    ///
    /// Each frame:
    ///   1. Reads the player world position.
    ///   2. Computes the direction from player to extractionWorldPos.
    ///   3. Rotates the RectTransform so the arrow points toward the extraction zone.
    ///   4. Clamps the arrow to a circular ring near the screen edge so it is always visible.
    ///   5. Destroys itself once the extraction zone is no longer active (player entered it).
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class ExtractionCompassTracker : MonoBehaviour
    {
        /// <summary>World-space position of the extraction zone.</summary>
        public Vector3 extractionWorldPos;

        /// <summary>Reference to the extraction zone GO — tracker destroys itself when zone is gone.</summary>
        public GameObject extractionZone;

        /// <summary>Radius of the screen-edge circle the arrow is clamped to (pixels).</summary>
        public float compassRadius = 200f;

        [Header("Dependencies (Phase 4 — assign in Inspector to skip tag lookup)")]
        [Tooltip("Assign the Player transform here. When null at Start, falls back to FindGameObjectWithTag(\"Player\").")]
        [SerializeField] private Transform _playerTransform;

        private RectTransform _rt;
        private Camera _cam;

        private void Start()
        {
            _rt  = GetComponent<RectTransform>();
            _cam = Camera.main;

            if (_playerTransform == null)
            {
                var playerGO = GameObject.FindGameObjectWithTag("Player");
                if (playerGO != null) _playerTransform = playerGO.transform;
            }
        }

        private void Update()
        {
            // Self-destruct when extraction zone is gone (player entered or it was destroyed)
            if (extractionZone == null || !extractionZone.activeInHierarchy)
            {
                Destroy(gameObject);
                return;
            }

            if (_cam == null || _playerTransform == null) return;

            // Project the extraction zone to screen space
            Vector3 extractionScreen = _cam.WorldToScreenPoint(extractionWorldPos);

            // If extraction zone is on-screen, hide the compass (player can see it directly)
            float screenW = Screen.width;
            float screenH = Screen.height;
            bool onScreen = extractionScreen.z > 0f
                            && extractionScreen.x > 0f && extractionScreen.x < screenW
                            && extractionScreen.y > 0f && extractionScreen.y < screenH;

            if (onScreen)
            {
                // Dim the compass but don't hide completely — let it fade out
                var img = GetComponent<Image>();
                if (img != null) img.color = new Color(img.color.r, img.color.g, img.color.b, 0.3f);
                return;
            }

            // Restore full alpha when off-screen
            var imgComp = GetComponent<Image>();
            if (imgComp != null) imgComp.color = new Color(imgComp.color.r, imgComp.color.g, imgComp.color.b, 0.9f);

            // Compute direction from screen center to extraction screen position
            Vector2 screenCenter = new Vector2(screenW * 0.5f, screenH * 0.5f);
            Vector2 dir = new Vector2(extractionScreen.x - screenCenter.x,
                                      extractionScreen.y - screenCenter.y);

            // Handle targets behind the camera (z < 0 → flip direction)
            if (extractionScreen.z < 0f) dir = -dir;

            if (dir.magnitude < 0.001f) return;

            // Rotate arrow to point in the computed direction
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;
            _rt.localRotation = Quaternion.Euler(0f, 0f, angle);

            // Clamp position to the compass ring
            Vector2 clampedPos = dir.normalized * compassRadius;
            _rt.anchoredPosition = clampedPos;
        }
    }
}
