# Implementation Plan: AI-Driven 3x3 Sector Graph Generation for Map 2 (5-Phase Execution)

## Overview
We are transitioning Map 2 to an **AI-Driven Room/Sector Graph Generation pipeline ($3 \times 3$ grid, 9 tactical sectors)** across a $60 \times 45$ unit canvas.

To ensure stability, clean testing, and zero regressions, work is broken down into **5 distinct, sequential phases**. Each phase is verified independently before moving to the next.

---

## Codebase Contracts (verified against current source)

| Source File | Key Contract |
|---|---|
| `MissionManager.cs` | Auto-finds `playerCombat` via `FindObjectOfType` if Inspector slot is empty; scene name must contain `"Map2"` (case-insensitive) for M2 logic to activate |
| `MissionZoneTrigger.cs` | Collider2D must be **IsTrigger** + Player tag must be `"Player"`; zone types: `GunShop`, `MissionEnd`, `TargetArea`, `GenericCheckpoint` |
| `PlayerInteraction.cs` | Uses **`interactableLayer`** for **both** E key (cars, zones) and F key (walls, cars) |
| `PlayerCombat.cs` | Uses **`enemyLayer`** for melee, pistol raycast, **and** gunshot noise alert radius (`AlertNearbyEnemies` — 14 unit radius) |
| `EnemyPatrol.cs` | Needs `Rigidbody2D`, at least 1 `waypoint` Transform; `gravityScale = 0` set in `Awake`; `Die()` does **not** call `MissionManager.OnTargetEliminated()` — this wire is missing and must be added in Phase 4 |
| `EnemyDetection.cs` | Needs `obstacleLayer` (currently set to `"Building"` only — cars/barriers are transparent to vision) + `playerLayer` (currently `"Default"`) |
| `WorldManifestApplier.cs` | Needs exact `locationId` strings; has procedural fallback for both guards and boss if prefabs are null; `ApplyEnvironment()` (GlobalLight2D, rain, fog, atmosphere overlay) **already fully implemented** |
| `AdaptiveDirectorClient.cs` | Calls `/api/mission/solve`; `JsonUtility.FromJson` is used — **does not support `Dictionary<string, T>`**; all new serializable types must use `List<T>` with an `id` field, not dict-keyed objects |
| `target_solver.py` | Location IDs: `warehouse_interior`, `open_plaza`, `far_docks`, `fortified_building`, `mid_street`; route IDs: `main_road_north`, `alley_south`, `highway_east`, `warehouse_front_entrance`; `AGGRESSOR` blocked_routes includes `"rooftop_access"` which has no barricade GameObject — silently ignored |
| `gemini_director.py` | Imports schema types from `target_solver.py`; tries Gemini API first, falls back to `_generate_fallback()`; ground truth is always loaded from `backend/data/map1_ground_truth.json` — a Map 2 ground truth file or dynamic path is needed |
| `profiler.py` | 5 archetypes: `GHOST`, `WRECKING_BALL`, `SPEEDRUNNER`, `AGGRESSOR`, `BALANCED`; fallback templates must cover all 5 |
| `test_pipeline.py` | Tests 4 mock players (GHOST, WRECKING_BALL, SPEEDRUNNER, AGGRESSOR); directly accesses `manifest_r["target"]`, `manifest_r["guards"]`, etc. — must be extended to assert `room_graph` schema when Phase 1 lands |
| `BurnableCar.cs` | `Explode()` AOE uses no layer mask — hits all colliders including player; tagged `"Vehicle"`, needs `BoxCollider2D` on `interactableLayer`; `carId` must be unique |
| `DestructibleWall.cs` | Needs `BoxCollider2D` (solid, not trigger); `wallId` must be unique for telemetry |
| `CameraFollow2D.cs` | Auto-finds by `"Player"` tag; no manual wiring needed |

---

## Map 1 Layout (as-built — Phase 2 Full City "Pauper Streets")

Map 1 is **already fully built** by `AdaptiveSceneBuilder.BuildMap1()`. The scene is a 60×40 unit city:

