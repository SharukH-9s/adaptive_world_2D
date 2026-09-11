using UnityEngine;
using UnityEngine.UI;
using AdaptiveWorld2D.Mission;

namespace AdaptiveWorld2D.UI
{
    /// <summary>
    /// Attached to the ObjectiveText UI element.
    /// Polls MissionManager each frame to keep the objective text current,
    /// and handles the animated slide-in whenever the objective changes.
    /// </summary>
    [RequireComponent(typeof(Text))]
    public class ObjectiveTextDriver : MonoBehaviour
    {
        [Header("Animation")]
        public float slideInDuration = 0.4f;
        public float displayHoldTime = 3.0f;

        private Text _text;
        private string _lastObjective = "";
        private float _holdTimer = 0f;
        private RectTransform _rect;
        private Vector2 _hiddenPos;
        private Vector2 _visiblePos;
        private bool _animating = false;

        private void Awake()
        {
            _text = GetComponent<Text>();
            _rect = GetComponent<RectTransform>();
            if (_rect != null)
            {
                _visiblePos = _rect.anchoredPosition;
                _hiddenPos  = _visiblePos + new Vector2(-300f, 0f);
            }
        }

        private void Update()
        {
            if (MissionManager.Instance == null) return;

            var mm = MissionManager.Instance;
            string current = (mm.objectiveText != null) ? mm.objectiveText.text : "";
            if (current != _lastObjective)
            {
                _lastObjective = current;
                _text.text = current;
                _holdTimer = displayHoldTime;
                _animating = true;
            }

            if (_animating && _rect != null)
            {
                _rect.anchoredPosition = Vector2.Lerp(_rect.anchoredPosition, _visiblePos, Time.deltaTime / slideInDuration * 5f);
                _holdTimer -= Time.deltaTime;
                if (_holdTimer <= 0f) _animating = false;
            }
        }
    }
}
