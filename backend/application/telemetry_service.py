"""Telemetry parsing and ingestion rules."""

from typing import Any
from fastapi import HTTPException

from ..telemetry import (
    CombatEvent,
    CombatStyle,
    DestructionEvent,
    DestructionType,
    MovementType,
    NavigationEvent,
    SurfaceType,
    TelemetryLog,
)


def accumulate_event(log: TelemetryLog, event: Any) -> None:
    """Append one API event to a telemetry log without rebuilding its profile."""
    event_type = event.event_type.lower()
    data = event.data
    timestamp = event.timestamp_s
    try:
        if event_type == "navigation":
            log.navigation_events.append(NavigationEvent(
                timestamp_s=timestamp,
                movement_type=getattr(
                    MovementType,
                    data.get("movement_type", "WALK").upper(),
                    MovementType.WALK,
                ),
                surface=getattr(
                    SurfaceType,
                    data.get("surface", "ROAD").upper(),
                    SurfaceType.ROAD,
                ),
                duration_s=float(data.get("duration_s", 1.0)),
                avg_speed=float(data.get("avg_speed", 1.0)),
                distance_m=float(data.get("distance_m", 0.0)),
            ))
        elif event_type == "combat":
            log.combat_events.append(CombatEvent(
                timestamp_s=timestamp,
                style=getattr(
                    CombatStyle,
                    data.get("style", "DIRECT_ASSAULT").upper(),
                    CombatStyle.DIRECT_ASSAULT,
                ),
                weapon=str(data.get("weapon", "pistol")),
                target_type=str(data.get("target_type", "guard")),
                lethal=bool(data.get("lethal", True)),
                distance_m=float(data.get("distance_m", 3.0)),
            ))
        elif event_type == "destruction":
            log.destruction_events.append(DestructionEvent(
                timestamp_s=timestamp,
                destruction_type=getattr(
                    DestructionType,
                    data.get("destruction_type", "WALL_BREAK").upper(),
                    DestructionType.WALL_BREAK,
                ),
                object_id=str(data.get("object_id", "unknown")),
            ))
        else:
            raise HTTPException(400, f"Unknown event_type: {event_type}")
        log.session_duration_s = max(log.session_duration_s, timestamp)
    except HTTPException:
        raise
    except Exception as error:
        raise HTTPException(400, f"Parse error: {error}")