- **Player spawn**: `(-25, 0, 0)` — west entrance
- **Gun Shop**: `(0, -7, 0)` — south alley, requires `E` key (`requireInteractButton = true`)
- **Extraction Point**: `(28, 0, 0)` — east highway, walk-in trigger (`requireInteractButton = false`)
- **4 vehicles**: Sports Red `(-20, 3)`, Sedan Blue `(4, -2)`, Sedan Black `(-6, -13)`, Police Cruiser `(20, 2)`
- **7 NPCs**: 4 patrol cops, 1 SWAT enforcer (50% resistance), 5 civilians, 2 gang thugs, 1 informant
- **Destructibles**: 4 concrete barriers, 3 wire fences, 3 crates, 4 fuel barrels
- **Buildings**: 2 warehouses, 2 storefronts, 2 slum tenements

> [!IMPORTANT]
> **5 required Unity Layers must be set up in Edit → Project Settings → Tags and Layers:**
> - `Layer 6: Interactable` — cars, walls, mission triggers (PlayerInteraction E/F keys)
> - `Layer 7: Enemy` — guards and boss (PlayerCombat raycast, melee, noise alert)
> - `Layer 8: Road` — surface detection (PlayerController telemetry)
> - `Layer 9: Alley` — surface detection
> - `Layer 10: Building` — surface detection + enemy vision obstruction
>
> **Without these layers, interaction, shooting, surface telemetry, and enemy vision all silently fail.**

> [!NOTE]
> **Known bugs to fix before or during Phase 4:**
> - `EnemyPatrol.Die()` never calls `MissionManager.OnTargetEliminated()` when `isTargetBoss == true` — Viktor's death does not complete the mission.
> - `EnemyDetection.obstacleLayer` is `"Building"` only — guards see through burning cars and concrete barriers. Should include `"Interactable"`.
> - `BurnableCar.Explode()` AOE has no layer mask — can inadvertently damage the player.
> - `WorldManifestApplier` tooltip on line 13 lists only 4 location anchors; `mid_street` is missing from the tooltip text (does not affect runtime).
> - Map 2 `BuildCityEnvironment_Legacy()` creates only one road strip — no Alley/Building surface zones, so `PlayerController.DetectSurface()` always reports `"ROAD"` on Map 2.
> - `CreateGuardPrefab()` in the scene builder assigns waypoints after `SaveAsPrefabAsset()` in some Unity versions — saved prefab may have empty waypoint list, causing guards not to patrol.

---

## 5-Phase Execution Roadmap

### Phase 1: Backend & Gemini Spatial Brain (Python)
*Goal: Python backend accepts telemetry and generates fully-specified 9-sector `room_graph` blueprints.*

1. **Schema & Models** — add to `backend/target_solver.py` (alongside existing flat types):
   - `SectorConfig`: `id` (NW/NC/NE/MW/MC/ME/SW/SC/SE), `module`, `center_x`, `center_y`, `guards` (list), `props` (list)
   - `ConnectionConfig`: `from_sector`, `to_sector`, `status` (`open` | `barricaded_wire_fence` | `barricaded_concrete` | `walled_off`)
   - `RoomGraphManifest`: `sectors` (**List**, not dict — required for Unity `JsonUtility`), `connections` (List), `target_sector`, `player_spawn_sector`, `extraction_sector`
   - `GuardEntityConfig`, `TargetBossConfig`
   - All new types imported into `gemini_director.py` from `target_solver.py` (existing import pattern)

2. **Gemini Spatial Prompt** — upgrade `gemini_director._call_gemini()`:
   - Add `room_graph` to the required JSON output schema, with sectors as a **JSON array** (not object/dict), e.g.:
     ```json
     "room_graph": {
       "sectors": [
         { "id": "NW", "module": "WAREHOUSE_INTERIOR", "center_x": -20, "center_y": 15, "guards": [], "props": [] },
         ...
       ],
       "connections": [
         { "from_sector": "NW", "to_sector": "NC", "status": "open" },
         ...
       ],
       "target_sector": "NW",
       "player_spawn_sector": "SW",
       "extraction_sector": "NE"
     }
     ```
   - Module vocabulary: `WAREHOUSE_INTERIOR`, `FORTIFIED_PLAZA`, `SLUM_ALLEYWAYS`, `URBAN_HIGHWAY`, `INDUSTRIAL_DEPOT`, `WATERFRONT_DOCKS`, `SECURITY_COMPOUND`
   - All 9 sector IDs must be present; connections cover all 12 adjacent pairs (horizontal + vertical neighbors only)
   - Inject `map2_ground_truth` context (canvas 60×45, sector grid layout) instead of Map 1 ground truth

