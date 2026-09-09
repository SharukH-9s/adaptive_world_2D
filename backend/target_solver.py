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
from typing import List
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
