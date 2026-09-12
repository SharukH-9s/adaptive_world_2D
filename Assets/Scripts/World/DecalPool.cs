using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using AdaptiveWorld2D.Director;

namespace AdaptiveWorld2D.World
{
    /// <summary>
    /// Bounded circular buffer decal pool (§5.1 — Bounded Environmental Polish & Decals).
    /// Enforces a strict ceiling of 50 active decals in the scene to prevent memory leaks and draw call bloat.
    /// Manages blood splatters, shell casings, explosion scorch marks, and wall debris on the "Decals" sorting layer.
    /// Fading decals are explicitly counted against the budget so the total in-scene count never exceeds 50.
    /// </summary>
    public class DecalPool : MonoBehaviour
    {
        public const int MAX_DECALS = 50;

        private static DecalPool _instance;
        public static DecalPool Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<DecalPool>();
                    if (_instance == null)
                    {
                        GameObject go = new GameObject("_DecalPool");
                        _instance = go.AddComponent<DecalPool>();
                        DontDestroyOnLoad(go);
                    }
                }
                return _instance;
            }
        }

        private readonly Queue<GameObject> _activeDecals = new Queue<GameObject>();
        private readonly LinkedList<GameObject> _fadingDecals = new LinkedList<GameObject>();
        private readonly Dictionary<GameObject, LinkedListNode<GameObject>> _fadingNodes = new Dictionary<GameObject, LinkedListNode<GameObject>>();

        private static Sprite _blobSprite;
        private static Sprite _casingSprite;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            ClearSceneDecals();
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
                CleanupStaticTextures();
            }
        }

        private void OnApplicationQuit()
        {
            CleanupStaticTextures();
        }

        /// <summary>
        /// Explicitly frees procedurally allocated unmanaged Texture2D buffers
        /// to ensure clean native memory teardown.
        /// </summary>
        public static void CleanupStaticTextures()
        {
            if (_blobSprite != null)
            {
                DestroyTextureObject(_blobSprite.texture);
                DestroyTextureObject(_blobSprite);
                _blobSprite = null;
            }

            if (_casingSprite != null)
            {
                DestroyTextureObject(_casingSprite.texture);
                DestroyTextureObject(_casingSprite);
                _casingSprite = null;
            }
        }

        private static void DestroyTextureObject(Object target)
        {
            if (target == null) return;

            if (Application.isPlaying)
                Destroy(target);
            else
                DestroyImmediate(target);
        }

        private void ClearSceneDecals()
        {
            foreach (GameObject decal in _activeDecals)
            {
                if (decal != null) Destroy(decal);
            }

            foreach (GameObject decal in _fadingDecals)
            {
                if (decal != null) Destroy(decal);
            }

            _activeDecals.Clear();
            _fadingDecals.Clear();
            _fadingNodes.Clear();
        }

        /// <summary>
        /// Registers an external decal (e.g. rubble from DestructibleWall) into the pool
        /// so it respects the maximum active decal budget.
        /// </summary>
        public void RegisterDecal(GameObject decal)
        {
            if (decal == null) return;
            EnforceBudget();
            _activeDecals.Enqueue(decal);
        }

        /// <summary>
        /// Spawns a blood splatter decal at the victim's position.
        /// </summary>
        public GameObject SpawnBlood(Vector3 position, Vector2 hitDirection)
        {
            EnforceBudget();

            GameObject blood = new GameObject("BloodDecal");
            blood.transform.SetParent(transform);
            blood.transform.position = new Vector3(position.x, position.y, 0.05f);

            // Random splatter rotation with bias in the direction of the hit
            float baseAngle = Mathf.Atan2(hitDirection.y, hitDirection.x) * Mathf.Rad2Deg;
            float randomAngle = baseAngle + Random.Range(-45f, 45f);
            blood.transform.rotation = Quaternion.Euler(0f, 0f, randomAngle);

            SpriteRenderer sr = blood.AddComponent<SpriteRenderer>();
            sr.sprite = GetBlobSprite();
            sr.color = new Color(0.48f, 0.06f, 0.06f, Random.Range(0.75f, 0.90f));
            sr.sortingLayerName = "Decals";
            sr.sortingOrder = -2;

            float scaleX = Random.Range(0.45f, 0.85f);
            float scaleY = Random.Range(0.25f, 0.55f);
            blood.transform.localScale = new Vector3(scaleX, scaleY, 1f);

            _activeDecals.Enqueue(blood);
            return blood;
        }

        /// <summary>
        /// Spawns an animated ejected brass shell casing that spins and settles on the ground (§5.2).
        /// Uses a shaded pixel-art brass casing sprite with rim, specular sheen, and hollow bore.
        /// </summary>
        public void SpawnCasing(Vector3 position, Vector2 ejectDirection)
        {
            EnforceBudget();

            GameObject casing = new GameObject("ShellCasing");
            casing.transform.SetParent(transform);
            casing.transform.position = new Vector3(position.x, position.y, 0.04f);

            SpriteRenderer sr = casing.AddComponent<SpriteRenderer>();
            sr.sprite = GetCasingSprite();
            sr.color = Color.white;
            sr.sortingLayerName = "Decals";
            sr.sortingOrder = -1;

            casing.transform.localScale = new Vector3(0.12f, 0.06f, 1f);

            _activeDecals.Enqueue(casing);
            StartCoroutine(AnimateCasing(casing, ejectDirection));
        }

        /// <summary>
        /// Spawns an explosion burn/scorch decal.
        /// </summary>
        public GameObject SpawnScorch(Vector3 position, float radius = 2.0f)
        {
            EnforceBudget();

            GameObject scorch = new GameObject("ScorchDecal");
            scorch.transform.SetParent(transform);
            scorch.transform.position = new Vector3(position.x, position.y, 0.05f);
            scorch.transform.rotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));

            SpriteRenderer sr = scorch.AddComponent<SpriteRenderer>();
            sr.sprite = GetBlobSprite();
            sr.color = new Color(0.08f, 0.08f, 0.09f, 0.82f); // Deep char black
            sr.sortingLayerName = "Decals";
            sr.sortingOrder = -3;

            scorch.transform.localScale = new Vector3(radius * 1.2f, radius * 1.1f, 1f);

            _activeDecals.Enqueue(scorch);
            return scorch;
        }

        private IEnumerator AnimateCasing(GameObject casing, Vector2 ejectDir)
        {
            Vector2 vel = ejectDir.normalized * Random.Range(1.8f, 3.2f);
            vel += Vector2.Perpendicular(ejectDir).normalized * Random.Range(-1.2f, 1.2f);

            float angularVel = Random.Range(-720f, 720f);
            float duration = 0.22f;
            float elapsed = 0f;

            while (elapsed < duration && casing != null)
            {
                elapsed += Time.deltaTime;
                casing.transform.position += (Vector3)(vel * Time.deltaTime);
                casing.transform.Rotate(0f, 0f, angularVel * Time.deltaTime);
                vel = Vector2.Lerp(vel, Vector2.zero, 12f * Time.deltaTime);
                yield return null;
            }
        }

        /// <summary>
        /// Strict budget enforcement: ensures (_activeDecals + _fadingDecals) never exceeds MAX_DECALS.
        /// If at capacity, any existing fading decal is immediately culled, and the oldest active decal begins fading.
        /// </summary>
        private void EnforceBudget()
        {
            CleanDeadReferences();

            while (_activeDecals.Count + _fadingDecals.Count >= MAX_DECALS)
            {
                // 1. If decals are already fading, immediately destroy the oldest fading decal (Deterministic FIFO)
                if (_fadingDecals.Count > 0)
                {
                    var firstNode = _fadingDecals.First;
                    GameObject oldestFading = firstNode.Value;
                    _fadingDecals.RemoveFirst();
                    _fadingNodes.Remove(oldestFading);

                    if (oldestFading != null)
                    {
                        Destroy(oldestFading);
                    }
                }
                else if (_activeDecals.Count > 0)
                {
                    // 2. Otherwise transition the oldest active decal to fading
                    GameObject oldest = _activeDecals.Dequeue();
                    if (oldest != null)
                    {
                        var node = _fadingDecals.AddLast(oldest);
                        _fadingNodes[oldest] = node;
                        StartCoroutine(FadeAndDestroy(oldest, 0.4f));
                    }
                }
                else
                {
                    break;
                }
            }
        }

        private void CleanDeadReferences()
        {
            var node = _fadingDecals.First;
            while (node != null)
            {
                var next = node.Next;
                if (node.Value == null)
                {
                    _fadingNodes.Remove(node.Value);
                    _fadingDecals.Remove(node);
                }
                node = next;
            }
        }

        private void RemoveFading(GameObject decal)
        {
            if (_fadingNodes.TryGetValue(decal, out var node))
            {
                _fadingDecals.Remove(node); // Strict O(1) linked-list pointer unlinking
                _fadingNodes.Remove(decal); // Strict O(1) dictionary removal
            }
        }

        private IEnumerator FadeAndDestroy(GameObject decal, float duration)
        {
            if (decal == null)
            {
                RemoveFading(decal);
                yield break;
            }

            SpriteRenderer sr = decal.GetComponent<SpriteRenderer>();
            if (sr == null)
            {
                RemoveFading(decal);
                Destroy(decal);
                yield break;
            }

            Color startCol = sr.color;
            float elapsed = 0f;

            while (elapsed < duration && decal != null)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                sr.color = new Color(startCol.r, startCol.g, startCol.b, Mathf.Lerp(startCol.a, 0f, t));
                yield return null;
            }

            if (decal != null)
            {
                RemoveFading(decal);
                Destroy(decal);
            }
        }

        private static Sprite GetBlobSprite()
        {
            if (_blobSprite != null) return _blobSprite;

            const int size = 32;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            Vector2 center = new Vector2((size - 1) / 2f, (size - 1) / 2f);
            float maxDist = (size / 2f);

            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), center) / maxDist;
                    float alpha = Mathf.Clamp01(1.0f - dist);
                    alpha = Mathf.SmoothStep(0f, 1f, alpha);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();

            _blobSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
            return _blobSprite;
        }

        /// <summary>
        /// Generates a shaded pixel-art brass casing sprite with rim, extractor groove,
        /// cylindrical metallic sheen, and hollow bore mouth.
        /// </summary>
        public static Sprite GetCasingSprite()
        {
            if (_casingSprite != null) return _casingSprite;

            const int width = 12;
            const int height = 6;
            Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;

            Color[] rowPalette = new[]
            {
                new Color(0.48f, 0.36f, 0.10f), // Bottom shadow
                new Color(0.72f, 0.56f, 0.15f), // Low midtone
                new Color(0.86f, 0.70f, 0.20f), // Core brass
                new Color(0.96f, 0.84f, 0.34f), // Specular band
                new Color(1.00f, 0.95f, 0.60f), // Bright highlight line
                new Color(0.82f, 0.65f, 0.18f)  // Top bevel edge
            };

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Color c;
                    if (x == 0) // Rim base
                    {
                        c = (y == 0 || y == height - 1) ? new Color(0.60f, 0.46f, 0.14f) : new Color(0.78f, 0.62f, 0.20f);
                    }
                    else if (x == 1) // Extractor groove indentation
                    {
                        c = new Color(0.38f, 0.28f, 0.08f);
                    }
                    else if (x == width - 1) // Open casing mouth
                    {
                        c = (y == 0 || y == height - 1) ? new Color(0.62f, 0.48f, 0.14f) : new Color(0.12f, 0.08f, 0.04f); // Hollow bore
                    }
                    else // Casing cylindrical body
                    {
                        c = rowPalette[y];
                    }
                    tex.SetPixel(x, y, c);
                }
            }
            tex.Apply();

            _casingSprite = Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100f);
            return _casingSprite;
        }
    }
}
