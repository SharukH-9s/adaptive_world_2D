"""
server.py
---------
FastAPI server for AdaptiveWorld2D.

Endpoints:
  GET  /                          → health
  POST /api/reset                 → clear session
  POST /api/telemetry/event       → ingest single event from Unity
  POST /api/telemetry/batch       → ingest batch from Unity
  GET  /api/player/profile        → live profile + archetype
  POST /api/mission/solve         → generate M2 manifest from M1 data
  GET  /api/export/training_data  → export (profile → manifest) pair for AI training
"""

import os
import json
from typing import Any, Optional
from dataclasses import asdict
from datetime import datetime
import uvicorn
from fastapi import FastAPI, HTTPException, Request
from fastapi.middleware.cors import CORSMiddleware
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse
from pydantic import BaseModel, Field

from .telemetry import (
    TelemetryLog, NavigationEvent, CombatEvent, DestructionEvent,
    MovementType, CombatStyle, SurfaceType, DestructionType
)
from .profiler import build_profile, PlayerProfile
from .target_solver import M2TargetSolver

app = FastAPI(
    title="Adaptive World Director 2D",
    description="Backend for AdaptiveWorld2D Unity game. Reads M1 playstyle, generates adaptive M2 target.",
    version="2.0.0"
)
app.add_middleware(CORSMiddleware, allow_origins=["*"], allow_methods=["*"], allow_headers=["*"])

@app.exception_handler(RequestValidationError)
async def validation_exception_handler(request: Request, exc: RequestValidationError):
    body = await request.body()
    print(f"[ERROR 422] Body: {body.decode('utf-8', errors='ignore')} | Errors: {exc.errors()}")
    return JSONResponse(status_code=422, content={"detail": exc.errors(), "body": body.decode('utf-8', errors='ignore')})


solver = M2TargetSolver()


# ---------------------------------------------------------------------------
# Server State
# ---------------------------------------------------------------------------

class GameState:
    def __init__(self):
        self.reset()

    def reset(self):
        self.log = TelemetryLog()
        self.profile: Optional[PlayerProfile] = None
        self.last_manifest = None

    def refresh(self) -> PlayerProfile:
        self.profile = build_profile(self.log)
        return self.profile

state = GameState()


# ---------------------------------------------------------------------------
# Request Schemas
# ---------------------------------------------------------------------------

class EventPayload(BaseModel):
    event_type: str
    timestamp_s: float = 0.0
    data: dict[str, Any]


class BatchPayload(BaseModel):
    events: list[EventPayload]


class SolveRequest(BaseModel):
    mission_id: str = "M02_THE_HIT"
    seed: Optional[int] = None


# ---------------------------------------------------------------------------
# Endpoints
# ---------------------------------------------------------------------------

@app.get("/")
def root():
    p = state.refresh()
    return {
        "service": "Adaptive World Director 2D",
        "status": "online",
        "current_archetype": p.archetype(),
        "total_kills": p.total_kills,
        "walls_destroyed": p.walls_destroyed,
        "cars_burnt": p.cars_burnt,
        "docs": "/docs"
    }


@app.post("/api/reset")
def reset():
    state.reset()
    return {"status": "reset", "message": "Session cleared to baseline."}


def _accumulate_event(ev: EventPayload) -> None:
    """Append a single event to the log without rebuilding the player profile."""
    etype = ev.event_type.lower()
    d = ev.data
    ts = ev.timestamp_s
    try:
        if etype == "navigation":
            state.log.navigation_events.append(NavigationEvent(
                timestamp_s=ts,
                movement_type=getattr(MovementType, d.get("movement_type", "WALK").upper(), MovementType.WALK),
                surface=getattr(SurfaceType, d.get("surface", "ROAD").upper(), SurfaceType.ROAD),
                duration_s=float(d.get("duration_s", 1.0)),
                avg_speed=float(d.get("avg_speed", 1.0)),
                distance_m=float(d.get("distance_m", 0.0)),
            ))
        elif etype == "combat":
            state.log.combat_events.append(CombatEvent(
                timestamp_s=ts,
                style=getattr(CombatStyle, d.get("style", "DIRECT_ASSAULT").upper(), CombatStyle.DIRECT_ASSAULT),
                weapon=str(d.get("weapon", "pistol")),
                target_type=str(d.get("target_type", "guard")),
                lethal=bool(d.get("lethal", True)),
                distance_m=float(d.get("distance_m", 3.0)),
            ))
        elif etype == "destruction":
            state.log.destruction_events.append(DestructionEvent(
                timestamp_s=ts,
                destruction_type=getattr(DestructionType, d.get("destruction_type", "WALL_BREAK").upper(), DestructionType.WALL_BREAK),
                object_id=str(d.get("object_id", "unknown")),
            ))
        else:
            raise HTTPException(400, f"Unknown event_type: {etype}")
        state.log.session_duration_s = max(state.log.session_duration_s, ts)
    except HTTPException:
        raise
    except Exception as e:
        raise HTTPException(400, f"Parse error: {e}")