3. **Map 2 Ground Truth** — create `backend/data/map2_ground_truth.json`:
   - Canvas: 60×45, sector cell size 20×15
   - Sector center coordinates: columns at X = -20, 0, +20; rows at Y = +15, 0, -15
   - Update `GeminiNarrativeDirector.__init__` to load `map2_ground_truth.json` for Map 2 generation (keep `map1_ground_truth.json` for Map 1 debriefing prompt context)

4. **Deterministic Heuristic Fallback** — update `gemini_director._generate_fallback()` for **all 5 archetypes** (BALANCED is currently missing from the new room_graph format; existing flat fields must be preserved for backward compat):
   - `GHOST`: SW=SLUM_ALLEYWAYS → NW=WAREHOUSE_INTERIOR, fog/night, wire fence connections
   - `WRECKING_BALL`: SW=INDUSTRIAL_DEPOT → NC=FORTIFIED_PLAZA, day/rain, concrete barricades
   - `SPEEDRUNNER`: SW=URBAN_HIGHWAY → NE=WATERFRONT_DOCKS, day/clear, open connections then roadblock at ME→NE
   - `AGGRESSOR`: MW=URBAN_HIGHWAY → NE=SECURITY_COMPOUND, night/rain, concrete + walled connections
   - `BALANCED`: SW=SLUM_ALLEYWAYS → MC=FORTIFIED_PLAZA, dusk/clear, mix of open and wire fence

5. **Update `test_pipeline.py`** — add `room_graph` schema assertions:
   - All 9 sector IDs present in `room_graph["sectors"]`
   - All sector entries have `id`, `module`, `center_x`, `center_y`
   - `target_sector`, `player_spawn_sector`, `extraction_sector` are valid sector IDs
   - All 5 archetypes tested (add a BALANCED mock player event set)
   - Success criteria: 5/5 archetypes return valid room_graph blueprints

6. **Verification**:
   - Run `python backend/test_pipeline.py`
   - Success: all 5 archetypes produce 9-sector blueprints with valid connections and no schema errors

---

### Phase 2: Unity Serialization & Manifest Handshake (C#)
*Goal: Unity deserializes the 9-sector `room_graph` JSON cleanly with zero data loss or parse errors.*

1. **Unity Data Contracts** — add to `unity_scripts/Director/AdaptiveDirectorClient.cs`:
   ```csharp
   [Serializable] public class SectorConfig {
       public string id;        // "NW", "NC", etc.
       public string module;    // "WAREHOUSE_INTERIOR", etc.
       public float center_x;
       public float center_y;
       public List<GuardSpawnConfig> guards;
       public List<string> props;
   }
   [Serializable] public class ConnectionConfig {
       public string from_sector;
       public string to_sector;
       public string status;    // "open", "barricaded_wire_fence", "barricaded_concrete", "walled_off"
   }
   [Serializable] public class RoomGraphManifest {
       public List<SectorConfig> sectors;      // List, NOT Dictionary — JsonUtility limitation
       public List<ConnectionConfig> connections;
       public string target_sector;
       public string player_spawn_sector;
       public string extraction_sector;
   }
   [Serializable] public class GuardSpawnConfig { public float x; public float y; public string patrol_pattern; }
   [Serializable] public class TargetBossConfig  { public string armor; public int health; public bool is_indoors; }
   ```
   - Extend `MissionManifest` with `public RoomGraphManifest room_graph;`

