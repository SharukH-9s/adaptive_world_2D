"""
telemetry.py
------------
Telemetry event data types for AdaptiveWorld2D.
Mission 1 records all player actions into these structures.
"""
from dataclasses import dataclass, field
from enum import Enum
from typing import List


class MovementType(Enum):
    WALK = "WALK"
    RUN = "RUN"
    DRIVE = "DRIVE"


class CombatStyle(Enum):
    STEALTH = "STEALTH"          # silent kill, melee/choke from behind
    DIRECT_ASSAULT = "DIRECT_ASSAULT"  # open gunfire
    EXPLOSIVE = "EXPLOSIVE"      # grenade, car bomb


class SurfaceType(Enum):
    ROAD = "ROAD"
    ALLEY = "ALLEY"
    BUILDING_INTERIOR = "BUILDING_INTERIOR"
    GRASS = "GRASS"


class DestructionType(Enum):
    WALL_BREAK = "WALL_BREAK"
    CAR_BURN = "CAR_BURN"
    BARREL_EXPLODE = "BARREL_EXPLODE"


@dataclass
class NavigationEvent:
    timestamp_s: float
    movement_type: MovementType
    surface: SurfaceType
    duration_s: float
    avg_speed: float            # 1.0 = walk, 2.5 = run, 8.0 = drive
    distance_m: float


@dataclass
class CombatEvent:
    timestamp_s: float
    style: CombatStyle
    weapon: str                 # "fists", "pistol", "knife", "grenade"
    target_type: str            # "guard", "civilian", "target"
    lethal: bool
    distance_m: float           # how far from target when killed


@dataclass
class DestructionEvent:
    timestamp_s: float
    destruction_type: DestructionType
    object_id: str              # e.g. "wall_north_3", "car_02"


@dataclass
class TelemetryLog:
    player_id: str = "player_01"
    mission_id: str = "M01_THE_DEAL"
    session_duration_s: float = 0.0
    navigation_events: List[NavigationEvent] = field(default_factory=list)
    combat_events: List[CombatEvent] = field(default_factory=list)
    destruction_events: List[DestructionEvent] = field(default_factory=list)
