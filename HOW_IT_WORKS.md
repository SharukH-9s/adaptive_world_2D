# AdaptiveWorld2D — How Everything Works

---

## The Big Picture

This is a 2D top-down game with two missions.
- **Map 1 is always fixed** — it tests how you play.
- **Map 2 adapts to you** — it changes based on how you played Map 1.

The entire system has 3 parts that work together:

| Part | What It Does |
|---|---|
| **Unity Scenes** | The visual game world the player sees and plays |
| **C# Scripts** | The logic (movement, combat, telemetry, scene control) |
| **Python Server** | The AI brain that classifies player behavior and generates Map 2s layout |

---

## Step 1: The Builder Script (Developer Does This Once)

### What is it?
AdaptiveSceneBuilder.cs is a Unity Editor script. It adds a menu item at the top of Unity:
- AdaptiveWorld > Build Map 1
- AdaptiveWorld > Build Map 2

### Why does it exist?
Having C# scripts and PNG assets in folders does nothing by itself. In Unity, GameObjects must exist in the scene with:
- Sprites attached (so you can see them)
- Colliders attached (so things can collide)
- Scripts attached (so they have behavior)
- References wired (e.g. MissionManager needs to know which object is the Player)

Doing all of that manually would take hours and is error-prone. The builder script does it automatically in 1 second.

### What does "Build Map 1" do?
When you click it, it instantly creates the entire Map 1 scene:
- Spawns the Player with all scripts attached (PlayerController, PlayerCombat, PlayerInteraction)
- Creates a FirePoint child on the Player (needed for shooting direction)
- Places 2 drivable/burnable cars on the street
- Places 3 destructible walls blocking shortcuts
- Creates the Underground Gun Shop trigger zone
- Creates the Extraction Point B trigger zone
- Places the GameManager with TelemetryReporter and MissionManager wired up
- Sets all layer masks (Interactable, Enemy, Road, Alley, Building) on all scripts

### What does "Build Map 2" do?
When you click it, it builds the full Map 2 foundation:
- Creates 5 Location Anchors (all inactive by default):
  - warehouse_interior
  - open_plaza
  - far_docks
  - fortified_building
  - mid_street
- Creates 12 Guard Spawn Points scattered across the map
- Creates the Guard Prefab (Soldier sprite + EnemyPatrol + EnemyDetection scripts)
- Creates the Boss Viktor Prefab (starts inactive, waiting to be placed)
- Creates 4 Route Barricades (all inactive by default):
  - main_road_north
  - alley_south
  - highway_east
  - warehouse_front_entrance
- Wires everything into WorldManifestApplier.cs (the script that listens to Python)

KEY POINT: Map 2 is built with ALL possibilities at once. All 5 locations exist.
All 4 barricades exist. Python decides which ones turn ON when you play.

---

## Step 2: Map 1 — The Fixed Diagnostic Test

### What the player sees:
A top-down 2D city street. Multiple paths from spawn to the Gun Shop to Extraction Point B:
- Wide main road (fast, open, risky)
- Narrow back alleys (slower, sneaky)
- Parking lot with cars (drivable, can smash walls)
- Brick walls blocking shortcuts (destructible)

### What the scripts are silently doing:
Every action the player takes is being recorded invisibly by TelemetryReporter.cs:

| Player Action | What Gets Recorded |
|---|---|
| Walking in alley | timeInAlley increases |
| Walking on main road | timeOnRoad increases |
| Pressing E to enter car | isDriving = true, car usage logged |
| Pressing F to burn car | carsBurned count goes up |
| Pressing F to break wall | wallsDestroyed count goes up |
| Firing the pistol | shotsFired count goes up |
| Knife/melee kills | meleeKills count goes up |

### When does Map 1 end?
When the player walks into Extraction Point B.

