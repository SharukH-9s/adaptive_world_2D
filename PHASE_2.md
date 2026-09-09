# Phase 2 — The Gemini LLM Narrative Director & World Evolution

> **Project:** AdaptiveWorld2D  
> **Phase:** 2 — LLM-Driven Emergent Narrative & Dynamic Level Generation  
> **Core Principle:** Natural Gameplay Evolution — No dialogue trees or menus. The player just plays, and Gemini autonomously evolves the narrative, objectives, and map architecture around their behavioral fingerprint.

---

## 1. Executive Vision & Philosophy

In Phase 1, our system proved that player telemetry from Map 1 could be collected by Unity, analyzed by Python, and used to dynamically adjust Map 2 using deterministic heuristics.

In **Phase 2**, we replace the static heuristic solver with an **LLM Narrative Director (Google Gemini API)**:
1. **The Game Knows the Story Spine:** A 10-node authored narrative arc covering the rise and fall of an underworld outlaw.
2. **The AI Knows the World Ground Truth:** Map 1 is fully mapped in precise detail (exact coordinates, asset stats, sightlines, patrol paths, destructible objects).
3. **The Player Plays Freely:** The player explores, fights, sneaks, or drives without interruption.
4. **The LLM Deduces the Persona:** By cross-referencing *what the player did* against *what was on Map 1*, Gemini extracts the player's true psychological playstyle.
5. **Emergent Next Chapter:** Gemini authoritatively writes the narrative briefing, radio dialogue, mission objectives, and the complete spatial manifest for Map 2 using the authorized Asset Catalog.

---

## 2. The 10 Story Spine Nodes

The fixed overarching narrative arc follows the journey of a desperate individual rising to the top of the criminal underworld and facing the inevitable consequences.

```
┌────────────────────────────────────────────────────────────────────────┐
│                        THE 10 STORY SPINES                             │
└──────────────────────────────────┬─────────────────────────────────────┘
                                   │
   [Node 01: Rock Bottom] ─────────┴────────► [Node 02: First Blood]
     Survival in slums, escape,                 First contracted underworld hit.
     discovering black market.                  
                   │                                          │
                   ▼                                          ▼
   [Node 03: Arms & Leverage] ◄────────────── [Node 04: The Syndicate]
     Raiding an armory; choosing                Recruiting specialists (Hacker,
     heavy vs silent arsenal.                   Muscle, Wheelman).
                   │                                          │
                   ▼                                          ▼
   [Node 05: Turf Conquest] ────────────────► [Node 06: The Transit Ambush]
     Eliminating the rival gang                 Intercepting an armored cash
     controlling city districts.                transport on highway.
                   │                                          │
                   ▼                                          ▼
   [Node 07: The Grand Vault] ◄────────────── [Node 08: State of Emergency]
     The central federal bank heist;            Federal task force & SWAT
     the pinnacle score.                        lock down the entire city.
                   │                                          │
                   ▼                                          ▼
   [Node 09: The Betrayal] ─────────────────► [Node 10: The Last Stand]
     Safehouse compromised by an                The unavoidable final fight;
     inside leak; desperate breakout.           legacy & epitaph generated.
```

### Node Detailed Specifications

