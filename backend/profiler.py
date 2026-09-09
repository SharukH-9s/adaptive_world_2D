"""
profiler.py
-----------
Reads M1 TelemetryLog and computes behavioral axes + archetype.
These scores drive the M2 target_solver.
"""
from dataclasses import dataclass
from .telemetry import TelemetryLog, CombatStyle, MovementType, DestructionType, SurfaceType


@dataclass
class PlayerProfile:
    player_id: str

    # Behavioral axes [0.0 - 1.0]
    stealth_ratio: float = 0.0       # ratio of stealth kills to all kills
    aggression: float = 0.0          # kill frequency * lethality rate
    avg_speed: float = 0.0           # normalised average movement speed
    vehicle_preference: float = 0.0  # fraction of time spent driving
    destruction_index: float = 0.0   # walls + cars destroyed (normalised)
    shortcut_ratio: float = 0.0      # fraction of time in alleys/buildings vs road

    # Raw counts (for AI training labels later)
    total_kills: int = 0
    stealth_kills: int = 0
    walls_destroyed: int = 0
    cars_burnt: int = 0
    drive_duration_s: float = 0.0
    total_nav_duration_s: float = 0.0

    def archetype(self) -> str:
        # 1. High destruction / explosive chaos
        if self.destruction_index > 0.5 or self.cars_burnt >= 2:
            return "WRECKING_BALL"
        # 2. Fast vehicle movement / rushing roads
        if self.avg_speed > 0.6 and self.vehicle_preference > 0.5:
            return "SPEEDRUNNER"
        # 3. Direct gunfire / high kill volume
        if self.aggression > 0.65 or self.total_kills >= 4:
            return "AGGRESSOR"
        # 4. Quiet / unseen / stealth knife or no alert
        if self.stealth_ratio > 0.65 and self.aggression < 0.3:
            return "GHOST"
        return "BALANCED"


def build_profile(log: TelemetryLog) -> PlayerProfile:
    p = PlayerProfile(player_id=log.player_id)

    # --- Combat analysis ---
    lethal_kills = [e for e in log.combat_events if e.lethal]
    p.total_kills = len(lethal_kills)
    p.stealth_kills = sum(1 for e in lethal_kills if e.style == CombatStyle.STEALTH)
    p.stealth_ratio = p.stealth_kills / max(p.total_kills, 1)
    
    # aggression = kills per minute of session, capped at 1.0
    session_mins = max(log.session_duration_s / 60.0, 1.0)
    p.aggression = min(p.total_kills / session_mins / 5.0, 1.0)
    if p.total_kills == 0:
        p.stealth_ratio = 1.0   # no kills = undisturbed ghost baseline
        p.aggression = 0.0

    # --- Navigation analysis ---
    drive_events = [e for e in log.navigation_events if e.movement_type == MovementType.DRIVE]
    p.drive_duration_s = sum(e.duration_s for e in drive_events)
    p.total_nav_duration_s = sum(e.duration_s for e in log.navigation_events) or 1.0
    p.vehicle_preference = p.drive_duration_s / p.total_nav_duration_s

    all_speeds = [e.avg_speed for e in log.navigation_events]
    raw_avg = sum(all_speeds) / len(all_speeds) if all_speeds else 0.0
    p.avg_speed = min(raw_avg / 8.0, 1.0)  # 8.0 = max drive speed

    shortcut_events = [e for e in log.navigation_events
                       if e.surface in (SurfaceType.ALLEY, SurfaceType.BUILDING_INTERIOR)]
    shortcut_dur = sum(e.duration_s for e in shortcut_events)
    p.shortcut_ratio = shortcut_dur / p.total_nav_duration_s

    # --- Destruction analysis ---
    p.walls_destroyed = sum(1 for e in log.destruction_events
                            if e.destruction_type == DestructionType.WALL_BREAK)
    p.cars_burnt = sum(1 for e in log.destruction_events
                       if e.destruction_type == DestructionType.CAR_BURN)
    total_destructions = p.walls_destroyed + p.cars_burnt
    p.destruction_index = min(total_destructions / 5.0, 1.0)

    return p
