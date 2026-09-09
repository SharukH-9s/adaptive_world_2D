# AdaptiveWorld2D — Complete System Guide & Walkthrough

> A comprehensive, presentation-ready guide to explaining the AdaptiveWorld2D architecture, data flow, and gameplay adaptation to friends, colleagues, and technical interviewers.

---

## 1. The 30-Second Elevator Pitch

> *"In traditional games like Hitman, GTA, or Hotline Miami, the missions are static—every player encounters the exact same enemy placements, obstacles, and routes regardless of playstyle.*
> 
> *In **AdaptiveWorld2D**, the game actively studies how you play. While you complete Mission 1, the engine silently streams your behavioral telemetry to an **AI Director microservice written in Python**. The Director analyzes your psychological profile (Ghost, Wrecking Ball, Speedrunner, or Aggressor) and **dynamically constructs Mission 2** specifically to counter and challenge your personal playstyle!"*

---

## 2. The 4-Layer Architecture (Software Engineering Perspective)

The system is designed with strict decoupling between the **game engine runtime** and the **AI decision service**:

```
┌────────────────────────────────────────────────────────────────────────┐
│  LAYER 1: DATA MODELS & DTOs (The Contract between Unity & Python)      │
└───────────────────────────────────┬────────────────────────────────────┘
                                    │
┌───────────────────────────────────▼────────────────────────────────────┐
│  LAYER 2: PRESENTATION & GAME SIMULATION RUNTIME (Unity 2D / C#)        │
└───────────────────────────────────┬────────────────────────────────────┘
                                    │ HTTP REST (Port 8001 / JSON)
┌───────────────────────────────────▼────────────────────────────────────┐
│  LAYER 3: ENGINE BRIDGE & CLIENT NETWORK BUS (C# Event Dispatcher)     │
└───────────────────────────────────┬────────────────────────────────────┘
                                    │
┌───────────────────────────────────▼────────────────────────────────────┐
│  LAYER 4: BACKEND DIRECTOR SERVICE (FastAPI / Behavioral Brain)        │
└────────────────────────────────────────────────────────────────────────┘
```

---

### Layer 1: Data Models & DTOs (The Shared Contract)
Defines the language spoken between the front-end game and the back-end AI:
* **Python (`backend/telemetry.py` & `target_solver.py`):**
  * `NavigationSample`, `CombatEvent`, `DestructionEvent`.
  * `TargetConfig`, `GuardConfig`, `EnvironmentConfig`, `RouteConfig`, and `M2Manifest`.
* **Unity C# (`TelemetryReporter.cs` & `WorldManifestApplier.cs`):**
  * Mirror `[Serializable]` C# structs that serialize and deserialize cleanly using Unity's native `JsonUtility`, avoiding manual string building and escaping errors.

---

### Layer 2: Presentation & Simulation Layer (Unity 2D Engine)
The interactive 2D top-down sandbox where the player moves and acts:
* **Player Systems (`PlayerController.cs`, `PlayerCombat.cs`, `PlayerInteraction.cs`):**
  * WASD movement, sprint, stealth knife takedowns, pistol shooting, driving vehicles, entering/exiting cars.
* **World Physics (`DestructibleWall.cs`, `BurnableCar.cs`):**
  * Brick walls that can be breached; drivable sedans/sports cars that can be driven or ignited with **F** for explosive area damage.
* **AI Perception (`EnemyPatrol.cs`, `EnemyDetection.cs`):**
  * Enemy soldiers with dynamic 2D vision cones that raycast against obstacles and hearing radii triggered by gunshots or footsteps.
* **Camera Stabilization (`CameraFollow2D.cs`):**
  * Smooth target tracking that auto-locks to the `"Player"` tag and fixes rotation to `Quaternion.identity` so the camera stays upright when the player spins to aim.

---

### Layer 3: Engine Bridge & Network Bus (Client Networking)
The glue that makes Unity talk to Python without stalling the game:
* **Event Logger (`TelemetryReporter.cs`):**
  * An invisible singleton that buffers player actions in memory (e.g., time spent in back alleys vs. main roads, wall breaks, car fires, melee kills).
* **Network Client (`AdaptiveDirectorClient.cs`):**
  * A persistent singleton (`DontDestroyOnLoad`) that handles asynchronous HTTP communication with `http://127.0.0.1:8001`.
* **Scene Reconstructor (`WorldManifestApplier.cs`):**
  * Runs on frame 1 of Map 2. It parses the Director's JSON manifest and physically commands the scene GameObjects.

---

### Layer 4: Backend Director Service (Python / FastAPI Brain)
The independent decision-making microservice:
1. **REST Controller (`server.py`):**
   * High-performance FastAPI endpoints: `/api/telemetry/batch`, `/api/player/profile`, `/api/mission/solve`.