| Node ID | Node Title | Narrative Objective | What Gemini Decides for this Node |
|---|---|---|---|
| `NODE_01` | **Rock Bottom** | Survive the slum streets, purchase first weapon, reach extraction. | Fixed baseline map for all players; provides the behavioral telemetry baseline. |
| `NODE_02` | **First Blood** | First formal contract from the underworld. | Decides whether it's a silent assassination or a loud, destructive enforcer hit based on Node 1 playstyle. |
| `NODE_03` | **Arms & Leverage** | Infiltrate or assault an armory to acquire high-grade equipment. | Decides security layout, whether guards are heavily armored, and item rewards (silenced vs heavy). |
| `NODE_04` | **The Syndicate** | Recruit key underworld crew members (Hacker, Muscle, Driver). | Decides who is in danger and needs rescuing based on the player's tactical weaknesses. |
| `NODE_05` | **Turf Conquest** | Take over rival gang territory across warehouses or commercial blocks. | Decides enemy faction behavior (aggressive rushers vs defensive snipers on rooftops). |
| `NODE_06` | **The Transit Ambush** | Rob an armored transport en route across city avenues. | Decides vehicle barricades, choke points, and police escort response times. |
| `NODE_07` | **The Grand Vault** | Infiltrate the Central Federal Bank and breach the primary vault. | Dynamically arranges vault security (laser/camera stealth bypass vs C4 brute-force breach routes). |
| `NODE_08` | **State of Emergency** | Escape a citywide manhunt orchestrated by SWAT and federal agents. | Populates streets with roadblocks, snipers, spotlights, and roaming cruisers. |
| `NODE_09` | **The Betrayal** | Ambushed in safehouse; fight way out through betrayed allies. | Creates close-quarters combat layout with destructible interior cover and vehicle extraction. |
| `NODE_10` | **The Last Stand** | The climactic final battle against overwhelming task force numbers. | Generates the arena layout, final boss enforcers, and writes the player's custom legacy epilogue. |

---

## 3. Master Asset Catalog (Abilities, Stats, Prefabs)

Gemini is strictly constrained to this catalog. It cannot invent assets Unity does not possess.

### 3.1 Actors & Humanoids

| Asset ID | Sprite / Prefab | Category | HP | Speed | Detection / Vision | Damage / Weapon | Special Behaviors |
|---|---|---|---|---|---|---|---|
| `actor_player` | `hitman1_stand.png` | Player | 100 | Walk: 3.5<br>Run: 6.0 | 360° Player FOV | Melee: 40<br>Pistol: 35<br>SMG: 25 | Can enter cars, take cover, kick open doors. |
| `actor_cop_patrol` | `Man Blue` | Law Enforcement | 60 | 3.0 | 8m cone (70°) | Revolver: 20<br>(0.8s cooldown) | Blows whistle on detection; alerts all allies within 15m radius. |
| `actor_cop_swat` | `Soldier 1` | Heavy Law | 120 | 2.5 | 12m cone (85°) | SMG: 25<br>(0.2s cooldown) | Kevlar Armor (50% bullet damage reduction); seeks cover before firing. |
| `actor_civilian` | `Woman Green`<br>`Man Brown` | Neutral | 25 | Walk: 2.0<br>Panic: 4.5 | 6m radius | None | Panics upon hearing gunshots or car explosions; runs toward nearest police. |
| `actor_informant` | `Man Old` | Neutral / Quest | 40 | 1.8 | 5m radius | None | Stationary NPC; unlocks mission intel, access codes, or secret alleys on interact. |
| `actor_gang_thug` | `Survivor 1` | Hostile / Gang | 70 | 3.8 | 9m cone (75°) | Shotgun: 50<br>Melee: 30 | Aggressive flanker; rushes player position if player stays in cover too long. |

### 3.2 Vehicles

| Asset ID | Sprite / Prefab | Category | HP | Top Speed | Explosion Damage | Special Behaviors |
|---|---|---|---|---|---|---|
| `veh_police_cruiser` | `car_blue_1` + Siren | Law Vehicle | 180 | 11.0 m/s | 60 (3.0m radius) | Sirens attract civilians & cops; spawns 2 `actor_cop_patrol` when engaged. |
| `veh_sports_car` | `car_red_1` | Civilian Fast | 120 | 14.5 m/s | 75 (3.5m radius) | Fast acceleration, lightweight, flammable after 80 damage. |
| `veh_civilian_sedan` | `car_black_1` | Civilian Standard| 150 | 9.0 m/s | 50 (2.5m radius) | Reliable waist-high bullet cover; becomes burnt wreck at 0 HP. |
| `veh_armored_truck` | `truck_white_1` | Heavy Transport | 350 | 6.5 m/s | 110 (5.0m radius)| Bulletproof glass; requires explosives or heavy gunfire to disable. |

