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

### Phase 1: Backend & Gemini Spatial Brain (Python) -- [COMPLETED]
*Goal: Python backend accepts telemetry and generates fully-specified 9-sector `room_graph` blueprints.*

- [x] **Schema & Models** (`backend/target_solver.py`):
  - `SectorConfig`: `id` (NW..SE), `module`, `center_x`, `center_y`, `guards`, `props`.
  - `ConnectionConfig`: `from_sector`, `to_sector`, `status` (`open` | `barricaded_wire_fence` | `barricaded_concrete` | `walled_off`).
  - `RoomGraphManifest`: `sectors` (stored as `List[SectorConfig]`, NOT dict, for Unity `JsonUtility`), `connections` (12 edges), `target_sector`, `player_spawn_sector`, `extraction_sector`.
  - `GuardEntityConfig`, `TargetBossConfig`.
  - *As-built addition:* Implemented **BFS Graph Reachability Algorithm** (`RoomGraphManifest.validate()`) verifying that `player_spawn` -> `target` -> `extraction` is 100% traversable (no impassable `walled_off` cuts).
  - *As-built addition:* Added two generic filler modules `OPEN_STREET` and `VACANT_LOT` alongside the 7 specialized modules (total: 9 modules).
- [x] **Gemini Spatial Prompt** (`backend/gemini_director.py`):
  - Injected Map 2 spatial grid schema into prompt; output requires 9 unique sectors and 12 orthogonal connection edges.
  - *As-built addition:* Added **Self-Healing Fallback Guard** (`_validate_and_repair_room_graph()`): if Gemini response fails validation or encounters a 429 quota limit, it automatically substitutes the validated archetype template so Unity never receives malformed JSON.
- [x] **Map 2 Ground Truth** (`backend/data/map2_ground_truth.json`):
  - 60×45 canvas, 20×15 cell size, sector grid coordinates, adjacency graph, and module descriptions.
- [x] **Deterministic Fallback Blueprints** (`backend/target_solver.py`):
  - Handcrafted complete 9-sector blueprints for all 5 archetypes (`GHOST`, `WRECKING_BALL`, `SPEEDRUNNER`, `AGGRESSOR`, `BALANCED`).
- [x] **Automated Pipeline Tests** (`backend/test_pipeline.py`):
  - Tests all 5 archetypes; verifies 9/9 sectors, 12/12 connections, and BFS path connectivity.
  - *Status:* Passed (5/5 archetypes valid and diverse).

---

### Phase 2: Unity Serialization & Manifest Handshake (C#) -- [COMPLETED]
*Goal: Unity deserializes the 9-sector `room_graph` JSON cleanly with zero data loss or parse errors.*

- [x] **Unity Data Contracts** (`unity_scripts/Director/AdaptiveDirectorClient.cs`):
  - Added matching `[Serializable]` classes: `SectorConfig`, `ConnectionConfig`, `RoomGraphManifest`, `GuardEntityConfig`, `TargetBossConfig`.
  - Extended `MissionManifest` with `public RoomGraphManifest room_graph;`.
  - *As-built addition:* Added `RoomGraphManifest.GetSector(string sectorId)` helper method for O(1) lookups.
- [x] **Console Logging & Verification** (`AdaptiveDirectorClient.cs`):
  - Added `LogRoomGraph()` logging every sector, module, center coords, props, and connection statuses.
  - *As-built addition:* Added data-loss warnings if sector count != 9 or connection count != 12.

---

### Phase 3: Unity Procedural Sector Builder -- Architecture (C#) -- [COMPLETED]
*Goal: Unity constructs the physical static architecture (ground, walls, roads, buildings) of the 9 sectors at Edit time.*