MissionZoneTrigger.cs detects this and tells MissionManager.cs to:
1. Package all the telemetry data into a JSON report
2. Send it to the Python server via POST /api/telemetry/batch
3. Request a Map 2 layout via POST /api/mission/solve
4. Wait for Pythons response
5. Load the Map 2 scene

---

## Step 3: The Python Server — The AI Brain

### What it does:
It receives your telemetry data and runs it through two scripts:

profiler.py — Classifies your playstyle into one of 4 Archetypes:

| Archetype | Behavior |
|---|---|
| GHOST | Mostly in alleys, no/few kills, quiet |
| WRECKING_BALL | Drove cars, smashed walls, burned things |
| SPEEDRUNNER | Got to Point B very fast, minimal detours |
| AGGRESSOR | High kill count, lots of shooting |

target_solver.py — Based on your archetype, generates a WorldManifest JSON:
- Which of the 5 locations to put Viktor in
- How many guards to spawn and where
- Which barricades to activate (blocking your favorite route)
- Viktors HP and armor level
- Weather and lighting settings

### Example output (for GHOST archetype):
```json
{
  "archetype": "GHOST",
  "target_location": "warehouse_interior",
  "guard_count": 5,
  "barricades_active": ["main_road_north", "warehouse_front_entrance"],
  "target_hp": 121,
  "target_armor": "medium",
  "weather": "foggy_night"
}
```

---

## Step 4: Map 2 — The Adaptive Assassination

### What happens at the first frame of Map 2:
WorldManifestApplier.cs runs in Start() and reads the JSON from Python.
It immediately:

1. Finds the matching Location Anchor — teleports Viktor there and activates him
2. Spawns the correct number of guards from the spawn points near Viktors location
3. Activates the correct barricades — blocking the players favorite route from Map 1
4. Sets guard vision cone sizes — wider if you were aggressive, narrower if you were sneaky
5. Sets Viktors HP and armor from the manifest
6. Adjusts the lighting — dark/night for Ghost, bright/day for Aggressor

### What the player experiences:
They see a completely different city district, with Viktor hiding somewhere, guards patrolling,
and some roads blocked. Where Viktor is and how the city is configured is unique to how they
played Map 1.

If they replay from the beginning and play differently in Map 1, Map 2 will be a completely
different experience.

---

## Full Flow Summary

[Developer clicks Build Map 1] -> Map 1 scene created in Unity Editor
[Developer clicks Build Map 2] -> Map 2 scene created with all options (all inactive)
[Developer adds both scenes to Build Settings]

(Now the developer never touches these again)

[Player presses Play in Unity]
  |
  v
[Map 1 loads — fixed city]
[Player plays their own way — TelemetryReporter records everything]
[Player reaches Extraction Point B]
  |
  v
[MissionManager sends telemetry to Python server]
[Python classifies archetype -> generates WorldManifest JSON]
  |
  v
[Map 2 loads]
[WorldManifestApplier reads JSON in frame 1]
[Activates the correct location, guards, barricades, lighting]
  |
  v
[Player plays Map 2 — the world was built around their playstyle]
[Player eliminates Viktor]
  |
  v
[Victory screen]

---

## What YOU (Developer) Need to Do

### One-time setup (before ever pressing Play):
1. Add 5 Layers in Unity: `Interactable`, `Enemy`, `Road`, `Alley`, `Building`
2. Add Tags in Unity: `Enemy`, `Player`, `Vehicle`
3. Ensure the Main Camera in both Map 1 and Map 2 has `CameraFollow2D` attached
4. Run Python server: `python run_server.py` in the `backend/` folder
5. Open Map 1 scene -> Click **AdaptiveWorld > Build Map 1** -> Save (Ctrl+S)
6. Open Map 2 scene -> Click **AdaptiveWorld > Build Map 2** -> Save (Ctrl+S)
7. Add both scenes to **File -> Build Settings** (Map 1 at index 0, Map 2 at index 1)

### Every time you play:
1. Make sure Python server is running
2. Open Map 1 scene
3. Press Play
4. That is it!
