"""
target_solver.py
----------------
Mission 2 Generator: reads the player profile from M1 and outputs a
fully-specified target configuration manifest.

This is the rule-based solver (v1).
Future version: replace with a trained ML model using these outputs as labels.
"""
import random
from dataclasses import dataclass, field, asdict
from typing import List, Optional
from .profiler import PlayerProfile


# ---------------------------------------------------------------------------
# Manifest data structures
# ---------------------------------------------------------------------------

@dataclass
class TargetConfig:
    name: str = "Viktor"
    location_id: str = "mid_street"        # maps to a Unity spawn point tag
    location_label: str = "Open Street"
    armor: str = "medium"                  # "none", "light", "medium", "heavy", "reinforced"
    health: int = 100
    movement: str = "patrol"              # "stationary", "patrol", "erratic"
    is_indoors: bool = False


@dataclass
class GuardConfig:
    count: int = 3
    vision_range: float = 5.0
    patrol_pattern: str = "static"        # "static", "active", "random"
    armor: str = "light"
    reaction_speed: float = 1.0          # multiplier: 1.0 = normal, 2.0 = fast
    label: str = "Standard Guards"


@dataclass
class EnvironmentConfig:
    time_of_day: str = "day"             # "day", "dusk", "night"
    weather: str = "clear"              # "clear", "overcast", "foggy", "rain"
    visibility: float = 1.0             # 0.0 = blind, 1.0 = perfect
    ambient_label: str = "Clear Day"


@dataclass
class RouteConfig:
    primary_route_id: str = "main_road"
    primary_label: str = "Main Road"
    blocked_routes: List[str] = field(default_factory=list)
    gps_hint: str = "Follow the main road north."


@dataclass
class M2Manifest:
    mission_id: str = "M02_THE_HIT"
    player_id: str = "player_01"
    player_archetype: str = "BALANCED"
    seed: int = 0

    target: TargetConfig = field(default_factory=TargetConfig)
    guards: GuardConfig = field(default_factory=GuardConfig)
    environment: EnvironmentConfig = field(default_factory=EnvironmentConfig)
    routes: RouteConfig = field(default_factory=RouteConfig)

    # Raw profile scores stored for AI training dataset export
    profile_stealth_ratio: float = 0.0
    profile_aggression: float = 0.0
    profile_destruction_index: float = 0.0
    profile_vehicle_preference: float = 0.0
    profile_avg_speed: float = 0.0

    reasoning: List[str] = field(default_factory=list)

    def to_dict(self):
        return asdict(self)


# ---------------------------------------------------------------------------
# Solver
# ---------------------------------------------------------------------------

