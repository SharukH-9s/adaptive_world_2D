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
    RoomGraphManifest, GridDimensions, PropConfig, build_room_graph,
    SECTOR_GRID_COORDS, SECTOR_ADJACENCY, SECTOR_MODULES, CONNECTION_STATUSES,
    _ARCHETYPE_DEFAULT_GRIDS,
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

    def __init__(
        self,
        ground_truth_file: Optional[str] = None,
        ground_truth_file_map2: Optional[str] = None,
        read_env_file: bool = True,
    ):
        self._read_env_file = read_env_file
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
        self.api_keys = self._load_api_keys(read_env_file=self._read_env_file)
        self.api_key = self.api_keys[0] if self.api_keys else ""
        self._active_key_index = 0

    @staticmethod
    def _mask_key(key: str) -> str:
        """Safely masks an API key for logs (e.g. AQ.A...5qsg)."""
        if not key:
            return "<empty>"
        if len(key) <= 8:
            return "***"
        return f"{key[:4]}...{key[-4:]}"

    def _load_api_keys(self, read_env_file: Optional[bool] = None) -> List[str]:
        """Loads all GEMINI_API_KEY* variables from environment or .env files."""
        if read_env_file is None:
            read_env_file = getattr(self, "_read_env_file", True)

        env_file_keys: Dict[str, str] = {}
        if read_env_file:
            # 1. Try python-dotenv on project root and backend folder
            try:
                from dotenv import load_dotenv
                base_dir = os.path.dirname(os.path.abspath(__file__))
                root_dir = os.path.dirname(base_dir)
                load_dotenv(os.path.join(root_dir, ".env"), override=False)
                load_dotenv(os.path.join(base_dir, ".env"), override=False)
            except Exception:
                pass

            # 2. Manual fallback parser for .env files to detect GEMINI_API_KEY*
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
                                    k_clean = k.strip()
                                    v_clean = v.strip().strip('"').strip("'")
                                    if (k_clean == "GEMINI_API_KEY" or k_clean.startswith("GEMINI_API_KEY_") or k_clean == "GEMINI_API_KEYS") and v_clean:
                                        env_file_keys[k_clean] = v_clean
                                        if k_clean not in os.environ:
                                            os.environ[k_clean] = v_clean
                    except Exception:
                        pass

        # 3. Collect and deduplicate keys in priority order:
        # GEMINI_API_KEY first, then GEMINI_API_KEY_1, GEMINI_API_KEY_2, etc.
        keys: List[str] = []

        def _add_key(raw_val: str) -> None:
            for part in raw_val.replace(";", ",").split(","):
                token = part.strip()
                if token and token not in keys:
                    keys.append(token)

        # Primary key
        primary = os.environ.get("GEMINI_API_KEY", env_file_keys.get("GEMINI_API_KEY", "")).strip()
        if primary:
            _add_key(primary)

        # Additional numbered/named keys
        all_var_names = set(os.environ.keys()) | set(env_file_keys.keys())
        extra_names = [
            name for name in all_var_names
            if (name.startswith("GEMINI_API_KEY_") or name == "GEMINI_API_KEYS")
        ]

        def _sort_key(name: str):
            parts = name.split("_")
            if parts[-1].isdigit():
                return (0, int(parts[-1]))
            return (1, name)

        extra_names.sort(key=_sort_key)

        for name in extra_names:
            val = os.environ.get(name, env_file_keys.get(name, "")).strip()
            if val:
                _add_key(val)

        return keys

    def _load_api_key(self, read_env_file: Optional[bool] = None) -> str:
        """Loads primary GEMINI_API_KEY from environment or .env files (backward compatibility)."""
        keys = self._load_api_keys(read_env_file=read_env_file)
        return keys[0] if keys else ""

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
        Tries Gemini API first with key rotation across all available API keys;
        falls back to deterministic heuristic director if unavailable or all keys exhausted.
        """
        self.api_keys = self._load_api_keys(read_env_file=getattr(self, "_read_env_file", True))
        self.api_key = self.api_keys[0] if self.api_keys else ""

        if self.api_keys:
            num_keys = len(self.api_keys)
            start_index = self._active_key_index % num_keys

            for attempt in range(num_keys):
                curr_idx = (start_index + attempt) % num_keys
                curr_key = self.api_keys[curr_idx]
                masked_key = self._mask_key(curr_key)

                try:
                    manifest_dict = self._call_gemini(profile, telemetry, seed, api_key=curr_key)
                    if manifest_dict:
                        self._active_key_index = curr_idx
                        self.api_key = curr_key
                        return manifest_dict
                except Exception as e:
                    logger.warning(
                        f"[Director] Gemini API call failed with key {masked_key} (key {curr_idx + 1}/{num_keys}): {e}"
                    )
                    if attempt + 1 < num_keys:
                        logger.info("[Director] Rotating to next available Gemini API key...")

            logger.warning(
                f"[Director] All {num_keys} Gemini API key(s) failed or hit quota limits. Falling back to heuristic director."
            )

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

    def _call_gemini(
        self,
        profile,
        telemetry,
        seed=None,
        api_key=None,
    ):
        from google import genai

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

        system_instruction = (
            "You are the Cognitive Narrative Director of the game 'AdaptiveWorld2D'.\n"
            "The game follows a 10-Node Underworld Crime Story Spine.\n"
            "Node 01 is 'Rock Bottom' (slum survival, purchasing first weapon, escaping to extraction).\n"
            "Node 02 is 'First Blood' (the player's first formal syndicate contract: eliminate Viktor, a rival lieutenant).\n\n"
            "You receive:\n"
            "1. Map 1 Ground Truth (layout, sectors, choke points, guard patrols, vehicles, destructible fences/walls, fuel barrels).\n"
            "2. The Player's actual telemetry from Node 01 (kills, combat style, route surfaces, vehicle usage, destruction events).\n\n"
            "Your Job:\n"
            "1. Correlate the player's telemetry against the physical geometry of Map 1:\n"
            "   - Did they run the main Boulevard, or sneak the South Alleys?\n"
            "   - Did they steal a vehicle (Sports Car / Police Cruiser) or traverse on foot?\n"
            "   - Did they blow up fuel barrels and breach concrete walls, or avoid alarms?\n"
            "   - Did they kill innocent civilians, patrol cops, or rival gang thugs?\n"
            "2. Deduce their true Psychological Archetype (GHOST, WRECKING_BALL, SPEEDRUNNER, AGGRESSOR, TACTICIAN, or BALANCED).\n"
            "3. Write a visceral Underworld Handler radio debriefing assessing their performance in Map 1.\n"
            "4. Advance the story to Node 02 ('First Blood'): write the mission briefing and dynamic objective text.\n"
            "5. Output the spatial manifest for Map 2 that strategically adapts to challenge or exploit their archetype:\n"
            "   - Target location must be one of: 'warehouse_interior', 'open_plaza', 'far_docks', 'fortified_building', 'mid_street'.\n"
            "   - Target armor: 'none', 'light', 'medium', 'heavy', 'reinforced'.\n"
            "   - Guards count: 3 to 10. Vision: 4.0 to 12.0. Reaction: 0.8 to 2.5.\n"
            "   - Routes: primary_route ('main_road', 'alley_south', 'highway_east'), blocked_routes "
            "(subset of ['main_road_north', 'alley_south', 'highway_east', 'warehouse_front_entrance']), cars_available (boolean).\n"
            "   - Environment: time_of_day ('day', 'dusk', 'night'), weather ('clear', 'fog', 'rain'), "
            "visibility (0.3 to 1.0), ambient_light (0.2 to 1.0).\n\n"
            "Return strictly valid JSON conforming to the requested schema.\n"
        )

        effective_key = api_key or self.api_key
        client = genai.Client(api_key=effective_key)
        model_name = __import__("os").environ.get("GEMINI_MODEL", "gemini-3.8-flash")

        # ── Pass 1: Narrative + Stats ────────────────────────────────────────
        pass1_dict = self._call_gemini_pass1(
            client, model_name, system_instruction, gt_summary, digest
        )
        if not pass1_dict:
            return None
        logger.info(
            "[Director] Pass 1 complete: archetype=%s, target=%s",
            pass1_dict.get("player_archetype"),
            pass1_dict.get("target", {}).get("location_id"),
        )

        # ── Pass 2: Room Graph (Pass 1 context injected) ─────────────────────
        pass2_dict = None
        try:
            pass2_dict = self._call_gemini_pass2(
                client, model_name, pass1_dict, map2_layout, seed
            )
            if pass2_dict and pass2_dict.get("room_graph"):
                logger.info("[Director] Pass 2 complete: room_graph generated.")
        except Exception as e:
            logger.warning(
                "[Director] Pass 2 room_graph generation failed (%s); will synthesize via archetype template.",
                e,
            )

        # ── Merge and validate ───────────────────────────────────────────────
        manifest_json = pass1_dict
        manifest_json["room_graph"] = pass2_dict.get("room_graph") if pass2_dict else None
        archetype = manifest_json.get("player_archetype", profile.archetype())
        self._validate_and_repair_room_graph(manifest_json, archetype, seed)
        if pass2_dict and pass2_dict.get("room_graph"):
            logger.info(
                "[Director] Successfully generated Node 02 via Gemini (two-pass): Archetype=%s",
                manifest_json.get("player_archetype"),
            )
        else:
            logger.info(
                "[Director] Successfully generated Node 02 via Gemini (Pass 1 narrative + template room_graph): Archetype=%s",
                manifest_json.get("player_archetype"),
            )
        return manifest_json

    # ── Schema constants ─────────────────────────────────────────────────────

    _SECTOR_ID_ENUM   = [
        "NW", "NC", "NE", "MW", "MC", "ME", "SW", "SC", "SE",
    ] + [f"R{r}_C{c}" for r in range(5) for c in range(5)]
    _MODULE_ENUM      = [
        "WAREHOUSE_INTERIOR", "FORTIFIED_PLAZA", "SLUM_ALLEYWAYS", "URBAN_HIGHWAY",
        "INDUSTRIAL_DEPOT", "WATERFRONT_DOCKS", "SECURITY_COMPOUND",
        "OPEN_STREET", "VACANT_LOT",
    ]
    _STATUS_ENUM      = ["open", "barricaded_wire_fence", "barricaded_concrete", "walled_off"]
    _ROLE_ENUM        = ["patrol", "sentry", "bodyguard"]
    _PROP_ENUM        = [
        "crate_stack", "power_breaker", "alarm_panel", "explosive_barrel",
        "vent_shaft", "surveillance_spotlight", "car", "fuel_barrel", "crate",
    ]
    _ARCHETYPE_ENUM   = ["GHOST", "WRECKING_BALL", "SPEEDRUNNER", "AGGRESSOR", "TACTICIAN", "BALANCED"]
    _LOCATION_ID_ENUM = ["warehouse_interior", "open_plaza", "far_docks", "fortified_building", "mid_street"]
    _ARMOR_ENUM       = ["none", "light", "medium", "heavy", "reinforced"]
    _MOVEMENT_ENUM    = ["stationary", "patrol", "erratic"]
    _TOD_ENUM         = ["day", "dusk", "night"]
    _WEATHER_ENUM     = ["clear", "fog", "rain"]
    _PATROL_ENUM      = ["static", "active", "random"]
    _ROUTE_ENUM       = ["main_road", "alley_south", "highway_east"]
    _BLOCKED_ENUM     = ["main_road_north", "alley_south", "highway_east", "warehouse_front_entrance"]

    @classmethod
    def _pass1_schema(cls):
        """response_schema for Pass 1: narrative + mission stats (no room_graph)."""
        return {
            "type": "OBJECT",
            "properties": {
                "mission_id":              {"type": "INTEGER"},
                "mission_title":           {"type": "STRING"},
                "player_archetype":        {"type": "STRING", "enum": cls._ARCHETYPE_ENUM},
                "psychological_deduction": {"type": "STRING"},
                "narrative_debriefing":    {"type": "STRING"},
                "mission_briefing":        {"type": "STRING"},
                "target": {
                    "type": "OBJECT",
                    "properties": {
                        "name":           {"type": "STRING"},
                        "location_id":    {"type": "STRING", "enum": cls._LOCATION_ID_ENUM},
                        "location_label": {"type": "STRING"},
                        "armor":          {"type": "STRING", "enum": cls._ARMOR_ENUM},
                        "health":         {"type": "INTEGER"},
                        "movement":       {"type": "STRING", "enum": cls._MOVEMENT_ENUM},
                        "is_indoors":     {"type": "BOOLEAN"},
                    },
                    "required": ["name", "location_id", "location_label", "armor",
                                 "health", "movement", "is_indoors"],
                },
                "guards": {
                    "type": "OBJECT",
                    "properties": {
                        "count":          {"type": "INTEGER"},
                        "vision_range":   {"type": "NUMBER"},
                        "reaction_speed": {"type": "NUMBER"},
                        "hearing_range":  {"type": "NUMBER"},
                        "armored":        {"type": "BOOLEAN"},
                        "high_alert":     {"type": "BOOLEAN"},
                        "patrol_pattern": {"type": "STRING", "enum": cls._PATROL_ENUM},
                    },
                    "required": ["count", "vision_range", "reaction_speed", "hearing_range",
                                 "armored", "high_alert", "patrol_pattern"],
                },
                "environment": {
                    "type": "OBJECT",
                    "properties": {
                        "time_of_day":   {"type": "STRING", "enum": cls._TOD_ENUM},
                        "weather":       {"type": "STRING", "enum": cls._WEATHER_ENUM},
                        "visibility":    {"type": "NUMBER"},
                        "ambient_light": {"type": "NUMBER"},
                        "ambient_label": {"type": "STRING"},
                    },
                    "required": ["time_of_day", "weather", "visibility", "ambient_light", "ambient_label"],
                },
                "routes": {
                    "type": "OBJECT",
                    "properties": {
                        "primary_route":         {"type": "STRING", "enum": cls._ROUTE_ENUM},
                        "primary_route_id":      {"type": "STRING", "enum": cls._ROUTE_ENUM},
                        "primary_label":         {"type": "STRING"},
                        "blocked_routes":        {"type": "ARRAY", "items": {"type": "STRING", "enum": cls._BLOCKED_ENUM}},
                        "reinforced_barricades": {"type": "ARRAY", "items": {"type": "STRING"}},
                        "cars_available":        {"type": "BOOLEAN"},
                    },
                    "required": ["primary_route", "primary_route_id", "primary_label",
                                 "blocked_routes", "reinforced_barricades", "cars_available"],
                },
                "reasoning": {"type": "ARRAY", "items": {"type": "STRING"}},
            },
            "required": [
                "mission_id", "mission_title", "player_archetype",
                "psychological_deduction", "narrative_debriefing", "mission_briefing",
                "target", "guards", "environment", "routes", "reasoning",
            ],
        }

    @classmethod
    def _pass2_schema(cls):
        """response_schema for Pass 2: room_graph only. Constrains every enum at sampler level."""
        return {
            "type": "OBJECT",
            "properties": {
                "room_graph": {
                    "type": "OBJECT",
                    "properties": {
                        "grid_dimensions": {
                            "type": "OBJECT",
                            "properties": {
                                "columns": {"type": "INTEGER"},
                                "rows": {"type": "INTEGER"},
                                "cell_width": {"type": "NUMBER"},
                                "cell_height": {"type": "NUMBER"},
                            },
                            "required": ["columns", "rows"],
                        },
                        "sectors": {
                            "type": "ARRAY",
                            "items": {
                                "type": "OBJECT",
                                "properties": {
                                    "id":       {"type": "STRING", "enum": cls._SECTOR_ID_ENUM},
                                    "module":   {"type": "STRING", "enum": cls._MODULE_ENUM},
                                    "center_x": {"type": "NUMBER"},
                                    "center_y": {"type": "NUMBER"},
                                    "guards": {
                                        "type": "ARRAY",
                                        "items": {
                                            "type": "OBJECT",
                                            "properties": {
                                                "sector_id": {"type": "STRING", "enum": cls._SECTOR_ID_ENUM},
                                                "offset_x":  {"type": "NUMBER"},
                                                "offset_y":  {"type": "NUMBER"},
                                                "role":      {"type": "STRING", "enum": cls._ROLE_ENUM},
                                                "armored":   {"type": "BOOLEAN"},
                                            },
                                            "required": ["sector_id", "offset_x", "offset_y", "role", "armored"],
                                        },
                                    },
                                    "props": {
                                        "type": "ARRAY",
                                        "items": {
                                            "type": "OBJECT",
                                            "properties": {
                                                "type": {"type": "STRING", "enum": cls._PROP_ENUM},
                                                "offset_x": {"type": "NUMBER"},
                                                "offset_y": {"type": "NUMBER"},
                                                "destructible": {"type": "BOOLEAN"},
                                                "interact_action": {"type": "STRING"},
                                                "connects_to": {"type": "STRING"},
                                            },
                                            "required": ["type"],
                                        },
                                    },
                                },
                                "required": ["id", "module", "center_x", "center_y", "guards", "props"],
                            },
                        },
                        "connections": {
                            "type": "ARRAY",
                            "items": {
                                "type": "OBJECT",
                                "properties": {
                                    "from_sector": {"type": "STRING", "enum": cls._SECTOR_ID_ENUM},
                                    "to_sector":   {"type": "STRING", "enum": cls._SECTOR_ID_ENUM},
                                    "status":      {"type": "STRING", "enum": cls._STATUS_ENUM},
                                },
                                "required": ["from_sector", "to_sector", "status"],
                            },
                        },
                        "target_sector":       {"type": "STRING", "enum": cls._SECTOR_ID_ENUM},
                        "player_spawn_sector": {"type": "STRING", "enum": cls._SECTOR_ID_ENUM},
                        "extraction_sector":   {"type": "STRING", "enum": cls._SECTOR_ID_ENUM},
                    },
                    "required": ["sectors", "connections", "target_sector",
                                 "player_spawn_sector", "extraction_sector"],
                }
            },
            "required": ["room_graph"],
        }

    def _generate_content_with_fallback(
        self, client: Any, preferred_model: str, contents: Any, config: Any
    ) -> Any:
        """Executes generate_content with transient retry (503/429) and model fallback."""
        candidates = [preferred_model]
        for fallback in ["gemini-3.8-flash", "gemini-2.5-flash", "gemini-3.6-flash"]:
            if fallback not in candidates:
                candidates.append(fallback)

        last_exc: Optional[Exception] = None
        for model in candidates:
            for attempt in range(2):
                try:
                    response = client.models.generate_content(
                        model=model,
                        contents=contents,
                        config=config,
                    )
                    return response
                except Exception as e:
                    last_exc = e
                    err_msg = str(e).lower()
                    # 404 Not Found / deprecated model -> skip directly to next model
                    if "404" in err_msg or "not_found" in err_msg or "no longer available" in err_msg:
                        logger.warning(
                            "[Director] Model '%s' not available (404/deprecated): %s. Trying fallback model...",
                            model, e,
                        )
                        break
                    # 503 Unavailable / 429 Resource Exhausted -> transient spike, wait briefly and retry once
                    if ("503" in err_msg or "unavailable" in err_msg or "429" in err_msg or "overloaded" in err_msg) and attempt == 0:
                        import time
                        logger.info(
                            "[Director] Model '%s' experiencing high demand (503/429); retrying in 1.5s...",
                            model,
                        )
                        time.sleep(1.5)
                        continue
                    # Other errors or second attempt failed: log and try next model
                    logger.warning(
                        "[Director] Generation with model '%s' failed (%s). Checking next candidate model...",
                        model, e,
                    )
                    break

        if last_exc:
            raise last_exc
        return None

    def _call_gemini_pass1(self, client, model_name, system_instruction, gt_summary, digest):
        """Pass 1: generate narrative + mission stats (no room_graph). Schema-enforced."""
        import json as _json
        from google.genai import types

        prompt = (
            "MAP 1 GROUND TRUTH:\n"
            + _json.dumps(gt_summary, indent=2)
            + "\n\nPLAYER RUN TELEMETRY DIGEST:\n"
            + _json.dumps(digest, indent=2)
            + "\n\nSTORY NODE:\nCurrent: NODE_01 (Rock Bottom) -> Next: NODE_02 (First Blood)\n\n"
            "Based on the player's telemetry, deduce their archetype and generate the mission narrative,\n"
            "target configuration, guard disposition, environment, and routes for Node 02.\n"
            "Do NOT generate the room_graph -- that will be done separately.\n"
        )
        response = self._generate_content_with_fallback(
            client=client,
            preferred_model=model_name,
            contents=prompt,
            config=types.GenerateContentConfig(
                system_instruction=system_instruction,
                response_mime_type="application/json",
                response_schema=self._pass1_schema(),
                temperature=0.7,
            ),
        )
        if response and response.text:
            return _json.loads(response.text.strip())
        return None

    def _call_gemini_pass2(self, client, model_name, pass1, map2_layout, seed):
        """Pass 2: generate room_graph using Pass 1 context as fixed ground truth. Schema-enforced."""
        import json as _json
        from google.genai import types

        target = pass1.get("target", {})
        guards = pass1.get("guards", {})
        env    = pass1.get("environment", {})

        # Leap 3: Extend map2_layout with dynamic grid palette & tactical prop options
        map2_layout_ext = dict(map2_layout)
        map2_layout_ext["available_grid_dimensions"] = [
            {"columns": 2, "rows": 3, "label": "Tight Gauntlet — claustrophobic corridors (ideal for GHOST)"},
            {"columns": 3, "rows": 3, "label": "Fortified Perimeter — multi-angle compound (AGGRESSOR / BALANCED)"},
            {"columns": 2, "rows": 4, "label": "Linear Run — long pursuit/escape vector (SPEEDRUNNER)"},
            {"columns": 3, "rows": 4, "label": "Sprawling Depot — wide vehicle battlefield (WRECKING_BALL)"},
        ]
        map2_layout_ext["grid_design_guidelines"] = (
            "Select grid dimensions based on player archetype and behavioral fingerprint: "
            "Compact layouts (2x3, 2x4) create high-density stealth tension. "
            "Wide layouts (3x4) favor vehicle ramming and high-speed chases. "
            "Balanced (3x3) allows multi-angle tactical approaches. "
            "Output grid_dimensions as {\"columns\": N, \"rows\": M}."
        )
        map2_layout_ext["available_props"] = [
            {"type": "crate_stack",             "description": "Static cover, optionally destructible"},
            {"type": "power_breaker",           "description": "On interact/damage: cuts sector lights, drops guard vision by 60%"},
            {"type": "alarm_panel",             "description": "Alerted guards navigate here to sound compound-wide lockdown"},
            {"type": "explosive_barrel",        "description": "100 HP, AoE damage, demolishes adjacent barricades on destruction"},
            {"type": "vent_shaft",              "description": "Crawl passage bypassing barricaded/walled sector borders"},
            {"type": "surveillance_spotlight",  "description": "Rotating 2D light cone; tripping it raises sector alert"},
            {"type": "car",                     "description": "Drivable vehicle for ramming barricades or fast traversal"},
        ]
        map2_layout_ext["prop_placement_guidelines"] = (
            "Place tactical props based on player psychology:\n"
            "- GHOST: Include power_breaker near target sector, alarm_panels at guard stations, "
            "vent_shaft bypassing walled corridors.\n"
            "- WRECKING_BALL: Place explosive_barrels along road choke points, cars for ramming.\n"
            "- SPEEDRUNNER: Place cars along main route, surveillance_spotlights at key intersections.\n"
            "- AGGRESSOR: Place alarm_panels and surveillance_spotlights in defensive layers.\n"
            "Props must specify type, offset_x, offset_y relative to sector center."
        )

        system_instruction_p2 = (
            "You are the Spatial Level Architect for 'AdaptiveWorld2D'.\n"
            "You receive a fully-decided mission context (archetype, target placement, guards, environment)\n"
            "and spatial grid guidelines. Your sole task is to generate the room_graph that spatially\n"
            "expresses that mission context.\n\n"
            "STRICT RULES:\n"
            "- grid_dimensions: specify columns and rows matching the selected archetype layout.\n"
            "- sectors: EXACTLY columns * rows entries. For 3x3, use NW..SE IDs; for non-3x3, use R{r}_C{c} IDs.\n"
            "  Each center_x and center_y MUST match grid center coordinates.\n"
            "- connections: all adjacent pairs must be present with status: open, barricaded_wire_fence, barricaded_concrete, walled_off.\n"
            "- target_sector, player_spawn_sector, extraction_sector must each be distinct valid sector ids.\n"
            "- CRITICAL PATH INVARIANT: A walkable path (treating only 'walled_off' as impassable) MUST\n"
            "  exist from player_spawn_sector to target_sector, AND from target_sector to extraction_sector.\n"
            "  Use 'barricaded_*' for chokepoints on the intended path. Reserve 'walled_off' for detours only.\n"
            "- props: tactically distribute interactive props (power_breaker, alarm_panel, explosive_barrel, vent_shaft, surveillance_spotlight, car, crate_stack) across sectors.\n"
            "- Choose sector modules thematically: match the target's location_id and archetype.\n"
        )

        prompt_p2 = (
            "ALREADY-DECIDED MISSION CONTEXT (treat these as immutable facts):\n"
            f"  player_archetype:        {pass1.get('player_archetype')}\n"
            f"  target.location_id:      {target.get('location_id')}\n"
            f"  target.is_indoors:       {target.get('is_indoors')}\n"
            f"  target.armor:            {target.get('armor')}\n"
            f"  environment.time_of_day: {env.get('time_of_day')}\n"
            f"  environment.weather:     {env.get('weather')}\n"
            f"  guards.count:            {guards.get('count')}\n"
            f"  guards.armored:          {guards.get('armored')}\n\n"
            "MAP 2 SPATIAL GUIDELINES & PROPS PALETTE:\n"
            + _json.dumps(map2_layout_ext, indent=2)
            + "\n\nGenerate ONLY the room_graph object that spatially expresses the above mission context.\n"
        )
        response = self._generate_content_with_fallback(
            client=client,
            preferred_model=model_name,
            contents=prompt_p2,
            config=types.GenerateContentConfig(
                system_instruction=system_instruction_p2,
                response_mime_type="application/json",
                response_schema=self._pass2_schema(),
                temperature=0.5,
            ),
        )
        if response and response.text:
            return _json.loads(response.text.strip())
        return None

    def _validate_and_repair_room_graph(self, manifest_json: Dict[str, Any], archetype: str, seed: Optional[int]) -> None:
        """Mutates manifest_json in place: ensures a structurally valid `room_graph` is present.
        If validation fails, the entire room_graph is replaced with the deterministic template
        (preserving grid_dimensions shape) for `archetype` so Unity never receives a malformed graph."""
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
                grid_dimensions=rg.get("grid_dimensions"),
            )
            errors = probe.validate()

        if errors:
            logger.warning(f"[Director] Gemini room_graph invalid ({errors}); repairing with deterministic template.")
            rng = random.Random(seed)
            grid_dims = rg.get("grid_dimensions") if isinstance(rg, dict) else None
            cols, rows = 3, 3
            if isinstance(grid_dims, dict):
                cols = grid_dims.get("columns", 3)
                rows = grid_dims.get("rows", 3)
            elif grid_dims is not None:
                cols = getattr(grid_dims, "columns", 3)
                rows = getattr(grid_dims, "rows", 3)
            manifest_json["room_graph"] = build_room_graph(archetype, rng, grid_shape=(cols, rows)).to_dict()

    def _generate_fallback(self, profile: PlayerProfile, telemetry: TelemetryLog, seed: Optional[int] = None) -> Dict[str, Any]:
        """
        Deterministic, narrative-rich fallback generator when Gemini API is offline.
        Uses archetype-specific dynamic grid topology and tactical props.
        """
        archetype = profile.archetype()
        digest = self._build_telemetry_digest(profile, telemetry)
        rng = random.Random(seed)
        grid_shape = _ARCHETYPE_DEFAULT_GRIDS.get(archetype, (3, 3))
        room_graph = build_room_graph(archetype, rng, grid_shape=grid_shape).to_dict()

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
