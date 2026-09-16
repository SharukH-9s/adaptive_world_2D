using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using AdaptiveWorld2D.World;
using AdaptiveWorld2D.Player;
using AdaptiveWorld2D.AI;
using AdaptiveWorld2D.Mission;

namespace AdaptiveWorld2D.Director
{
    /// <summary>
    /// Scene-facing coordinator component for Map 2 (Architecture Improvement Plan, Phase 6).
    /// Preserves all serialized Inspector properties, scene lifecycle hooks, and static helpers
    /// while delegating manifest application to dedicated world systems.
    /// </summary>
    public class WorldManifestApplier : MonoBehaviour
    {
        [Header("Target Boss Setup")]
        public GameObject targetPrefab;
        public GameObject currentTargetInstance;
        [Tooltip("Named anchor transforms for possible target locations: warehouse_interior, open_plaza, far_docks, fortified_building")]
        public List<LocationAnchor> locationAnchors = new List<LocationAnchor>();

        [Header("Guards Setup")]
        public GameObject guardPrefab;
        public List<Transform> guardSpawnPoints = new List<Transform>();

        [Header("Obstacles & Roadblocks")]
        public List<NamedBarricade> routeBarricades = new List<NamedBarricade>();
        public List<GameObject> drivableCars = new List<GameObject>();

        [Header("Environment & Weather")]
        public Component globalLight2D; // Assigned at runtime; accepts URP Light2D if present
        public ParticleSystem rainParticles;
        public ParticleSystem fogParticles;

        [System.Serializable]
        public struct LocationAnchor
        {
            public string locationId;
            public Transform spawnTransform;
        }

        [System.Serializable]
        public struct NamedBarricade
        {
            public string routeId;
            public GameObject barricadeObject;
        }

        /// <summary>
        /// The active <see cref="WorldManifestApplier"/> in the current scene, registered in
        /// <c>Awake</c> so <see cref="AdaptiveDirectorClient"/> can resolve it without a
        /// scene search (Architecture Improvement Plan, Phase 4).
        /// </summary>
        internal static WorldManifestApplier SceneInstance { get; private set; }

        private ManifestCoordinator _coordinator;
        private ulong _bootstrappedSceneHandle;

        /// <summary>
        /// The underlying coordinator orchestrating the world systems.
        /// </summary>
        public ManifestCoordinator Coordinator
        {
            get
            {
                if (_coordinator == null)
                    _coordinator = new ManifestCoordinator(this);
                return _coordinator;
            }
        }

        /// <summary>
        /// Currently active guards in the scene.
        /// </summary>
        public List<GameObject> ActiveGuards => Coordinator.ActiveGuards;

        /// <summary>
        /// Current extraction zone instance in the scene.
        /// </summary>
        public GameObject ExtractionZone => Coordinator.ExtractionZone;

        private void Awake()
        {
            SceneInstance = this;
            _coordinator = new ManifestCoordinator(this);
        }

        private void Start()
        {
            BootstrapForMap2();
        }

        public void BootstrapForMap2(UnityEngine.SceneManagement.Scene targetScene = default)
        {
            UnityEngine.SceneManagement.Scene currentScene = targetScene.IsValid()
                ? targetScene
                : (gameObject.scene.IsValid() ? gameObject.scene : UnityEngine.SceneManagement.SceneManager.GetActiveScene());

            bool isMap2 = (!string.IsNullOrEmpty(currentScene.name) && currentScene.name.IndexOf("Map2", StringComparison.OrdinalIgnoreCase) >= 0)
                          || IsMap2Scene(gameObject);

            if (!isMap2)
                return;

            ulong sceneHandle = currentScene.handle.GetRawData();
            if (_bootstrappedSceneHandle == sceneHandle && _bootstrappedSceneHandle != 0)
                return;

            _bootstrappedSceneHandle = sceneHandle;
            if (AdaptiveDirectorClient.Instance != null)
            {
                AdaptiveDirectorClient.Instance.OnManifestReceived -= ApplyManifest;
                AdaptiveDirectorClient.Instance.OnManifestReceived += ApplyManifest;
            }

            // Delay by one frame so MissionManager.Start() finishes first
            StartCoroutine(ApplyManifestNextFrame());
        }