### 3.3 Obstacles, Structures & Hazards

| Asset ID | Sprite / Prefab | Category | HP | Destructible? | Bullet Pass? | Tactical Function |
|---|---|---|---|---|---|---|
| `struct_building_brick` | `vector_tiles.svg` | Structure | $\infty$ | No | No | Completely blocks movement, vision, and projectiles. Creates city streets and alleys. |
| `barrier_concrete` | `barrier_red.png` | Cover | 100 | Yes (to rubble) | No | Waist-high cover; stops cars and absorbing bullets until broken. |
| `hazard_barrel_fuel` | `barrel_red_down.png`| Hazard | 20 | Yes (Explodes) | Triggers on hit | Explodes with 90 damage in 3.0m radius; chains to nearby barrels or cars. |
| `fence_wire_gate` | `barrier_white.png` | Barrier | 50 | Yes | Yes (Bullets) | Blocks walking/driving, but player and enemies can see and shoot through it. |

### 3.4 Interactive Items & Objective Anchors

| Asset ID | Sprite / Prefab | Purpose |
|---|---|---|
| `trigger_gun_shop` | `weapon_gun.png` marker | Arms purchase zone; triggers armed hitman state. |
| `trigger_extraction` | `arrow_yellow.png` beacon | Level completion trigger; calculates final telemetry batch. |
| `item_cash_briefcase` | `barrel_white.png` | Optional secondary loot pickup (+rep, +underworld credits). |
| `console_alarm_box` | Wall terminal | Can be destroyed or interacted with to disable area reinforcements. |

---

## 4. Map 1 Ground Truth Blueprint ("Top to Bottom")

For Gemini to accurately correlate player behavior, it must know the exact environment. Below is the ground truth layout serialized for the LLM:

```
[Y: +20] ┌─────────────────────────────────────────────────────────────┐
         │ NORTH INDUSTRIAL SECTOR                                     │
         │ • Warehouse A ([-15, 12] to [-5, 20])                       │
         │ • Warehouse B ([5, 12] to [15, 20])                         │
         │ • Barriers: barrier_n1 [-3, 4], barrier_n2 [0, 4], n3 [3, 4]│
         │ • Guard: actor_cop_swat patrolling between [-5, 10] & [5, 10]│
[Y:  +6] ├─────────────────────────────────────────────────────────────┤
         │ CENTRAL BOULEVARD (Asphalt Highway, Y: -3 to +3)            │
         │ • Sports Car Red at [-4, 2]                                 │
         │ • Civilian Sedan Blue at [4, -2]                            │
         │ • Cop Cruiser cruising between [-15, 0] and [15, 0]         │
         │ • Civilians wandering sidewalks (Y = +3.5 and Y = -3.5)     │
[Y:   0] │ SPAWN: [0, 0]  |  GUN SHOP: [8, 0]  |  EXTRACTION: [18, 0]  │
[Y:  -6] ├─────────────────────────────────────────────────────────────┤
         │ SOUTH SLUM & ALLEYWAYS                                      │
         │ • Narrow alleyways with brick walls                         │
         │ • 2 Fuel Barrels at [-6, -8] and [2, -10]                   │
         │ • 1 Gang Thug waiting in alleyway [-8, -6]                  │
         │ • Wire fence enclosing hidden rear route to extraction      │
[Y: -20] └─────────────────────────────────────────────────────────────┘
         [-25]                                                     [+25]
```

### The Ground Truth Data Contract (`map1_ground_truth.json`):

