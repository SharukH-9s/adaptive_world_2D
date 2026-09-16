#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using AdaptiveWorld2D.Player;
using AdaptiveWorld2D.Director;
using AdaptiveWorld2D.Mission;
using AdaptiveWorld2D.World;
using AdaptiveWorld2D.AI;

/// <summary>
/// AdaptiveSceneBuilder — One-click scene setup for AdaptiveWorld2D.
///
/// Menu Items:
///   AdaptiveWorld > Build Map 1 — Full Phase 2 City (Pauper Streets)
///   AdaptiveWorld > Build Map 2 — Adaptive Assassination Foundation
///
/// MAP 1 LAYOUT (60 x 40 units, X: -30 to +30, Y: -20 to +20):
///
/// [Y:+20] ┌──────────────────────────────────────────────────────────────┐
///         │  NORTH INDUSTRIAL SECTOR                                     │
///         │  • 2 Warehouses  • SWAT Guard  • 2 Fuel Barrels              │
///         │  • Cash Briefcase (optional loot)                            │
/// [Y: +6] ├──────────────────────────────────────────────────────────────┤
///         │  CENTRAL BOULEVARD (4-lane asphalt highway, Y: -3 to +3)    │
///         │  • 2 Patrol Police • 2 Civilian Sedans • Police Cruiser      │
///         │  • 2 Concrete Roadblocks (East checkpoint)                  │
///         │  • 3 Walking Civilians                                       │
/// [Y:  0] │  PLAYER SPAWN [-25, 0]  |  GUN SHOP [0, -7]  |              │
///         │  EXTRACTION [+28, 0]                                         │
/// [Y: -6] ├──────────────────────────────────────────────────────────────┤
///         │  SOUTH ALLEYWAYS                                             │
///         │  • 2 Rival Gang Thugs  • Wire Fences  • Dark cover          │
///         │  • 1 Fuel Barrel  • 1 Civilian Bystander  • Slum buildings  │
/// [Y:-20] └──────────────────────────────────────────────────────────────┘
///          [-30]                                                     [+30]
/// </summary>
public static class AdaptiveSceneBuilder
{
    // ─────────────────────────────────────────────────────────────
    //  Layer indices (must match Edit > Project Settings > Layers)
    // ─────────────────────────────────────────────────────────────
    private const int LAYER_INTERACTABLE = 6;
    private const int LAYER_ENEMY        = 7;
    private const int LAYER_ROAD         = 8;
    private const int LAYER_ALLEY        = 9;
    private const int LAYER_BUILDING     = 10;

    private static void EnsureTagsRegistered()
    {
        UnityEngine.Object[] assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
        if (assets == null || assets.Length == 0) return;

        UnityEditor.SerializedObject tagManager = new UnityEditor.SerializedObject(assets[0]);
        UnityEditor.SerializedProperty tagsProp = tagManager.FindProperty("tags");
        if (tagsProp == null) return;

        string[] requiredTags = { "Explosive", "NPC", "Civilian", "Building", "Interactable" };
        bool modified = false;

        foreach (string reqTag in requiredTags)
        {
            bool exists = false;
            for (int i = 0; i < tagsProp.arraySize; i++)
            {
                if (tagsProp.GetArrayElementAtIndex(i).stringValue == reqTag)
                {
                    exists = true;
                    break;
                }
            }

            if (!exists)
            {
                int index = tagsProp.arraySize;
                tagsProp.InsertArrayElementAtIndex(index);
                tagsProp.GetArrayElementAtIndex(index).stringValue = reqTag;
                modified = true;
            }
        }

        if (modified)
        {
            tagManager.ApplyModifiedProperties();
            UnityEditor.AssetDatabase.SaveAssets();
        }
    }

    private static void SafeSetTag(GameObject go, string tag)
    {
        try
        {
            go.tag = tag;
        }
        catch
        {
            if (tag == "Explosive")
            {
                try { go.tag = "Explosion"; return; } catch { }
            }
            if (tag == "NPC")
            {
                try { go.tag = "Civilian"; return; } catch { }
            }
            Debug.LogWarning($"[SceneBuilder] Tag '{tag}' is not defined in Unity Project Settings. Object '{go.name}' left untagged.");
        }
    }


    internal static bool EnsureFullRectSpriteImporter(TextureImporter importer)
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


    [MenuItem("AdaptiveWorld/Ensure Sprites Imported", priority = 99)]
    public static void EnsureAssetsImportedAsSprites()
    {
        string assetsDir = System.IO.Path.Combine(Application.dataPath, "assets");
        if (!System.IO.Directory.Exists(assetsDir)) return;

        string[] extensions = { "*.png", "*.jpg" };
        bool reimportedAny = false;
        foreach (var ext in extensions)
        {
            var files = System.IO.Directory.GetFiles(assetsDir, ext, System.IO.SearchOption.AllDirectories);
            foreach (var file in files)
            {
                string relPath = "Assets" + file.Substring(Application.dataPath.Length).Replace('\\', '/');
                TextureImporter importer = AssetImporter.GetAtPath(relPath) as TextureImporter;
                if (EnsureFullRectSpriteImporter(importer))
                    reimportedAny = true;
            }
        }
        if (reimportedAny)
        {
            AssetDatabase.Refresh();
            Debug.Log("[SceneBuilder] Reimported textures in Assets/assets as 2D Sprites.");
        }
    }

    private static Sprite LoadSprite(string relativePath)
    {
        string fullPath = "Assets/assets/" + relativePath;
        Sprite s = AssetDatabase.LoadAssetAtPath<Sprite>(fullPath);
        if (s != null) return s;

        // Try reimporting if imported as default texture
        TextureImporter importer = AssetImporter.GetAtPath(fullPath) as TextureImporter;
        if (EnsureFullRectSpriteImporter(importer))
        {
            s = AssetDatabase.LoadAssetAtPath<Sprite>(fullPath);
            if (s != null) return s;
        }

        string filename = System.IO.Path.GetFileNameWithoutExtension(relativePath);
        string[] guids  = AssetDatabase.FindAssets(filename + " t:Sprite");
        if (guids != null && guids.Length > 0)
        {
            s = AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GUIDToAssetPath(guids[0]));
            if (s != null) return s;
        }