        private System.Collections.IEnumerator ApplyManifestNextFrame()
        {
            yield return null; // wait one frame

            if (AdaptiveDirectorClient.latestManifest != null)
            {
                Debug.Log("[WorldManifestApplier] Applying cached in-memory manifest on scene load.");
                ApplyManifest(AdaptiveDirectorClient.latestManifest);
            }
            else
            {
                // Try the on-disk cache before hitting the network
                MissionManifest diskCached = AdaptiveDirectorClient.LoadCachedManifest();
                if (diskCached != null)
                {
                    Debug.Log("[WorldManifestApplier] Applying manifest loaded from disk cache.");
                    AdaptiveDirectorClient.latestManifest = diskCached;
                    ApplyManifest(diskCached);
                }
                else if (AdaptiveDirectorClient.Instance != null)
                {
                    Debug.Log("[WorldManifestApplier] No cached manifest — requesting fresh manifest for Map2 from Director...");
                    AdaptiveDirectorClient.Instance.RequestMission2Manifest(GetMissionSeed(), null);
                    StartCoroutine(ApplyFallbackIfManifestMissing(5f));
                }
                else
                {
                    Debug.LogWarning("[WorldManifestApplier] No Director Client available — applying default fallback target & guards.");
                    ApplyDefaultFallback();
                }
            }
        }

        private IEnumerator ApplyFallbackIfManifestMissing(float timeout)
        {
            yield return new WaitForSeconds(timeout);

            if (IsMap2Scene() && AdaptiveDirectorClient.latestManifest == null)
            {
                Debug.LogWarning("[WorldManifestApplier] Director manifest did not arrive in time; applying the default Map 2 fallback.");
                ApplyDefaultFallback();
            }
        }

        private void ApplyDefaultFallback()
        {
            ApplyManifest(FallbackManifestProvider.CreateDefaultFallback());
        }

        private void OnDestroy()
        {
            if (AdaptiveDirectorClient.Instance != null)
            {
                AdaptiveDirectorClient.Instance.OnManifestReceived -= ApplyManifest;
            }

            if (SceneInstance == this)
                SceneInstance = null;
        }

        private static int GetMissionSeed()
        {
            return Mathf.Abs(DateTime.UtcNow.Ticks.GetHashCode());
        }

