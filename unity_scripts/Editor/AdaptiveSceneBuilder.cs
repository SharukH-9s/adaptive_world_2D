#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using UnityEngine.Rendering.Universal;
using AdaptiveWorld2D.Player;
using AdaptiveWorld2D.Director;
using AdaptiveWorld2D.Mission;
using AdaptiveWorld2D.World;
using AdaptiveWorld2D.AI;

/// <summary>
/// AdaptiveSceneBuilder — One-click scene setup for AdaptiveWorld2D with Kenney Assets.
/// 
/// Menu Items:
///   AdaptiveWorld > Build Map 1 (City Exploration & Gun Shop)
///   AdaptiveWorld > Build Map 2 (Adaptive Assassination Foundation)
///
/// Run each button once inside the corresponding Unity scene, then save.
/// After that, just press Play on Map 1 — Map 2 is loaded automatically
/// and adapted at runtime via the Python Adaptive Director server.
/// </summary>
public static class AdaptiveSceneBuilder
{
    // ─────────────────────────────────────────────────────────────
    //  Layer indices  (must match Edit > Project Settings > Layers)
    // ─────────────────────────────────────────────────────────────
    private const int LAYER_INTERACTABLE = 6;
    private const int LAYER_ENEMY        = 7;
    private const int LAYER_ROAD         = 8;
    private const int LAYER_ALLEY        = 9;
    private const int LAYER_BUILDING     = 10;

    // Built-in white square sprite fallback
    private static Sprite GetWhiteSprite()
    {
        return AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
    }

    /// <summary>
    /// Loads a sprite from Assets/assets/{relativePath}, or queries AssetDatabase if path differs.
    /// Safely falls back to GetWhiteSprite() if not found.
    /// </summary>
    private static Sprite LoadSprite(string relativePath)
    {
        string fullPath = "Assets/assets/" + relativePath;
        Sprite s = AssetDatabase.LoadAssetAtPath<Sprite>(fullPath);
        if (s != null) return s;

        // Fallback: search by filename in project
        string filename = System.IO.Path.GetFileNameWithoutExtension(relativePath);
        string[] guids = AssetDatabase.FindAssets(filename + " t:Sprite");
        if (guids != null && guids.Length > 0)
        {
            string foundPath = AssetDatabase.GUIDToAssetPath(guids[0]);
            s = AssetDatabase.LoadAssetAtPath<Sprite>(foundPath);
            if (s != null) return s;
        }

        Debug.LogWarning($"[SceneBuilder] Could not find sprite at '{relativePath}'. Falling back to built-in sprite.");
        return GetWhiteSprite();
    }

    // ─────────────────────────────────────────────────────────────
    //  MAP 1 BUILDER
    // ─────────────────────────────────────────────────────────────
    [MenuItem("AdaptiveWorld/Build Map 1 (City Exploration & Gun Shop)")]
    public static void BuildMap1()
    {
        Debug.Log("[SceneBuilder] === BUILDING MAP 1 ===");

        // Dark urban slate camera background
        if (Camera.main != null)
        {
            Camera.main.backgroundColor = new Color(0.12f, 0.13f, 0.15f);
        }

        BuildCityEnvironment();
        GameObject player      = BuildPlayer(new Vector3(0, 0, 0));
        GameObject gameManager = BuildGameManager(player);
        BuildCars();
        BuildDestructibleWalls();
        BuildMap1Triggers();
        BuildUI(gameManager);

        Debug.Log("[SceneBuilder] MAP 1 BUILT SUCCESSFULLY. Save the scene now (Ctrl+S).");
        EditorUtility.DisplayDialog("Map 1 Built!",
            "Map 1 is ready with Kenney 2D Top-Down assets!\n\n" +
            "• Hitman Character (turns into armed stance when buying gun)\n" +
            "• Real Top-Down Sports Cars & Sedans\n" +
            "• Concrete Street Barriers & Knockdown Barrels\n" +
            "• Underground Gun Shop with weapon display icon\n" +
            "• Extraction Point B with directional marker\n" +
            "• Asphalt City Road Surface\n\n" +
            "Save the scene now (Ctrl+S).",
            "Got it!");
    }