2. **Behavioral Profiler (`profiler.py`):**
   * Normalizes raw metrics into 4 normalized mathematical ratios ($0.0 \rightarrow 1.0$):
     * **Stealth Ratio:** Time in alleys, lack of alarms, melee-to-gunshot ratio.
     * **Destruction Index:** Walls demolished, vehicles ignited/blown up.
     * **Aggression Score:** Total kills, shots fired, loud combat duration.
     * **Speed Index:** Average velocity and time-to-objective.
3. **Constraint Solver (`target_solver.py`):**
   * The rule-based expert system that takes the player's profile and synthesizes the adapted Mission 2 layout.

---

## 3. Deep Dive: Director Solver vs. Scene Applier

A common point of confusion is who does what between Python and Unity. Use this **Architect vs. Builder** analogy:

```
┌────────────────────────────────────────────────────────┐
│  Python: target_solver.py                              │
│  "THE ARCHITECT" (Decides what should happen)          │
└──────────────────────────┬─────────────────────────────┘
                           │ Outputs: JSON Blueprint
                           ▼
┌────────────────────────────────────────────────────────┐
│  Unity: WorldManifestApplier.cs                        │
│  "THE BUILDER" (Actually makes it physical in the game)│
└────────────────────────────────────────────────────────┘
```

| Feature | 🧠 Director Solver (`target_solver.py`) | 🔨 Scene Applier (`WorldManifestApplier.cs`) |
|---|---|---|
| **Environment** | Python / FastAPI backend (Port 8001) | Unity 2D / C# Game Engine |
| **Analogy** | **The Architect** (Designs the blueprint) | **The Construction Crew** (Builds the scene) |
| **Input** | Numerical telemetry stats (kills, speed, destruction) | The JSON manifest string received over HTTP |
| **Output** | A declarative JSON string (`M2Manifest`) | Active GameObjects, colliders, and visual effects |
| **What it knows** | Math, probabilities, game design rules, constraints | GameObjects, Sprites, Transforms, Colliders, Cameras |
| **What it CANNOT do** | Cannot render sprites, move cameras, or toggle colliders | Does not calculate player archetypes or solve constraint math |

### Concrete Data Hand-Off Example:

1. **Python (`target_solver.py`)** decides:
   ```json
   {
     "target_location": "warehouse_interior",
     "target_hp": 140,
     "target_armor": "medium",
     "guard_count": 5,
     "weather": "foggy_night",
     "barricades_active": ["main_road_north"]
   }
   ```
2. **Unity (`WorldManifestApplier.cs`)** receives that JSON and executes:
   * Finds the `warehouse_interior` anchor in the scene and teleports Boss Viktor to its position.
   * Sets Viktor's `EnemyPatrol.currentHealth = 140`.
   * Activates 5 guard GameObjects near the warehouse and expands their vision cone range.
   * Turns on the `BoxCollider2D` and Sprite for the `main_road_north` roadblock.
   * Changes `Camera.main.backgroundColor` to midnight blue and turns on the fog particle system.

---

## 4. The 4 Player Archetypes

The Behavioral Profiler classifies the player into one of 4 archetypes (or `BALANCED` if mixed):

| Archetype | How the Player Played in Map 1 | How Map 2 Adapts to Challenge Them |
|---|---|---|
| 🥷 **GHOST** | Sneaked through dark alleys, avoided open roads, zero alarms, quiet knife kills | Boss Viktor retreats into a tight, labyrinthine warehouse; guards have long vision cones; exterior shrouded in thick night fog; alleys are congested |
| 🚜 **WRECKING BALL** | Smashed brick walls, drove vehicles into obstacles, set cars on fire | Viktor equips reinforced heavy armor in an open plaza; exterior roads are blocked by barricades; guards carry heavy weapons |
| ⚡ **SPEEDRUNNER** | Bypassed detours, sprinted straight from spawn to Gun Shop to Point B at high speed | Viktor is placed at the furthest dockyard; vehicle routes are severed; timed barricades force on-foot navigation |
| 💥 **AGGRESSOR** | Loud frontal gunfights, high kill count, rapid pistol usage | Viktor retreats into a locked-down fortified building with maximum HP; highest guard count (8–14 units) with fast reaction times and heavy armor |
| ⚖️ **BALANCED** | Mixed tactics with no single extreme | Baseline encounter: Viktor patrols mid-street with standard medium armor; all roads and alleys remain open with zero barricades |

---

## 5. End-to-End Walkthrough: A Concrete Playthrough Story

Walk your audience through this real scenario to make the whole concept click:

```
[Player in Map 1] 
       │  Plays as a "Wrecking Ball"
       │  • Jumps into a sedan, rams through 2 destructible brick walls
       │  • Presses 'F' on a car to ignite it, triggering an explosive blast
       │  • Reaches Extraction Point B
       ▼
[TelemetryReporter.cs]
       │  Flushes buffered events to backend:
       │  POST http://127.0.0.1:8001/api/telemetry/batch
       ▼
[server.py & profiler.py]
       │  Calculates scores:
       │  • destruction_index = 0.60
       │  • aggression = 0.40
       │  • stealth_ratio = 0.00
       │  Result: Player is classified as WRECKING_BALL
       ▼
[target_solver.py]
       │  Solves level constraints:
       │  • Target: Open Plaza (exposed), Reinforced Armor, 220 HP
       │  • Guards: 8 units, heavy armor
       │  • Barricades: Block 'main_road_north'
       │  • Weather: Clear Day (full visibility)
       ▼
[WorldManifestApplier.cs in Map 2]
          Frame 1: Reads JSON, teleports Viktor, spawns 8 armored guards,
          and raises concrete barricades across the main road.
          The player now experiences a custom level built to counter them!
```