2. **Logging** — in `AdaptiveDirectorClient.SolveMissionCoroutine()`, after deserialization log:
   - Sector count, target sector, player spawn sector, connection statuses

3. **Verification**:
   - Trigger `/api/mission/solve` from Unity Play mode (backend must be running)
   - Console must show all 9 sectors, target location, and connection states with no null/empty fields

---

### Phase 3: Unity Procedural Sector Builder — Architecture (C#)
*Goal: Unity constructs the physical static architecture (ground, walls, roads, buildings) of the 9 sectors at Edit time.*

1. **New Editor Script** — `unity_scripts/Editor/ProceduralSectorBuilder.cs` (inside `#if UNITY_EDITOR`):
   - Menu item: `AdaptiveWorld/Build Map 2 — Sector Graph Preview`
   - Reads `AdaptiveDirectorClient.latestManifest.room_graph` OR accepts a hardcoded test manifest
   - Clears previous sector geometry before rebuilding

2. **Sector Coordinate Mapping** — canvas 60×45:
   - Columns: X = -20 (West), 0 (Center), +20 (East)
   - Rows: Y = +15 (North), 0 (Mid), -15 (South)
   - Sector cell size: 20 wide × 15 tall

3. **Module Constructors** — one method per module type:
   - `BuildWarehouseInterior(Vector3 center)`: 14×10 perimeter walls, 2 door cutouts, interior partition wall, crate cover clusters
   - `BuildFortifiedPlaza(Vector3 center)`: open asphalt ground, 4 sandbag/barrier clusters, central plaza marker
   - `BuildSlumAlleyways(Vector3 center)`: dark alley ground (Alley layer), tenement facades, 2 narrow corridor gaps
   - `BuildUrbanHighway(Vector3 center)`: 4-lane Road layer surface, sidewalk strips, streetlights (PointLight2D)
   - `BuildIndustrialDepot(Vector3 center)`: industrial yard ground, container stack cover (solid BoxCollider2D)
   - `BuildWaterfrontDocks(Vector3 center)`: pier ground, water boundary (non-traversable solid wall at edge)
   - `BuildSecurityCompound(Vector3 center)`: outer concrete walls with guarded gate, interior courtyard

4. **Inter-Sector Gateways** — for each `ConnectionConfig`:
   - Calculate the shared boundary midpoint between adjacent sectors
   - `open`: no object placed
   - `barricaded_wire_fence`: place `DestructibleWall` (40 HP wire fence, `isTrigger = false`)
   - `barricaded_concrete`: place `DestructibleWall` (100 HP concrete, `isTrigger = false`) with a `Rigidbody2D (Kinematic)` to ensure static collision
   - `walled_off`: place solid non-destructible `BoxCollider2D` building wall

5. **Verification**:
   - Run menu item in Unity Editor; visually confirm 9 sectors tile without gaps or overlapping colliders
   - Check Physics 2D debug view for broken geometry

---

### Phase 4: Dynamic Entities & Mission Flow (C#)
*Goal: Populate the sectors with Viktor, bodyguards, patrol guards, barrels, vehicles, and the extraction trigger.*

1. **Fix `EnemyPatrol.Die()`** — add the missing boss-death wire:
   ```csharp
   if (isTargetBoss)
       MissionManager.Instance?.OnTargetEliminated();
   ```

2. **Fix `EnemyDetection.obstacleLayer`** — update `CreateEnemy()` in `AdaptiveSceneBuilder.cs` and `CreateProceduralGuard()` in `WorldManifestApplier.cs`:
   ```csharp
   detection.obstacleLayer = LayerMask.GetMask("Building", "Interactable");
   ```

3. **Target & Escort Spawning** — `WorldManifestApplier` resolves `target_sector` → center coordinates → spawns Viktor:
   - Viktor HP and armor from `manifest.target` (already implemented); wire to sector center from `room_graph`
   - Spawn 1–2 personal bodyguards (from `manifest.guards`) flanking Viktor at ±1.5 units

4. **Guard Patrol Placement** — distribute `manifest.guards.count` guards across non-target sectors:
   - Assign guards in pairs to each occupied sector; give each pair 2 waypoints within the sector bounds
   - Apply `manifest.guards.vision_range`, `reaction_speed`, `hearing_range`, and `armored` resistance