class M2TargetSolver:
    """
    Maps M1 PlayerProfile → M2Manifest using weighted probability rolls.

    Adaptation logic:
      GHOST       → target hides indoors, tight corridors, alert guards, few in count
      WRECKING_BALL → target is in the open but armored, more guards, fortified area
      SPEEDRUNNER  → target is far, vehicle routes cut, guards spread out
      AGGRESSOR    → target is heavily armored, massive guard count, fortified building
      BALANCED    → moderate everything
    """

    def solve(self, profile: PlayerProfile, seed: int = None) -> M2Manifest:
        rng = random.Random(seed)
        archetype = profile.archetype()

        manifest = M2Manifest(
            player_id=profile.player_id,
            player_archetype=archetype,
            seed=seed or 0,
            profile_stealth_ratio=round(profile.stealth_ratio, 3),
            profile_aggression=round(profile.aggression, 3),
            profile_destruction_index=round(profile.destruction_index, 3),
            profile_vehicle_preference=round(profile.vehicle_preference, 3),
            profile_avg_speed=round(profile.avg_speed, 3),
        )

        reasons = []

        # ── Target ──────────────────────────────────────────────────────────
        if archetype == "GHOST":
            # Ghost players expect light targets → give them a hard-to-reach indoor target
            manifest.target = TargetConfig(
                name="Viktor",
                location_id="warehouse_interior",
                location_label="Warehouse Interior (tight corridors)",
                armor=rng.choice(["medium", "heavy"]),
                health=rng.randint(120, 160),
                movement="patrol",
                is_indoors=True,
            )
            reasons.append(f"GHOST: Target placed indoors (warehouse). Armor elevated. "
                           f"Stealth route is deliberately congested with guards.")

        elif archetype == "WRECKING_BALL":
            # Aggressive destroyer → target is armored, open area, expects a loud fight
            manifest.target = TargetConfig(
                name="Viktor",
                location_id="open_plaza",
                location_label="Open Plaza (exposed)",
                armor=rng.choice(["heavy", "reinforced"]),
                health=rng.randint(180, 250),
                movement="stationary",
                is_indoors=False,
            )
            reasons.append(f"WRECKING_BALL: Target in open plaza with reinforced armor. "
                           f"Expects chaotic assault. Barricades deployed.")

        elif archetype == "SPEEDRUNNER":
            # Fast driver → target is far, vehicle routes removed
            manifest.target = TargetConfig(
                name="Viktor",
                location_id="far_docks",
                location_label="Dockyard (far end of map)",
                armor=rng.choice(["light", "medium"]),
                health=rng.randint(80, 120),
                movement="erratic",
                is_indoors=False,
            )
            reasons.append(f"SPEEDRUNNER: Target placed at far docks. Cars removed from "
                           f"primary roads. Player must traverse on foot.")

        elif archetype == "AGGRESSOR":
            # Loud killer → fortified building, many armored guards
            manifest.target = TargetConfig(
                name="Viktor",
                location_id="fortified_building",
                location_label="Fortified Office (locked down)",
                armor="reinforced",
                health=rng.randint(250, 350),
                movement="stationary",
                is_indoors=True,
            )
            reasons.append(f"AGGRESSOR: Target in fortified building, maximum armor. "
                           f"Enemies have learned from player's aggression — they are armored.")

        else:  # BALANCED
            manifest.target = TargetConfig(
                name="Viktor",
                location_id="mid_street",
                location_label="Mid-City Street",
                armor="medium",
                health=100,
                movement="patrol",
                is_indoors=False,
            )
            reasons.append("BALANCED: Standard target config — moderate difficulty.")

        # ── Guards ──────────────────────────────────────────────────────────
        guard_count_map = {
            "GHOST":         rng.randint(3, 5),   # few guards but very alert
            "WRECKING_BALL": rng.randint(6, 10),  # many guards
            "SPEEDRUNNER":   rng.randint(2, 4),   # spread out, low count
            "AGGRESSOR":     rng.randint(8, 14),  # maximum guards
            "BALANCED":      rng.randint(3, 6),
        }
        vision_range_map = {
            "GHOST":         rng.uniform(7.0, 10.0),  # alert guards see far
            "WRECKING_BALL": rng.uniform(4.0, 6.0),   # guards ready for brawl, less scanning
            "SPEEDRUNNER":   rng.uniform(5.0, 7.0),
            "AGGRESSOR":     rng.uniform(5.0, 8.0),
            "BALANCED":      rng.uniform(5.0, 6.0),
        }
        reaction_map = {
            "GHOST":         rng.uniform(1.5, 2.0),   # very fast reaction
            "WRECKING_BALL": rng.uniform(0.8, 1.2),
            "SPEEDRUNNER":   rng.uniform(1.0, 1.4),
            "AGGRESSOR":     rng.uniform(1.2, 1.8),   # learned to shoot first
            "BALANCED":      1.0,
        }
        manifest.guards = GuardConfig(
            count=guard_count_map[archetype],
            vision_range=round(vision_range_map[archetype], 2),
            patrol_pattern="active" if archetype in ("GHOST", "AGGRESSOR") else "static",
            armor="heavy" if archetype in ("AGGRESSOR", "WRECKING_BALL") else "light",
            reaction_speed=round(reaction_map[archetype], 2),
            label=f"{guard_count_map[archetype]} Guards ({archetype.replace('_', ' ').title()})",
        )
        reasons.append(
            f"Guards: count={manifest.guards.count}, vision={manifest.guards.vision_range}m, "
            f"reaction={manifest.guards.reaction_speed}x, armor={manifest.guards.armor}"
        )

        # ── Environment ─────────────────────────────────────────────────────
        env_options = {
            "GHOST":         ("night",  "foggy",    0.35, "Foggy Night"),
            "WRECKING_BALL": ("day",    "clear",    1.0,  "Clear Day — Full Exposure"),
            "SPEEDRUNNER":   ("dusk",   "overcast", 0.6,  "Dusk Overcast"),
            "AGGRESSOR":     ("night",  "rain",     0.45, "Heavy Rain at Night"),
            "BALANCED":      ("day",    "overcast", 0.75, "Overcast Day"),
        }
        tod, weather, vis, label = env_options[archetype]
        manifest.environment = EnvironmentConfig(
            time_of_day=tod, weather=weather, visibility=vis, ambient_label=label
        )
        reasons.append(f"Environment: {label} (visibility={vis})")

        # ── Routes ──────────────────────────────────────────────────────────
        route_map = {
            "GHOST": RouteConfig(
                primary_route_id="alley_network",
                primary_label="Alley Network",
                blocked_routes=["main_road_north", "warehouse_front_entrance"],
                gps_hint="Direct entry blocked. Approach through south alleys.",
            ),
            "WRECKING_BALL": RouteConfig(
                primary_route_id="main_road_north",
                primary_label="Main Road North",
                blocked_routes=["alley_south"],
                gps_hint="Barricades placed. Approach from main road.",
            ),
            "SPEEDRUNNER": RouteConfig(
                primary_route_id="alley_network",
                primary_label="Alley Network (cars removed)",
                blocked_routes=["main_road_north", "highway_east"],
                gps_hint="No vehicles available. Head through the alleys on foot.",
            ),
            "AGGRESSOR": RouteConfig(
                primary_route_id="main_road_north",
                primary_label="Main Road North (heavily guarded)",
                blocked_routes=["alley_south", "rooftop_access"],
                gps_hint="Fortified perimeter. Expect heavy resistance from main road.",
            ),
            "BALANCED": RouteConfig(
                primary_route_id="main_road_north",
                primary_label="Main Road North",
                blocked_routes=[],
                gps_hint="Standard approach. Use your judgment.",
            ),
        }
        manifest.routes = route_map[archetype]
        reasons.append(
            f"Primary route: {manifest.routes.primary_label} | "
            f"Blocked: {manifest.routes.blocked_routes}"
        )
        reasons.append("[OK] Invariant check: target location guaranteed reachable via primary route.")

        manifest.reasoning = reasons
        return manifest