@app.post("/api/telemetry/event")
def ingest_event(payload: EventPayload):
    _accumulate_event(payload)
    p = state.refresh()
    return {"status": "ingested", "event_type": payload.event_type.lower(), "current_archetype": p.archetype()}


@app.post("/api/telemetry/batch")
def ingest_batch(payload: BatchPayload):
    """Accumulate all events first, then rebuild profile once — O(N) not O(N²)."""
    for ev in payload.events:
        _accumulate_event(ev)
    p = state.refresh()
    return {"status": "batch_ingested", "count": len(payload.events), "current_archetype": p.archetype()}


@app.get("/api/player/profile")
def get_profile():
    p = state.refresh()
    return {
        "player_id": p.player_id,
        "archetype": p.archetype(),
        "stealth_ratio": round(p.stealth_ratio, 3),
        "aggression": round(p.aggression, 3),
        "avg_speed": round(p.avg_speed, 3),
        "vehicle_preference": round(p.vehicle_preference, 3),
        "destruction_index": round(p.destruction_index, 3),
        "shortcut_ratio": round(p.shortcut_ratio, 3),
        "total_kills": p.total_kills,
        "stealth_kills": p.stealth_kills,
        "walls_destroyed": p.walls_destroyed,
        "cars_burnt": p.cars_burnt,
    }


@app.post("/api/mission/solve")
def solve_mission(req: SolveRequest):
    p = state.refresh()
    manifest = solver.solve(p, seed=req.seed)
    state.last_manifest = manifest

    # Persist session and final Map 2 manifest to disk for AI training & analytics
    try:
        data_dir = os.path.join(os.path.dirname(os.path.abspath(__file__)), "data")
        os.makedirs(data_dir, exist_ok=True)
        sessions_file = os.path.join(data_dir, "sessions.jsonl")
        record = {
            "timestamp": datetime.utcnow().isoformat(),
            "profile": asdict(p),
            "manifest": manifest.to_dict()
        }
        with open(sessions_file, "a", encoding="utf-8") as f:
            f.write(json.dumps(record) + "\n")
        print(f"[DATA] Session successfully saved to {sessions_file}")
    except Exception as e:
        print(f"[DATA ERROR] Failed to save session to disk: {e}")

    return manifest.to_dict()


@app.get("/api/export/sessions")
def export_all_sessions():
    """Returns all persisted gameplay sessions from disk."""
    sessions_file = os.path.join(os.path.dirname(os.path.abspath(__file__)), "data", "sessions.jsonl")
    if not os.path.exists(sessions_file):
        return {"total_sessions": 0, "sessions": []}
    
    sessions = []
    with open(sessions_file, "r", encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if line:
                sessions.append(json.loads(line))
    return {"total_sessions": len(sessions), "sessions": sessions}


@app.get("/api/export/training_data")
def export_training_data():
    """
    Export a (profile_inputs → manifest_outputs) pair.
    Future: feed these into an ML model to replace the rule-based solver.
    """
    p = state.refresh()
    if state.last_manifest is None:
        return {"error": "No manifest generated yet. Call /api/mission/solve first."}
    m = state.last_manifest
    return {
        "training_sample": {
            "inputs": {
                "stealth_ratio": p.stealth_ratio,
                "aggression": p.aggression,
                "avg_speed": p.avg_speed,
                "vehicle_preference": p.vehicle_preference,
                "destruction_index": p.destruction_index,
                "shortcut_ratio": p.shortcut_ratio,
            },
            "outputs": {
                "archetype": m.player_archetype,
                "target_armor": m.target.armor,
                "target_health": m.target.health,
                "target_location": m.target.location_id,
                "target_indoors": m.target.is_indoors,
                "guard_count": m.guards.count,
                "guard_vision_range": m.guards.vision_range,
                "guard_reaction_speed": m.guards.reaction_speed,
                "env_visibility": m.environment.visibility,
                "env_time_of_day": m.environment.time_of_day,
                "primary_route": m.routes.primary_route_id,
                "blocked_routes": m.routes.blocked_routes,
            }
        }
    }


def start():
    uvicorn.run("backend.server:app", host="127.0.0.1", port=8001, reload=False, log_level="info")

if __name__ == "__main__":
    start()