- [x] **New Editor Script** (`unity_scripts/Editor/ProceduralSectorBuilder.cs`):
  - Built with `#if UNITY_EDITOR` using modern Unity 6-compatible APIs (`AssetDatabase`, `SpriteRenderer`, `BoxCollider2D`, `Rigidbody2D`).
  - *As-built addition:* Added **7 Menu Items** instead of 1:
    - `AdaptiveWorld > Build Map 2 -- Sector Graph > From Live or Cached Manifest`
    - 5 archetype offline test builders: `Test - GHOST`, `Test - WRECKING_BALL`, `Test - SPEEDRUNNER`, `Test - AGGRESSOR`, `Test - BALANCED`
    - `Clear`
  - *As-built addition:* Embedded `TestManifests` static class containing complete offline test manifests for all 5 archetypes directly in C#, allowing instant previewing in Unity without the Python server running.
- [x] **Coordinate Mapping**:
  - Pre-mapped 9 cells on 60×45 canvas (cols: -20, 0, +20; rows: +15, 0, -15; cell size: 20×15).
- [x] **Module Constructors**:
  - Implemented 9 module builders (7 specialized + 2 filler):
    - `BuildWarehouseInterior`: 14×10 perimeter walls with dual door gaps, interior partition wall, crate clusters.
    - `BuildFortifiedPlaza`: open asphalt ground, 4 sandbag/barrier bunkers, central plaza marker.
    - `BuildSlumAlleyways`: dark alley ground on Layer 9 (Alley), tenement facades, cross-alley gaps.
    - `BuildUrbanHighway`: 4-lane road on Layer 8 (Road), sidewalks, streetlight pole markers.
    - `BuildIndustrialDepot`: industrial ground, container stack cover.
    - `BuildWaterfrontDocks`: pier ground, solid non-traversable water hazard boundary.
    - `BuildSecurityCompound`: concrete bunker walls on Layer 10 (Building) with guarded south gate.
    - `BuildOpenStreet`, `BuildVacantLot`: generic connectors.
- [x] **Inter-Sector Gateways**:
  - Evaluates all 12 orthogonal adjacent sector pairs.
  - *As-built addition:* Automatic orientation solver (`sideBySide` check) properly sizing horizontal vs vertical gates.
  - Places `open` (clear), `barricaded_wire_fence` (40 HP), `barricaded_concrete` (100 HP + Kinematic RB), or `walled_off` (solid Building wall).
- [x] **Role Markers** (*As-built addition*):
  - Automatically drops visual `RoleMarker_SPAWN`, `RoleMarker_TARGET`, and `RoleMarker_EXTRACTION` badges over the designated sectors for instant Editor hierarchy readability.

---

### Phase 4: Dynamic Entities & Mission Flow (C#) -- [COMPLETED]
*Goal: Populate the sectors with Viktor, bodyguards, patrol guards, barrels, vehicles, and the extraction trigger.*

- [x] **Fix `EnemyPatrol.Die()`** — Added `using AdaptiveWorld2D.Mission;` and the boss-death wire:
   ```csharp
   if (isTargetBoss) MissionManager.Instance?.OnTargetEliminated();
   ```

- [x] **Fix `EnemyDetection.obstacleLayer`** — Updated `CreateProceduralGuard()` and `CreateProceduralBoss()` in `WorldManifestApplier.cs`:
   ```csharp
   det.obstacleLayer = LayerMask.GetMask("Building", "Interactable");
   det.playerLayer   = LayerMask.GetMask("Default");
   ```

- [x] **Fix `BurnableCar.Explode()` AOE** — Replaced unmasked `OverlapCircleAll` with targeted layer masks: `enemyLayerMask` for enemy damage, `interactableLayerMask` for wall/barrel damage, and a 2.5× noise-alert radius for `AlertToNoise()`. Player can no longer be hit.

- [x] **`ApplyManifest()` upgraded** — Now orchestrates all Phase 4 sub-systems via `room_graph`:
   - `ApplyTarget(manifest.target, rg)` — sector-aware; resolves `target_sector` center from `rg`
   - `ApplyGuards(manifest.guards, rg)` — sector-distributed across non-target sectors
   - `ApplyTacticalProps(manifest.routes, rg)`
   - `ApplyPlayerSpawn(rg)`
   - `ApplyExtractionZone(rg)`

- [x] **Target & Escort Spawning** — `ApplyTarget()` reads `room_graph.target_sector` center; `SpawnViktorBodyguards()` places 2 personal bodyguards flanking Viktor at ±1.8 units with elevated stats (80 HP, 25% resistance, 8-unit vision, 1.8× reaction).