# ---------------------------------------------------------------------------
# Room / Sector Graph (3x3 tactical grid) -- Map 2 AI-Driven redesign, Phase 1
# ---------------------------------------------------------------------------

# Canvas: 60 x 45 units. Columns X = -20/0/+20 (W/C/E). Rows Y = +15/0/-15 (N/M/S).
SECTOR_GRID_COORDS = {
    "NW": (-20.0, 15.0), "NC": (0.0, 15.0), "NE": (20.0, 15.0),
    "MW": (-20.0, 0.0), "MC": (0.0, 0.0), "ME": (20.0, 0.0),
    "SW": (-20.0, -15.0), "SC": (0.0, -15.0), "SE": (20.0, -15.0),
}

# All 12 unique adjacent pairs (horizontal + vertical neighbors only, no diagonals).
SECTOR_ADJACENCY = [
    ("NW", "NC"), ("NC", "NE"),
    ("MW", "MC"), ("MC", "ME"),
    ("SW", "SC"), ("SC", "SE"),
    ("NW", "MW"), ("MW", "SW"),
    ("NC", "MC"), ("MC", "SC"),
    ("NE", "ME"), ("ME", "SE"),
]

SECTOR_MODULES = (
    "WAREHOUSE_INTERIOR", "FORTIFIED_PLAZA", "SLUM_ALLEYWAYS", "URBAN_HIGHWAY",
    "INDUSTRIAL_DEPOT", "WATERFRONT_DOCKS", "SECURITY_COMPOUND",
    "OPEN_STREET", "VACANT_LOT",  # generic filler modules for thematically-unassigned sectors
)

