# Adaptive World Engine: Architecture & Game Development Lifecycle

> **Comprehensive Technical Architecture, System Layers, and Development Lifecycle for the Adaptive World Director and Game Engine Integration.**

---

## 1. Executive Summary & Core Paradigm

Traditional video games operate on **static, hard-coded content**: level designers manually craft roads, place enemies, place walls, and script linear triggers. Every player experiences the exact same mission obstacles, regardless of whether they play like a ghost in the shadows or a chaotic demolition expert.

The **Adaptive World Engine** inverts this relationship:
> **The Game Designer authors the space of possibilities and gameplay invariants; the Adaptive Director decides which configuration becomes physical reality at runtime based on the player's unique behavioral profile and the world's physical state.**

To achieve this without reinventing game engines (physics, rendering, input handling, and audio), the system is built on a **Strictly Decoupled Architecture**:

```
┌─────────────────────────────────────────────────────────────────────────┐
│                     FRONT-END: GAME RUNTIME ENGINE                      │
│             (Unity 2D / Godot 4 / Three.js 3D Web Client)               │
│   • Renders graphics & plays audio                                      │
│   • Runs 2D/3D physics, colliders, and destructible assets              │
│   • Runs local enemy patrol, vision cone raycasts, and pathfinding      │
│   • Streams micro-telemetry events asynchronously to the Director       │
│   • Receives declarative Scenario Manifests and alters the scene        │
└────────────────────────────────────┬────────────────────────────────────┘
                                     │ Local HTTP/REST (JSON DTOs)
                                     ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                    BACK-END: ADAPTIVE WORLD DIRECTOR                    │
│                        (FastAPI / Python Service)                       │
│   • Ingests and normalizes player telemetry streams                     │
│   • Classifies player behavior into real-time behavioral archetypes     │
│   • Tracks persistent world state diffs (destroyed walls, bridges, heat)│
│   • Solves mission constraints via Weighted Probability & Graph Solvers │
│   • Serves as the data collection and training pipeline for future AI   │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## 2. Multi-Layer System Architecture

The project is structured into four distinct, modular layers. Each layer has an explicit responsibility and strict boundaries:

```
┌─────────────────────────────────────────────────────────────────────────┐
│ LAYER 1: GAMEPLAY SIMULATION & RUNTIME ENGINE (Unity / Client)         │
│   ├── Player Systems (Movement, Driving, Combat, Inventory)             │
│   ├── World & Environmental Mechanics (Destructible Walls, Explosives)  │
│   ├── AI Perception & Navigation (Vision Cone Raycasts, Dynamic NavMesh)│
│   ├── Telemetry Reporter (Client-side buffer, batching, async dispatch) │
│   └── World Manifest Applier (Scene reconstruction, spawner, weather)   │
└────────────────────────────────────┬────────────────────────────────────┘
                                     │
┌────────────────────────────────────▼────────────────────────────────────┐
│ LAYER 2: ENGINE BRIDGE & COMMUNICATION LAYER                            │
│   ├── REST Client (`AdaptiveDirectorClient.cs` / `.gd` / `.js`)         │
│   ├── Data Transfer Objects (DTOs) & JSON Serialization                 │
│   └── Offline Fallback & Graceful Degradation Handler                   │
└────────────────────────────────────┬────────────────────────────────────┘
                                     │
┌────────────────────────────────────▼────────────────────────────────────┐
│ LAYER 3: ADAPTIVE WORLD DIRECTOR CORE (Decision Engine)                 │
│   ├── Telemetry Ingestion & Filtering (`telemetry.py`, `server.py`)     │
│   ├── Behavioral Profiler & Archetype Engine (`profiler.py`)            │
│   ├── World State & Delta Graph Tracker (`world_state.py`)              │
│   ├── City Graph Navigation & Reachability Engine (`city_graph.py`)     │
│   └── Scenario Orchestrator & Constraint Solver (`target_solver.py`)    │
└────────────────────────────────────┬────────────────────────────────────┘
                                     │
┌────────────────────────────────────▼────────────────────────────────────┐
│ LAYER 4: DATA PIPELINE & AI / MACHINE LEARNING EVOLUTION                │
│   ├── Telemetry Datasets & Replay Harvesting                            │
│   ├── Supervised Behavioral Cloning & RL Reward Models                  │
│   └── Procedural Generation Generative Models (Future AI Upgrade)       │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## 3. Which Part Does What: Detailed Component Breakdown

### Layer 1: Gameplay Simulation & Runtime Engine (Client)
This layer runs at 60 FPS on the player's machine. It handles instant physical and visual feedback:

