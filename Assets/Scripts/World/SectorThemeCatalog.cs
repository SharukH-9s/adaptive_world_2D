using System;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace AdaptiveWorld2D.World
{
    /// <summary>
    /// Centralized Theme Catalog backed by serialized Sprite references (§5.2 — Shared Tile & Theme Architecture).
    /// Provides consistent, themed Kenney sprites for both Editor builders (AdaptiveSceneBuilder)
    /// and Runtime procedural generators (SectorRuntimeBuilder).
    /// </summary>
    [CreateAssetMenu(fileName = "SectorThemeCatalog", menuName = "AdaptiveWorld/Visuals/Sector Theme Catalog")]
    public sealed class SectorThemeCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class ModuleTiles
        {
            [Tooltip("Sector module ID matching RoomGraphManifest (e.g. URBAN_HIGHWAY, SLUM_ALLEYWAYS)")]
            public string module;
            public Sprite[] roadTiles;
            public Sprite[] floorTiles;
        }

        [Header("Module Tile Sets")]
        public ModuleTiles[] modules;

        [Header("Structural Tiles")]
        public Sprite wallBrickTile;
        public Sprite wallReinforcedTile;
        public Sprite defaultRoadTile;
        public Sprite defaultFloorTile;

        // Runtime fast lookup map
        private Dictionary<string, ModuleTiles> _lookup;
        private static SectorThemeCatalog _cachedInstance;

        private void OnEnable()
        {
            BuildLookup();
        }

        private void BuildLookup()
        {
            if (_lookup != null) return;
            _lookup = new Dictionary<string, ModuleTiles>(StringComparer.OrdinalIgnoreCase);

            if (modules != null)
            {
                foreach (var m in modules)
                {
                    if (m != null && !string.IsNullOrEmpty(m.module))
                    {
                        _lookup[m.module] = m;
                    }
                }
            }
        }

        private static Sprite _fallbackRoadSprite;
        private static Sprite _fallbackWallSprite;

        private static Sprite GetFallbackRoadSprite()
        {
            if (_fallbackRoadSprite != null) return _fallbackRoadSprite;
            _fallbackRoadSprite = CreateProceduralTile(new Color(0.20f, 0.20f, 0.22f)); // Dark asphalt
            return _fallbackRoadSprite;
        }

        private static Sprite GetFallbackWallSprite()
        {
            if (_fallbackWallSprite != null) return _fallbackWallSprite;
            _fallbackWallSprite = CreateProceduralTile(new Color(0.38f, 0.28f, 0.24f)); // Brick red-brown
            return _fallbackWallSprite;
        }

        private static Sprite CreateProceduralTile(Color baseColor)
        {
            const int s = 16;
            Texture2D tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            for (int y = 0; y < s; y++)
            {
                for (int x = 0; x < s; x++)
                {
                    float noise = ((x * 7 + y * 13) % 5) * 0.02f - 0.04f;
                    Color c = new Color(
                        Mathf.Clamp01(baseColor.r + noise),
                        Mathf.Clamp01(baseColor.g + noise),
                        Mathf.Clamp01(baseColor.b + noise),
                        1f
                    );
                    tex.SetPixel(x, y, c);
                }
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 16f);
        }

        /// <summary>
        /// Gets a ground/road sprite for the given sector module.
        /// Returns the first available tile, default road tile, or procedural asphalt tile.
        /// </summary>
        public Sprite GetRoadTile(string module)
        {
            BuildLookup();
            if (!string.IsNullOrEmpty(module) && _lookup != null && _lookup.TryGetValue(module, out var m))
            {
                if (m.roadTiles != null && m.roadTiles.Length > 0 && m.roadTiles[0] != null)
                    return m.roadTiles[0];
                if (m.floorTiles != null && m.floorTiles.Length > 0 && m.floorTiles[0] != null)
                    return m.floorTiles[0];
            }

            return defaultRoadTile != null ? defaultRoadTile : GetFallbackRoadSprite();
        }

        /// <summary>
        /// Gets a floor tile for interior or non-road sectors (e.g. warehouses, plazas).
        /// </summary>
        public Sprite GetFloorTile(string module)
        {
            BuildLookup();
            if (!string.IsNullOrEmpty(module) && _lookup != null && _lookup.TryGetValue(module, out var m))
            {
                if (m.floorTiles != null && m.floorTiles.Length > 0 && m.floorTiles[0] != null)
                    return m.floorTiles[0];
                if (m.roadTiles != null && m.roadTiles.Length > 0 && m.roadTiles[0] != null)
                    return m.roadTiles[0];
            }

            return defaultFloorTile != null ? defaultFloorTile : (defaultRoadTile != null ? defaultRoadTile : GetFallbackRoadSprite());
        }

        /// <summary>
        /// Gets structural wall sprite (brick or reinforced concrete).
        /// </summary>
        public Sprite GetWallTile(bool reinforced = false)
        {
            if (reinforced && wallReinforcedTile != null)
                return wallReinforcedTile;
            if (wallBrickTile != null)
                return wallBrickTile;

            return GetFallbackWallSprite();
        }

        /// <summary>
        /// Retrieves the default catalog instance.
        /// Resolves via Resources.Load at runtime, auto-populates via AssetDatabase in Editor,
        /// or generates an in-memory textured fallback instance so visual builders never receive null.
        /// </summary>
        public static SectorThemeCatalog GetDefault()
        {
            if (_cachedInstance != null) return _cachedInstance;

            // 1. Try loading from Resources
            _cachedInstance = Resources.Load<SectorThemeCatalog>("SectorThemeCatalog");
            if (_cachedInstance != null) return _cachedInstance;

#if UNITY_EDITOR
            // 2. Try loading from known asset path
            _cachedInstance = AssetDatabase.LoadAssetAtPath<SectorThemeCatalog>("Assets/Resources/SectorThemeCatalog.asset");
            if (_cachedInstance != null) return _cachedInstance;

            string[] guids = AssetDatabase.FindAssets("t:SectorThemeCatalog");
            if (guids != null && guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                _cachedInstance = AssetDatabase.LoadAssetAtPath<SectorThemeCatalog>(path);
                if (_cachedInstance != null) return _cachedInstance;
            }

            // 3. In Editor: auto-generate and populate the asset if not yet created!
            _cachedInstance = CreateOrPopulateAsset();
            if (_cachedInstance != null) return _cachedInstance;
#endif

            // 4. Standalone / Runtime fallback in-memory instance
            _cachedInstance = CreateInstance<SectorThemeCatalog>();
            _cachedInstance.name = "SectorThemeCatalog_RuntimeFallback";
            _cachedInstance.defaultRoadTile = GetFallbackRoadSprite();
            _cachedInstance.defaultFloorTile = GetFallbackRoadSprite();
            _cachedInstance.wallBrickTile = GetFallbackWallSprite();
            _cachedInstance.wallReinforcedTile = GetFallbackWallSprite();

            return _cachedInstance;
        }

#if UNITY_EDITOR
        /// <summary>
        /// Automatically generates and links the SectorThemeCatalog ScriptableObject asset
        /// with references to the existing Kenney top-down shooter tiles.
        /// </summary>
        // NOTE: This is intentionally a private copy of the same helper in AdaptiveSceneBuilder.
        // SectorThemeCatalog lives in the runtime assembly (Assets/Scripts/World/) and cannot
        // reference AdaptiveSceneBuilder which is in the editor-only assembly (Assets/Scripts/Editor/).
        // Unity's assembly boundary applies even inside #if UNITY_EDITOR blocks.
        private static bool EnsureFullRectSpriteImporter(TextureImporter importer)
        {
            if (importer == null) return false;

            TextureImporterSettings settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            if (importer.textureType == TextureImporterType.Sprite &&
                settings.spriteMeshType == SpriteMeshType.FullRect)
            {
                return false;
            }

            importer.textureType = TextureImporterType.Sprite;
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
            return true;
        }

        public static SectorThemeCatalog CreateOrPopulateAsset()
        {
            const string assetDir = "Assets/Resources";
            const string assetPath = "Assets/Resources/SectorThemeCatalog.asset";

            if (!AssetDatabase.IsValidFolder(assetDir))
            {
                AssetDatabase.CreateFolder("Assets", "Resources");
            }

            var catalog = AssetDatabase.LoadAssetAtPath<SectorThemeCatalog>(assetPath);
            if (catalog == null)
            {
                catalog = CreateInstance<SectorThemeCatalog>();
                AssetDatabase.CreateAsset(catalog, assetPath);
            }

            Func<string, Sprite> loadTile = (filename) =>
            {
                string path = $"Assets/assets/kenney_topdown_shooter/PNG/Tiles/{filename}";
                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                EnsureFullRectSpriteImporter(importer);

                Sprite s = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (s == null)
                {
                    string[] guids = AssetDatabase.FindAssets($"{System.IO.Path.GetFileNameWithoutExtension(filename)} t:Sprite");
                    if (guids != null && guids.Length > 0)
                        s = AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GUIDToAssetPath(guids[0]));
                }
                return s;
            };

            catalog.defaultRoadTile = loadTile("tile_01.png");
            catalog.defaultFloorTile = loadTile("tile_43.png");
            catalog.wallBrickTile = loadTile("tile_129.png");
            catalog.wallReinforcedTile = loadTile("tile_131.png");

            catalog.modules = new[]
            {
                new ModuleTiles
                {
                    module = "URBAN_HIGHWAY",
                    roadTiles = new[] { loadTile("tile_01.png"), loadTile("tile_02.png") },
                    floorTiles = new[] { loadTile("tile_01.png") }
                },
                new ModuleTiles
                {
                    module = "OPEN_STREET",
                    roadTiles = new[] { loadTile("tile_01.png"), loadTile("tile_02.png") },
                    floorTiles = new[] { loadTile("tile_01.png") }
                },
                new ModuleTiles
                {
                    module = "SLUM_ALLEYWAYS",
                    roadTiles = new[] { loadTile("tile_19.png"), loadTile("tile_20.png") },
                    floorTiles = new[] { loadTile("tile_19.png") }
                },
                new ModuleTiles
                {
                    module = "INDUSTRIAL_DEPOT",
                    roadTiles = new[] { loadTile("tile_43.png"), loadTile("tile_44.png") },
                    floorTiles = new[] { loadTile("tile_43.png") }
                },
                new ModuleTiles
                {
                    module = "WAREHOUSE_INTERIOR",
                    roadTiles = new[] { loadTile("tile_43.png") },
                    floorTiles = new[] { loadTile("tile_43.png"), loadTile("tile_44.png") }
                },
                new ModuleTiles
                {
                    module = "FORTIFIED_PLAZA",
                    roadTiles = new[] { loadTile("tile_88.png"), loadTile("tile_95.png") },
                    floorTiles = new[] { loadTile("tile_88.png") }
                },
                new ModuleTiles
                {
                    module = "WATERFRONT_DOCKS",
                    roadTiles = new[] { loadTile("tile_159.png"), loadTile("tile_160.png") },
                    floorTiles = new[] { loadTile("tile_159.png") }
                },
                new ModuleTiles
                {
                    module = "SECURITY_COMPOUND",
                    roadTiles = new[] { loadTile("tile_43.png"), loadTile("tile_131.png") },
                    floorTiles = new[] { loadTile("tile_43.png") }
                },
                new ModuleTiles
                {
                    module = "VACANT_LOT",
                    roadTiles = new[] { loadTile("tile_17.png"), loadTile("tile_18.png") },
                    floorTiles = new[] { loadTile("tile_17.png") }
                }
            };

            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            Debug.Log($"[SectorThemeCatalog] Successfully configured and saved {assetPath} with Kenney tile bindings.");
            return catalog;
        }

        [InitializeOnLoadMethod]
        private static void AutoPopulateOnEditorStartup()
        {
            EditorApplication.delayCall += () =>
            {
                if (!Application.isPlaying && !System.IO.File.Exists("Assets/Resources/SectorThemeCatalog.asset"))
                {
                    CreateOrPopulateAsset();
                }
            };
        }
#endif
    }
}