    // ─────────────────────────────────────────────────────────────
    //  MAP 2 BUILDER
    // ─────────────────────────────────────────────────────────────
    [MenuItem("AdaptiveWorld/Build Map 2 (Adaptive Assassination Foundation)")]
    public static void BuildMap2()
    {
        Debug.Log("[SceneBuilder] === BUILDING MAP 2 ===");

        if (Camera.main != null)
        {
            Camera.main.backgroundColor = new Color(0.12f, 0.13f, 0.15f);
        }

        BuildCityEnvironment();
        GameObject player      = BuildPlayer(new Vector3(-22, -12, 0));
        PlayerCombat combat = player.GetComponent<PlayerCombat>();
        if (combat != null)
        {
            combat.hasPistol = true;
            combat.pistolAmmo = 24;
            SpriteRenderer sr = player.GetComponent<SpriteRenderer>();
            if (sr != null && combat.armedSprite != null)
            {
                sr.sprite = combat.armedSprite;
            }
        }
        GameObject gameManager = BuildGameManager(player);

        WorldManifestApplier applier = gameManager.GetComponent<WorldManifestApplier>();

        BuildLocationAnchors(applier);
        BuildRouteBarricades(applier);
        BuildGuardInfrastructure(applier);
        BuildUI(gameManager);

        Debug.Log("[SceneBuilder] MAP 2 BUILT SUCCESSFULLY. Save the scene now (Ctrl+S).");
        EditorUtility.DisplayDialog("Map 2 Built!",
            "Map 2 foundation is ready with Kenney 2D assets!\n\n" +
            "• Armed Soldier Guard Prefabs\n" +
            "• Viktor Target Boss Prefab\n" +
            "• Concrete Route Barricades\n" +
            "• 5 Target Location Anchors\n" +
            "• WorldManifestApplier fully wired\n\n" +
            "Save the scene now (Ctrl+S).",
            "Got it!");
    }

    // ─────────────────────────────────────────────────────────────
    //  SHARED: BUILD CITY ENVIRONMENT (Roads & Atmosphere)
    // ─────────────────────────────────────────────────────────────
    private static void BuildCityEnvironment()
    {
        if (GameObject.Find("CityRoad_Main") != null) return;

        GameObject road = new GameObject("CityRoad_Main");
        road.transform.position = new Vector3(9f, 0f, 0f);
        road.layer = LAYER_ROAD;

        SpriteRenderer sr = road.AddComponent<SpriteRenderer>();
        Sprite roadSprite = LoadSprite("kenney_vehicles/PNG/Tiles/Asphalt road/road_asphalt01.png");
        sr.sprite = roadSprite;
        sr.drawMode = SpriteDrawMode.Tiled;
        sr.size = new Vector2(38f, 6.5f);
        sr.sortingOrder = -10;

        BoxCollider2D col = road.AddComponent<BoxCollider2D>();
        col.size = new Vector2(38f, 6.5f);
        col.isTrigger = true; // Walkable trigger for road surface telemetry detection
    }

    // ─────────────────────────────────────────────────────────────
    //  SHARED: BUILD PLAYER
    // ─────────────────────────────────────────────────────────────
    private static GameObject BuildPlayer(Vector3 position)
    {
        // Reuse existing Player if already in scene
        GameObject existing = GameObject.FindGameObjectWithTag("Player");
        if (existing != null)
        {
            Debug.Log("[SceneBuilder] Player already exists in scene, reusing.");
            existing.transform.position = position;
            SpriteRenderer existingSr = existing.GetComponent<SpriteRenderer>();
            if (existingSr != null)
            {
                existingSr.sprite = LoadSprite("kenney_topdown_shooter/PNG/Hitman 1/hitman1_stand.png");
                existingSr.color = Color.white;
                existing.transform.localScale = Vector3.one;
            }
            ConfigurePlayerComponents(existing);
            return existing;
        }

        GameObject player = new GameObject("Player");
        player.transform.position = position;
        player.tag = "Player";

        // Hitman 1 stand sprite
        SpriteRenderer sr = player.AddComponent<SpriteRenderer>();
        sr.sprite = LoadSprite("kenney_topdown_shooter/PNG/Hitman 1/hitman1_stand.png");
        sr.color = Color.white;
        sr.sortingOrder = 5;
        player.transform.localScale = Vector3.one;

        // Physics
        Rigidbody2D rb = player.AddComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.freezeRotation = true;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        CircleCollider2D col = player.AddComponent<CircleCollider2D>();
        col.radius = 0.35f;

        ConfigurePlayerComponents(player);

        Debug.Log("[SceneBuilder] Player created at " + position);
        return player;
    }

