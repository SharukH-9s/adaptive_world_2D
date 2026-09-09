using System;
using UnityEngine;
using UnityEngine.UI;
using AdaptiveWorld2D.Director;
using AdaptiveWorld2D.Player;

namespace AdaptiveWorld2D.Mission
{
    public class MissionManager : MonoBehaviour
    {
        public static MissionManager Instance { get; private set; }

        [Header("Mission Status")]
        public int currentMission = 1;
        public bool hasBoughtGun = false;
        public bool missionCompleted = false;

        [Header("UI Banners")]
        public Text objectiveText;
        public Text debriefingText;
        public GameObject briefingPanel;

        [Header("References")]
        public PlayerCombat playerCombat;
        public PlayerController playerController;

        private void Awake()
        {
            Instance = this;
            // Force mission number based on which scene we're in — overrides any Inspector value
            currentMission = IsMap2Scene() ? 2 : 1;
        }

        private void Start()
        {
            bool isMap2 = IsMap2Scene();

            if (playerCombat == null)
                playerCombat = FindObjectOfType<PlayerCombat>();

            // Auto-find objectiveText if Inspector slot is empty
            if (objectiveText == null)
            {
                var allTexts = FindObjectsOfType<Text>();
                foreach (var t in allTexts)
                {
                    if (t.name == "ObjectiveText" || t.gameObject.name == "ObjectiveText")
                    {
                        objectiveText = t;
                        break;
                    }
                }
                if (objectiveText == null)
                    Debug.LogWarning("[MissionManager] objectiveText not found — UI text won't update!");
            }

            // Auto-find briefingPanel if not wired
            if (briefingPanel == null)
            {
                var go = GameObject.Find("BriefingPanel");
                if (go != null) briefingPanel = go;
            }

            // Subscribe to Director manifest events so objective text updates live when manifest arrives
            if (AdaptiveDirectorClient.Instance != null)
            {
                AdaptiveDirectorClient.Instance.OnManifestReceived += OnDirectorManifestReceived;
            }

            if (!isMap2)
            {
                hasBoughtGun = false;
                missionCompleted = false;
                AdaptiveDirectorClient.latestManifest = null;

                SetObjective("MISSION 1: Reach the Underground Gun Shop and purchase a firearm.");

                if (AdaptiveDirectorClient.Instance != null)
                {
                    AdaptiveDirectorClient.Instance.currentArchetype = "GHOST";
                    AdaptiveDirectorClient.Instance.ResetDirectorSession();
                }
            }
            else // Map2
            {
                hasBoughtGun = true;
                if (playerCombat != null)
                    playerCombat.UnlockPistol(24);

                string targetLoc = AdaptiveDirectorClient.latestManifest != null && AdaptiveDirectorClient.latestManifest.target != null
                    ? AdaptiveDirectorClient.latestManifest.target.location_label
                    : "the marked location";
                SetObjective($"MISSION 2: Eliminate the Target at {targetLoc}!");
            }

            if (briefingPanel != null) briefingPanel.SetActive(false);
        }

        private void OnDirectorManifestReceived(MissionManifest manifest)
        {
            if (currentMission == 2 && manifest != null && manifest.target != null)
            {
                SetObjective($"MISSION 2: Eliminate the Target at {manifest.target.location_label}!");
            }
        }

        private void OnDestroy()
        {
            if (AdaptiveDirectorClient.Instance != null)
            {
                AdaptiveDirectorClient.Instance.OnManifestReceived -= OnDirectorManifestReceived;
            }
        }

        /// <summary>Returns true when the active scene is the Map 2 assassination mission.</summary>
        private static bool IsMap2Scene() =>
            UnityEngine.SceneManagement.SceneManager.GetActiveScene().name
                .IndexOf("Map2", StringComparison.OrdinalIgnoreCase) >= 0;

        public void SetObjective(string text)
        {
            if (objectiveText != null)
            {
                objectiveText.text = text;
            }
            Debug.Log($"[MissionManager] Objective: {text}");
        }

        public void OnZoneReached(MissionZoneTrigger zone)
        {
            if (currentMission == 1)
            {
                HandleMission1(zone);
            }
            else if (currentMission == 2)
            {
                HandleMission2(zone);
            }
        }