```json
{
  "map_id": "MAP_01_SLUM_EXODUS",
  "dimensions": { "min_x": -25, "max_x": 25, "min_y": -20, "max_y": 20 },
  "key_locations": {
    "player_spawn": [0, 0],
    "underground_gun_shop": [8, 0],
    "extraction_exit": [18, 0]
  },
  "tactical_choke_points": [
    { "id": "choke_north_barriers", "pos": [0, 4], "guarded_by": "actor_cop_swat" },
    { "id": "choke_main_street", "pos": [12, 0], "guarded_by": "veh_police_cruiser" },
    { "id": "choke_south_alley", "pos": [-4, -8], "hazard": "hazard_barrel_fuel" }
  ],
  "registered_assets": [
    { "id": "car_sports_red", "type": "veh_sports_car", "pos": [-4, 2], "hp": 120 },
    { "id": "car_sedan_blue", "type": "veh_civilian_sedan", "pos": [4, -2], "hp": 150 },
    { "id": "barrier_01", "type": "barrier_concrete", "pos": [-3, 4], "hp": 100 },
    { "id": "barrier_02", "type": "barrier_concrete", "pos": [0, 4], "hp": 100 },
    { "id": "barrier_03", "type": "barrier_concrete", "pos": [3, 4], "hp": 100 },
    { "id": "cop_01", "type": "actor_cop_patrol", "pos": [12, 2], "route": [[12, 2], [6, 2]] },
    { "id": "swat_01", "type": "actor_cop_swat", "pos": [0, 10], "route": [[-5, 10], [5, 10]] },
    { "id": "civ_01", "type": "actor_civilian", "pos": [-2, 3], "wander": true },
    { "id": "civ_02", "type": "actor_civilian", "pos": [6, -3], "wander": true },
    { "id": "barrel_01", "type": "hazard_barrel_fuel", "pos": [-6, -8], "hp": 20 }
  ]
}
```

---

## 5. Telemetry & Psychological Profile Deduction

When the player completes Map 1, Unity dispatches telemetry. Because Gemini has the Ground Truth Blueprint above, it extracts meaning:

```
                  ┌───────────────────────────────┐
                  │    Map 1 Ground Truth Schema  │
                  └───────────────┬───────────────┘
                                  │
┌───────────────────────────┐     │      ┌──────────────────────────────────┐
│  Unity Player Telemetry   │ ────┼────► │     Gemini Cognitive Engine      │
│  (Hits, Kills, Path, Time)│     │      │ (Correlates actions vs geometry) │
└───────────────────────────┘     │      └────────────────┬─────────────────┘
                                  │                       │
                                  ▼                       ▼
                     [PLAYER PROFILING ARTIFACT]
                     • Playstyle: Aggressive Rusher
                     • Collateral: High (Blew up civilian sedan)
                     • Tactical: Low (Bypassed cover entirely)
                     • Preferred Weapon: Shotgun / Melee
```

### Archetype Deduction Matrix:

| Telemetry Signature | Geometric Context from Map 1 | Gemini's Deduced Archetype |
|---|---|---|
| `shots_fired > 25`, `kills_police >= 2`, `walls_broken >= 2` | Confronted SWAT head-on at North Barriers; destroyed barricades. | **Juggernaut / Enforcer** |
| `shots_fired == 0`, `time_in_alleyways > 70%`, `detection_count == 0` | Avoided main boulevard; snuck through South Alleys behind wire fence. | **Ghost / Silent Infiltrator** |
| `vehicle_drive_time > 80%`, `completion_time < 25s` | Stole Sports Car at `[-4, 2]`; sped down highway ignoring gun shop. | **Wheelman / Speedrunner** |
| `barrels_detonated >= 1`, `accuracy > 80%`, `health_lost < 20` | Shot explosive barrel at `[-6, -8]` to eliminate thug from distance. | **Tactical Marksman** |

---

## 6. Gemini Director API Contract (Map 2 Generation)

### The Prompt Architecture
1. **System Prompt:** Rules, safety bounds, available asset catalog, and story milestone context.
2. **Input Context:**
   * Story Progress: Node 1 $\rightarrow$ Target: Node 2 (*First Blood*).
   * Map 1 Ground Truth Blueprint.
   * Player Telemetry & Action Log.
3. **Task:** Generate a cohesive narrative transition and a fully realized spatial layout for Map 2 in valid JSON.

### Output JSON Schema (What Gemini Returns to Unity):