CONNECTION_STATUSES = ("open", "barricaded_wire_fence", "barricaded_concrete", "walled_off")
IMPASSABLE_STATUSES = ("walled_off",)  # only status that fully blocks connectivity (others are destructible)


@dataclass
class GuardEntityConfig:
    """A single guard placement within a sector, offset from the sector center."""
    sector_id: str = "NW"
    offset_x: float = 0.0
    offset_y: float = 0.0
    role: str = "patrol"          # "patrol", "sentry", "bodyguard"
    armored: bool = False


@dataclass
class TargetBossConfig:
    """Reserved for future direct spatial-stat binding of the boss to the room graph.
    The live pipeline currently keeps boss stats in the top-level `target` object and
    only references its sector via `RoomGraphManifest.target_sector`."""
    name: str = "Viktor"
    sector_id: str = "NW"
    armor: str = "medium"
    health: int = 100
    movement: str = "patrol"
    is_indoors: bool = True


@dataclass
class SectorConfig:
    id: str = "NW"                                   # one of the 9 grid ids
    module: str = "OPEN_STREET"
    center_x: float = 0.0
    center_y: float = 0.0
    guards: List[GuardEntityConfig] = field(default_factory=list)
    props: List[str] = field(default_factory=list)    # e.g. "fuel_barrel", "crate", "car"


@dataclass
class ConnectionConfig:
    from_sector: str = "NW"
    to_sector: str = "NC"
    status: str = "open"          # one of CONNECTION_STATUSES


@dataclass
class RoomGraphManifest:
    sectors: List[SectorConfig] = field(default_factory=list)
    connections: List[ConnectionConfig] = field(default_factory=list)
    target_sector: str = "NW"
    player_spawn_sector: str = "SW"
    extraction_sector: str = "NE"

    def to_dict(self):
        return asdict(self)

    def validate(self) -> List[str]:
        """Returns a list of schema/connectivity problems. Empty list = fully valid."""
        errors: List[str] = []

        ids = {s.id if not isinstance(s, dict) else s["id"] for s in self.sectors}
        expected_ids = set(SECTOR_GRID_COORDS.keys())
        if ids != expected_ids:
            missing = expected_ids - ids
            extra = ids - expected_ids
            if missing:
                errors.append(f"Missing sectors: {sorted(missing)}")
            if extra:
                errors.append(f"Unknown sectors: {sorted(extra)}")

        def _c(conn, attr):
            return conn[attr] if isinstance(conn, dict) else getattr(conn, attr)

        edge_set = {(_c(c, "from_sector"), _c(c, "to_sector")) for c in self.connections}
        expected_edges = set(SECTOR_ADJACENCY)
        if edge_set != expected_edges:
            missing_edges = expected_edges - edge_set
            extra_edges = edge_set - expected_edges
            if missing_edges:
                errors.append(f"Missing connections: {sorted(missing_edges)}")
            if extra_edges:
                errors.append(f"Non-adjacent/unknown connections: {sorted(extra_edges)}")

        for c in self.connections:
            status = _c(c, "status")
            if status not in CONNECTION_STATUSES:
                errors.append(f"Invalid connection status '{status}' on {_c(c, 'from_sector')}->{_c(c, 'to_sector')}")

        for key in ("target_sector", "player_spawn_sector", "extraction_sector"):
            val = getattr(self, key)
            if val not in SECTOR_GRID_COORDS:
                errors.append(f"{key}='{val}' is not a valid sector id")

        if not errors:
            if not self._is_reachable(self.player_spawn_sector, self.target_sector):
                errors.append(
                    f"Target sector '{self.target_sector}' unreachable from spawn "
                    f"'{self.player_spawn_sector}' (blocked by walled_off edges)"
                )
            if not self._is_reachable(self.target_sector, self.extraction_sector):
                errors.append(
                    f"Extraction sector '{self.extraction_sector}' unreachable from target "
                    f"'{self.target_sector}' (blocked by walled_off edges)"
                )

        return errors

    def _is_reachable(self, start: str, goal: str) -> bool:
        """BFS over the sector graph. Only 'walled_off' edges are treated as impassable
        (barricades are destructible, so they still count as traversable)."""
        if start == goal:
            return True

        def _c(conn, attr):
            return conn[attr] if isinstance(conn, dict) else getattr(conn, attr)

        traversable: dict = {}
        for c in self.connections:
            if _c(c, "status") in IMPASSABLE_STATUSES:
                continue
            a, b = _c(c, "from_sector"), _c(c, "to_sector")
            traversable.setdefault(a, set()).add(b)
            traversable.setdefault(b, set()).add(a)

        visited = {start}
        frontier = [start]
        while frontier:
            nxt = []
            for node in frontier:
                for neighbor in traversable.get(node, ()):
                    if neighbor not in visited:
                        visited.add(neighbor)
                        nxt.append(neighbor)
            frontier = nxt
        return goal in visited