    private static void ConfigurePlayerComponents(GameObject player)
    {
        // PlayerController
        PlayerController controller = player.GetOrAddComponent<PlayerController>();
        controller.walkSpeed  = 3.5f;
        controller.runSpeed   = 6.0f;
        controller.driveSpeed = 12.0f;
        controller.roadLayer    = LayerMask.GetMask("Road");
        controller.alleyLayer   = LayerMask.GetMask("Alley");
        controller.buildingLayer = LayerMask.GetMask("Building");

        // PlayerCombat
        PlayerCombat combat = player.GetOrAddComponent<PlayerCombat>();
        combat.enemyLayer = LayerMask.GetMask("Enemy");
        combat.meleeRange  = 1.5f;
        combat.meleeDamage = 40;
        combat.gunRange    = 15f;
        combat.gunDamage   = 35;
        combat.fireRate    = 0.25f;
        combat.armedSprite = LoadSprite("kenney_topdown_shooter/PNG/Hitman 1/hitman1_gun.png");

        // FirePoint child transform
        Transform existingFP = player.transform.Find("FirePoint");
        if (existingFP == null)
        {
            GameObject fp = new GameObject("FirePoint");
            fp.transform.SetParent(player.transform);
            fp.transform.localPosition = new Vector3(0.2f, 0.4f, 0f);
            combat.firePoint = fp.transform;
        }
        else
        {
            combat.firePoint = existingFP;
        }

        // PlayerInteraction
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
        if (existing != null)
        {
            Debug.Log("[SceneBuilder] GameManager already exists, reusing.");
            return existing;
        }

        GameObject gm = new GameObject("GameManager");

        // AdaptiveDirectorClient — REST bridge to Python on port 8001
        AdaptiveDirectorClient dirClient = gm.AddComponent<AdaptiveDirectorClient>();
        dirClient.apiBaseUrl    = "http://127.0.0.1:8001";
        dirClient.flushInterval = 2.0f;
        dirClient.autoFlush     = true;

        // TelemetryReporter — event bus singleton
        gm.AddComponent<TelemetryReporter>();

        // WorldManifestApplier — applies Python's manifest at Map 2 runtime
        gm.AddComponent<WorldManifestApplier>();

        // MissionManager — mission state machine
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
    //  MAP 1: DRIVABLE / BURNABLE CARS
    // ─────────────────────────────────────────────────────────────
    private static void BuildCars()
    {
        CreateCar("Car_01", new Vector3(-4f, 2f, 0f), "car_01", "SPORTS",
            "kenney_vehicles/PNG/Cars/car_red_1.png",
            "kenney_vehicles/PNG/Cars/car_black_1.png");

        CreateCar("Car_02", new Vector3( 4f,-2f, 0f), "car_02", "SEDAN",
            "kenney_vehicles/PNG/Cars/car_blue_1.png",
            "kenney_vehicles/PNG/Cars/car_black_1.png");
    }

    private static void CreateCar(string name, Vector3 pos, string carId, string carType, string spritePath, string burntSpritePath)
    {
        if (GameObject.Find(name) != null) return;

        GameObject car = new GameObject(name);
        car.transform.position = pos;
        car.layer = LAYER_INTERACTABLE;
        car.tag   = "Vehicle";

        Sprite cleanSprite = LoadSprite(spritePath);
        Sprite burntSprite = LoadSprite(burntSpritePath);

        SpriteRenderer sr = car.AddComponent<SpriteRenderer>();
        sr.sprite = cleanSprite;
        sr.color = Color.white;
        car.transform.localScale = Vector3.one;
        sr.sortingOrder = 3;

        BoxCollider2D col = car.AddComponent<BoxCollider2D>();
        col.size = new Vector2(0.72f, 1.32f);
        col.isTrigger = false;

        Rigidbody2D rb = car.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;
        rb.freezeRotation = true;

        BurnableCar bc = car.AddComponent<BurnableCar>();
        bc.carId            = carId;
        bc.carType          = carType;
        bc.cleanCarSprite   = cleanSprite;
        bc.burningCarSprite = cleanSprite;
        bc.wreckedCarSprite = burntSprite;

        Debug.Log($"[SceneBuilder] Created car: {name} at {pos}");
    }

    // ─────────────────────────────────────────────────────────────
    //  MAP 1: DESTRUCTIBLE WALLS (Concrete Street Barriers)
    // ─────────────────────────────────────────────────────────────
    private static void BuildDestructibleWalls()
    {
        CreateWall("Wall_01", new Vector3(-3f, 4f, 0f), "wall_north_01");
        CreateWall("Wall_02", new Vector3( 0f, 4f, 0f), "wall_north_02");
        CreateWall("Wall_03", new Vector3( 3f, 4f, 0f), "wall_north_03");
    }

    private static void CreateWall(string name, Vector3 pos, string wallId)
    {
        if (GameObject.Find(name) != null) return;

        GameObject wall = new GameObject(name);
        wall.transform.position = pos;
        wall.layer = LAYER_INTERACTABLE;

        Sprite intact  = LoadSprite("kenney_vehicles/PNG/Objects/barrier_red.png");
        Sprite damaged = LoadSprite("kenney_vehicles/PNG/Objects/barrier_white.png");
        Sprite rubble  = LoadSprite("kenney_vehicles/PNG/Objects/barrel_red_down.png");

        SpriteRenderer sr = wall.AddComponent<SpriteRenderer>();
        sr.sprite = intact;
        sr.color = Color.white;
        wall.transform.localScale = Vector3.one;
        sr.sortingOrder = 2;

        BoxCollider2D col = wall.AddComponent<BoxCollider2D>();
        col.size = new Vector2(2.1f, 0.62f);
        col.isTrigger = false;

        DestructibleWall dw = wall.AddComponent<DestructibleWall>();
        dw.wallId        = wallId;
        dw.maxHealth     = 100;
        dw.intactSprite  = intact;
        dw.damagedSprite = damaged;
        dw.rubbleSprite  = rubble;

        Debug.Log($"[SceneBuilder] Created wall: {name} at {pos}");
    }

    // ─────────────────────────────────────────────────────────────
    //  MAP 1: MISSION TRIGGER ZONES (Gun Shop & Extraction)
    // ─────────────────────────────────────────────────────────────
    private static void BuildMap1Triggers()
    {
        CreateGunShop(new Vector3(8f, 0f, 0f));
        CreateExtractionPoint(new Vector3(18f, 0f, 0f));
    }

    private static void CreateGunShop(Vector3 pos)
    {
        if (GameObject.Find("GunShop") != null) return;

        GameObject zone = new GameObject("GunShop");
        zone.transform.position = pos;
        zone.layer = LAYER_INTERACTABLE;

        // Ground store mat
        SpriteRenderer sr = zone.AddComponent<SpriteRenderer>();
        sr.sprite = LoadSprite("kenney_vehicles/PNG/Objects/barrier_white_race.png");
        sr.color = new Color(1f, 0.9f, 0.3f, 0.85f);
        zone.transform.localScale = new Vector3(1.2f, 1.2f, 1f);
        sr.sortingOrder = 1;

        // Floating Gun Icon badge
        GameObject gunIcon = new GameObject("GunIcon");
        gunIcon.transform.SetParent(zone.transform);
        gunIcon.transform.localPosition = new Vector3(0f, 0.35f, 0f);
        gunIcon.transform.localScale = new Vector3(2f, 2f, 1f);
        SpriteRenderer gunSr = gunIcon.AddComponent<SpriteRenderer>();
        gunSr.sprite = LoadSprite("kenney_topdown_shooter/PNG/weapon_gun.png");
        gunSr.sortingOrder = 4;

        BoxCollider2D col = zone.AddComponent<BoxCollider2D>();
        col.size = new Vector2(2f, 2f);
        col.isTrigger = true;

        MissionZoneTrigger trigger = zone.AddComponent<MissionZoneTrigger>();
        trigger.zoneType              = MissionZoneTrigger.ZoneType.GunShop;
        trigger.zoneName              = "Underground Gun Shop";
        trigger.requireInteractButton = true;
        trigger.triggerOnce           = true;

        Debug.Log($"[SceneBuilder] Created GunShop at {pos}");
    }

    private static void CreateExtractionPoint(Vector3 pos)
    {
        if (GameObject.Find("ExtractionPointB") != null) return;

        GameObject zone = new GameObject("ExtractionPointB");
        zone.transform.position = pos;
        zone.layer = LAYER_INTERACTABLE;

        // Extraction beacon arrow
        SpriteRenderer sr = zone.AddComponent<SpriteRenderer>();
        sr.sprite = LoadSprite("kenney_vehicles/PNG/Objects/arrow_yellow.png");
        sr.color = new Color(0.2f, 1f, 0.4f, 0.85f);
        zone.transform.localScale = new Vector3(2.5f, 2.5f, 1f);
        sr.sortingOrder = 1;

        BoxCollider2D col = zone.AddComponent<BoxCollider2D>();
        col.size = new Vector2(1.2f, 1.2f);
        col.isTrigger = true;

        MissionZoneTrigger trigger = zone.AddComponent<MissionZoneTrigger>();
        trigger.zoneType              = MissionZoneTrigger.ZoneType.MissionEnd;
        trigger.zoneName              = "Extraction Point B";
        trigger.requireInteractButton = false;
        trigger.triggerOnce           = false;

        Debug.Log($"[SceneBuilder] Created ExtractionPointB at {pos}");
    }

    // ─────────────────────────────────────────────────────────────
    //  MAP 2: LOCATION ANCHORS (all start inactive)
    // ─────────────────────────────────────────────────────────────
    private static void BuildLocationAnchors(WorldManifestApplier applier)
    {
        applier.locationAnchors.Clear();

        // These 5 locationId strings MUST match target_solver.py exactly
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
            string goName = "Anchor_" + id;
            GameObject anchor = GameObject.Find(goName) ?? new GameObject(goName);
            anchor.transform.position = pos;
            anchor.SetActive(false); // Python activates the correct one at runtime

            if (anchor.GetComponent<SpriteRenderer>() == null)
            {
                SpriteRenderer sr = anchor.AddComponent<SpriteRenderer>();
                sr.sprite = LoadSprite("kenney_vehicles/PNG/Objects/arrow_white.png");
                sr.color = new Color(0.8f, 0.4f, 1f, 0.6f);
                sr.sortingOrder = 0;
            }

            applier.locationAnchors.Add(new WorldManifestApplier.LocationAnchor
            {
                locationId     = id,
                spawnTransform = anchor.transform,
            });

            Debug.Log($"[SceneBuilder] Created location anchor: {id} at {pos}");
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  MAP 2: ROUTE BARRICADES (all start inactive)
    // ─────────────────────────────────────────────────────────────
    private static void BuildRouteBarricades(WorldManifestApplier applier)
    {
        applier.routeBarricades.Clear();

        // routeId strings MUST match target_solver.py route IDs exactly
        var barricades = new (string routeId, Vector3 pos)[]
        {
            ("main_road_north",          new Vector3(  0f,  8f, 0f)),
            ("alley_south",              new Vector3(  0f, -6f, 0f)),
            ("highway_east",             new Vector3( 20f,  0f, 0f)),
            ("warehouse_front_entrance", new Vector3(-12f,  8f, 0f)),
        };

        foreach (var (routeId, pos) in barricades)
        {
            string goName = "Barricade_" + routeId;
            GameObject b = GameObject.Find(goName) ?? new GameObject(goName);
            b.transform.position = pos;
            b.layer = LAYER_INTERACTABLE;
            b.SetActive(false); // Python toggles these on/off at runtime

            if (b.GetComponent<SpriteRenderer>() == null)
            {
                SpriteRenderer sr = b.AddComponent<SpriteRenderer>();
                sr.sprite = LoadSprite("kenney_vehicles/PNG/Objects/barrier_red.png");
                sr.color = Color.white;
                b.transform.localScale = Vector3.one;
                sr.sortingOrder = 2;
            }

            if (b.GetComponent<BoxCollider2D>() == null)
            {
                BoxCollider2D col = b.AddComponent<BoxCollider2D>();
                col.size = new Vector2(2.1f, 0.62f);
                col.isTrigger = false;
            }

            applier.routeBarricades.Add(new WorldManifestApplier.NamedBarricade
            {
                routeId         = routeId,
                barricadeObject = b,
            });

            Debug.Log($"[SceneBuilder] Created barricade: {routeId} at {pos}");
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  MAP 2: GUARD SPAWN POINTS + PREFABS
    // ─────────────────────────────────────────────────────────────
    private static void BuildGuardInfrastructure(WorldManifestApplier applier)
    {
        applier.guardSpawnPoints.Clear();

        // 12 guard spawn positions spread across the map
        Vector3[] spawnPositions = new Vector3[]
        {
            new Vector3(-15f, -8f, 0f), new Vector3(-10f, -5f, 0f),
            new Vector3( -5f,  0f, 0f), new Vector3(  0f,  5f, 0f),
            new Vector3(  5f, 10f, 0f), new Vector3( 10f,  5f, 0f),
            new Vector3( 15f,  0f, 0f), new Vector3( 10f, -5f, 0f),
            new Vector3(  5f,-10f, 0f), new Vector3(  0f,-10f, 0f),
            new Vector3(-10f,  8f, 0f), new Vector3(-15f,  3f, 0f),
        };

        for (int i = 0; i < spawnPositions.Length; i++)
        {
            string spName = $"GuardSpawn_{i + 1:D2}";
            GameObject sp = GameObject.Find(spName) ?? new GameObject(spName);
            sp.transform.position = spawnPositions[i];
            applier.guardSpawnPoints.Add(sp.transform);
        }

        // Guard Prefab
        if (applier.guardPrefab == null)
            applier.guardPrefab = CreateGuardPrefab("GuardPrefab", false);

        // Viktor Boss Prefab
        if (applier.targetPrefab == null)
        {
            GameObject viktorPrefab = CreateGuardPrefab("ViktorPrefab", true);
            applier.targetPrefab = viktorPrefab;
            applier.currentTargetInstance = null; // will be set at runtime
        }

        Debug.Log("[SceneBuilder] Guard infrastructure built: 12 spawn points + prefabs.");
    }

    private static GameObject CreateGuardPrefab(string prefabName, bool isBoss)
    {
        // Build the template in scene first, then convert to prefab
        GameObject go = new GameObject(prefabName);

        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        if (isBoss)
        {
            sr.sprite = LoadSprite("kenney_topdown_shooter/PNG/Survivor 1/survivor1_gun.png");
            sr.color = new Color(1f, 0.45f, 0.45f); // subtle tint for boss
        }
        else
        {
            sr.sprite = LoadSprite("kenney_topdown_shooter/PNG/Soldier 1/soldier1_gun.png");
            sr.color = Color.white;
        }
        sr.sortingOrder = 5;
        go.transform.localScale = Vector3.one;

        Rigidbody2D rb = go.AddComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.freezeRotation = true;

        CircleCollider2D col = go.AddComponent<CircleCollider2D>();
        col.radius = 0.35f;
        go.layer = LAYER_ENEMY;
        go.tag = "Enemy";

        // 2 Waypoint children for patrol
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
        // Wire the waypoints
        foreach (Transform child in go.transform)
        {
            if (child.name.StartsWith("Waypoint_"))
                patrol.waypoints.Add(child);
        }

        EnemyDetection detection = go.AddComponent<EnemyDetection>();
        detection.visionRange   = isBoss ? 8f : 6f;
        detection.obstacleLayer = LayerMask.GetMask("Building");
        detection.playerLayer   = LayerMask.GetMask("Default");

        // Save as prefab in Assets/unity_scripts/
        string prefabPath = "Assets/unity_scripts/" + prefabName + ".prefab";
        GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
        Object.DestroyImmediate(go); // remove the scene copy

        Debug.Log($"[SceneBuilder] Created prefab: {prefabPath}");
        return savedPrefab;
    }

    // ─────────────────────────────────────────────────────────────
    //  SHARED: BUILD UI CANVAS
    // ─────────────────────────────────────────────────────────────
    private static void BuildUI(GameObject gameManager)
    {
        if (GameObject.Find("UICanvas") != null) return;

        // Canvas
        GameObject canvasGO = new GameObject("UICanvas");
        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasGO.AddComponent<CanvasScaler>();
        canvasGO.AddComponent<UnityEngine.UI.GraphicRaycaster>();

        // Objective Text (bottom of screen)
        GameObject objTextGO = new GameObject("ObjectiveText");
        objTextGO.transform.SetParent(canvasGO.transform, false);
        Text objText = objTextGO.AddComponent<Text>();
        objText.text      = "MISSION 1: Reach the Underground Gun Shop and purchase a firearm.";
        objText.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        objText.fontSize  = 16;
        objText.color     = Color.white;
        objText.alignment = TextAnchor.LowerCenter;
        RectTransform objRT = objTextGO.GetComponent<RectTransform>();
        objRT.anchorMin = new Vector2(0f, 0f);
        objRT.anchorMax = new Vector2(1f, 0.1f);
        objRT.offsetMin = new Vector2(10f, 10f);
        objRT.offsetMax = new Vector2(-10f, 0f);

        // Briefing Panel (starts hidden — shown when Map 2 manifest arrives)
        GameObject panelGO = new GameObject("BriefingPanel");
        panelGO.transform.SetParent(canvasGO.transform, false);
        UnityEngine.UI.Image panelImg = panelGO.AddComponent<UnityEngine.UI.Image>();
        panelImg.color = new Color(0f, 0f, 0f, 0.75f);
        RectTransform panelRT = panelGO.GetComponent<RectTransform>();
        panelRT.anchorMin = new Vector2(0.1f, 0.4f);
        panelRT.anchorMax = new Vector2(0.9f, 0.9f);
        panelRT.offsetMin = Vector2.zero;
        panelRT.offsetMax = Vector2.zero;

        // Debriefing Text inside panel
        GameObject debriefGO = new GameObject("DebriefingText");
        debriefGO.transform.SetParent(panelGO.transform, false);
        Text debriefText = debriefGO.AddComponent<Text>();
        debriefText.text      = "";
        debriefText.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        debriefText.fontSize  = 14;
        debriefText.color     = Color.white;
        debriefText.alignment = TextAnchor.MiddleCenter;
        RectTransform debriefRT = debriefGO.GetComponent<RectTransform>();
        debriefRT.anchorMin = Vector2.zero;
        debriefRT.anchorMax = Vector2.one;
        debriefRT.offsetMin = new Vector2(20f, 20f);
        debriefRT.offsetMax = new Vector2(-20f, -20f);

        panelGO.SetActive(false);

        // Wire UI references into MissionManager
        MissionManager mm = gameManager.GetComponent<MissionManager>();
        if (mm != null)
        {
            mm.objectiveText  = objText;
            mm.debriefingText = debriefText;
            mm.briefingPanel  = panelGO;
        }

        Debug.Log("[SceneBuilder] UI Canvas created and wired into MissionManager.");
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  Extension helper — GetOrAddComponent
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