---

## 6. Architecture Component Reference Table

| Component | Language / Tool | Key File | Primary Responsibility |
|---|---|---|---|
| **Physics & Controls** | Unity 2D (C#) | [`PlayerController.cs`](file:///c:/Users/xDDDDD/OneDrive/Desktop/AdaptiveWorld2D/unity_scripts/Player/PlayerController.cs) | Top-down movement, sprint, vehicle driving |
| **Combat & Takedowns** | Unity 2D (C#) | [`PlayerCombat.cs`](file:///c:/Users/xDDDDD/OneDrive/Desktop/AdaptiveWorld2D/unity_scripts/Player/PlayerCombat.cs) | Knife takedowns, pistol shooting, alert noise |
| **Environmental Objects** | Unity 2D (C#) | [`DestructibleWall.cs`](file:///c:/Users/xDDDDD/OneDrive/Desktop/AdaptiveWorld2D/unity_scripts/World/DestructibleWall.cs), [`BurnableCar.cs`](file:///c:/Users/xDDDDD/OneDrive/Desktop/AdaptiveWorld2D/unity_scripts/World/BurnableCar.cs) | Collidable/destructible elements that feed telemetry |
| **Perception AI** | Unity 2D (C#) | [`EnemyDetection.cs`](file:///c:/Users/xDDDDD/OneDrive/Desktop/AdaptiveWorld2D/unity_scripts/AI/EnemyDetection.cs) | Vision cone raycasting and hearing triggers |
| **Camera Tracking** | Unity 2D (C#) | [`CameraFollow2D.cs`](file:///c:/Users/xDDDDD/OneDrive/Desktop/AdaptiveWorld2D/unity_scripts/World/CameraFollow2D.cs) | Smooth camera follow with rotation stabilization |
| **Telemetry Buffer** | Unity 2D (C#) | [`TelemetryReporter.cs`](file:///c:/Users/xDDDDD/OneDrive/Desktop/AdaptiveWorld2D/unity_scripts/Director/TelemetryReporter.cs) | Memory buffer for player telemetry events |
| **Scene Builder** | Unity Editor (C#) | [`AdaptiveSceneBuilder.cs`](file:///c:/Users/xDDDDD/OneDrive/Desktop/AdaptiveWorld2D/unity_scripts/Editor/AdaptiveSceneBuilder.cs) | 1-click procedural scene generation for Maps 1 & 2 |
| **REST Server** | Python / FastAPI | [`server.py`](file:///c:/Users/xDDDDD/OneDrive/Desktop/AdaptiveWorld2D/backend/server.py), [`run_server.py`](file:///c:/Users/xDDDDD/OneDrive/Desktop/AdaptiveWorld2D/backend/run_server.py) | Microservice listening on port 8001 |
| **Behavior Profiler** | Python | [`profiler.py`](file:///c:/Users/xDDDDD/OneDrive/Desktop/AdaptiveWorld2D/backend/profiler.py) | Mathematical playstyle classifier (4 archetypes) |
| **Director Solver** | Python | [`target_solver.py`](file:///c:/Users/xDDDDD/OneDrive/Desktop/AdaptiveWorld2D/backend/target_solver.py) | Heuristic constraint solver generating Map 2 manifest |
| **Scene Applier** | Unity 2D (C#) | [`WorldManifestApplier.cs`](file:///c:/Users/xDDDDD/OneDrive/Desktop/AdaptiveWorld2D/unity_scripts/Director/WorldManifestApplier.cs) | Translates manifest JSON into physical Unity GameObjects |

---

## 7. Quick Talking Points (Cheat Sheet)

When presenting this project, hit these 5 core points in order:

1. **The Hook:** We built a 2D top-down game with an external AI Director microservice that personalizes the level dynamically based on your playstyle.
2. **Decoupled Architecture:** Unity handles rendering, physics, and gameplay simulation; Python handles analytics, profiling, and level constraint solving.
3. **Type-Safe Communication:** They talk over local HTTP using serialized JSON DTOs mirrored in both C# and Python dataclasses.
4. **The Behavioral Engine:** The Python profiler calculates normalized metrics for stealth, aggression, destruction, and speed to classify the player into one of 4 archetypes.
5. **Deterministic Level Adaptation:** The Director outputs a declarative JSON blueprint, which the Unity Scene Applier executes on frame 1 to adapt boss location, armor, guard alerts, weather, and barricaded routes.