5. **Tactical Props & Hazards**:
   - Spawn fuel barrels (`DestructibleWall`, 20 HP) in `INDUSTRIAL_DEPOT` and `WAREHOUSE_INTERIOR` sectors
   - Spawn 1–2 `BurnableCar` vehicles in `URBAN_HIGHWAY` and `FORTIFIED_PLAZA` sectors if `routes.cars_available`
   - Fix `BurnableCar.Explode()` AOE: add `enemyLayer` mask to `Physics2D.OverlapCircleAll`

6. **Player Spawn & Extraction Zone**:
   - Reposition Player to center of `room_graph.player_spawn_sector` on manifest apply
   - Place `MissionZoneTrigger (ZoneType.MissionEnd)` at center of `room_graph.extraction_sector`
   - Extraction trigger disabled until `MissionManager.missionCompleted == true` (set by `OnTargetEliminated`)

7. **Verification**:
   - Play Map 2: guards patrol their sectors, Viktor is in his sector, extraction zone is reachable only after Viktor dies

---

### Phase 5: Atmospheric Lighting & Visual Polish
*Goal: Enhance top-down visual feel — the core lighting and weather systems are already implemented; this phase adds the remaining streetlight work and validates all visual states.*

> [!NOTE]
> `WorldManifestApplier.ApplyEnvironment()` **already implements**: GlobalLight2D intensity + color by time-of-day, rain particle system (`EnsureRainSystem()`), fog particle system (`EnsureFogSystem()`), and screen-space atmosphere overlay (`EnsureAtmosphereOverlay()`). Steps 1 and 2 below are the remaining new work only.

1. **Streetlights** — in `ProceduralSectorBuilder`, add to `BuildUrbanHighway()` and `BuildFortifiedPlaza()`:
   - Spawn `PointLight2D` GameObjects at regular intervals (every 8 units along the highway axis)
   - Color: warm amber (`#FFCC66`) for highway, cool blue-white for plaza
   - Radius: 4–6 units; intensity driven by `manifest.environment.ambient_light`

2. **Map 2 Surface Zones** — fix the missing Alley/Building surface coverage in Map 2:
   - `BuildSlumAlleyways()` ground collider must be on **Layer 9 (Alley)** so `PlayerController.DetectSurface()` reports `"ALLEY"` in alley sectors
   - `BuildWarehouseInterior()` and `BuildSecurityCompound()` interior floors must be on **Layer 10 (Building)** so surface reports `"BUILDING_INTERIOR"`
   - Without this, all Map 2 telemetry reports `"ROAD"` regardless of player position

3. **Verification**:
   - GHOST manifest (fog/night): scene shows deep midnight blue tint, fog particles, dim GlobalLight2D
   - WRECKING_BALL manifest (rain/dusk): amber tint, rain particles, streetlights on
   - SPEEDRUNNER manifest (clear/day): bright GlobalLight2D, no overlays
   - Walk into alley sector and confirm console shows `surface = ALLEY` in telemetry

---

## User Review Required

> [!IMPORTANT]
> **Sequential Approval**:
> We will execute **Phase 1** first. Once Phase 1 tests pass, we will present the results and proceed to Phase 2.

---

## Verification Plan

### Phase 1 Automated Tests
- Command: `python backend/test_pipeline.py`
- Success Criteria: **5/5 archetypes** (including BALANCED) return complete 9-sector blueprints with valid connections, correct sector IDs, and no schema errors.

### Phase 2
- Trigger `/api/mission/solve` from Unity; Unity console logs all 9 sectors and connection statuses.

### Phase 3
- Menu item builds visually correct 9-sector layout; Physics 2D debug shows no broken colliders.

### Phase 4
- Full playtest: patrol → infiltrate → kill Viktor → extraction unlocks.

### Phase 5
- Visual verification of 3 distinct atmosphere states (night/fog, dusk/rain, day/clear) + surface telemetry accuracy on Map 2.