1. **Player Controller (`PlayerController.cs`):**
   * Processes player inputs (WASD, sprint, interact, vehicle enter/exit).
   * Calculates movement velocities and tracks the player's ground surface type (`ROAD`, `ALLEY`, `BUILDING_INTERIOR`, `OFFROAD`).
2. **Player Combat System (`PlayerCombat.cs`):**
   * Manages melee takedowns (stealth knife attacks from behind) and ranged gunfire (pistols, rifles).
   * Emits acoustic alerts in a circular radius when noisy weapons fire, alerting nearby AI guards.
3. **Interactive & Destructible Environment:**
   * **Destructible Wall (`DestructibleWall.cs`):**
     * **Intact State:** `BoxCollider2D` active, blocks physical movement, projectiles, and enemy line-of-sight raycasts.
     * **Damage Thresholds:** Swaps visual sprites when HP < 50%.
     * **Breach State:** At HP $\le$ 0, disables the collider, swaps sprite to ground rubble decal, triggers debris particles, updates navigation mesh to walkable, and calls `TelemetryReporter.Instance.ReportDestruction("WALL_BREAK", wallId)`.
   * **Burnable Vehicle (`BurnableCar.cs`):**
     * Drivable asset with physics; can be ignited to trigger a 3-second fuse followed by an Area-of-Effect (AOE) explosion, destroying adjacent walls and eliminating nearby enemies.
4. **Enemy AI & Perception (`EnemyPatrol.cs`, `EnemyDetection.cs`):**
   * **Vision Cones:** Projects 2D raycasts in a forward arc. If a ray hits an `Obstacle` layer (wall), vision is blocked; if it hits the player without obstruction, the guard enters `Alert/Pursuit`.
   * **Dynamic Pathfinding (NavMesh2D / A* Grid):** Routes guards toward points of interest. When a wall breaks, the nav grid flags the node as walkable, allowing guards to path through the breach.
5. **Telemetry Reporter (`TelemetryReporter.cs`):**
   * Collects gameplay micro-events (movement speeds, weapon choices, stealth kills, walls broken).
   * Buffers them in memory and dispatches them in asynchronous HTTP batches to prevent framerate hitching.
6. **World Manifest Applier (`WorldManifestApplier.cs`):**
   * Runs when entering a new mission or loading an adaptive scenario.
   * Parses the declarative `scenario_manifest.json` received from the Director:
     * Teleports or spawns the Target Boss at the solver-determined coordinates.
     * Injects boss attributes: HP, armor multiplier, weapon loadout.
     * Configures guard vision range, field-of-view angle, and movement speed.
     * Activates physical obstacles (concrete barriers, roadblocks) or environmental effects (rain, fog opacity).

---

### Layer 2: Engine Bridge & Communication Layer
Provides an engine-agnostic communication interface over standard HTTP/REST:

* **Engine Adapters Available:**
  * **Unity (C#):** `engine_adapters/unity/AdaptiveDirectorClient.cs`
  * **Godot 4 (GDScript):** `engine_adapters/godot/AdaptiveDirectorClient.gd`
  * **Web/Three.js (JavaScript):** `engine_client/adaptive_client.js`
* **Core API Endpoints:**
  * `POST /api/telemetry/event` & `POST /api/telemetry/batch`: Streams player telemetry events.
  * `POST /api/mission/solve`: Requests an adaptive scenario manifest for a specific mission ID.
  * `GET /api/player/profile`: Retrieves current behavioral metrics and archetype classification.
  * `GET /api/world/state`: Retrieves the global delta graph (destroyed nodes, heat levels).
  * `POST /api/mission/complete`: Reports mission outcome, compounding consequences into future missions.
* **Fail-Safe & Graceful Degradation:**
  * If the Director backend is unreachable or times out (> 1.5s), the client engine falls back to an embedded deterministic default scenario manifest, ensuring the game never freezes or crashes.

---

### Layer 3: Adaptive World Director Core (Python / FastAPI Backend)
The analytical brain of the architecture. It runs decoupled from the rendering loop:

```
Telemetry Stream ──► Ingestion Buffer ──► Profiler ──► Behavioral Vector ──┐
                                                                           ▼
World Mod Events ──► World State Diff ──► Graph State ──────────► [Scenario Solver]
                                                                           ▲
Mission Invariants ────────────────────────────────────────────────────────┘
                                                                           │
                                                                           ▼
                                                             Scenario Manifest (JSON)
```

1. **Telemetry Ingestor (`core/telemetry.py`):**
   * Validates and parses incoming events into typed telemetry models:
     * `NavigationSample`: Velocity, duration, distance, surface type (`ROAD`, `ALLEY`, `BUILDING_INTERIOR`).
     * `CombatEvent`: Engagement style (`STEALTH_TAKEDOWN`, `GUNFIGHT`, `EVASION`), weapon type, distance, lethality.
     * `WorldModEvent`: Structural impact (`WALL_BREAK`, `CAR_BURN`, `BRIDGE_COLLAPSE`).
2. **Behavioral Profiler (`core/profiler.py`):**
   * Converts raw numbers into normalized behavioral scores $[0.0, 1.0]$:
     * **Stealth Ratio:** $\frac{\text{Stealth Takedowns}}{\text{Total Engagements}}$
     * **Aggression Index:** Weighted measure of combat duration, close-range gunfights, and kill rate.
     * **Destruction Index:** Property damage count, wall breaches, and vehicle burn events.
     * **Pacing / Speed Index:** Percentage of movement spent sprinting or driving versus walking.
   * Classifies the player into one of five primary **Archetypes**:
     * `GHOST`: High stealth, low alert time, minimal property damage, prefers alleys/interiors.
     * `WRECKING_BALL`: High destruction index, frequent explosive use, rams walls, causes heavy noise.
     * `SPEEDRUNNER`: High velocity, bypasses combat encounters, optimal direct navigation.
     * `AGGRESSOR`: High kill count, direct frontal gunfights, medium-to-high noise.
     * `BALANCED`: Mixed tactics with no single dominant extreme.
3. **World State & Delta Tracker (`core/world_state.py`):**
   * Maintains a persistent graph of what has changed in the world:
     * Permanently destroyed bridges, gates, or walls.
     * Law enforcement / syndicate heat index.
     * Faction alert levels.
4. **City Navigation Graph (`core/city_graph.py`):**
   * Models the urban environment as an interconnected topological graph:
     * Nodes: Intersections, buildings, alleys, bridges, rooftops, safehouses.
     * Edges: Physical routes with associated distance, capacity, and risk weights.
   * **Reachability & Solvability Verifier:** When a player destroys a wall or blows up a bridge, the graph recalculates paths using Dijkstra's algorithm. If all main routes are severed, the graph automatically synthesizes emergency fallback routes (e.g., sewage bypass, service docks) to prevent soft-locking the game.
5. **Scenario Orchestrator & Constraint Solver (`core/scenario_solver.py`, `missions/`):**
   * Uses a **Weighted Probability (Biased Dice)** model to generate the mission:
     1. **Hard Invariants (Contracts):** Guarantees that narrative rules are preserved (e.g., target boss must be beatable, mission must have at least 2 viable escape paths).
     2. **Distribution Biasing:** Weights encounter variables against the player profile:
        * *If player is a GHOST:* Boss retreats to an inner vault equipped with motion sensors, security cameras, and narrow guard patrols; thick fog and rain shroud the exterior.
        * *If player is a WRECKING_BALL:* Boss reinforces exterior walls with indestructible steel barriers, sets up explosive tripwires along former breach points, and equips guards with heavy armor and shotguns.
     3. **Stochastic Roll:** Rolls a seed-driven distribution so replays feel organic rather than identical.
     4. **Output Manifest:** Serializes the result into a clean `scenario_manifest.json` sent to the engine.

---

### Layer 4: AI & Machine Learning Evolution (Future Horizon)
The modular architecture was specifically engineered to allow seamless replacement of the heuristic rule solver with trained machine learning models:

* **Current State (Heuristic Weighted Solver):** Fast, predictable, 100% bug-free, guaranteed invariant adherence.
* **Phase 2 (Supervised Learning / Imitation):** As players play through the game, their telemetry sessions and resulting difficulty curves are recorded into training datasets.
* **Phase 3 (Reinforcement Learning / Generative Director):**
  * An RL model (e.g., trained via PPO) acts as the Director.
  * **Objective Function (Reward):** Maximize player engagement (tension curve, close calls, flow state) while maintaining narrative invariants.
  * The model outputs dynamic scene manifests directly into the same REST contract without requiring any code changes in the Unity engine client.

---

## 4. End-to-End Game Development Lifecycle

Building a game using this adaptive architecture follows a structured 6-phase engineering lifecycle:

```
┌─────────────────────────────────────────────────────────────────────────┐
│ PHASE 1: GAMEPLAY INVARIANTS & STORY CONTRACT SPECIFICATION             │
│   • Define story beats that must never break (e.g., Target must die)    │
│   • Identify variable parameters (Boss location, armor, guard vision)   │
└────────────────────────────────────┬────────────────────────────────────┘
                                     │
┌────────────────────────────────────▼────────────────────────────────────┐
│ PHASE 2: ENGINE PROTOTYPING & CORE MECHANICS                            │
│   • Build player controller, 2D physics, destructibles, combat, AI      │
│   • Create greybox map with multiple viable routes (roads, alleys)      │
└────────────────────────────────────┬────────────────────────────────────┘
                                     │
┌────────────────────────────────────▼────────────────────────────────────┐
│ PHASE 3: TELEMETRY SCHEMA & EVENT INSTRUMENTATION                       │
│   • Hook telemetry calls into weapon fire, collisions, movement, kills  │
│   • Establish batching and non-blocking transport                       │
└────────────────────────────────────┬────────────────────────────────────┘
                                     │
┌────────────────────────────────────▼────────────────────────────────────┐
│ PHASE 4: DIRECTOR IMPLEMENTATION & SOLVER VALIDATION                    │
│   • Write Python profiler, city graph, and scenario solver              │
│   • Build automated mock tests (verify 100% solvability across all archetypes) │
└────────────────────────────────────┬────────────────────────────────────┘
                                     │
┌────────────────────────────────────▼────────────────────────────────────┐
│ PHASE 5: ENGINE-TO-DIRECTOR INTEGRATION & MANIFEST APPLIER              │
│   • Drop in `AdaptiveDirectorClient.cs` and `WorldManifestApplier.cs`   │
│   • Test Mission 1 (playstyle observation) → Mission 2 (adaptation)     │
└────────────────────────────────────┬────────────────────────────────────┘
                                     │
┌────────────────────────────────────▼────────────────────────────────────┐
│ PHASE 6: AUTOMATED REGRESSION TESTING & LIVE DATA COLLECTION            │
│   • Run automated autopilot tests across GHOST, WRECKING_BALL, etc.    │
│   • Log datasets for training the next-generation AI model              │
└─────────────────────────────────────────────────────────────────────────┘
```

### Phase 1: Contract Specification
* Developers separate gameplay into **Fixed Invariants** (e.g., "The target is located in District B; the player must have at least 1 weapon") and **Fluid Variants** (e.g., "Number of bodyguards: 3 to 12; Weather: Clear to Heavy Storm; Entry Points: Roof vs Front Door vs Breached Wall").

### Phase 2: Core Mechanics & Greyboxing
* Build the physical mechanics in Unity:
  * Character movement, shooting, stealth takedowns, vehicle driving.
  * Destructible objects (walls with `DestructibleWall.cs`, cars with `BurnableCar.cs`).
  * Guard AI with vision cone raycasting.

### Phase 3: Telemetry Instrumentation
* Add telemetry triggers at key interaction moments:
  * When a wall breaks $\to$ `ReportDestruction("WALL_BREAK")`.
  * When a guard dies silently $\to$ `ReportCombat("STEALTH_TAKEDOWN")`.
  * Every 5 seconds $\to$ Flush movement distance and average velocity.

### Phase 4: Director & Heuristic Verification
* Build and unit-test the Python backend before touching the engine:
  * Run synthetic player profiles (`automated_game_test.py`).
  * Validate that the graph reachability algorithm never produces an impossible mission layout.

### Phase 5: Scene Manifest Application
* In Unity, write `WorldManifestApplier.cs` to read the incoming JSON and update the game scene:
  * Instantiate barricades on specified coordinates.
  * Adjust `EnemyDetection` parameters (e.g., increase vision cone angle from $60^\circ$ to $110^\circ$).
  * Set camera post-processing and particle rain systems.

### Phase 6: Automated Testing & Dataset Harvesting
* Run automated client test scripts to simulate different playthroughs.
* Store telemetry and manifest pairings in SQLite/JSONL for future AI training.

---

## 5. Concrete File & Directory Mapping: AdaptiveWorld2D

Here is how the **AdaptiveWorld2D** project is structured across the Unity engine, Python backend, and pixel assets:

```
AdaptiveWorld2D/
├── README_UNITY_SETUP.md            # Step-by-step Unity 2D setup & import guide
├── ARCHITECTURE.md                  # Master architecture & technical specification
│
├── backend/                         # LAYER 3: ADAPTIVE WORLD DIRECTOR (FastAPI, Port 8001)
│   ├── server.py                    # REST API: /api/telemetry/batch, /api/mission/solve, /api/player/profile
│   ├── run_server.py                # Server launcher script (port 8001)
│   ├── telemetry.py                 # Telemetry models: NavigationSample, CombatEvent, DestructionEvent
│   ├── profiler.py                  # Behavioral profiler: Archetypes (GHOST, WRECKING_BALL, etc.)
│   ├── target_solver.py             # M2 Adaptive Solver: Boss location, armor, guard vision, rain/fog
│   └── test_pipeline.py             # Automated E2E verification test suite (100% pass)
│
├── unity_scripts/                   # LAYER 1 & 2: UNITY 2D SCRIPTS (Drop into Assets/Scripts/)
│   ├── Director/                    # Engine Bridge & Scene Manifest Applier
│   │   ├── AdaptiveDirectorClient.cs# REST client communicating with port 8001
│   │   ├── TelemetryReporter.cs     # Singleton buffering & flushing movement/combat/destruction
│   │   └── WorldManifestApplier.cs  # Injects M2 manifest (boss stats, guard FOV, weather, barricades)
│   ├── Player/                      # Player Controls & Interaction
│   │   ├── PlayerController.cs      # Top-down WASD, walk/sprint/drive, surface detection
│   │   ├── PlayerCombat.cs          # Melee stealth knife takedowns, pistol shooting, sound alert radius
│   │   └── PlayerInteraction.cs     # E key (enter/exit car, buy gun), F key (ignite car, damage wall)
│   ├── World/                       # Destructibles, Environment & Camera Tracking
│   │   ├── CameraFollow2D.cs        # Smooth top-down tracking, rotation lock, tag auto-detection
│   │   ├── DestructibleWall.cs      # Intact/damaged/rubble states, collider toggle, debris, reports WALL_BREAK
│   │   └── BurnableCar.cs           # Drivable car, ignition fuse, explosive AOE damage, reports CAR_BURN
│   ├── AI/                          # Enemy Perception & Patrol
│   │   ├── EnemyPatrol.cs           # Waypoint patrolling, chase on alert, HP & damage resistance
│   │   └── EnemyDetection.cs        # 2D vision cone raycasting against obstacles, hearing radius
│   ├── Mission/                     # Mission Objectives & Trigger Volumes
│   │   ├── MissionManager.cs        # M1 (Buy Gun -> Extraction) -> queries Director -> M2 (Kill Target)
│   │   └── MissionZoneTrigger.cs    # 2D trigger volume for gun shop, extraction, and target zones
│   └── Editor/                      # Custom Unity Editor Tools
│       └── AdaptiveSceneBuilder.cs  # 1-click procedural builder for Map 1 & Map 2 foundation
│
└── assets/                          # 1,016 Transparent CC0 Pixel Art Assets (Kenney)
    ├── kenney_topdown_shooter/      # 587 PNGs: Hitman, Guards, Soldiers, Civilians, Tiles, Weapons
    │   ├── PNG/Hitman 1/            # Player character sprites (gun, hold, knife, reload)
    │   ├── PNG/Soldier 1/           # Guard / enemy sprites
    │   ├── PNG/Survivor 1/          # Target Boss / Civilian sprites
    │   └── PNG/Tiles/               # Walls, doors, floors, crates, barrels, destructible objects
    └── kenney_vehicles/             # 429 PNGs: Cars, motorcycles, road tiles, objects
        ├── PNG/Cars/                # Top-down sedans, police cars, trucks, sports cars
        ├── PNG/Tiles/               # Asphalt roads, intersections, sidewalks, grass
        └── PNG/Objects/             # Barricades, cones, oil slicks, barriers
```

---

## 6. Summary: The Complete Runtime Flow

When a player launches the game and plays through Mission 1 into Mission 2:

1. **Player Plays Mission 1:** They choose how to reach the gun store (e.g., sneaking through alleys vs. driving a truck through three brick walls).
2. **Real-time Telemetry:** The client engine batches these actions and sends them to `http://127.0.0.1:8001/api/telemetry/batch`.
3. **Behavioral Profiling:** The Director analyzes the stream and assigns an Archetype (e.g. `WRECKING_BALL`) while logging the destroyed wall coordinates.
4. **Mission 2 Trigger:** The player steps into the Mission 2 start trigger. The client calls `POST /api/mission/solve`.
5. **Constraint Solving:** The Director checks narrative invariants, evaluates the destroyed walls, biases the odds against the player's brute-force tendencies, verifies path reachability, and returns a tailored `scenario_manifest.json`.
6. **Dynamic World Adaptation:** The client's `WorldManifestApplier` consumes the manifest: the target boss receives reinforced armor, previously breached walls are fortified with steel plates, and guards set up an ambush pointed at entry corridors.
7. **The Result:** The game feels alive, intelligent, and reactive to how the player actually plays, without requiring hundreds of hand-scripted alternate levels.