```json
{
  "node_id": "NODE_02",
  "node_title": "First Blood",
  "narrative_context": {
    "briefing_title": "The Syndicate's Audition",
    "story_synopsis": "The bloodbath you unleashed on 5th Avenue didn't go unnoticed. Don Falcone's enforcers know you have no fear of the badge. They need someone with heavy hands to clean out a rival debt-collector.",
    "radio_chatter_start": "Falcon sends his regards. Put these loan sharks in the dirt, and you're in the family.",
    "radio_chatter_objective": "Clear the warehouse floor! Leave nobody standing!",
    "radio_chatter_extract": "Clean sweep. Van is waiting around the corner."
  },
  "mission_rules": {
    "primary_objective": {
      "type": "ELIMINATE_ALL_ENEMIES",
      "target_count": 4
    },
    "secondary_objective": {
      "type": "DESTROY_ASSETS",
      "target_id": "veh_sports_car",
      "bonus_cash": 1000
    },
    "time_limit_seconds": 180
  },
  "map_manifest": {
    "environment_theme": "WAREHOUSE_DISTRICT",
    "grid_bounds": { "min_x": -30, "max_x": 30, "min_y": -20, "max_y": 20 },
    "player_spawn": [-22, -12],
    "extraction_zone": [24, 14],
    "structures": [
      { "type": "struct_building_brick", "rect": [-10, 5, 20, 10] },
      { "type": "barrier_concrete", "pos": [-15, -4], "rotation": 0 },
      { "type": "barrier_concrete", "pos": [-15, 0], "rotation": 0 },
      { "type": "hazard_barrel_fuel", "pos": [2, 7] },
      { "type": "hazard_barrel_fuel", "pos": [5, 7] }
    ],
    "vehicles": [
      { "id": "target_getaway_car", "type": "veh_sports_car", "pos": [18, 12], "is_target": true }
    ],
    "actors": [
      {
        "id": "thug_enforcer_01",
        "type": "actor_gang_thug",
        "pos": [-5, 6],
        "behavior": "PATROL",
        "waypoints": [[-5, 6], [5, 6]],
        "aggression": 0.9
      },
      {
        "id": "thug_enforcer_02",
        "type": "actor_gang_thug",
        "pos": [0, -2],
        "behavior": "GUARD_STATIC",
        "vision_range": 10
      },
      {
        "id": "swat_reinforcement",
        "type": "actor_cop_swat",
        "pos": [10, 10],
        "behavior": "AMBUSH_ON_ALERT"
      }
    ]
  }
}
```

---

## 7. Implementation Roadmap for Phase 2

```
┌────────────────────────────────────────────────────────────────────────┐
│ PHASE 2 IMPLEMENTATION PIPELINE                                        │
└──────────────────────────────────┬─────────────────────────────────────┘
                                   │
  [Step 1: Ground Truth Datasets]  ├─► Create `backend/data/map1_blueprint.json`
                                   │   Create `backend/data/asset_catalog.json`
                                   │
  [Step 2: Gemini API Integration] ├─► Build `backend/gemini_director.py`
                                   │   Integrate `google-genai` SDK
                                   │   Set structured JSON response schemas
                                   │
  [Step 3: Server Bridge Endpoint] ├─► Add `POST /director/next-node` in FastAPI
                                   │   Accepts telemetry -> calls Gemini -> returns Node 2
                                   │
  [Step 4: Unity Manifest Applier] ├─► Extend `WorldManifestApplier.cs` to instantiate
                                   │   roads, structures, hazards, and dynamic actor routes
                                   │
  [Step 5: End-to-End Verification]├─► Play Map 1 -> Stream telemetry ->
                                       Confirm Gemini produces valid, playable Map 2
```

---

## 8. Summary of Principles

* **Player Freedom:** Zero intrusive dialogs or morality pop-ups.
* **LLM as Creative Architect:** Generates missions, dialogues, objectives, and spatial layouts dynamically.
* **Deterministic Engine Safety:** Unity verifies all coordinates and prefabs against the strict Asset Catalog to prevent impossible or broken maps.
