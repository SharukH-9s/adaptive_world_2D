"""
gemini_director.py
------------------
Phase 2 — The Gemini LLM Narrative Director & Dynamic World Evolution.

Responsibilities:
1. Ground Truth Awareness: Ingests map1_ground_truth.json (geometry, sectors, assets).
2. Telemetry Correlation: Cross-references player navigation, combat, and destruction events
   against the physical world truth of Map 1.
3. Psychological Deduction: Deduces player behavioral fingerprint (Stealth, Aggression,
   Destruction, Speed, Collateral Damage).
4. Story Spine Progression: Advances from Node 01 (Rock Bottom) to Node 02 (First Blood)
   along the 10-node underworld narrative arc.
5. Emergent Spatial Manifest: Generates Mission 2 target location, guard disposition,
   environmental atmosphere, and route barricades using the strictly authorized Asset Catalog.
6. Graceful Fallback: Runs offline deterministic heuristics with rich narrative templates
   if GEMINI_API_KEY is not set or network is unreachable.
"""

import os
import json
import random
import logging
from typing import Optional, Any, Dict, List
from dataclasses import asdict

from .telemetry import TelemetryLog, SurfaceType, MovementType, CombatStyle, DestructionType
from .profiler import PlayerProfile
from .target_solver import (
    M2Manifest, TargetConfig, GuardConfig, EnvironmentConfig, RouteConfig,
    RoomGraphManifest, build_room_graph,
    SECTOR_GRID_COORDS, SECTOR_ADJACENCY, SECTOR_MODULES, CONNECTION_STATUSES,
)

logger = logging.getLogger("gemini_director")
logging.basicConfig(level=logging.INFO)

# 10 Story Spine definitions
STORY_SPINES = {
    "NODE_01": {
        "title": "Rock Bottom",
        "description": "Survival in Pauper Streets slums. Purchase first weapon, navigate streets, escape.",
        "location": "Pauper Streets (Slum District)"
    },
    "NODE_02": {
        "title": "First Blood",
        "description": "First contracted underworld hit on syndicate lieutenant Viktor.",
        "location": "Industrial District / Docks / Plaza"
    },
    "NODE_03": {
        "title": "Arms & Leverage",
        "description": "Raiding a fortified armory; choosing heavy vs silenced tactical equipment.",
        "location": "Syndicate Armory Compound"
    },
    "NODE_04": {
        "title": "The Syndicate",
        "description": "Recruiting underworld specialists (Hacker, Muscle, Wheelman) based on playstyle gaps.",
        "location": "Safehouse & Safehouses"
    },
    "NODE_05": {
        "title": "Turf Conquest",
        "description": "Eliminating rival faction controlling commercial blocks and warehouses.",
        "location": "Chinatown & Commercial District"
    },
    "NODE_06": {
        "title": "The Transit Ambush",
        "description": "Intercepting an armored cash transport on the transit highway.",
        "location": "Elevated Expressway & Checkpoints"
    },
    "NODE_07": {
        "title": "The Grand Vault",
        "description": "Infiltrating the Central Federal Bank and breaching the subterranean vault.",
        "location": "Central Federal Bank"
    },
    "NODE_08": {
        "title": "State of Emergency",
        "description": "Escaping a citywide manhunt orchestrated by SWAT and federal agents.",
        "location": "Lockdown City Center"
    },
    "NODE_09": {
        "title": "The Betrayal",
        "description": "Ambushed in the safehouse by an inside leak; desperate combat breakout.",
        "location": "Underground Safehouse"
    },
    "NODE_10": {
        "title": "The Last Stand",
        "description": "The climactic final battle against overwhelming task force numbers; legacy epilogue.",
        "location": "Harbor Terminal / Final Citadel"
    }
}