- [x] **Guard Patrol Placement** — `ApplyGuards()` has two paths:
   - **Path A (room_graph present):** Iterates all non-target sectors; uses explicit per-sector `SectorConfig.guards` entities if defined; auto-distributes guard budget evenly otherwise. `SpawnSectorGuard()` sets sector-bound waypoints (±4 units from center, ±1.5 Y stagger). Sentry role uses tighter ±1.5 unit range.
   - **Path B (no room_graph):** Legacy scatter fallback preserved.

- [x] **Tactical Props & Hazards** — `ApplyTacticalProps()` + `SpawnFuelBarrel()` + `SpawnBurnableCar()`:
   - Fuel barrels (20 HP `DestructibleWall`) in every `INDUSTRIAL_DEPOT` and `WAREHOUSE_INTERIOR` sector (2 per sector, offset ±3 units from center)
   - Burnable cars (`BurnableCar`) in `URBAN_HIGHWAY` and `FORTIFIED_PLAZA` sectors when `routes.cars_available == true`

- [x] **Player Spawn** — `ApplyPlayerSpawn()`: teleports Player tag object to `player_spawn_sector` center; zeros `Rigidbody2D.linearVelocity`.

- [x] **Extraction Zone** — `ApplyExtractionZone()`: creates procedural green `MissionZoneTrigger(MissionEnd)` at `extraction_sector` center. Starts `SetActive(false)`. `WaitAndEnableExtractionZone()` coroutine polls every 0.5 s; when `MissionManager.missionCompleted == true` (triggered by Viktor's death), zone enables and flashes bright green.

- [x] **Verification**:
   - Play Map 2: guards patrol their sectors, Viktor is in his sector, extraction zone is reachable only after Viktor dies

---

### Phase 5: Atmospheric Lighting & Visual Polish
*Goal: Enhance top-down visual feel — the core lighting and weather systems are already implemented; this phase adds the remaining streetlight work and validates all visual states.*

> [!NOTE]
> `WorldManifestApplier.ApplyEnvironment()` **already implements**: GlobalLight2D intensity + color by time-of-day, rain particle system (`EnsureRainSystem()`), fog particle system (`EnsureFogSystem()`), and screen-space atmosphere overlay (`EnsureAtmosphereOverlay()`). Steps 1 and 2 below are the remaining new work only.

- [x] **Streetlights** — in `SectorRuntimeBuilder` & `StreetlightController`, added to `BuildUrbanHighway()` and `BuildFortifiedPlaza()` (and slum alleys):
   - `StreetlightController.cs` placed on poles across highways (every 8 units), plaza corners, and alleys
   - Color: warm amber (`#FFCC66`, `Color(1, 0.8, 0.4)`) for highway, cool blue-white (`Color(0.75, 0.88, 1)`) for plaza, flickering amber for slum alleys
   - Radius: 4.5–6.5 units; intensity and state dynamically modulated by `manifest.environment.ambient_light` and `time_of_day` via `SetEnvironmentLighting()`

- [x] **Map 2 Surface Zones** — fixed Alley/Building surface coverage in Map 2:
   - `BuildSlumAlleyways()` ground collider is on **Layer 9 (Alley)** so `PlayerController.DetectSurface()` reports `"ALLEY"` in alley sectors
   - `BuildWarehouseInterior()` and `BuildSecurityCompound()` interior floors are on **Layer 10 (Building)** so surface reports `"BUILDING_INTERIOR"`
   - Streets, plazas, highways, lots, piers, and depots report `"ROAD"`

- [x] **Verification**:
   - GHOST manifest (fog/night): scene shows deep midnight blue tint, fog particles, dim GlobalLight2D, guard vision scaled down
   - WRECKING_BALL manifest (rain/dusk): amber tint, rain particles, streetlights brightly lit
   - SPEEDRUNNER manifest (clear/day): bright GlobalLight2D, streetlights dimmed, no weather overlays
   - Walking into alley sector triggers `DetectSurface()` reporting `surface = ALLEY` in movement telemetry

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
