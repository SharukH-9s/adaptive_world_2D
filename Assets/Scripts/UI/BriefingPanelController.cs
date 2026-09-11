using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace AdaptiveWorld2D.UI
{
    /// <summary>
    /// Controls the BriefingPanel and DebriefingText UI elements.
    /// Shown at mission start / mission end with fade-in/out animations.
    /// </summary>
    public class BriefingPanelController : MonoBehaviour
    {
        [Header("Briefing")]
        public CanvasGroup briefingGroup;
        public Text        missionTitleText;
        public Text        briefingBodyText;
        public Button      dismissButton;

        [Header("Debriefing")]
        public CanvasGroup debriefingGroup;
        public Text        debriefingText;
        public Button      continueButton;

        [Header("Timing")]
        public float fadeDuration   = 0.5f;
        public float autoShowDelay  = 0.8f;  // Seconds after scene load before showing briefing

        private void Start()
        {
            SetGroupVisible(briefingGroup,  false);
            SetGroupVisible(debriefingGroup, false);

            if (dismissButton  != null) dismissButton.onClick.AddListener(HideBriefing);
            if (continueButton != null) continueButton.onClick.AddListener(HideDebriefing);

            StartCoroutine(AutoShowBriefing());
        }

        // ── Briefing ───────────────────────────────────────────────

        private IEnumerator AutoShowBriefing()
        {
            yield return new WaitForSeconds(autoShowDelay);
            // Build text from MissionManager if available
            string title = "MISSION BRIEFING";
            string body  = "Reach your objective. Stay sharp.";

            var mm = Mission.MissionManager.Instance;
            if (mm != null && mm.objectiveText != null && !string.IsNullOrEmpty(mm.objectiveText.text))
                body = mm.objectiveText.text;

            ShowBriefing(title, body);
        }

        public void ShowBriefing(string title, string body)
        {
            if (missionTitleText != null) missionTitleText.text = title;
            if (briefingBodyText != null)  briefingBodyText.text  = body;
            StartCoroutine(FadeGroup(briefingGroup, true));
        }

        public void HideBriefing() => StartCoroutine(FadeGroup(briefingGroup, false));

        // ── Debriefing ─────────────────────────────────────────────

        public void ShowDebriefing(string summary)
        {
            if (debriefingText != null) debriefingText.text = summary;
            StartCoroutine(FadeGroup(debriefingGroup, true));
        }

        public void HideDebriefing() => StartCoroutine(FadeGroup(debriefingGroup, false));

        // ── Helpers ────────────────────────────────────────────────

        private IEnumerator FadeGroup(CanvasGroup group, bool show)
        {
            if (group == null) yield break;
            group.gameObject.SetActive(true);
            float start = group.alpha;
            float end   = show ? 1f : 0f;
            float t = 0f;
            while (t < fadeDuration)
            {
                t += Time.deltaTime;
                group.alpha = Mathf.Lerp(start, end, t / fadeDuration);
                yield return null;
            }
            group.alpha = end;
            if (!show) group.gameObject.SetActive(false);
        }

        private static void SetGroupVisible(CanvasGroup g, bool visible)
        {
            if (g == null) return;
            g.alpha          = visible ? 1f : 0f;
            g.interactable   = visible;
            g.blocksRaycasts = visible;
            g.gameObject.SetActive(visible);
        }
    }
}