def _sector(id_: str, module: str, guards=None, props=None) -> "SectorConfig":
    x, y = SECTOR_GRID_COORDS[id_]
    return SectorConfig(id=id_, module=module, center_x=x, center_y=y,
                         guards=guards or [], props=props or [])


def _edges(status_map: dict) -> List["ConnectionConfig"]:
    """status_map: {(from,to): status}. Any adjacency omitted from the map defaults to 'open'."""
    out = []
    for (a, b) in SECTOR_ADJACENCY:
        status = status_map.get((a, b), status_map.get((b, a), "open"))
        out.append(ConnectionConfig(from_sector=a, to_sector=b, status=status))
    return out


def build_room_graph(archetype: str, rng: Optional[random.Random] = None) -> RoomGraphManifest:
    """
    Handcrafted 9-sector blueprint templates, one per archetype.

    Each template guarantees (verified by RoomGraphManifest.validate()):
      - All 9 sectors present with a module + coordinates.
      - All 12 adjacency edges present with a valid status.
      - A non-'walled_off' path exists spawn -> target -> extraction.
    """
    rng = rng or random.Random()

    if archetype == "GHOST":
        # Slum Alleys -> Warehouse Interior: tight corridors, fog, wire fences.
        sectors = [
            _sector("SW", "SLUM_ALLEYWAYS", guards=[GuardEntityConfig("SW", -1, 0, "patrol")]),
            _sector("SC", "SLUM_ALLEYWAYS", guards=[GuardEntityConfig("SC", 0, 0, "sentry")]),
            _sector("SE", "VACANT_LOT"),
            _sector("MW", "SLUM_ALLEYWAYS", guards=[GuardEntityConfig("MW", 1, 0, "patrol")]),
            _sector("MC", "OPEN_STREET"),
            _sector("ME", "URBAN_HIGHWAY", guards=[GuardEntityConfig("ME", 0, 1, "patrol")]),
            _sector("NW", "WAREHOUSE_INTERIOR", props=["crate", "crate"],
                    guards=[GuardEntityConfig("NW", -2, 0, "bodyguard", armored=True),
                            GuardEntityConfig("NW", 2, 0, "bodyguard", armored=True)]),
            _sector("NC", "FORTIFIED_PLAZA", guards=[GuardEntityConfig("NC", 0, 0, "sentry")]),
            _sector("NE", "WATERFRONT_DOCKS"),
        ]
        connections = _edges({
            ("NW", "NC"): "open",
            ("NC", "NE"): "barricaded_wire_fence",
            ("MW", "MC"): "barricaded_wire_fence",
            ("MC", "ME"): "walled_off",
            ("SW", "SC"): "open",
            ("SC", "SE"): "barricaded_concrete",
            ("NW", "MW"): "barricaded_wire_fence",
            ("MW", "SW"): "open",
            ("NC", "MC"): "barricaded_wire_fence",
            ("MC", "SC"): "walled_off",
            ("NE", "ME"): "barricaded_wire_fence",
            ("ME", "SE"): "open",
        })
        target_sector, spawn_sector, extraction_sector = "NW", "SW", "NE"

    elif archetype == "WRECKING_BALL":
        # Industrial Depot -> Fortified Plaza: explosive barrels, concrete roadblocks, ramming vehicles.
        sectors = [
            _sector("SW", "INDUSTRIAL_DEPOT", props=["fuel_barrel", "fuel_barrel"]),
            _sector("SC", "INDUSTRIAL_DEPOT", props=["fuel_barrel"]),
            _sector("SE", "VACANT_LOT"),
            _sector("MW", "URBAN_HIGHWAY", props=["car"]),
            _sector("MC", "FORTIFIED_PLAZA",
                    guards=[GuardEntityConfig("MC", -2, 0, "bodyguard", armored=True),
                            GuardEntityConfig("MC", 2, 0, "bodyguard", armored=True)]),
            _sector("ME", "URBAN_HIGHWAY", props=["car"]),
            _sector("NW", "WAREHOUSE_INTERIOR"),
            _sector("NC", "OPEN_STREET"),
            _sector("NE", "WATERFRONT_DOCKS"),
        ]
        connections = _edges({
            ("NW", "NC"): "walled_off",
            ("NC", "NE"): "barricaded_concrete",
            ("MW", "MC"): "barricaded_concrete",
            ("MC", "ME"): "barricaded_concrete",
            ("SW", "SC"): "open",
            ("SC", "SE"): "walled_off",
            ("NW", "MW"): "barricaded_concrete",
            ("MW", "SW"): "open",
            ("NC", "MC"): "barricaded_concrete",
            ("MC", "SC"): "walled_off",
            ("NE", "ME"): "barricaded_concrete",
            ("ME", "SE"): "walled_off",
        })
        target_sector, spawn_sector, extraction_sector = "MC", "SW", "NE"

    elif archetype == "SPEEDRUNNER":
        # Urban Highway -> Waterfront Docks: direct road, roadblock chokepoints.
        sectors = [
            _sector("SW", "URBAN_HIGHWAY", props=["car"]),
            _sector("SC", "URBAN_HIGHWAY"),
            _sector("SE", "WATERFRONT_DOCKS"),
            _sector("MW", "OPEN_STREET"),
            _sector("MC", "URBAN_HIGHWAY", props=["car"]),
            _sector("ME", "URBAN_HIGHWAY"),
            _sector("NW", "VACANT_LOT"),
            _sector("NC", "FORTIFIED_PLAZA"),
            _sector("NE", "WATERFRONT_DOCKS",
                    guards=[GuardEntityConfig("NE", 0, 1, "sentry"),
                            GuardEntityConfig("NE", 0, -1, "patrol")]),
        ]
        connections = _edges({
            ("NW", "NC"): "walled_off",
            ("NC", "NE"): "barricaded_concrete",
            ("MW", "MC"): "open",
            ("MC", "ME"): "open",
            ("SW", "SC"): "open",
            ("SC", "SE"): "barricaded_concrete",
            ("NW", "MW"): "walled_off",
            ("MW", "SW"): "open",
            ("NC", "MC"): "barricaded_concrete",
            ("MC", "SC"): "walled_off",
            ("NE", "ME"): "barricaded_concrete",
            ("ME", "SE"): "open",
        })
        target_sector, spawn_sector, extraction_sector = "NE", "SW", "SE"

    elif archetype == "AGGRESSOR":
        # Urban Highway -> Security Compound: heavy SWAT guards, bunker crossfire.
        sectors = [
            _sector("MW", "URBAN_HIGHWAY"),
            _sector("SW", "VACANT_LOT"),
            _sector("SC", "OPEN_STREET"),
            _sector("NE", "WATERFRONT_DOCKS"),
            _sector("MC", "URBAN_HIGHWAY", props=["car"]),
            _sector("ME", "FORTIFIED_PLAZA",
                    guards=[GuardEntityConfig("ME", -1, 1, "sentry", armored=True),
                            GuardEntityConfig("ME", 1, -1, "sentry", armored=True)]),
            _sector("NW", "WAREHOUSE_INTERIOR"),
            _sector("NC", "INDUSTRIAL_DEPOT", props=["fuel_barrel"]),
            _sector("SE", "SECURITY_COMPOUND",
                    guards=[GuardEntityConfig("SE", -2, 0, "bodyguard", armored=True),
                            GuardEntityConfig("SE", 2, 0, "bodyguard", armored=True),
                            GuardEntityConfig("SE", 0, 2, "sentry", armored=True)]),
        ]
        connections = _edges({
            ("NW", "NC"): "barricaded_concrete",
            ("NC", "NE"): "walled_off",
            ("MW", "MC"): "open",
            ("MC", "ME"): "barricaded_concrete",
            ("SW", "SC"): "open",
            ("SC", "SE"): "barricaded_concrete",
            ("NW", "MW"): "barricaded_concrete",
            ("MW", "SW"): "walled_off",
            ("NC", "MC"): "barricaded_concrete",
            ("MC", "SC"): "walled_off",
            ("NE", "ME"): "barricaded_concrete",
            ("ME", "SE"): "barricaded_concrete",
        })
        target_sector, spawn_sector, extraction_sector = "SE", "MW", "NE"

    else:  # BALANCED (and TACTICIAN, if ever reintroduced)
        sectors = [
            _sector("SW", "SLUM_ALLEYWAYS", guards=[GuardEntityConfig("SW", 0, 0, "patrol")]),
            _sector("SC", "OPEN_STREET"),
            _sector("SE", "VACANT_LOT"),
            _sector("MW", "URBAN_HIGHWAY", props=["car"]),
            _sector("MC", "INDUSTRIAL_DEPOT", props=["fuel_barrel"]),
            _sector("ME", "OPEN_STREET"),
            _sector("NW", "WAREHOUSE_INTERIOR"),
            _sector("NC", "FORTIFIED_PLAZA",
                    guards=[GuardEntityConfig("NC", -1, 0, "bodyguard"),
                            GuardEntityConfig("NC", 1, 0, "bodyguard")]),
            _sector("NE", "WATERFRONT_DOCKS"),
        ]
        connections = _edges({
            ("NW", "NC"): "open",
            ("NC", "NE"): "barricaded_wire_fence",
            ("MW", "MC"): "open",
            ("MC", "ME"): "barricaded_wire_fence",
            ("SW", "SC"): "barricaded_wire_fence",
            ("SC", "SE"): "walled_off",
            ("NW", "MW"): "barricaded_concrete",
            ("MW", "SW"): "open",
            ("NC", "MC"): "open",
            ("MC", "SC"): "walled_off",
            ("NE", "ME"): "barricaded_wire_fence",
            ("ME", "SE"): "open",
        })
        target_sector, spawn_sector, extraction_sector = "NC", "SW", "NE"

    return RoomGraphManifest(
        sectors=sectors,
        connections=connections,
        target_sector=target_sector,
        player_spawn_sector=spawn_sector,
        extraction_sector=extraction_sector,
    )
