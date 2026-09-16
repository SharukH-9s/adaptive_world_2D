# Baseline Behavior & Characterization Record (Phase 0)

This document records the exact baseline behavior, public API surface, and runtime flows of **AdaptiveWorld2D** prior to architectural refactoring on branch `feature/arch-phase-0-1`.

---

## 1. Pipeline Baseline Verification (backend/test_pipeline.py)

Executed on: September 15, 2026 (Local)  
Environment: Python 3.12 (in-process FastAPI TestClient)  
Result: **ALL PIPELINE TESTS PASSED**

### Characterization of the 5 Test Profiles

| Player Profile | Archetype Computed | Stealth Ratio | Aggression | Destruction Index | Avg Speed | Target Armor | Guards | Target Location | RoomGraph Target Sector |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **GHOST** | `GHOST` | 1.00 | 0.13 | 0.00 | 0.14 m/s | light | 7 | `warehouse_interior` | `MC` (Middle Center) |
| **WRECKING_BALL** | `WRECKING_BALL` | 0.00 | 0.40 | 0.60 | 0.31 m/s | reinforced | 8 | `open_plaza` | `MC` (Middle Center) |
| **SPEEDRUNNER** | `SPEEDRUNNER` | 1.00 | 0.00 | 0.00 | 0.97 m/s | medium | 7 | `fortified_building` | `NC` (North Center) |
| **AGGRESSOR** | `AGGRESSOR` | 0.00 | 1.00 | 0.00 | 0.31 m/s | heavy | 8 | `fortified_building` | `NE` (North East) |
| **BALANCED** | `BALANCED` | 0.00 | 0.17 | 0.00 | 0.31 m/s | medium | 6 | `fortified_building` | `MC` (Middle Center) |

### Room Graph Invariants
- Exactly 9 sectors (`NW`, `NC`, `NE`, `MW`, `MC`, `ME`, `SW`, `SC`, `SE`).
- Exactly 12 grid connections.
- Spawn (`SW`) $\rightarrow$ Target $\rightarrow$ Extraction (`NE` or `SE`) is continuously reachable with 0 `walled_off` traverse errors.

---

## 2. Public API Surface Contracts

### A. `AdaptiveDirectorClient.cs`
**Namespace**: `AdaptiveWorld2D.Director`  
**Base Class**: `UnityEngine.MonoBehaviour`

#### Public Properties & Fields
- `public static AdaptiveDirectorClient Instance { get; private set; }`
- `public string apiBaseUrl = "http://127.0.0.1:8001"`
- `public float flushInterval = 2.0f`
- `public bool autoFlush = true`
- `public string currentArchetype = "UNKNOWN"`
- `public static MissionManifest latestManifest`
- `public event Action<string> OnArchetypeChanged`
- `public event Action<MissionManifest> OnManifestReceived`

#### Public Methods
- `public static void CacheManifestToDisk(string json)`
- `public static MissionManifest LoadCachedManifest()`
- `public void QueueEvent(string eventType, string jsonData)`
- `public void FlushQueue()`
- `public void RequestMission2Manifest(int seed = 42, Action<MissionManifest> callback = null)`
- `public void ResetDirectorSession(Action onDone = null)`

#### Serialized DTO Classes
- `TargetManifest` (`location_id`, `location_label`, `coordinates`, `is_indoors`, `armor`, `health`, `weapon`, `defense_style`, `target_archetype`)
- `GuardsManifest` (`count`, `vision_range`, `reaction_speed`, `hearing_range`, `high_alert`, `armored`, `patrol_pattern`)
- `EnvironmentManifest` (`time_of_day`, `weather`, `visibility`, `rain_intensity`, `ambient_light`, `ambient_label`)
- `RoutesManifest` (`primary_route`, `primary_label`, `blocked_routes`, `reinforced_barricades`, `cars_available`)
- `GuardEntityConfig` (`sector_id`, `offset_x`, `offset_y`, `role`, `armored`)
- `SectorConfig` (`id`, `module`, `center_x`, `center_y`, `guards`, `props`)
- `ConnectionConfig` (`from_sector`, `to_sector`, `status`)
- `RoomGraphManifest` (`sectors`, `connections`, `target_sector`, `player_spawn_sector`, `extraction_sector`, `GetSector(id)`)
- `MissionManifest` (`mission_id`, `mission_title`, `player_archetype`, `mission_briefing`, `narrative_debriefing`, `psychological_deduction`, `target`, `guards`, `environment`, `routes`, `room_graph`, `Sanitize()`)
- `PlayerProfileData`

