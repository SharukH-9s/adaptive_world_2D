# AdaptiveWorld2D — Unity Quick Setup Guide

This project is a 2D top-down adaptive game where:
1. **Mission 1:** The player navigates through the city to buy a firearm at the Underground Gun Shop and extracts at Point B. The player can walk, sprint, drive cars, destroy walls, burn cars, or take stealthy alleyways.
2. **Mission 2:** The Python Adaptive Director reads the telemetry from Mission 1, classifies the player's playstyle, and reshapes Mission 2 (the assassination target's location, armor, health, guard vision cones/numbers, ambient weather, and road blockages).

---

## 1. Project Directory Structure
All project files are organized in `AdaptiveWorld2D/` on your Desktop:
```
AdaptiveWorld2D/
├── assets/                           # 1,016+ Transparent PNG Game Assets (CC0 Public Domain)
│   ├── kenney_topdown_shooter/
│   │   ├── PNG/
│   │   │   ├── Hitman 1/             # Player / Hitman (hold gun, reload, knife takedown, walk)
│   │   │   ├── Man Blue/             # Alternate player skins
│   │   │   ├── Soldier 1/            # Guard / Armed enemy sprites
│   │   │   ├── Survivor 1/           # Target Boss / Civilian characters
│   │   │   └── Tiles/                # Walls, doors, floors, crates, barrels, destructible objects
│   │   ├── Spritesheet/              # Full unified spritesheet
│   │   └── Tilesheet/                # Unified city & indoor tilesheet
│   └── kenney_vehicles/
│       ├── PNG/Cars/                 # Top-down Sedans, Police cars, Trucks, Sports cars
│       ├── PNG/Tiles/                # Asphalt roads, intersections, sidewalks, grass
│       └── PNG/Objects/              # Barricades, cones, oil slicks, barriers
├── backend/
│   ├── telemetry.py                  # Data models for navigation, combat, destruction
│   ├── profiler.py                   # Classifies Ghost, Wrecking Ball, Speedrunner, Aggressor
│   ├── target_solver.py              # Mission 2 constraint solver
│   ├── server.py                     # FastAPI REST endpoints
│   ├── run_server.py                 # Launcher on port 8001
│   └── test_pipeline.py              # Automated E2E verification test
└── unity_scripts/
    ├── Director/
    │   ├── AdaptiveDirectorClient.cs # REST API bridge to Director (port 8001)
    │   ├── TelemetryReporter.cs      # Central event sender helper
    │   └── WorldManifestApplier.cs   # Dynamically adapts M2 scene
    ├── Player/
    │   ├── PlayerController.cs       # WASD, sprint, drive, surface tracking
    │   ├── PlayerCombat.cs           # Melee, stealth kill, pistol shooting
    │   └── PlayerInteraction.cs      # E (enter/exit car, buy gun), F (burn car, break wall)
    ├── World/
    │   ├── CameraFollow2D.cs         # Top-down camera tracking, rotation stabilization
    │   ├── DestructibleWall.cs       # HP-based wall destruction & rubble
    │   └── BurnableCar.cs            # Drivable car, ignition, explosive blast
    ├── AI/
    │   ├── EnemyPatrol.cs            # Waypoints, patrol/chase, damage resistance
    │   └── EnemyDetection.cs         # Dynamic vision cone & hearing radius
    ├── Mission/
    │   ├── MissionManager.cs         # M1 -> M2 progression & UI briefs
    │   └── MissionZoneTrigger.cs     # Triggers for Gun Shop, Point B, Target
    └── Editor/
        └── AdaptiveSceneBuilder.cs   # 1-click procedural builder for Map 1 & Map 2
```

---

## 2. Launch the Backend Server
Open PowerShell in the backend directory:
```powershell
cd C:\Users\xDDDDD\OneDrive\Desktop\AdaptiveWorld2D\backend
python run_server.py
```
The server will start on `http://127.0.0.1:8001`.

---

## 3. Unity Project Setup (2D)
1. Open **Unity Hub** -> Click **New Project** -> Select **2D (URP)** or **2D Core**.
2. Name it `AdaptiveWorld2D`.
3. In Unity's **Project** panel (under `Assets`), drag and drop:
   - The `assets/` folder (contains all transparent PNG characters, vehicles, tiles).
   - The `unity_scripts/` folder.
4. Because these are clean, transparent `.png` files, Unity immediately recognizes them as 2D Sprites with true transparency.

---

## 4. Setting Up the Scene
1. **Game Manager GameObject**:
   - Add `AdaptiveDirectorClient` (set `Api Base Url: http://127.0.0.1:8001`).
   - Add `TelemetryReporter`.
   - Add `WorldManifestApplier`.
   - Add `MissionManager`.
2. **Player GameObject**:
   - Add `SpriteRenderer` with your chosen character sprite from `assets/kenney_topdown_shooter/PNG/Hitman 1/`.
   - Add `Rigidbody2D` (Freeze Z Rotation, Gravity Scale 0).
   - Add `CircleCollider2D`.
   - Add `PlayerController`, `PlayerCombat`, and `PlayerInteraction`.
   - Tag as `"Player"`.
3. **World Elements**:
   - **Gun Shop:** Place a building or counter tile with a `BoxCollider2D` (IsTrigger = true) and `MissionZoneTrigger` (Zone Type = `GunShop`).
   - **Extraction Point B:** Place a checkpoint marker with `BoxCollider2D` (IsTrigger = true) and `MissionZoneTrigger` (Zone Type = `MissionEnd`).
   - **Walls:** Add `DestructibleWall` and `BoxCollider2D`.
   - **Cars:** Place car sprites from `assets/kenney_vehicles/PNG/Cars/` with `BurnableCar` and `BoxCollider2D`. Tag with `"Vehicle"`.
4. **Target Anchors (for Mission 2)**:
   - In `WorldManifestApplier`, assign your 4 anchor transforms:
     - `warehouse_interior`
     - `open_plaza`
     - `far_docks`
     - `fortified_building`
5. **Main Camera**:
   - Add `CameraFollow2D` (smoothly follows GameObject tagged `"Player"`, locks rotation upright).

> **Tip:** You can skip manual placement by clicking **AdaptiveWorld > Build Map 1** and **AdaptiveWorld > Build Map 2** in the top Unity menu bar!

---

## 5. Controls
- **W, A, S, D:** Move
- **Left Shift:** Sprint
- **Left Mouse Click:** Melee takedown (knife/fist) or Shoot pistol (once unlocked)
- **E:** Interact (Enter / Exit vehicle, Buy Gun at Gun Shop)
- **F:** Destructive Action (Ignite car to burn/explode, damage wall)