        /// <summary>Returns true when the context or active/loaded scene is Map 2.</summary>
        public static bool IsMap2Scene(GameObject context = null)
        {
            if (context != null && context.scene.IsValid() && !string.IsNullOrEmpty(context.scene.name))
            {
                if (context.scene.name.IndexOf("Map2", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            var activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (!string.IsNullOrEmpty(activeScene.name) &&
                activeScene.name.IndexOf("Map2", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                var s = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (s.isLoaded && !string.IsNullOrEmpty(s.name) &&
                    s.name.IndexOf("Map2", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Applies a manifest to the current scene by delegating to the <see cref="ManifestCoordinator"/>.
        /// </summary>
        public void ApplyManifest(MissionManifest manifest)
        {
            if (manifest == null) return;
            if (!IsMap2Scene()) return;

            Coordinator.ApplyManifest(
                manifest,
                ref targetPrefab,
                ref guardPrefab,
                ref globalLight2D,
                ref rainParticles,
                ref fogParticles,
                locationAnchors,
                routeBarricades,
                drivableCars,
                guardSpawnPoints);

            currentTargetInstance = Coordinator.CurrentTargetInstance;
        }

        /// <summary>
        /// Thin wrapper delegating to SectorRuntimeBuilder so that World-namespace callers
        /// do not depend on the Director namespace solely for a white pixel sprite.
        /// </summary>
        public static Sprite GetUniversalWhiteSprite()
            => AdaptiveWorld2D.World.SectorRuntimeBuilder.GetUniversalWhiteSprite();

        /// <summary>
        /// Procedurally creates a fully functional Player in Map 2 if not present in the scene.
        /// </summary>
        public static GameObject CreateProceduralPlayer(Vector3 pos)
        {
            GameObject player = new GameObject("Player");
            player.transform.position = pos;
            player.tag = "Player";

            SpriteRenderer sr = player.AddComponent<SpriteRenderer>();
            sr.sprite = TargetSpawner.CreateCircleSprite(0.35f, new Color(0.2f, 0.6f, 1f));
            sr.sortingOrder = 5;

            Rigidbody2D rb = player.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.freezeRotation = true;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            CircleCollider2D col = player.AddComponent<CircleCollider2D>();
            col.radius = 0.35f;

            var ctrl = player.AddComponent<PlayerController>();
            ctrl.walkSpeed = 3.5f;
            ctrl.runSpeed = 6.0f;
            ctrl.driveSpeed = 12.0f;
            ctrl.roadLayer = LayerMask.GetMask("Road");
            ctrl.alleyLayer = LayerMask.GetMask("Alley");
            ctrl.buildingLayer = LayerMask.GetMask("Building");

            var combat = player.AddComponent<PlayerCombat>();
            combat.enemyLayer = LayerMask.GetMask("Enemy");
            combat.hasPistol = true;
            combat.pistolAmmo = 24;
            combat.meleeRange = 1.5f;
            combat.meleeDamage = 40;
            combat.gunRange = 15f;
            combat.gunDamage = 35;
            combat.fireRate = 0.25f;

            GameObject fp = new GameObject("FirePoint");
            fp.transform.SetParent(player.transform);
            fp.transform.localPosition = new Vector3(0.2f, 0.4f, 0f);
            combat.firePoint = fp.transform;

            var interaction = player.AddComponent<PlayerInteraction>();
            interaction.interactRadius = 2.0f;
            interaction.interactableLayer = LayerMask.GetMask("Interactable");

            MissionManager mm = MissionManager.Instance;
            if (mm == null)
            {
#if UNITY_2023_1_OR_NEWER
                mm = FindAnyObjectByType<MissionManager>();
#else
                mm = FindObjectOfType<MissionManager>();
#endif
            }
            if (mm != null)
            {
                mm.playerCombat = combat;
                mm.playerController = ctrl;
            }

            Camera cam = Camera.main;
            if (cam != null)
            {
                var camFollow = cam.GetComponent<CameraFollow2D>() ?? cam.gameObject.AddComponent<CameraFollow2D>();
                camFollow.target = player.transform;
            }

            Debug.Log($"[WorldManifestApplier] Procedural Player created and wired at {pos}.");
            return player;
        }

        private static Font GetDefaultFont()
        {
            Font f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (f == null) f = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (f == null) f = Font.CreateDynamicFontFromOSFont("Arial", 14);
            return f;
        }

        public static void CreateRuntimeUI(GameObject gm, MissionManager mm,
            string objectiveMsg = "MISSION 2: Eliminate Viktor. Adapt to enemy defenses.")
        {
            GameObject canvasGO = new GameObject("UICanvas");
            Canvas canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<UnityEngine.UI.GraphicRaycaster>();

            Font font = GetDefaultFont();

            // Objective text
            GameObject objGO = new GameObject("ObjectiveText");
            objGO.transform.SetParent(canvasGO.transform, false);
            Text objText = objGO.AddComponent<Text>();
            objText.text = objectiveMsg;
            objText.font = font;
            objText.fontSize = 16;
            objText.color = Color.white;
            objText.alignment = TextAnchor.LowerCenter;
            RectTransform objRT = objGO.GetComponent<RectTransform>();
            objRT.anchorMin = new Vector2(0f, 0f);
            objRT.anchorMax = new Vector2(1f, 0.1f);
            objRT.offsetMin = new Vector2(10f, 10f);
            objRT.offsetMax = new Vector2(-10f, 0f);

            // Briefing panel
            GameObject panelGO = new GameObject("BriefingPanel");
            panelGO.transform.SetParent(canvasGO.transform, false);
            var panelImg = panelGO.AddComponent<Image>();
            panelImg.color = new Color(0f, 0f, 0f, 0.85f);
            RectTransform panelRT = panelGO.GetComponent<RectTransform>();
            panelRT.anchorMin = new Vector2(0.1f, 0.25f);
            panelRT.anchorMax = new Vector2(0.9f, 0.85f);
            panelRT.offsetMin = Vector2.zero;
            panelRT.offsetMax = Vector2.zero;

            GameObject debriefGO = new GameObject("DebriefingText");
            debriefGO.transform.SetParent(panelGO.transform, false);
            Text debriefText = debriefGO.AddComponent<Text>();
            debriefText.text = "";
            debriefText.font = font;
            debriefText.fontSize = 14;
            debriefText.color = Color.white;
            debriefText.alignment = TextAnchor.MiddleCenter;
            RectTransform debriefRT = debriefGO.GetComponent<RectTransform>();
            debriefRT.anchorMin = Vector2.zero;
            debriefRT.anchorMax = Vector2.one;
            debriefRT.offsetMin = new Vector2(20f, 20f);
            debriefRT.offsetMax = new Vector2(-20f, -20f);

            panelGO.SetActive(false);

            if (mm != null)
            {
                mm.objectiveText = objText;
                mm.debriefingText = debriefText;
                mm.briefingPanel = panelGO;
            }
        }
    }
}