        private void HandleMission1(MissionZoneTrigger zone)
        {
            if (zone.zoneType == MissionZoneTrigger.ZoneType.GunShop && !hasBoughtGun)
            {
                hasBoughtGun = true;
                if (playerCombat != null) playerCombat.UnlockPistol(24);
                SetObjective("MISSION 1: Gun acquired! Proceed to Extraction Point B.");
            }
            else if (zone.zoneType == MissionZoneTrigger.ZoneType.MissionEnd)
            {
                if (hasBoughtGun)
                {
                    CompleteMission1();
                }
                else
                {
                    SetObjective("EXTRACTION LOCKED: You must buy a gun at the Underground Gun Shop first!");
                    zone.ResetTrigger(); // Reset so it can trigger again when you return with the gun!
                }
            }
        }

        private void CompleteMission1()
        {
            if (missionCompleted) return;
            missionCompleted = true;

            Debug.Log("[MissionManager] Mission 1 Complete! Contacting Adaptive Director...");
            SetObjective("MISSION 1 COMPLETE! Generating Adaptive Mission 2...");

            // Flush telemetry
            if (AdaptiveDirectorClient.Instance != null)
            {
                AdaptiveDirectorClient.Instance.FlushQueue();
                AdaptiveDirectorClient.Instance.RequestMission2Manifest(42, OnMission2Ready);
            }
        }

        private void OnMission2Ready(MissionManifest manifest)
        {
            currentMission = 2;

            System.Text.StringBuilder sb = new System.Text.StringBuilder();

            if (!string.IsNullOrEmpty(manifest.narrative_debriefing))
            {
                sb.AppendLine("<b><color=#FFD700>=== HANDLER DEBRIEFING ===</color></b>");
                sb.AppendLine(manifest.narrative_debriefing);
                sb.AppendLine();
            }

            string title = !string.IsNullOrEmpty(manifest.mission_title) ? manifest.mission_title : "NODE 02: FIRST BLOOD";
            sb.AppendLine($"<b><color=#00E5FF>=== {title.ToUpper()} ===</color></b>");

            if (!string.IsNullOrEmpty(manifest.mission_briefing))
            {
                sb.AppendLine(manifest.mission_briefing);
                sb.AppendLine();
            }

            sb.AppendLine($"<b>Target:</b> {manifest.target.location_label} ({manifest.target.armor.ToUpper()} Armor, {manifest.target.health} HP)");
            sb.AppendLine($"<b>Security:</b> {manifest.guards.count} Guards | <b>Weather:</b> {manifest.environment.ambient_label}");
            sb.AppendLine($"<b>Director Assessment:</b> Adapted for <color=#FF5555>{manifest.player_archetype}</color>");
            sb.AppendLine("\n<i>[Deploying to Next Operation in 6 seconds...]</i>");

            string briefing = sb.ToString();

            SetObjective($"MISSION 1 COMPLETE! Transitioning to {title}...");

            if (debriefingText != null) debriefingText.text = briefing;
            if (briefingPanel != null) briefingPanel.SetActive(true);

            Debug.Log($"[MissionManager] {briefing}");

            StartCoroutine(LoadMap2Routine(6.0f));
        }

        private System.Collections.IEnumerator LoadMap2Routine(float delay)
        {
            yield return new WaitForSeconds(delay);
            UnityEngine.SceneManagement.SceneManager.LoadScene("Map2");
        }

        private void HandleMission2(MissionZoneTrigger zone)
        {
            if (zone.zoneType == MissionZoneTrigger.ZoneType.TargetArea)
            {
                Debug.Log("[MissionManager] In Target Area! Engage with caution.");
            }
        }

        public void OnTargetEliminated()
        {
            missionCompleted = true;
            string victoryMsg = "MISSION 2 ACCOMPLISHED!\n" +
                                "Target eliminated. World successfully adapted to your playstyle.";
            SetObjective(victoryMsg);
            if (debriefingText != null) debriefingText.text = victoryMsg;
            if (briefingPanel != null) briefingPanel.SetActive(true);
            Debug.Log("[MissionManager] " + victoryMsg);
        }
    }
}