class GeminiNarrativeDirector:
    """
    Cognitive AI Director powered by Google Gemini API.
    """

    def __init__(self, ground_truth_file: Optional[str] = None, ground_truth_file_map2: Optional[str] = None):
        base_dir = os.path.dirname(os.path.abspath(__file__))
        if ground_truth_file is None:
            ground_truth_file = os.path.join(base_dir, "data", "map1_ground_truth.json")
        if ground_truth_file_map2 is None:
            ground_truth_file_map2 = os.path.join(base_dir, "data", "map2_ground_truth.json")

        self.ground_truth = self._load_ground_truth(ground_truth_file)
        self.ground_truth_map2 = self._load_ground_truth(
            ground_truth_file_map2,
            default={
                "map_name": "Node 02 Sector Grid",
                "dimensions": {"width": 60, "height": 45},
                "sector_grid_coords": SECTOR_GRID_COORDS,
                "sector_adjacency": SECTOR_ADJACENCY,
                "modules": list(SECTOR_MODULES),
                "connection_statuses": list(CONNECTION_STATUSES),
            },
        )
        self.api_key = self._load_api_key()

    def _load_api_key(self) -> str:
        """Loads GEMINI_API_KEY from environment or .env files with multiple fallbacks."""
        # 1. Try python-dotenv on project root and backend folder
        try:
            from dotenv import load_dotenv
            base_dir = os.path.dirname(os.path.abspath(__file__))
            root_dir = os.path.dirname(base_dir)
            load_dotenv(os.path.join(root_dir, ".env"), override=False)
            load_dotenv(os.path.join(base_dir, ".env"), override=False)
        except Exception:
            pass

        key = os.environ.get("GEMINI_API_KEY", "").strip()
        if key:
            return key

        # 2. Manual fallback parser for .env
        base_dir = os.path.dirname(os.path.abspath(__file__))
        candidate_paths = [
            os.path.join(os.path.dirname(base_dir), ".env"),
            os.path.join(base_dir, ".env")
        ]
        for p in candidate_paths:
            if os.path.exists(p):
                try:
                    with open(p, "r", encoding="utf-8") as f:
                        for line in f:
                            line = line.strip()
                            if line and not line.startswith("#") and "=" in line:
                                k, v = line.split("=", 1)
                                if k.strip() == "GEMINI_API_KEY":
                                    val = v.strip().strip('"').strip("'")
                                    if val:
                                        os.environ["GEMINI_API_KEY"] = val
                                        return val
                except Exception:
                    pass

        return ""

    def _load_ground_truth(self, path: str, default: Optional[Dict[str, Any]] = None) -> Dict[str, Any]:
        if os.path.exists(path):
            try:
                with open(path, "r", encoding="utf-8") as f:
                    return json.load(f)
            except Exception as e:
                logger.error(f"[Director] Error loading ground truth: {e}")
        return default or {"map_name": "Pauper Streets", "dimensions": {"width": 60, "height": 40}}

    def generate_node_2(self, profile: PlayerProfile, telemetry: TelemetryLog, seed: Optional[int] = None) -> Dict[str, Any]:
        """
        Generates the Node 02 Manifest and Story Briefing/Debriefing based on Map 1 telemetry.
        Tries Gemini API first; falls back to deterministic heuristic director if unavailable.
        """
        self.api_key = self._load_api_key()
        if self.api_key:
            try:
                manifest_dict = self._call_gemini(profile, telemetry, seed)
                if manifest_dict:
                    return manifest_dict
            except Exception as e:
                logger.warning(f"[Director] Gemini API call failed: {e}. Falling back to heuristic director.")

        return self._generate_fallback(profile, telemetry, seed)

    def _build_telemetry_digest(self, profile: PlayerProfile, telemetry: TelemetryLog) -> Dict[str, Any]:
        """Summarizes the player's run with ground-truth tactical context."""
        surface_dist = {"road": 0.0, "alley": 0.0, "offroad": 0.0}
        vehicle_time = 0.0
        foot_time = 0.0

        for ne in telemetry.navigation_events:
            surf_name = ne.surface.name.lower()
            if surf_name in surface_dist:
                surface_dist[surf_name] += ne.distance_m
            else:
                surface_dist["offroad"] += ne.distance_m

            if ne.movement_type == MovementType.DRIVE:
                vehicle_time += ne.duration_s
            else:
                foot_time += ne.duration_s

        combat_targets: Dict[str, int] = {}
        weapons_used: Dict[str, int] = {}
        for ce in telemetry.combat_events:
            combat_targets[ce.target_type] = combat_targets.get(ce.target_type, 0) + 1
            weapons_used[ce.weapon] = weapons_used.get(ce.weapon, 0) + 1

        destruct_types: Dict[str, int] = {}
        destroyed_objects: List[str] = []
        for de in telemetry.destruction_events:
            dtype = de.destruction_type.name.lower()
            destruct_types[dtype] = destruct_types.get(dtype, 0) + 1
            destroyed_objects.append(de.object_id)

        return {
            "session_duration_s": round(telemetry.session_duration_s, 2),
            "archetype_computed": profile.archetype(),
            "stealth_ratio": round(profile.stealth_ratio, 3),
            "aggression": round(profile.aggression, 3),
            "destruction_index": round(profile.destruction_index, 3),
            "vehicle_preference": round(profile.vehicle_preference, 3),
            "avg_speed": round(profile.avg_speed, 2),
            "total_kills": profile.total_kills,
            "stealth_kills": profile.stealth_kills,
            "walls_destroyed": profile.walls_destroyed,
            "cars_burnt": profile.cars_burnt,
            "distance_by_surface_m": {k: round(v, 2) for k, v in surface_dist.items()},
            "movement_time_s": {"foot": round(foot_time, 2), "vehicle": round(vehicle_time, 2)},
            "targets_neutralized": combat_targets,
            "weapons_used": weapons_used,
            "destroyed_objects": destroyed_objects,
            "destroyed_types": destruct_types
        }

    def _call_gemini(self, profile: PlayerProfile, telemetry: TelemetryLog, seed: Optional[int] = None) -> Optional[Dict[str, Any]]:
        from google import genai
        from google.genai import types

        digest = self._build_telemetry_digest(profile, telemetry)
        gt_summary = {
            "map_name": self.ground_truth.get("map_name", "Pauper Streets"),
            "sectors": self.ground_truth.get("sectors", {}),
            "key_locations": self.ground_truth.get("key_locations", {}),
            "choke_points": self.ground_truth.get("choke_points", [])
        }
        map2_layout = {
            "canvas": self.ground_truth_map2.get("dimensions", {"width": 60, "height": 45}),
            "sector_grid_coords": self.ground_truth_map2.get("sector_grid_coords", SECTOR_GRID_COORDS),
            "sector_adjacency": self.ground_truth_map2.get("sector_adjacency", SECTOR_ADJACENCY),
            "modules": self.ground_truth_map2.get("modules", list(SECTOR_MODULES)),
            "connection_statuses": self.ground_truth_map2.get("connection_statuses", list(CONNECTION_STATUSES)),
        }

        system_instruction = f"""
You are the Cognitive Narrative Director of the game 'AdaptiveWorld2D'.
The game follows a 10-Node Underworld Crime Story Spine.
Node 01 is 'Rock Bottom' (slum survival, purchasing first weapon, escaping to extraction).
Node 02 is 'First Blood' (the player's first formal syndicate contract: eliminate Viktor, a rival lieutenant).

You receive:
1. Map 1 Ground Truth (layout, sectors, choke points, guard patrols, vehicles, destructible fences/walls, fuel barrels).
2. The Player's actual telemetry from Node 01 (kills, combat style, route surfaces, vehicle usage, destruction events).

Your Job:
1. Correlate the player's telemetry against the physical geometry of Map 1:
   - Did they run the main Boulevard, or sneak the South Alleys?
   - Did they steal a vehicle (Sports Car / Police Cruiser) or traverse on foot?
   - Did they blow up fuel barrels and breach concrete walls, or avoid alarms?
   - Did they kill innocent civilians, patrol cops, or rival gang thugs?
2. Deduce their true Psychological Archetype (GHOST, WRECKING_BALL, SPEEDRUNNER, AGGRESSOR, TACTICIAN, or BALANCED).
3. Write a visceral Underworld Handler radio debriefing assessing their performance in Map 1.
4. Advance the story to Node 02 ('First Blood'): write the mission briefing and dynamic objective text.
5. Output the spatial manifest for Map 2 that strategically adapts to challenge or exploit their archetype:
   - Target location must be one of: 'warehouse_interior', 'open_plaza', 'far_docks', 'fortified_building', 'mid_street'.
   - Target armor: 'none', 'light', 'medium', 'heavy', 'reinforced'.
   - Guards count: 3 to 10. Vision: 4.0 to 12.0. Reaction: 0.8 to 2.5.
   - Routes: primary_route ('main_road', 'alley_south', 'highway_east'), blocked_routes (subset of ['main_road_north', 'alley_south', 'highway_east', 'warehouse_front_entrance']), cars_available (boolean).
   - Environment: time_of_day ('day', 'dusk', 'night'), weather ('clear', 'fog', 'rain'), visibility (0.3 to 1.0), ambient_light (0.2 to 1.0).
6. Additionally, output a `room_graph`: a full 9-sector tactical blueprint of Map 2, laid out on the
   3x3 grid supplied to you in MAP 2 SPATIAL GRID below.
   - `sectors` MUST be a JSON ARRAY (not an object) with EXACTLY 9 entries, one per sector id
     (NW, NC, NE, MW, MC, ME, SW, SC, SE), each with its exact `center_x`/`center_y` from the grid.
   - Each sector's `module` must be one of the provided `modules`, chosen to match the archetype's
     narrative (e.g. GHOST favors SLUM_ALLEYWAYS/WAREHOUSE_INTERIOR; WRECKING_BALL favors
     INDUSTRIAL_DEPOT/FORTIFIED_PLAZA; SPEEDRUNNER favors URBAN_HIGHWAY/WATERFRONT_DOCKS;
     AGGRESSOR favors URBAN_HIGHWAY/SECURITY_COMPOUND). Use OPEN_STREET/VACANT_LOT as filler
     for thematically-unassigned sectors.
   - `connections` MUST contain EXACTLY the 12 adjacency pairs listed in `sector_adjacency` below,
     each assigned one of the provided `connection_statuses`.
   - `target_sector`, `player_spawn_sector`, and `extraction_sector` must each be a distinct sector id.
   - CRITICAL INVARIANT: a path using only 'open' or 'barricaded_*' edges (never 'walled_off') MUST
     exist from `player_spawn_sector` to `target_sector`, and from `target_sector` to
     `extraction_sector`. Use 'barricaded_*' statuses for chokepoints on the intended path, and
     reserve 'walled_off' only for edges meant to force the player around.

Return strictly valid JSON conforming to the requested schema.
"""

        prompt = f"""
MAP 1 GROUND TRUTH:
{json.dumps(gt_summary, indent=2)}

MAP 2 SPATIAL GRID (destination -- you must populate this exact 3x3 sector grid):
{json.dumps(map2_layout, indent=2)}

PLAYER RUN TELEMETRY DIGEST:
{json.dumps(digest, indent=2)}

STORY NODE:
Current: NODE_01 (Rock Bottom) -> Next: NODE_02 (First Blood)

Generate the complete Director response in the following JSON format:
{{
  "mission_id": 2,
  "mission_title": "NODE 02: First Blood",
  "player_archetype": "<GHOST|WRECKING_BALL|SPEEDRUNNER|AGGRESSOR|TACTICIAN|BALANCED>",
  "psychological_deduction": "<1-2 sentences analyzing their psychological fingerprint based on Map 1 choices>",
  "narrative_debriefing": "<Handler's debriefing analyzing their Map 1 actions in gritty underworld dialogue>",
  "mission_briefing": "<Handler's mission contract briefing for Node 02>",
  "target": {{
    "name": "Viktor",
    "location_id": "<warehouse_interior|open_plaza|far_docks|fortified_building|mid_street>",
    "location_label": "<Human readable location>",
    "armor": "<none|light|medium|heavy|reinforced>",
    "health": <int 80 to 250>,
    "movement": "<stationary|patrol|erratic>",
    "is_indoors": <bool>
  }},
  "guards": {{
    "count": <int 3 to 10>,
    "vision_range": <float 4.0 to 12.0>,
    "reaction_speed": <float 0.8 to 2.5>,
    "hearing_range": <float 6.0 to 15.0>,
    "armored": <bool>,
    "high_alert": <bool>,
    "patrol_pattern": "<static|active|random>"
  }},
  "environment": {{
    "time_of_day": "<day|dusk|night>",
    "weather": "<clear|fog|rain>",
    "visibility": <float 0.3 to 1.0>,
    "ambient_light": <float 0.2 to 1.0>,
    "ambient_label": "<Short descriptive label>"
  }},
  "routes": {{
    "primary_route": "<main_road|alley_south|highway_east>",
    "primary_route_id": "<main_road|alley_south|highway_east>",
    "primary_label": "<Route Name>",
    "blocked_routes": ["<subset of: main_road_north, alley_south, highway_east, warehouse_front_entrance>"],
    "reinforced_barricades": [],
    "cars_available": <bool>
  }},
  "room_graph": {{
    "sectors": [
      {{
        "id": "<NW|NC|NE|MW|MC|ME|SW|SC|SE>",
        "module": "<one of the 9 modules from MAP 2 SPATIAL GRID>",
        "center_x": <float, must exactly match sector_grid_coords for this id>,
        "center_y": <float, must exactly match sector_grid_coords for this id>,
        "guards": [
          {{ "sector_id": "<same id>", "offset_x": <float -3 to 3>, "offset_y": <float -3 to 3>, "role": "<patrol|sentry|bodyguard>", "armored": <bool> }}
        ],
        "props": ["<fuel_barrel|crate|car>"]
      }}
      // ... exactly 9 entries total, one per sector id, all ids unique and covering the full grid
    ],
    "connections": [
      {{ "from_sector": "<id>", "to_sector": "<adjacent id>", "status": "<one of connection_statuses>" }}
      // ... exactly the 12 pairs listed in sector_adjacency above, no more, no fewer
    ],
    "target_sector": "<sector id where Viktor is located -- should thematically match target.location_id>",
    "player_spawn_sector": "<sector id, distinct from target_sector>",
    "extraction_sector": "<sector id, distinct from player_spawn_sector>"
  }},
  "reasoning": [
    "<reason 1>",
    "<reason 2>"
  ]
}}
"""
        client = genai.Client(api_key=self.api_key)
        response = client.models.generate_content(
            model="gemini-2.5-flash",
            contents=prompt,
            config=types.GenerateContentConfig(
                system_instruction=system_instruction,
                response_mime_type="application/json",
                temperature=0.7
            )
        )

        if response and response.text:
            cleaned_text = response.text.strip()
            manifest_json = json.loads(cleaned_text)
            archetype = manifest_json.get("player_archetype", profile.archetype())
            self._validate_and_repair_room_graph(manifest_json, archetype, seed)
            logger.info(f"[Director] Successfully generated Node 02 via Gemini: Archetype={manifest_json.get('player_archetype')}")
            return manifest_json

        return None

    def _validate_and_repair_room_graph(self, manifest_json: Dict[str, Any], archetype: str, seed: Optional[int]) -> None:
        """Mutates manifest_json in place: ensures a structurally valid `room_graph` is present.
        Gemini output is not guaranteed to be schema-perfect (wrong sector count, broken
        connectivity, hallucinated ids). If validation fails, the entire room_graph is replaced
        with the deterministic handcrafted template for `archetype` so Unity never receives a
        malformed graph."""
        rg = manifest_json.get("room_graph")
        if rg is None:
            errors: List[str] = ["room_graph missing from Gemini response"]
        else:
            probe = RoomGraphManifest(
                sectors=rg.get("sectors", []),
                connections=rg.get("connections", []),
                target_sector=rg.get("target_sector", ""),
                player_spawn_sector=rg.get("player_spawn_sector", ""),
                extraction_sector=rg.get("extraction_sector", ""),
            )
            errors = probe.validate()

        if errors:
            logger.warning(f"[Director] Gemini room_graph invalid ({errors}); repairing with deterministic template.")
            rng = random.Random(seed)
            manifest_json["room_graph"] = build_room_graph(archetype, rng).to_dict()

    def _generate_fallback(self, profile: PlayerProfile, telemetry: TelemetryLog, seed: Optional[int] = None) -> Dict[str, Any]:
        """
        Deterministic, narrative-rich fallback generator when Gemini API is offline.
        """
        archetype = profile.archetype()
        digest = self._build_telemetry_digest(profile, telemetry)
        rng = random.Random(seed)
        room_graph = build_room_graph(archetype, rng).to_dict()

        # Base archetype tailoring
        if archetype == "GHOST":
            location_id = "warehouse_interior"
            location_label = "Warehouse Interior (Tight Corridors)"
            target_armor = "medium"
            target_hp = 140
            indoors = True
            guard_count = 5
            guard_vision = 9.0
            guard_reaction = 1.8
            time_of_day = "night"
            weather = "fog"
            visibility = 0.6
            ambient_light = 0.35
            ambient_label = "Midnight Fog"
            primary_route = "alley_south"
            blocked = ["main_road_north"]
            cars_avail = False
            debrief = (
                "Impressive work in Pauper Streets. You moved like smoke through the southern alleys, "
                "bypassing police checkpoints and keeping collateral at zero. The syndicate appreciates operators who leave no trail."
            )
            brief = (
                "CONTRACT: Viktor has barricaded himself deep inside the industrial warehouse. "
                "He expects hitmen, so guards are trained to detect whispers. Find a breach point and neutralize him silently."
            )
            deduction = "Player exhibits high stealth patience and disciplined avoidance of open sightlines and unnecessary alarms."

        elif archetype == "WRECKING_BALL":
            location_id = "open_plaza"
            location_label = "Open Plaza (Fortified Barricades)"
            target_armor = "reinforced"
            target_hp = 220
            indoors = False
            guard_count = 8
            guard_vision = 7.0
            guard_reaction = 1.5
            time_of_day = "dusk"
            weather = "rain"
            visibility = 0.85
            ambient_light = 0.65
            ambient_label = "Rain-soaked Twilight"
            primary_route = "main_road"
            blocked = ["alley_south", "warehouse_front_entrance"]
            cars_avail = True
            debrief = (
                f"You demolished half of Pauper Streets! {profile.walls_destroyed} walls smashed and {profile.cars_burnt} vehicles torched. "
                "The entire police district is in shock. Loud, catastrophic, but undeniably effective."
            )
            brief = (
                "CONTRACT: Viktor knows you're coming after that boulevard spectacle. He set up concrete barriers and heavy enforcers "
                "in the open plaza. You wanted a warzone? You've got one. Bring heavy caliber."
            )
            deduction = "Player demonstrates destructive momentum, using environmental demolition and explosive hazards to overwhelm obstacles."

        elif archetype == "SPEEDRUNNER":
            location_id = "far_docks"
            location_label = "Docks Perimeter (Far Escape Route)"
            target_armor = "light"
            target_hp = 110
            indoors = False
            guard_count = 6
            guard_vision = 8.5
            guard_reaction = 2.0
            time_of_day = "day"
            weather = "clear"
            visibility = 1.0
            ambient_light = 0.95
            ambient_label = "High Noon Sunlight"
            primary_route = "highway_east"
            blocked = ["highway_east"]
            cars_avail = False  # Cut vehicle advantage
            debrief = (
                f"Clocked through the city in record time (avg speed: {profile.avg_speed:.1f} m/s). "
                "You didn't linger or hesitate. Fast hands, fast feet."
            )
            brief = (
                "CONTRACT: Viktor is preparing to board a freighter at the far docks. We severed his vehicle line, "
                "but you'll have to hunt him down on foot before he escapes across the water."
            )
            deduction = "Player optimizes for rapid completion and velocity, bypassing secondary encounters."

        elif archetype == "AGGRESSOR":
            location_id = "fortified_building"
            location_label = "Fortified Compound"
            target_armor = "heavy"
            target_hp = 200
            indoors = True
            guard_count = 9
            guard_vision = 8.0
            guard_reaction = 1.6
            time_of_day = "night"
            weather = "rain"
            visibility = 0.7
            ambient_light = 0.4
            ambient_label = "Stormy Night"
            primary_route = "main_road"
            blocked = ["alley_south"]
            cars_avail = True
            debrief = (
                f"Body count in the slums: {profile.total_kills}. You confronted every patrol officer and gang sentry head-on. "
                "The underworld knows you don't negotiate."
            )
            brief = (
                "CONTRACT: Viktor locked himself inside his fortified stronghold with armed mercenaries. "
                "He thinks Kevlar and numbers can stop you. Prove him wrong."
            )
            deduction = "Player engages aggressively with lethal firearms, seeking direct confrontation over evasion."

        else:  # BALANCED / TACTICIAN
            location_id = "mid_street"
            location_label = "Central Intersection"
            target_armor = "medium"
            target_hp = 150
            indoors = False
            guard_count = 6
            guard_vision = 7.0
            guard_reaction = 1.2
            time_of_day = "dusk"
            weather = "clear"
            visibility = 0.9
            ambient_light = 0.75
            ambient_label = "Amber Sunset"
            primary_route = "main_road"
            blocked = []
            cars_avail = True
            debrief = (
                "Methodical execution in Pauper Streets. Balanced use of roads and cover, calculated lethal engagements, "
                "and clean extraction."
            )
            brief = (
                "CONTRACT: Viktor is supervising an arms shipment at the central crossroads. "
                "Multiple avenues of approach exist. Assess the perimeter and execute when ready."
            )
            deduction = "Player exhibits versatile tactical gameplay, balancing stealth repositioning with decisive combat."

        return {
            "mission_id": 2,
            "mission_title": "NODE 02: First Blood",
            "player_archetype": archetype,
            "psychological_deduction": deduction,
            "narrative_debriefing": debrief,
            "mission_briefing": brief,
            "target": {
                "name": "Viktor",
                "location_id": location_id,
                "location_label": location_label,
                "armor": target_armor,
                "health": target_hp,
                "movement": "patrol",
                "is_indoors": indoors
            },
            "guards": {
                "count": guard_count,
                "vision_range": guard_vision,
                "reaction_speed": guard_reaction,
                "hearing_range": guard_vision + 4.0,
                "armored": archetype in ["AGGRESSOR", "WRECKING_BALL"],
                "high_alert": archetype == "GHOST",
                "patrol_pattern": "active"
            },
            "environment": {
                "time_of_day": time_of_day,
                "weather": weather,
                "visibility": visibility,
                "ambient_light": ambient_light,
                "ambient_label": ambient_label
            },
            "routes": {
                "primary_route": primary_route,
                "primary_route_id": primary_route,
                "primary_label": primary_route.replace("_", " ").title(),
                "blocked_routes": blocked,
                "reinforced_barricades": [],
                "cars_available": cars_avail
            },
            "room_graph": room_graph,
            "reasoning": [
                f"Adapted for archetype '{archetype}' based on Map 1 telemetry.",
                f"Telemetry: Kills={profile.total_kills}, Destruction={profile.destruction_index:.2f}, StealthRatio={profile.stealth_ratio:.2f}.",
                f"Deployment: Target={target_armor} armor at {location_id} with {guard_count} guards."
            ]
        }
