# Implementation Plan: AdaptiveSceneBuilder (Two-Scene Setup)

## What was analyzed

Every script was read before finalizing this plan. The contracts derived are:

| Source File | Key Contract |
|---|---|
| `MissionManager.cs` | Needs `playerCombat` + `playerController` refs; `zoneType = GunShop` and `MissionEnd` |
| `MissionZoneTrigger.cs` | Collider2D must be **IsTrigger** + Player tag must be `"Player"` |
| `PlayerInteraction.cs` | Uses **`interactableLayer`** for `E` key (cars, zones) and `F` key (walls, cars) |
| `PlayerCombat.cs` | Uses **`enemyLayer`** for melee + pistol raycast. `firePoint` child transform needed |
| `EnemyPatrol.cs` | Needs `Rigidbody2D`, at least 1 `waypoint` Transform to patrol; `gravityScale = 0` |
| `EnemyDetection.cs` | Needs `obstacleLayer` + `playerLayer` for vision cone raycast |
| `WorldManifestApplier.cs` | Needs exact `locationId` strings from `target_solver.py` + `guardPrefab` + `targetPrefab` |
| `target_solver.py` | Location IDs: `warehouse_interior`, `open_plaza`, `far_docks`, `fortified_building`, `mid_street`; route IDs: `main_road_north`, `alley_south`, `highway_east`, `warehouse_front_entrance` |
| `BurnableCar.cs` | Tagged `"Vehicle"`, needs `BoxCollider2D` on `interactableLayer`; `carId` must be unique |
| `DestructibleWall.cs` | Needs `BoxCollider2D` (solid, not trigger); `wallId` must be unique for telemetry |
| `AdaptiveDirectorClient.cs` | Calls `/api/mission/solve` with `SolveRequest{seed}` |
| `CameraFollow2D.cs` | Auto-finds by `"Player"` tag; no manual wiring needed |

---

## Open Questions / Decisions Resolved

> [!IMPORTANT]
> **5 required Unity Layers must be set up first in Unity's Project Settings:**
> - `Interactable` — for cars, walls, mission triggers (used by `PlayerInteraction`)
> - `Enemy` — for guards and boss (used by `PlayerCombat` raycast + melee)
> - `Road` — for surface detection (used by `PlayerController.DetectSurface`)
> - `Alley` — for surface detection
> - `Building` — for surface detection (maps to `BUILDING_INTERIOR`)
>
> **Without these layers set, interaction (E/F), shooting, and surface telemetry will silently fail.**

> [!NOTE]
> **BALANCED archetype** uses `location_id = "mid_street"` which is a 5th anchor added to `WorldManifestApplier`.
>
> **Guards need waypoints**: `EnemyPatrol.Patrol()` returns early if `waypoints.Count == 0`. The scene builder creates 2 waypoint empties per guard and wires them in.
>
> **Map 2 Builder builds the Foundation**: All 5 location anchors and all 4 barricades are created initially inactive. At runtime in Play mode, `WorldManifestApplier.cs` reads the Python manifest and activates only the relevant layout for your archetype.

---

## Proposed Changes

---

### Component 1: Unity Layer & Tag Setup (Manual, One-Time)

In Unity: **Edit → Project Settings → Tags and Layers**
1. Add Tag: `Enemy`
2. Add 5 User Layers (e.g. slots 6–10):
   ```
   Layer 6:  Interactable
   Layer 7:  Enemy
   Layer 8:  Road
   Layer 9:  Alley
   Layer 10: Building
   ```

---

### Component 2: The Automated Scene Builder

#### [NEW] `Assets/unity_scripts/Editor/AdaptiveSceneBuilder.cs`

This Editor script provides **two separate menu items** under `AdaptiveWorld`:

---

### Action A: `AdaptiveWorld > Build Map 1 (City Exploration & Gun Shop)`
*Run while inside the Map 1 scene:*

1. **Player Setup:**
   * Spawns Hitman player at `(0, 0, 0)`.
   * Attaches `Rigidbody2D` (gravityScale=0, FreezeRotation=true), `CircleCollider2D`.
   * Attaches `PlayerController`, `PlayerCombat`, `PlayerInteraction`.
   * Creates child `FirePoint` transform for pistol muzzle.
   * Auto-configures layer masks (`interactableLayer`, `enemyLayer`, `roadLayer`, `alleyLayer`, `buildingLayer`).
2. **Environment & Obstacles:**
   * Places 2 drivable/burnable cars (`Car_01`, `Car_02`) with `BurnableCar.cs`.
   * Places 3 destructible walls (`Wall_01`, `Wall_02`, `Wall_03`) with `DestructibleWall.cs`.
   * Sets up road and alley surface collider zones.
3. **Mission Triggers:**
   * `GunShop` zone at `(8, 0, 0)` with `MissionZoneTrigger` (`ZoneType.GunShop`, require E key).
   * `ExtractionPointB` zone at `(18, 0, 0)` with `MissionZoneTrigger` (`ZoneType.MissionEnd`, walk-in trigger).
4. **GameManager:**
   * Instantiates `GameManager` with `TelemetryReporter`, `MissionManager`, and `AdaptiveDirectorClient`.
   * Wires `MissionManager.playerController` and `playerCombat`.
   * Creates overlay UI with Objective text and debriefing panel.

---

### Action B: `AdaptiveWorld > Build Map 2 (Adaptive Assassination)`
*Run while inside the Map 2 scene:*

1. **Player Setup:**
   * Spawns Player at Map 2 spawn point with full combat/interaction setup.
2. **5 Target Location Anchors (starts inactive):**
   * `warehouse_interior` at `(-12, 12, 0)`
   * `open_plaza` at `(0, 12, 0)`
   * `far_docks` at `(-20, -10, 0)`
   * `fortified_building` at `(15, 12, 0)`
   * `mid_street` at `(5, 5, 0)`
3. **Dynamic Barricades (starts inactive):**
   * `main_road_north` at `(0, 8, 0)`
   * `alley_south` at `(0, -6, 0)`
   * `highway_east` at `(20, 0, 0)`
   * `warehouse_front_entrance` at `(-12, 8, 0)`
4. **Guards & Boss Prefabs:**
   * Creates 12 Guard spawn positions with patrol waypoints.
   * Creates Guard and Viktor Boss prefabs with `EnemyPatrol` and `EnemyDetection`.
5. **Director Manifest Link:**
   * Wires all 5 anchors, 12 spawners, 4 barricades, and boss prefab into `WorldManifestApplier.cs`.
   * Connects `AdaptiveDirectorClient` to listen for incoming runtime manifests.

---

## Verification Plan

### 1. Build Map 1
* Open Map 1 in Unity → Click **AdaptiveWorld > Build Map 1**.
* Verify Player, Cars, Walls, GunShop, and ExtractionPointB appear in the Hierarchy without errors.

### 2. Build Map 2
* Open Map 2 in Unity → Click **AdaptiveWorld > Build Map 2**.
* Verify all 5 location anchors and barricades appear in the Hierarchy and are correctly linked in `WorldManifestApplier`.

### 3. End-to-End Gameplay Loop
* Start backend server: `python run_server.py`.
* Open Map 1 and hit **Play**:
  * Move with WASD, enter car with `E`, break wall with `F`.
  * Buy gun at Gun Shop with `E`.
  * Walk into Extraction Point B.
* Check console: Telemetry sent to Python -> Python responds with solved manifest -> Map 2 loads.
* Verify Map 2 adapts Viktor's spawn, barricades, and guards according to your playstyle.