---

### B. `WorldManifestApplier.cs`
**Namespace**: `AdaptiveWorld2D.Director`  
**Base Class**: `UnityEngine.MonoBehaviour`

#### Public Properties & Serialized Fields
- `public GameObject targetPrefab`
- `public GameObject currentTargetInstance`
- `public List<LocationAnchor> locationAnchors`
- `public GameObject guardPrefab`
- `public List<Transform> guardSpawnPoints`
- `public List<NamedBarricade> routeBarricades`
- `public List<GameObject> drivableCars`
- `public Component globalLight2D`
- `public ParticleSystem rainParticles`
- `public ParticleSystem fogParticles`

#### Public Methods
- `public static bool IsMap2Scene(GameObject context = null)`
- `public void BootstrapForMap2(UnityEngine.SceneManagement.Scene targetScene = default)`
- `public void ApplyManifest(MissionManifest manifest)`

---

### C. `MissionManager.cs`
**Namespace**: `AdaptiveWorld2D.Mission`  
**Base Class**: `UnityEngine.MonoBehaviour`

#### Public Properties & Serialized Fields
- `public static MissionManager Instance { get; private set; }`
- `public int currentMission = 1`
- `public bool hasBoughtGun = false`
- `public bool missionCompleted = false`
- `public Text objectiveText`
- `public Text debriefingText`
- `public GameObject briefingPanel`
- `public PlayerCombat playerCombat`
- `public PlayerController playerController`

#### Public Methods
- `public void SetObjective(string text)`
- `public void OnZoneReached(MissionZoneTrigger zone)`
- `public void OnGunPurchased()`
- `public void OnTargetEliminated()`
- `public void OnExtractionReached()`
- `public void ShowDebriefing(string debriefText, string briefingText = "")`

---

## 3. Verified Unity Runtime Flows

1. **Map 1 Startup**:
   - `MissionManager.Awake()` sets `currentMission = 1`.
   - `AdaptiveDirectorClient.Instance.ResetDirectorSession()` flushes previous server telemetry.
   - Cache file `Temp/AdaptiveWorld_LatestManifest.json` is deleted on non-Map2 scene load.
   - `PlayerController` and `PlayerCombat` queue movement/combat events via `QueueEvent()`.
   - Every `flushInterval` (2.0s), `FlushQueue()` issues `POST /api/telemetry/batch`.

2. **Transition to Map 2**:
   - `SceneManager.LoadScene("Map2")` is triggered upon reaching Extraction Point B in Map 1.
   - `AdaptiveDirectorClient.OnSceneLoaded` detects `isMap2 = true`.
   - `WorldManifestApplier.BootstrapForMap2()` subscribes to `OnManifestReceived`.
   - Priority hierarchy for manifest application:
     1. In-memory `AdaptiveDirectorClient.latestManifest`
     2. On-disk cache `Temp/AdaptiveWorld_LatestManifest.json`
     3. Network request `AdaptiveDirectorClient.Instance.RequestMission2Manifest()`
     4. Default fallback `ApplyDefaultFallback()` after timeout or failure.

3. **Manifest Application Sequence**:
   - `SectorRuntimeBuilder.Build(rg)` builds the 3x3 tactical grid and nav connections.
   - `ApplyGuards()` spawns guard entities and configures vision cones and patrol routes.
   - `ApplyTarget()` instantiates boss Viktor, attaches weapons, and assigns armor/health.
   - `ApplyEnvironment()` adjusts weather particles (rain/fog) and global light 2D.
   - `ApplyRoutes()` enables/disables barricades and routes.
   - `ApplyTacticalProps()` spawns crates/fuel barrels.
   - `ApplyPlayerSpawn()` positions the player in the spawn sector.
   - `ApplyExtractionZone()` activates extraction collider in the designated sector.
