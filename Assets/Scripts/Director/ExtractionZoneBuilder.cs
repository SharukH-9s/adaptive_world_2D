using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using AdaptiveWorld2D.Mission;

namespace AdaptiveWorld2D.Director
{
    /// <summary>
    /// Builds and manages the extraction zone and HUD compass indicator
    /// based on the room graph extraction sector (Architecture Improvement Plan, Phase 6).
    /// </summary>
    public static class ExtractionZoneBuilder
    {
        /// <summary>
        /// Creates and manages the extraction zone (MissionZoneTrigger.ZoneType.MissionEnd) at the
        /// center of the extraction_sector. The trigger GameObject starts DISABLED and is unlocked
        /// once MissionManager.missionCompleted == true (i.e. Viktor is eliminated).
        /// </summary>
        public static GameObject BuildExtractionZone(
            RoomGraphManifest rg,
            MonoBehaviour coroutineHost,
            ref GameObject extractionZone)
        {
            if (rg == null || string.IsNullOrEmpty(rg.extraction_sector)) return extractionZone;

            var sector = rg.GetSector(rg.extraction_sector);
            if (sector == null) return extractionZone;

            // Destroy any previously placed extraction zone
            if (extractionZone != null)
            {
                UnityEngine.Object.Destroy(extractionZone);
                extractionZone = null;
            }

            Vector3 center = new Vector3(sector.center_x, sector.center_y, 0f);

            // Build the extraction zone GameObject
            extractionZone = new GameObject("ExtractionZone_Map2");
            extractionZone.transform.position = center;

            // Visual indicator — a green glowing circle on the ground
            var sr = extractionZone.AddComponent<SpriteRenderer>();
            sr.sprite = TargetSpawner.CreateCircleSprite(1.8f, new Color(0.0f, 1.0f, 0.4f, 0.55f));
            sr.sortingOrder = -1; // Render below player/enemies

            // Trigger collider
            var col = extractionZone.AddComponent<CircleCollider2D>();
            col.isTrigger = true;
            col.radius = 2.0f;

            // MissionZoneTrigger component
            var trigger = extractionZone.AddComponent<MissionZoneTrigger>();
            trigger.zoneType = MissionZoneTrigger.ZoneType.MissionEnd;
            trigger.zoneName = "Extraction Point Alpha";
            trigger.requireInteractButton = false;
            trigger.triggerOnce = true;

            // Start LOCKED — only unlocked after missionCompleted
            extractionZone.SetActive(false);

            // Start polling coroutine to unlock the zone when Viktor dies
            if (coroutineHost != null)
            {
                coroutineHost.StartCoroutine(WaitAndEnableExtractionZone(extractionZone));
            }

            Debug.Log($"[ExtractionZoneBuilder] Extraction zone placed at sector '{rg.extraction_sector}' ({center}) — LOCKED until target eliminated.");
            return extractionZone;
        }

        private static IEnumerator WaitAndEnableExtractionZone(GameObject zone)
        {
            var wait = new WaitForSeconds(0.5f);
            while (true)
            {
                yield return wait;

                if (zone == null) yield break;

                if (MissionManager.Instance != null && MissionManager.Instance.missionCompleted)
                {
                    zone.SetActive(true);

                    // Animate unlock: flash the extraction zone sprite bright green
                    var sr = zone.GetComponent<SpriteRenderer>();
                    if (sr != null)
                    {
                        sr.color = new Color(0.0f, 1.0f, 0.4f, 0.9f);
                    }

                    Debug.Log("[ExtractionZoneBuilder] Extraction zone UNLOCKED! Viktor has been eliminated.");

                    // Spawn the HUD extraction compass pointer
                    SpawnExtractionCompass(zone.transform.position, zone);

                    yield break;
                }
            }
        }

        public static void SpawnExtractionCompass(Vector3 extractionWorldPos, GameObject extractionZone)
        {
#if UNITY_2023_1_OR_NEWER
            var canvas = UnityEngine.Object.FindAnyObjectByType<Canvas>();
#else
            var canvas = UnityEngine.Object.FindObjectOfType<Canvas>();
#endif
            if (canvas == null)
            {
                Debug.LogWarning("[ExtractionZoneBuilder] No Canvas found — cannot spawn extraction compass.");
                return;
            }

            // Create the compass container
            GameObject compassGO = new GameObject("ExtractionCompass");
            compassGO.transform.SetParent(canvas.transform, false);

            var rt = compassGO.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(28f, 40f);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);

            var img = compassGO.AddComponent<Image>();
            img.color = new Color(0.0f, 1.0f, 0.35f, 0.9f);
            img.sprite = CreateArrowSprite();
            img.raycastTarget = false;

            // Label
            GameObject labelGO = new GameObject("CompassLabel");
            labelGO.transform.SetParent(compassGO.transform, false);
            var labelRT = labelGO.AddComponent<RectTransform>();
            labelRT.sizeDelta = new Vector2(90f, 18f);
            labelRT.anchoredPosition = new Vector2(0f, -22f);
            var labelText = labelGO.AddComponent<Text>();
            labelText.text = "EXTRACT";
            labelText.fontSize = 10;
            labelText.fontStyle = FontStyle.Bold;
            labelText.color = new Color(0.0f, 1.0f, 0.35f, 0.9f);
            labelText.alignment = TextAnchor.MiddleCenter;

            // Attach the tracker behaviour
            var tracker = compassGO.AddComponent<ExtractionCompassTracker>();
            tracker.extractionWorldPos = extractionWorldPos;
            tracker.extractionZone = extractionZone;
            tracker.compassRadius = Mathf.Min(Screen.width, Screen.height) * 0.38f;

            Debug.Log("[ExtractionZoneBuilder] Extraction compass spawned.");
        }

        public static Sprite CreateArrowSprite()
        {
            int w = 28, h = 40;
            Texture2D tex = new Texture2D(w, h);
            Color clear = Color.clear;
            Color white = Color.white;

            for (int y = 0; y < h; y++)
            {
                float halfWidth = (w * 0.5f) * (1f - (float)y / (h - 1));
                int left = Mathf.RoundToInt(w * 0.5f - halfWidth);
                int right = Mathf.RoundToInt(w * 0.5f + halfWidth);
                for (int x = 0; x < w; x++)
                    tex.SetPixel(x, y, (x >= left && x <= right) ? white : clear);
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), w);
        }
    }
}
