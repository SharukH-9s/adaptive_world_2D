using UnityEngine;
using UnityEngine.UI;
using AdaptiveWorld2D.Mission;

namespace AdaptiveWorld2D.UI
{
    /// <summary>
    /// Main HUD controller attached to UICanvas.
    /// Drives objective text, kill counter, ammo display, and alert status.
    /// Subscribes to MissionManager events for state-driven UI updates.
    /// </summary>
    public class HUDController : MonoBehaviour
    {
        [Header("Objective")]
        public Text objectiveText;
        public Text subObjectiveText;

        [Header("Stats")]
        public Text killCountText;
        public Text ammoText;
        public Text alertStatusText;

        [Header("Boss Health")]
        public GameObject bossHealthBarRoot;
        public Slider bossHealthSlider;
        public Text bossNameText;

        [Header("Panels")]
        public GameObject briefingPanel;
        public GameObject debriefingPanel;

        private static HUDController _instance;
        public static HUDController Instance => _instance;

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(this); return; }
            _instance = this;
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        private void Start()
        {
            HideBriefing();
            HideDebriefing();
            if (bossHealthBarRoot != null) bossHealthBarRoot.SetActive(false);
            RefreshObjective();
        }

        // ── Objective ──────────────────────────────────────────────

        public void SetObjective(string main, string sub = "")
        {
            if (objectiveText != null)  objectiveText.text  = main;
            if (subObjectiveText != null) subObjectiveText.text = sub;
        }

        public void RefreshObjective()
        {
            if (MissionManager.Instance == null) return;
            var mm = MissionManager.Instance;
            string obj = (mm.objectiveText != null) ? mm.objectiveText.text : "";
            SetObjective(obj);
        }

        // ── Kill Counter ───────────────────────────────────────────

        public void UpdateKillCount(int kills, int total)
        {
            if (killCountText != null)
                killCountText.text = $"Eliminated: {kills}/{total}";
        }

        // ── Ammo ───────────────────────────────────────────────────

        public void UpdateAmmo(int current, int reserve)
        {
            if (ammoText != null)
                ammoText.text = $"{current} / {reserve}";
        }

        // ── Alert Status ───────────────────────────────────────────

        public void SetAlertStatus(string status)
        {
            if (alertStatusText == null) return;
            alertStatusText.text = status;
            alertStatusText.color = status switch
            {
                "ALERT"    => new Color(1f, 0.2f, 0.2f),
                "ALARMED"  => new Color(1f, 0.55f, 0f),
                "SEARCHING"=> new Color(1f, 0.9f, 0f),
                _          => new Color(0.5f, 1f, 0.5f), // clear
            };
        }

        // ── Boss Health Bar ────────────────────────────────────────

        public void ShowBossHealth(string bossName, float normalised)
        {
            if (bossHealthBarRoot == null) return;
            bossHealthBarRoot.SetActive(true);
            if (bossNameText   != null) bossNameText.text = bossName;
            if (bossHealthSlider != null) bossHealthSlider.value = Mathf.Clamp01(normalised);
        }

        public void UpdateBossHealth(float normalised)
        {
            if (bossHealthSlider != null) bossHealthSlider.value = Mathf.Clamp01(normalised);
            if (normalised <= 0f && bossHealthBarRoot != null)
                bossHealthBarRoot.SetActive(false);
        }

        // ── Briefing Panel ─────────────────────────────────────────

        public void ShowBriefing(string missionTitle, string bodyText)
        {
            if (briefingPanel == null) return;
            briefingPanel.SetActive(true);
            var texts = briefingPanel.GetComponentsInChildren<Text>();
            if (texts.Length >= 1) texts[0].text = missionTitle;
            if (texts.Length >= 2) texts[1].text = bodyText;
        }

        public void HideBriefing()
        {
            if (briefingPanel != null) briefingPanel.SetActive(false);
        }

        // ── Debriefing Panel ───────────────────────────────────────

        public void ShowDebriefing(string summary)
        {
            if (debriefingPanel == null) return;
            debriefingPanel.SetActive(true);
            var t = debriefingPanel.GetComponentInChildren<Text>();
            if (t != null) t.text = summary;
        }

        public void HideDebriefing()
        {
            if (debriefingPanel != null) debriefingPanel.SetActive(false);
        }
    }
}