        Debug.LogWarning($"[SceneBuilder] Sprite not found: '{relativePath}'. Using fallback.");
        return WorldManifestApplier.GetUniversalWhiteSprite();
    }

    // ═════════════════════════════════════════════════════════════
    //  BATCH BUILD & SAVE BOTH MAPS
    // ═════════════════════════════════════════════════════════════
    [MenuItem("AdaptiveWorld/Generate & Populate SectorThemeCatalog", priority = 1)]
    public static void MenuPopulateCatalog()
    {
        EnsureAssetsImportedAsSprites();
        AdaptiveWorld2D.World.SectorThemeCatalog.CreateOrPopulateAsset();
    }

    [MenuItem("AdaptiveWorld/Build & Save BOTH Maps (Map 1 & Map 2)", priority = 0)]
    public static void BuildAndSaveBothMaps()
    {
        Debug.Log("[SceneBuilder] === BATCH BUILDING BOTH MAPS ===");
        EnsureTagsRegistered();
        EnsureAssetsImportedAsSprites();
        AdaptiveWorld2D.World.SectorThemeCatalog.CreateOrPopulateAsset();

        // 1. Open and build Map 1
        var map1Scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Map1.unity");
        BuildMap1();
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(map1Scene);
        Debug.Log("[SceneBuilder] Saved Map1.unity successfully.");

        // 2. Open or CREATE Map 2
        const string map2Path = "Assets/Scenes/Map2.unity";
        UnityEngine.SceneManagement.Scene map2Scene;
        if (System.IO.File.Exists(System.IO.Path.Combine(Application.dataPath, "../" + map2Path)))
        {
            map2Scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(map2Path);
        }
        else
        {
            Debug.Log("[SceneBuilder] Map2.unity not found — creating new scene.");
            map2Scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Single);
            // Save it immediately so it exists on disk before building
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(map2Scene, map2Path);
        }
        BuildMap2();
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(map2Scene);
        Debug.Log("[SceneBuilder] Saved Map2.unity successfully.");

        // 3. Return to Map 1
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Map1.unity");
        Debug.Log("[SceneBuilder] Switched back to Map 1. Ready to play!");

        var playerObj = GameObject.FindWithTag("Player");
        if (playerObj != null)
        {
            Selection.activeGameObject = playerObj;
            if (SceneView.lastActiveSceneView != null)
                SceneView.lastActiveSceneView.Frame(new Bounds(new Vector3(0f, 0f, 0f), new Vector3(65f, 40f, 1f)), false);
        }

        EditorUtility.DisplayDialog("Build Complete!",
            "Both Map 1 and Map 2 were built and saved directly to disk!\n\n" +
            "Map 1 is now open in the editor.\n\n" +
            "Press PLAY to test the full mission loop!", "Let's Go!");
    }


    // ═════════════════════════════════════════════════════════════
    //  MAP 1  —  Phase 2 Full City "Pauper Streets"
    // ═════════════════════════════════════════════════════════════
    [MenuItem("AdaptiveWorld/Build Map 1 — Full Phase 2 City (Pauper Streets)", priority = 10)]
    public static void BuildMap1()
    {
        Debug.Log("[SceneBuilder] === BUILDING MAP 1 — FULL PHASE 2 CITY ===");
        EnsureTagsRegistered();
        EnsureAssetsImportedAsSprites();
        AdaptiveWorld2D.World.SectorThemeCatalog.CreateOrPopulateAsset();

        // Automatically clear any leftover old objects from previous builds
        ClearAllExceptCamera();

        // ── Ground surface first ──────────────────────────────────
        BuildMap1_Roads();

        // ── Architecture ──────────────────────────────────────────
        BuildMap1_NorthWarehouses();
        BuildMap1_CentralStorefronts();
        BuildMap1_SouthSlumBlocks();
        BuildMap1_PerimeterBoundaries();

        // ── Player ────────────────────────────────────────────────
        GameObject player = BuildPlayer(new Vector3(-25f, 0f, 0f));

        // ── Camera: Position at Spawn & Attach Smooth Follow ──────
        SetupCamera(player, new Vector3(-25f, 0f, -10f));

        // ── Vehicles ─────────────────────────────────────────────
        BuildMap1_Vehicles();

        // ── Cover, Barriers & Hazards ─────────────────────────────
        BuildMap1_Cover();
        BuildMap1_FuelBarrels();

        // ── NPCs: Police, Civilians, Thugs, Informant ─────────────
        BuildMap1_PatrolPolice();
        BuildMap1_SwatGuard();
        BuildMap1_Civilians();
        BuildMap1_GangThugs();
        BuildMap1_Informant();

        // ── Mission Triggers ──────────────────────────────────────
        BuildMap1_GunShop(new Vector3(0f, -7f, 0f));
        BuildMap1_CashBriefcase(new Vector3(-7f, 14f, 0f));
        BuildMap1_ExtractionPoint(new Vector3(28f, 0f, 0f));

        // ── GameManager & UI ─────────────────────────────────────
        GameObject gm = BuildGameManager(player);
        BuildUI(gm, "MISSION 1: Reach the Underground Gun Shop — then escape the city.");

        // Auto-save to disk so user never encounters an empty scene on load
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        if (player != null)
        {
            Selection.activeGameObject = player;
            if (SceneView.lastActiveSceneView != null)
                SceneView.lastActiveSceneView.Frame(new Bounds(new Vector3(0f, 0f, 0f), new Vector3(65f, 40f, 1f)), false);
        }

        Debug.Log("[SceneBuilder] MAP 1 BUILT AND SAVED TO DISK SUCCESSFULLY.");
        EditorUtility.DisplayDialog("Map 1 Built & Saved!",
            "Phase 2 Map 1 — Pauper Streets is ready and saved to disk!\n\n" +
            "  NORTH  — 2 Warehouses, SWAT guard, explosive barrels, briefcase loot\n" +
            "  CENTER — 4-lane boulevard, 2 cop patrols, police cruiser, 3 civilians\n" +
            "  SOUTH  — Dark alleys, 2 gang thugs, wire fences, shadows\n\n" +
            "Total entities: 41\n\n" +
            "Press PLAY to start!", "Got it!");
    }

    private static void ClearAllExceptCamera()
    {
        var roots = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
        for (int i = roots.Length - 1; i >= 0; i--)
        {
            var r = roots[i];
            if (r == null) continue;
            if (r.GetComponent<Camera>() != null || r.name == "Main Camera") continue;
            var light2DType = System.Type.GetType("UnityEngine.Rendering.Universal.Light2D, Unity.RenderPipelines.Universal.Runtime");
            if (light2DType != null && r.GetComponent(light2DType) != null) continue;
            Object.DestroyImmediate(r);
        }
    }

    private static void SetupCamera(GameObject player, Vector3 initialPos)
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            GameObject camGO = new GameObject("Main Camera");
            cam = camGO.AddComponent<Camera>();
            camGO.tag = "MainCamera";
            camGO.AddComponent<AudioListener>();
        }

        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.orthographic = true;
        cam.orthographicSize = 6.5f;
        cam.backgroundColor = new Color(0.10f, 0.11f, 0.13f);
        cam.transform.position = initialPos;

        CameraFollow2D follow = cam.gameObject.GetOrAddComponent<CameraFollow2D>();
        if (player != null) follow.target = player.transform;
        follow.smoothSpeed = 10f;
        follow.cameraZ = -10f;
    }

    // ─────────────────────────────────────────────────────────────
    //  MAP 1: ROAD SURFACE (Catalog-driven asphalt & alley tiles)
    // ─────────────────────────────────────────────────────────────
    private static void BuildMap1_Roads()
    {
        var catalog = AdaptiveWorld2D.World.SectorThemeCatalog.GetDefault();
        Sprite highwayTile = catalog != null ? catalog.GetRoadTile("URBAN_HIGHWAY") : null;
        Sprite alleyTile = catalog != null ? catalog.GetRoadTile("SLUM_ALLEYWAYS") : null;

        // Main 4-lane central boulevard (Y: -3 to +3)
        CreateRoadSurface("Road_Boulevard", new Vector3(0f, 0f, 0f),
            new Vector2(62f, 7f), LAYER_ROAD, highwayTile);

        // North service road (Y: +10)
        CreateRoadSurface("Road_North_Service", new Vector3(0f, 10f, 0f),
            new Vector2(62f, 3f), LAYER_ROAD, highwayTile);

        // South alley ground (Y: -12, darker alley tile)
        CreateRoadSurface("Road_South_Alley", new Vector3(-8f, -12f, 0f),
            new Vector2(30f, 5f), LAYER_ALLEY, alleyTile,
            new Color(0.7f, 0.7f, 0.7f));
    }

    private static void CreateRoadSurface(string name, Vector3 pos, Vector2 size,
        int layer,
        Sprite sprite = null,
        Color? tint = null)
    {
        GameObject road = GameObject.Find(name);
        if (road == null)
        {
            road = new GameObject(name);
        }

        road.transform.position = pos;
        road.layer = layer;

        SpriteRenderer sr = road.GetComponent<SpriteRenderer>();
        if (sr == null)
        {
            sr = road.AddComponent<SpriteRenderer>();
        }

        if (sprite == null)
        {
            var catalog = AdaptiveWorld2D.World.SectorThemeCatalog.GetDefault();
            sprite = catalog != null ? catalog.GetRoadTile("URBAN_HIGHWAY") : LoadSprite("kenney_topdown_shooter/PNG/Tiles/tile_01.png");
        }
        sr.sprite    = sprite;
        sr.drawMode  = SpriteDrawMode.Tiled;
        sr.size      = size;
        sr.color     = tint ?? Color.white;
        sr.sortingLayerName = "Background";
        sr.sortingOrder = -10;

        // Walkable trigger for road telemetry
        BoxCollider2D col = road.GetComponent<BoxCollider2D>();
        if (col == null)
        {
            col = road.AddComponent<BoxCollider2D>();
        }
        col.size      = size;
        col.isTrigger = true;
    }

    // ─────────────────────────────────────────────────────────────
    //  MAP 1: BUILDINGS — NORTH INDUSTRIAL WAREHOUSES
    // ─────────────────────────────────────────────────────────────
    private static void BuildMap1_NorthWarehouses()
    {
        // Warehouse A — West side of north sector
        CreateBuilding("Building_WarehouseA",
            new Vector3(-14f, 15f, 0f), new Vector2(12f, 8f),
            new Color(0.45f, 0.42f, 0.38f)); // dark concrete

        // Warehouse B — East side of north sector
        CreateBuilding("Building_WarehouseB",
            new Vector3(10f, 15f, 0f), new Vector2(16f, 8f),
            new Color(0.40f, 0.38f, 0.35f));
    }

    // ─────────────────────────────────────────────────────────────
    //  MAP 1: BUILDINGS — CENTRAL BOULEVARD STOREFRONTS
    // ─────────────────────────────────────────────────────────────
    private static void BuildMap1_CentralStorefronts()
    {
        // North-side strip mall (above boulevard)
        CreateBuilding("Building_StorefrontNorth",
            new Vector3(-5f, 6f, 0f), new Vector2(20f, 4f),
            new Color(0.55f, 0.50f, 0.45f));

        // East-side commercial block (near extraction)
        CreateBuilding("Building_StorefrontEast",
            new Vector3(22f, 6f, 0f), new Vector2(10f, 4f),
            new Color(0.52f, 0.48f, 0.42f));
    }

    // ─────────────────────────────────────────────────────────────
    //  MAP 1: BUILDINGS — SOUTH SLUM TENEMENTS
    // ─────────────────────────────────────────────────────────────
    private static void BuildMap1_SouthSlumBlocks()
    {
        // West slum block — frames player spawn
        CreateBuilding("Building_SlumWest",
            new Vector3(-22f, -8f, 0f), new Vector2(8f, 6f),
            new Color(0.38f, 0.36f, 0.33f));

        // East slum block — closes off south alley
        CreateBuilding("Building_SlumEast",
            new Vector3(5f, -10f, 0f), new Vector2(10f, 6f),
            new Color(0.36f, 0.34f, 0.31f));
    }

    private static void BuildMap1_PerimeterBoundaries()
    {
        float halfW = 31f;
        float halfH = 21f;
        float thickness = 1.5f;
        Color wallColor = new Color(0.18f, 0.17f, 0.16f);

        CreateBuilding("Perimeter_N", new Vector3(0f, halfH, 0f), new Vector2(halfW * 2f + thickness, thickness), wallColor);
        CreateBuilding("Perimeter_S", new Vector3(0f, -halfH, 0f), new Vector2(halfW * 2f + thickness, thickness), wallColor);
        CreateBuilding("Perimeter_W", new Vector3(-halfW, 0f, 0f), new Vector2(thickness, halfH * 2f + thickness), wallColor);
        // On East side, leave an opening at Y = 0 for the extraction point!
        CreateBuilding("Perimeter_E_North", new Vector3(halfW, 12f, 0f), new Vector2(thickness, 18f), wallColor);
        CreateBuilding("Perimeter_E_South", new Vector3(halfW, -12f, 0f), new Vector2(thickness, 18f), wallColor);
    }

    private static void CreateBuilding(string name, Vector3 pos, Vector2 size, Color color)
    {
        if (GameObject.Find(name) != null) return;

        GameObject b = new GameObject(name);
        b.transform.position = pos;
        b.layer = LAYER_BUILDING;
        SafeSetTag(b, "Building");

        SpriteRenderer sr = b.AddComponent<SpriteRenderer>();
        var catalog = AdaptiveWorld2D.World.SectorThemeCatalog.GetDefault();
        sr.sprite    = catalog != null ? catalog.GetWallTile(false) : WorldManifestApplier.GetUniversalWhiteSprite();
        sr.drawMode  = SpriteDrawMode.Tiled;
        sr.size      = size;
        sr.color     = Color.white;
        sr.sortingLayerName = "Obstacles";
        sr.sortingOrder = 0;

        BoxCollider2D col = b.AddComponent<BoxCollider2D>();
        col.size      = size;
        col.isTrigger = false; // SOLID — blocks movement

        Debug.Log($"[SceneBuilder] Building '{name}' at {pos}, size {size}");
    }

    // ─────────────────────────────────────────────────────────────
    //  MAP 1: VEHICLES
    // ─────────────────────────────────────────────────────────────
    private static void BuildMap1_Vehicles()
    {
        // Red sports car — near player spawn; fast getaway for speedrunners
        CreateCar("Car_SportsRed", new Vector3(-20f, 3f, 0f),
            "car_01", "SPORTS",
            "kenney_vehicles/PNG/Cars/car_red_1.png",
            "kenney_vehicles/PNG/Cars/car_black_1.png");

        // Blue sedan — parked on boulevard curb; mobile cover
        CreateCar("Car_SedanBlue", new Vector3(4f, -2f, 0f),
            "car_02", "SEDAN",
            "kenney_vehicles/PNG/Cars/car_blue_1.png",
            "kenney_vehicles/PNG/Cars/car_black_1.png");

        // Black sedan — tucked in south alley lot
        CreateCar("Car_SedanBlack", new Vector3(-6f, -13f, 0f),
            "car_03", "SEDAN",
            "kenney_vehicles/PNG/Cars/car_black_1.png",
            "kenney_vehicles/PNG/Cars/car_black_5.png");

        // Police cruiser — stationed at East roadblock checkpoint
        CreateCar("Car_PoliceCruiser", new Vector3(20f, 2f, 0f),
            "car_police_01", "POLICE",
            "kenney_vehicles/PNG/Cars/car_blue_2.png",
            "kenney_vehicles/PNG/Cars/car_black_2.png");
    }

    private static void CreateCar(string name, Vector3 pos, string carId, string carType,
        string spritePath, string burntSpritePath)
    {
        if (GameObject.Find(name) != null) return;

        GameObject car = new GameObject(name);
        car.transform.position = pos;
        car.layer = LAYER_INTERACTABLE;
        SafeSetTag(car, "Vehicle");

        Sprite cleanSprite = LoadSprite(spritePath);
        Sprite burntSprite = LoadSprite(burntSpritePath);

        SpriteRenderer sr = car.AddComponent<SpriteRenderer>();
        sr.sprite = cleanSprite;
        sr.color  = Color.white;
        sr.sortingOrder = 3;
        car.transform.localScale = Vector3.one;

        BoxCollider2D col = car.AddComponent<BoxCollider2D>();
        col.size = new Vector2(0.72f, 1.32f);

        Rigidbody2D rb = car.AddComponent<Rigidbody2D>();
        rb.bodyType      = RigidbodyType2D.Kinematic;
        rb.gravityScale  = 0f;
        rb.freezeRotation = true;

        BurnableCar bc = car.AddComponent<BurnableCar>();
        bc.carId            = carId;
        bc.carType          = carType;
        bc.cleanCarSprite   = cleanSprite;
        bc.burningCarSprite = burntSprite;
        bc.wreckedCarSprite = burntSprite;

        Debug.Log($"[SceneBuilder] Car '{name}' at {pos}");
    }

    // ─────────────────────────────────────────────────────────────
    //  MAP 1: COVER (barriers, wire fences, crates)
    // ─────────────────────────────────────────────────────────────
    private static void BuildMap1_Cover()
    {
        // ── East police roadblock (2 concrete barriers side by side) ──
        CreateConcreteBarrier("Barrier_EastRoad_L", new Vector3(17f,  2f, 0f), 90f);
        CreateConcreteBarrier("Barrier_EastRoad_R", new Vector3(17f, -2f, 0f), 90f);

        // ── Mid-boulevard street cover ────────────────────────────────
        CreateConcreteBarrier("Barrier_Street_01", new Vector3(-8f, 1.5f, 0f), 0f);
        CreateConcreteBarrier("Barrier_Street_02", new Vector3( 8f, 1.5f, 0f), 0f);

        // ── South alley wire fences (destructible shortcuts) ──────────
        CreateWireFence("Fence_SouthAlley_01", new Vector3(-16f, -6f, 0f), 0f);
        CreateWireFence("Fence_SouthAlley_02", new Vector3( -3f, -6f, 0f), 0f);
        CreateWireFence("Fence_NorthYard",     new Vector3( -3f,  7f, 0f), 0f);

        // ── Alley dumpsters / crate cover ─────────────────────────────
        CreateCrateObject("Crate_Alley_01", new Vector3(-12f, -9f, 0f));
        CreateCrateObject("Crate_Alley_02", new Vector3( -8f, -9f, 0f));
        CreateCrateObject("Crate_North_01", new Vector3( -3f, 12f, 0f));
    }

    private static void CreateConcreteBarrier(string name, Vector3 pos, float rotZ)
    {
        if (GameObject.Find(name) != null) return;

        GameObject wall = new GameObject(name);
        wall.transform.position = pos;
        wall.transform.rotation = Quaternion.Euler(0f, 0f, rotZ);
        wall.layer = LAYER_INTERACTABLE;

        Sprite intact  = LoadSprite("kenney_vehicles/PNG/Objects/barrier_red.png");
        Sprite damaged = LoadSprite("kenney_vehicles/PNG/Objects/barrier_white.png");
        Sprite rubble  = LoadSprite("kenney_vehicles/PNG/Objects/barrel_red_down.png");

        SpriteRenderer sr = wall.AddComponent<SpriteRenderer>();
        sr.sprite = intact;
        sr.sortingOrder = 2;
        wall.transform.localScale = Vector3.one;

        BoxCollider2D col = wall.AddComponent<BoxCollider2D>();
        col.size = new Vector2(2.1f, 0.62f);

        DestructibleWall dw = wall.AddComponent<DestructibleWall>();
        dw.wallId        = name.ToLower();
        dw.maxHealth     = 100;
        dw.intactSprite  = intact;
        dw.damagedSprite = damaged;
        dw.rubbleSprite  = rubble;

        Debug.Log($"[SceneBuilder] Concrete barrier '{name}' at {pos}");
    }

    private static void CreateWireFence(string name, Vector3 pos, float rotZ)
    {
        if (GameObject.Find(name) != null) return;

        GameObject fence = new GameObject(name);
        fence.transform.position = pos;
        fence.transform.rotation = Quaternion.Euler(0f, 0f, rotZ);
        fence.layer = LAYER_INTERACTABLE;

        SpriteRenderer sr = fence.AddComponent<SpriteRenderer>();
        sr.sprite = LoadSprite("kenney_vehicles/PNG/Objects/barrier_white.png");
        sr.color  = new Color(0.8f, 0.8f, 0.8f, 0.9f);
        sr.sortingOrder = 2;
        fence.transform.localScale = new Vector3(2f, 1f, 1f);

        BoxCollider2D col = fence.AddComponent<BoxCollider2D>();
        col.size = new Vector2(2f, 0.3f); // thin but blocks walking

        DestructibleWall dw = fence.AddComponent<DestructibleWall>();
        dw.wallId        = name.ToLower();
        dw.maxHealth     = 40;
        dw.intactSprite  = sr.sprite;
        dw.damagedSprite = LoadSprite("kenney_vehicles/PNG/Objects/barrier_red.png");
        dw.rubbleSprite  = LoadSprite("kenney_vehicles/PNG/Objects/cone_down.png");

        Debug.Log($"[SceneBuilder] Wire fence '{name}' at {pos}");
    }

    private static void CreateCrateObject(string name, Vector3 pos)
    {
        if (GameObject.Find(name) != null) return;

        GameObject crate = new GameObject(name);
        crate.transform.position = pos;
        crate.layer = LAYER_INTERACTABLE;

        SpriteRenderer sr = crate.AddComponent<SpriteRenderer>();
        sr.sprite = LoadSprite("kenney_vehicles/PNG/Objects/barrel_blue.png");
        sr.color  = new Color(0.5f, 0.5f, 0.6f);
        sr.sortingOrder = 2;
        crate.transform.localScale = new Vector3(0.9f, 0.9f, 1f);

        BoxCollider2D col = crate.AddComponent<BoxCollider2D>();
        col.size = new Vector2(0.7f, 0.7f);

        Debug.Log($"[SceneBuilder] Crate '{name}' at {pos}");
    }

    // ─────────────────────────────────────────────────────────────
    //  MAP 1: HAZARDS — EXPLOSIVE FUEL BARRELS
    // ─────────────────────────────────────────────────────────────
    private static void BuildMap1_FuelBarrels()
    {
        // North warehouse compound — tactical trap cluster
        CreateFuelBarrel("Barrel_NorthA", new Vector3(-5f,  12f, 0f));
        CreateFuelBarrel("Barrel_NorthB", new Vector3(-3f,  12f, 0f));

        // South alley choke point
        CreateFuelBarrel("Barrel_SouthAlley", new Vector3(-10f, -9f, 0f));

        // Near East roadblock — risky area denial option for player
        CreateFuelBarrel("Barrel_EastRoad", new Vector3(15f, 3f, 0f));
    }

    private static void CreateFuelBarrel(string name, Vector3 pos)
    {
        if (GameObject.Find(name) != null) return;

        GameObject barrel = new GameObject(name);
        barrel.transform.position = pos;
        barrel.layer = LAYER_INTERACTABLE;
        SafeSetTag(barrel, "Explosive");

        SpriteRenderer sr = barrel.AddComponent<SpriteRenderer>();
        sr.sprite = LoadSprite("kenney_vehicles/PNG/Objects/barrel_red.png");
        sr.color  = Color.white;
        sr.sortingOrder = 2;
        barrel.transform.localScale = new Vector3(0.8f, 0.8f, 1f);

        CircleCollider2D col = barrel.AddComponent<CircleCollider2D>();
        col.radius = 0.35f;

        // Reuse DestructibleWall for explosion logic (low HP = instant pop)
        DestructibleWall dw = barrel.AddComponent<DestructibleWall>();
        dw.wallId        = name.ToLower();
        dw.maxHealth     = 20;
        dw.intactSprite  = sr.sprite;
        dw.damagedSprite = LoadSprite("kenney_vehicles/PNG/Objects/barrel_red_down.png");
        dw.rubbleSprite  = LoadSprite("kenney_vehicles/PNG/Objects/barrel_red_down.png");

        Debug.Log($"[SceneBuilder] Fuel barrel '{name}' at {pos}");
    }

    // ─────────────────────────────────────────────────────────────
    //  MAP 1: NPCs — PATROL POLICE (4 officers)
    // ─────────────────────────────────────────────────────────────
    private static void BuildMap1_PatrolPolice()
    {
        // Cop 1 — Central boulevard west patrol (X: -15 to -5)
        CreateEnemy("Cop_Boulevard_West",
            new Vector3(-15f, 2f, 0f),
            new Vector3[] { new(-15f, 2f), new(-5f, 2f) },
            "kenney_topdown_shooter/PNG/Man Blue/manBlue_gun.png",
            hp: 60, speed: 3.0f, chaseSpeed: 4.5f, vision: 8f, angle: 70f,
            resistance: 0f, isBoss: false);

        // Cop 2 — Central boulevard east patrol (X: +5 to +15)
        CreateEnemy("Cop_Boulevard_East",
            new Vector3(5f, -2f, 0f),
            new Vector3[] { new(5f, -2f), new(15f, -2f) },
            "kenney_topdown_shooter/PNG/Man Blue/manBlue_gun.png",
            hp: 60, speed: 3.0f, chaseSpeed: 4.5f, vision: 8f, angle: 70f,
            resistance: 0f, isBoss: false);

        // Cop 3 — East checkpoint (stationary guard by police cruiser)
        CreateEnemy("Cop_Checkpoint",
            new Vector3(22f, 0f, 0f),
            new Vector3[] { new(22f, 2f), new(22f, -2f) },
            "kenney_topdown_shooter/PNG/Man Blue/manBlue_gun.png",
            hp: 60, speed: 1.5f, chaseSpeed: 4.5f, vision: 10f, angle: 80f,
            resistance: 0f, isBoss: false);

        // Cop 4 — North industrial entrance patrol
        CreateEnemy("Cop_NorthEntrance",
            new Vector3(-3f, 8f, 0f),
            new Vector3[] { new(-8f, 8f), new(3f, 8f) },
            "kenney_topdown_shooter/PNG/Man Blue/manBlue_gun.png",
            hp: 60, speed: 2.5f, chaseSpeed: 4.5f, vision: 8f, angle: 70f,
            resistance: 0f, isBoss: false);
    }

    // ─────────────────────────────────────────────────────────────
    //  MAP 1: NPCs — SWAT ENFORCER (1, inside North Warehouse)
    // ─────────────────────────────────────────────────────────────
    private static void BuildMap1_SwatGuard()
    {
        CreateEnemy("SWAT_WarehouseGuard",
            new Vector3(-7f, 14f, 0f),
            new Vector3[] { new(-10f, 14f), new(-4f, 14f) },
            "kenney_topdown_shooter/PNG/Soldier 1/soldier1_machine.png",
            hp: 120, speed: 2.0f, chaseSpeed: 3.5f, vision: 12f, angle: 85f,
            resistance: 0.5f, isBoss: false);
    }

    // ─────────────────────────────────────────────────────────────
    //  MAP 1: NPCs — CIVILIANS (5 total)
    // ─────────────────────────────────────────────────────────────
    private static void BuildMap1_Civilians()
    {
        // Civ 1 & 2 — Central boulevard sidewalk (north side)
        CreateCivilian("Civilian_Blvd_N1", new Vector3(-10f, 4f, 0f),
            "kenney_topdown_shooter/PNG/Woman Green/womanGreen_stand.png");
        CreateCivilian("Civilian_Blvd_N2", new Vector3(  5f, 4f, 0f),
            "kenney_topdown_shooter/PNG/Man Brown/manBrown_stand.png");

        // Civ 3 & 4 — West slum near player spawn
        CreateCivilian("Civilian_Slum_01", new Vector3(-22f, 3f, 0f),
            "kenney_topdown_shooter/PNG/Man Brown/manBrown_stand.png");
        CreateCivilian("Civilian_Slum_02", new Vector3(-18f,-2f, 0f),
            "kenney_topdown_shooter/PNG/Woman Green/womanGreen_stand.png");

        // Civ 5 — South alley entrance (panics if alley fight breaks out)
        CreateCivilian("Civilian_SouthAlley", new Vector3(-4f, -7f, 0f),
            "kenney_topdown_shooter/PNG/Woman Green/womanGreen_stand.png");
    }

    private static void CreateCivilian(string name, Vector3 pos, string spritePath)
    {
        if (GameObject.Find(name) != null) return;

        GameObject civ = new GameObject(name);
        civ.transform.position = pos;
        SafeSetTag(civ, "Civilian");

        SpriteRenderer sr = civ.AddComponent<SpriteRenderer>();
        sr.sprite = LoadSprite(spritePath);
        sr.sortingOrder = 4;
        civ.transform.localScale = Vector3.one;

        CircleCollider2D col = civ.AddComponent<CircleCollider2D>();
        col.radius    = 0.25f;
        col.isTrigger = true;

        Rigidbody2D rb = civ.AddComponent<Rigidbody2D>();
        rb.gravityScale   = 0f;
        rb.freezeRotation = true;
        rb.bodyType       = RigidbodyType2D.Kinematic;

        Debug.Log($"[SceneBuilder] Civilian '{name}' at {pos}");
    }

    // ─────────────────────────────────────────────────────────────
    //  MAP 1: NPCs — RIVAL GANG THUGS (2, South Alleys)
    // ─────────────────────────────────────────────────────────────
    private static void BuildMap1_GangThugs()
    {
        // Thug 1 — lurking in alley mouth
        CreateEnemy("GangThug_01",
            new Vector3(-13f, -10f, 0f),
            new Vector3[] { new(-15f, -10f), new(-10f, -10f) },
            "kenney_topdown_shooter/PNG/Survivor 1/survivor1_gun.png",
            hp: 70, speed: 3.8f, chaseSpeed: 5.0f, vision: 9f, angle: 75f,
            resistance: 0f, isBoss: false);

        // Thug 2 — deeper in the alley darkness
        CreateEnemy("GangThug_02",
            new Vector3(-18f, -12f, 0f),
            new Vector3[] { new(-20f, -12f), new(-16f, -12f) },
            "kenney_topdown_shooter/PNG/Survivor 1/survivor1_gun.png",
            hp: 70, speed: 3.8f, chaseSpeed: 5.0f, vision: 9f, angle: 75f,
            resistance: 0f, isBoss: false);
    }

    // ─────────────────────────────────────────────────────────────
    //  MAP 1: NPC — UNDERWORLD INFORMANT (outside Gun Shop)
    // ─────────────────────────────────────────────────────────────
    private static void BuildMap1_Informant()
    {
        if (GameObject.Find("NPC_Informant") != null) return;

        GameObject npc = new GameObject("NPC_Informant");
        npc.transform.position = new Vector3(-2f, -7f, 0f);
        SafeSetTag(npc, "NPC");

        SpriteRenderer sr = npc.AddComponent<SpriteRenderer>();
        sr.sprite = LoadSprite("kenney_topdown_shooter/PNG/Man Old/manOld_stand.png");
        sr.sortingOrder = 4;
        npc.transform.localScale = Vector3.one;

        CircleCollider2D col = npc.AddComponent<CircleCollider2D>();
        col.radius    = 0.3f;
        col.isTrigger = true;

        Debug.Log("[SceneBuilder] Underworld Informant created at [-2, -7]");
    }

    // ─────────────────────────────────────────────────────────────
    //  SHARED ENEMY FACTORY
    // ─────────────────────────────────────────────────────────────
    private static void CreateEnemy(string name, Vector3 spawnPos, Vector3[] waypointPositions,
        string spritePath, int hp, float speed, float chaseSpeed,
        float vision, float angle, float resistance, bool isBoss)
    {
        if (GameObject.Find(name) != null) return;

        GameObject enemy = new GameObject(name);
        enemy.transform.position = spawnPos;
        enemy.layer = LAYER_ENEMY;
        SafeSetTag(enemy, "Enemy");

        SpriteRenderer sr = enemy.AddComponent<SpriteRenderer>();
        sr.sprite = LoadSprite(spritePath);
        sr.color  = Color.white;
        sr.sortingOrder = 5;
        enemy.transform.localScale = Vector3.one;

        Rigidbody2D rb = enemy.AddComponent<Rigidbody2D>();
        rb.gravityScale   = 0f;
        rb.freezeRotation = true;

        CircleCollider2D col = enemy.AddComponent<CircleCollider2D>();
        col.radius = 0.35f;

        // Build waypoint children
        var wpTransforms = new System.Collections.Generic.List<Transform>();
        for (int i = 0; i < waypointPositions.Length; i++)
        {
            GameObject wp = new GameObject($"WP_{i}");
            wp.transform.SetParent(enemy.transform);
            wp.transform.position = waypointPositions[i];
            wpTransforms.Add(wp.transform);
        }

        EnemyPatrol patrol = enemy.AddComponent<EnemyPatrol>();
        patrol.isTargetBoss    = isBoss;
        patrol.maxHealth       = hp;
        patrol.patrolSpeed     = speed;
        patrol.chaseSpeed      = chaseSpeed;
        patrol.damageResistance = resistance;
        foreach (var t in wpTransforms) patrol.waypoints.Add(t);

        EnemyDetection detection = enemy.AddComponent<EnemyDetection>();
        detection.visionRange    = vision;
        detection.visionAngle    = angle;
        detection.hearingRange   = vision + 4f;
        detection.obstacleLayer  = LayerMask.GetMask("Building");
        detection.playerLayer    = LayerMask.GetMask("Default");

        Debug.Log($"[SceneBuilder] Enemy '{name}' spawned at {spawnPos} | HP:{hp} Vision:{vision}m");
    }

    // ─────────────────────────────────────────────────────────────
    //  MAP 1: MISSION TRIGGERS
    // ─────────────────────────────────────────────────────────────
    private static void BuildMap1_GunShop(Vector3 pos)
    {
        if (GameObject.Find("GunShop") != null) return;

        GameObject zone = new GameObject("GunShop");
        zone.transform.position = pos;
        zone.layer = LAYER_INTERACTABLE;

        // Glowing shop floor marker
        SpriteRenderer sr = zone.AddComponent<SpriteRenderer>();
        sr.sprite = LoadSprite("kenney_vehicles/PNG/Objects/barrier_white_race.png");
        sr.color  = new Color(1f, 0.88f, 0.2f, 0.85f);
        zone.transform.localScale = new Vector3(1.5f, 1.5f, 1f);
        sr.sortingOrder = 1;

        // Floating weapon icon
        GameObject icon = new GameObject("GunShop_Icon");
        icon.transform.SetParent(zone.transform);
        icon.transform.localPosition = new Vector3(0f, 0.45f, 0f);
        icon.transform.localScale    = new Vector3(2.5f, 2.5f, 1f);
        SpriteRenderer iconSr = icon.AddComponent<SpriteRenderer>();
        iconSr.sprite = LoadSprite("kenney_topdown_shooter/PNG/weapon_gun.png");
        iconSr.sortingOrder = 6;

        BoxCollider2D col = zone.AddComponent<BoxCollider2D>();
        col.size      = new Vector2(2.5f, 2.5f);
        col.isTrigger = true;

        MissionZoneTrigger trigger = zone.AddComponent<MissionZoneTrigger>();
        trigger.zoneType              = MissionZoneTrigger.ZoneType.GunShop;
        trigger.zoneName              = "Underground Gun Shop";
        trigger.requireInteractButton = true;
        trigger.triggerOnce           = true;

        Debug.Log($"[SceneBuilder] GunShop created at {pos}");
    }

    private static void BuildMap1_CashBriefcase(Vector3 pos)
    {
        if (GameObject.Find("CashBriefcase") != null) return;

        GameObject brief = new GameObject("CashBriefcase");
        brief.transform.position = pos;
        brief.layer = LAYER_INTERACTABLE;
        SafeSetTag(brief, "Interactable");

        SpriteRenderer sr = brief.AddComponent<SpriteRenderer>();
        sr.sprite = LoadSprite("kenney_vehicles/PNG/Objects/barrel_blue_down.png");
        sr.color  = new Color(0.9f, 0.85f, 0.1f);
        sr.sortingOrder = 3;
        brief.transform.localScale = new Vector3(0.8f, 0.8f, 1f);

        CircleCollider2D col = brief.AddComponent<CircleCollider2D>();
        col.radius    = 0.4f;
        col.isTrigger = true;

        // Label above
        GameObject label = new GameObject("BriefcaseLabel");
        label.transform.SetParent(brief.transform);
        label.transform.localPosition = new Vector3(0f, 0.5f, 0f);

        Debug.Log($"[SceneBuilder] Cash Briefcase (optional loot) at {pos}");
    }

    private static void BuildMap1_ExtractionPoint(Vector3 pos)
    {
        if (GameObject.Find("ExtractionPoint_Map1") != null) return;

        GameObject zone = new GameObject("ExtractionPoint_Map1");
        zone.transform.position = pos;
        zone.layer = LAYER_INTERACTABLE;

        SpriteRenderer sr = zone.AddComponent<SpriteRenderer>();
        sr.sprite = LoadSprite("kenney_vehicles/PNG/Objects/arrow_yellow.png");
        sr.color  = new Color(0.2f, 1f, 0.4f, 0.9f);
        zone.transform.localScale = new Vector3(3f, 3f, 1f);
        sr.sortingOrder = 1;

        // Pulsing ring indicator (inner marker)
        GameObject inner = new GameObject("ExtractionInner");
        inner.transform.SetParent(zone.transform);
        inner.transform.localPosition = Vector3.zero;
        inner.transform.localScale = new Vector3(0.5f, 0.5f, 1f);
        SpriteRenderer innerSr = inner.AddComponent<SpriteRenderer>();
        innerSr.sprite = LoadSprite("kenney_vehicles/PNG/Objects/arrow_white.png");
        innerSr.color  = new Color(1f, 1f, 1f, 0.5f);
        innerSr.sortingOrder = 2;

        BoxCollider2D col = zone.AddComponent<BoxCollider2D>();
        col.size      = new Vector2(3f, 3f);
        col.isTrigger = true;

        MissionZoneTrigger trigger = zone.AddComponent<MissionZoneTrigger>();
        trigger.zoneType              = MissionZoneTrigger.ZoneType.MissionEnd;
        trigger.zoneName              = "City Exit — East Highway";
        trigger.requireInteractButton = false;
        trigger.triggerOnce           = true;

        Debug.Log($"[SceneBuilder] Extraction Point at {pos}");
    }

    // ═════════════════════════════════════════════════════════════
    //  MAP 2  —  Adaptive Assassination Foundation & Sector Graph
    // ═════════════════════════════════════════════════════════════
    [MenuItem("AdaptiveWorld/Build Map 2 — Adaptive Assassination Foundation", priority = 11)]
    public static void BuildMap2()
    {
        Debug.Log("[SceneBuilder] === BUILDING MAP 2 ===");
        EnsureAssetsImportedAsSprites();
        AdaptiveWorld2D.World.SectorThemeCatalog.CreateOrPopulateAsset();

        ClearAllExceptCamera();

        // Clear legacy road if present
        GameObject legacyRoad = GameObject.Find("CityRoad_Main");
        if (legacyRoad != null) Object.DestroyImmediate(legacyRoad);

        GameObject player      = BuildPlayer(new Vector3(-20f, -15f, 0f));
        SetupCamera(player, new Vector3(-20f, -15f, -10f));

        PlayerCombat combat = player.GetComponent<PlayerCombat>();
        if (combat != null)
        {
            combat.hasPistol  = true;
            combat.pistolAmmo = 24;
            SpriteRenderer sr = player.GetComponent<SpriteRenderer>();
            if (sr != null && combat.armedSprite != null)
                sr.sprite = combat.armedSprite;
        }

        GameObject gm = BuildGameManager(player);
        WorldManifestApplier applier = gm.GetComponent<WorldManifestApplier>();

        BuildLocationAnchors(applier);
        BuildRouteBarricades(applier);
        BuildGuardInfrastructure(applier);
        BuildUI(gm, "MISSION 2: Eliminate the target. Route determined by the AI Director.");

        // Build default balanced sector architecture for editor preview.
        // Use ProceduralSectorBuilder.Build (not SectorRuntimeBuilder.Build directly) so the
        // scene is consistently marked dirty and saved through the same wrapper as all other paths.
        ProceduralSectorBuilder.Build(ProceduralSectorBuilder.TestManifests.Balanced());

        // Auto-save to disk so Map2.unity is permanently serialized
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        Debug.Log("[SceneBuilder] MAP 2 BUILT AND SAVED TO DISK SUCCESSFULLY.");
        EditorUtility.DisplayDialog("Map 2 Built & Saved!",
            "Map 2 — Adaptive Assassination Foundation ready and saved to disk!\n\n" +
            "• 9-Sector Tactical Graph (Balanced Baseline Preview)\n" +
            "• WorldManifestApplier wired\n" +
            "• 12 guard spawn points\n" +
            "• 5 location anchors\n" +
            "• 4 route barricades\n\n" +
            "Scene file saved automatically.", "Got it!");
    }


    // ─────────────────────────────────────────────────────────────
    //  SHARED: BUILD PLAYER
    // ─────────────────────────────────────────────────────────────
    private static GameObject BuildPlayer(Vector3 position)
    {
        GameObject existing = GameObject.FindGameObjectWithTag("Player");
        if (existing != null)
        {
            existing.transform.position = position;
            SpriteRenderer esr = existing.GetComponent<SpriteRenderer>();
            if (esr != null)
            {
                esr.sprite = LoadSprite("kenney_topdown_shooter/PNG/Hitman 1/hitman1_stand.png");
                esr.color  = Color.white;
                existing.transform.localScale = Vector3.one;
            }
            ConfigurePlayerComponents(existing);
            return existing;
        }

        GameObject player = new GameObject("Player");
        player.transform.position = position;
        SafeSetTag(player, "Player");

        SpriteRenderer sr = player.AddComponent<SpriteRenderer>();
        sr.sprite = LoadSprite("kenney_topdown_shooter/PNG/Hitman 1/hitman1_stand.png");
        sr.color  = Color.white;
        sr.sortingOrder = 5;
        player.transform.localScale = Vector3.one;

        Rigidbody2D rb = player.AddComponent<Rigidbody2D>();
        rb.gravityScale          = 0f;
        rb.freezeRotation        = true;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        CircleCollider2D col = player.AddComponent<CircleCollider2D>();
        col.radius = 0.35f;

        ConfigurePlayerComponents(player);
        Debug.Log("[SceneBuilder] Player created at " + position);
        return player;
    }

    private static void ConfigurePlayerComponents(GameObject player)
    {
        PlayerController ctrl = player.GetOrAddComponent<PlayerController>();
        ctrl.walkSpeed    = 3.5f;
        ctrl.runSpeed     = 6.0f;
        ctrl.driveSpeed   = 12.0f;
        ctrl.roadLayer    = LayerMask.GetMask("Road");
        ctrl.alleyLayer   = LayerMask.GetMask("Alley");
        ctrl.buildingLayer = LayerMask.GetMask("Building");

        PlayerCombat combat = player.GetOrAddComponent<PlayerCombat>();
        combat.enemyLayer  = LayerMask.GetMask("Enemy");
        combat.meleeRange  = 1.5f;
        combat.meleeDamage = 40;
        combat.gunRange    = 15f;
        combat.gunDamage   = 35;
        combat.fireRate    = 0.25f;
        combat.armedSprite = LoadSprite("kenney_topdown_shooter/PNG/Hitman 1/hitman1_gun.png");

        Transform existingFP = player.transform.Find("FirePoint");
        if (existingFP == null)
        {
            GameObject fp = new GameObject("FirePoint");
            fp.transform.SetParent(player.transform);
            fp.transform.localPosition = new Vector3(0.2f, 0.4f, 0f);
            combat.firePoint = fp.transform;
        }
        else { combat.firePoint = existingFP; }

        PlayerInteraction interaction = player.GetOrAddComponent<PlayerInteraction>();
        interaction.interactRadius    = 2.0f;
        interaction.interactableLayer = LayerMask.GetMask("Interactable");
    }

    // ─────────────────────────────────────────────────────────────
    //  SHARED: BUILD GAME MANAGER
    // ─────────────────────────────────────────────────────────────
    private static GameObject BuildGameManager(GameObject player)
    {
        GameObject existing = GameObject.Find("GameManager");
        if (existing != null) return existing;

        GameObject gm = new GameObject("GameManager");

        AdaptiveDirectorClient dirClient = gm.AddComponent<AdaptiveDirectorClient>();
        dirClient.apiBaseUrl    = "http://127.0.0.1:8001";
        dirClient.flushInterval = 2.0f;
        dirClient.autoFlush     = true;

        gm.AddComponent<TelemetryReporter>();
        gm.AddComponent<WorldManifestApplier>();

        MissionManager mm = gm.AddComponent<MissionManager>();
        if (player != null)
        {
            mm.playerCombat     = player.GetComponent<PlayerCombat>();
            mm.playerController = player.GetComponent<PlayerController>();
        }

        Debug.Log("[SceneBuilder] GameManager created and wired.");
        return gm;
    }

    // ─────────────────────────────────────────────────────────────
    //  MAP 2: LOCATION ANCHORS
    // ─────────────────────────────────────────────────────────────
    private static void BuildLocationAnchors(WorldManifestApplier applier)
    {
        applier.locationAnchors.Clear();

        var anchors = new (string id, Vector3 pos)[]
        {
            ("warehouse_interior",  new Vector3(-12f,  12f, 0f)),
            ("open_plaza",          new Vector3(  0f,  12f, 0f)),
            ("far_docks",           new Vector3(-20f, -10f, 0f)),
            ("fortified_building",  new Vector3( 15f,  12f, 0f)),
            ("mid_street",          new Vector3(  5f,   5f, 0f)),
        };

        foreach (var (id, pos) in anchors)
        {
            string     goName  = "Anchor_" + id;
            GameObject anchor  = GameObject.Find(goName) ?? new GameObject(goName);
            anchor.transform.position = pos;
            anchor.SetActive(false);

            if (anchor.GetComponent<SpriteRenderer>() == null)
            {
                SpriteRenderer sr = anchor.AddComponent<SpriteRenderer>();
                sr.sprite = LoadSprite("kenney_vehicles/PNG/Objects/arrow_white.png");
                sr.color  = new Color(0.8f, 0.4f, 1f, 0.6f);
                sr.sortingOrder = 0;
            }

            applier.locationAnchors.Add(new WorldManifestApplier.LocationAnchor
            {
                locationId     = id,
                spawnTransform = anchor.transform,
            });
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  MAP 2: ROUTE BARRICADES
    // ─────────────────────────────────────────────────────────────
    private static void BuildRouteBarricades(WorldManifestApplier applier)
    {
        applier.routeBarricades.Clear();

        var barricades = new (string routeId, Vector3 pos)[]
        {
            ("main_road_north",          new Vector3(  0f,  8f, 0f)),
            ("alley_south",              new Vector3(  0f, -6f, 0f)),
            ("highway_east",             new Vector3( 20f,  0f, 0f)),
            ("warehouse_front_entrance", new Vector3(-12f,  8f, 0f)),
        };

        foreach (var (routeId, pos) in barricades)
        {
            string     goName = "Barricade_" + routeId;
            GameObject b      = GameObject.Find(goName) ?? new GameObject(goName);
            b.transform.position = pos;
            b.layer = LAYER_INTERACTABLE;
            b.SetActive(false);

            if (b.GetComponent<SpriteRenderer>() == null)
            {
                SpriteRenderer sr = b.AddComponent<SpriteRenderer>();
                sr.sprite = LoadSprite("kenney_vehicles/PNG/Objects/barrier_red.png");
                sr.sortingOrder = 2;
            }

            if (b.GetComponent<BoxCollider2D>() == null)
            {
                BoxCollider2D col = b.AddComponent<BoxCollider2D>();
                col.size = new Vector2(2.1f, 0.62f);
            }

            applier.routeBarricades.Add(new WorldManifestApplier.NamedBarricade
            {
                routeId         = routeId,
                barricadeObject = b,
            });
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  MAP 2: GUARD INFRASTRUCTURE
    // ─────────────────────────────────────────────────────────────
    private static void BuildGuardInfrastructure(WorldManifestApplier applier)
    {
        applier.guardSpawnPoints.Clear();

        Vector3[] spawnPositions =
        {
            new(-15f, -8f, 0f), new(-10f, -5f, 0f),
            new( -5f,  0f, 0f), new(  0f,  5f, 0f),
            new(  5f, 10f, 0f), new( 10f,  5f, 0f),
            new( 15f,  0f, 0f), new( 10f, -5f, 0f),
            new(  5f,-10f, 0f), new(  0f,-10f, 0f),
            new(-10f,  8f, 0f), new(-15f,  3f, 0f),
        };

        for (int i = 0; i < spawnPositions.Length; i++)
        {
            string     spName = $"GuardSpawn_{i + 1:D2}";
            GameObject sp     = GameObject.Find(spName) ?? new GameObject(spName);
            sp.transform.position = spawnPositions[i];
            applier.guardSpawnPoints.Add(sp.transform);
        }

        if (applier.guardPrefab == null)
            applier.guardPrefab = CreateGuardPrefab("GuardPrefab", false);

        if (applier.targetPrefab == null)
            applier.targetPrefab = CreateGuardPrefab("ViktorPrefab", true);
    }

    private static GameObject CreateGuardPrefab(string prefabName, bool isBoss)
    {
        GameObject go = new GameObject(prefabName);

        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = isBoss
            ? LoadSprite("kenney_topdown_shooter/PNG/Survivor 1/survivor1_gun.png")
            : LoadSprite("kenney_topdown_shooter/PNG/Soldier 1/soldier1_gun.png");
        sr.color = isBoss ? new Color(1f, 0.45f, 0.45f) : Color.white;
        sr.sortingOrder = 5;
        go.transform.localScale = Vector3.one;

        Rigidbody2D rb = go.AddComponent<Rigidbody2D>();
        rb.gravityScale   = 0f;
        rb.freezeRotation = true;

        CircleCollider2D col = go.AddComponent<CircleCollider2D>();
        col.radius = 0.35f;
        go.layer   = LAYER_ENEMY;
        SafeSetTag(go, "Enemy");

        for (int w = 0; w < 2; w++)
        {
            GameObject wp = new GameObject("Waypoint_" + w);
            wp.transform.SetParent(go.transform);
            wp.transform.localPosition = new Vector3(w == 0 ? -1.5f : 1.5f, 0f, 0f);
        }

        EnemyPatrol patrol = go.AddComponent<EnemyPatrol>();
        patrol.patrolSpeed = isBoss ? 1.5f : 2.0f;
        patrol.chaseSpeed  = isBoss ? 3.0f : 4.0f;
        patrol.maxHealth   = isBoss ? 150  : 60;
        foreach (Transform child in go.transform)
            if (child.name.StartsWith("Waypoint_")) patrol.waypoints.Add(child);

        EnemyDetection det = go.AddComponent<EnemyDetection>();
        det.visionRange   = isBoss ? 8f : 6f;
        det.obstacleLayer = LayerMask.GetMask("Building");
        det.playerLayer   = LayerMask.GetMask("Default");

        string folder = "Assets/Prefabs";
        if (!AssetDatabase.IsValidFolder(folder))
        {
            if (!System.IO.Directory.Exists(System.IO.Path.Combine(Application.dataPath, "Prefabs")))
                System.IO.Directory.CreateDirectory(System.IO.Path.Combine(Application.dataPath, "Prefabs"));
            AssetDatabase.Refresh();
        }

        string prefabPath = $"{folder}/{prefabName}.prefab";
        GameObject savedPrefab = null;
        try
        {
            savedPrefab = PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            Debug.Log($"[SceneBuilder] Prefab saved: {prefabPath}");
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
        return savedPrefab;
    }

    // ─────────────────────────────────────────────────────────────
    //  SHARED: BUILD UI CANVAS
    // ─────────────────────────────────────────────────────────────
    private static void BuildUI(GameObject gm, string objectiveMsg)
    {
        if (GameObject.Find("UICanvas") != null) return;
        MissionManager mm = gm.GetComponent<MissionManager>();
        WorldManifestApplier.CreateRuntimeUI(gm, mm, objectiveMsg);
        Debug.Log("[SceneBuilder] UI Canvas created.");
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  Extension helper
// ─────────────────────────────────────────────────────────────────────────────
public static class GameObjectExtensions
{
    public static T GetOrAddComponent<T>(this GameObject go) where T : Component
    {
        T comp = go.GetComponent<T>();
        return comp != null ? comp : go.AddComponent<T>();
    }
}
#endif
