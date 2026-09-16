"""Thin FastAPI routing layer for AdaptiveWorld2D."""

from dataclasses import asdict
from typing import Any, Annotated, Optional

import uvicorn
from fastapi import FastAPI, Header, Request
from fastapi.exceptions import RequestValidationError
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import JSONResponse
from pydantic import BaseModel

from ..application.game_state import GameState
from ..application.mission_service import MissionService
from ..application.profile_service import ProfileService
from ..application.session_store import SessionStore
from ..application.telemetry_service import accumulate_event
from ..infrastructure.director_provider import get_director, get_solver
from ..infrastructure.session_repository import SessionRepository


app = FastAPI(
    title="Adaptive World Director 2D",
    description="Backend for AdaptiveWorld2D Unity game. Reads M1 playstyle, generates adaptive M2 target.",
    version="2.0.0",
)
app.add_middleware(CORSMiddleware, allow_origins=["*"], allow_methods=["*"], allow_headers=["*"])


@app.exception_handler(RequestValidationError)
async def validation_exception_handler(request: Request, exc: RequestValidationError):
    body = await request.body()
    print(f"[ERROR 422] Body: {body.decode('utf-8', errors='ignore')} | Errors: {exc.errors()}")
    return JSONResponse(
        status_code=422,
        content={"detail": exc.errors(), "body": body.decode("utf-8", errors="ignore")},
    )


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
# Session Management
# ---------------------------------------------------------------------------

session_store = SessionStore()
repository = SessionRepository()


def _resolve_session(x_session_id: Annotated[Optional[str], Header()] = None):
    """FastAPI dependency: resolves the GameState for the current request.

    The ``X-Session-Id`` header is optional.  When absent (current Unity
    client never sends it) the call is routed to the default session,
    preserving existing single-player behavior exactly.
    """
    return session_store.get_or_create(x_session_id), x_session_id


# ---------------------------------------------------------------------------
# Endpoints
# ---------------------------------------------------------------------------

@app.get("/")
def root(x_session_id: Annotated[Optional[str], Header()] = None):
    gs = session_store.get_or_create(x_session_id)
    profile = gs.refresh()
    return {
        "service": "Adaptive World Director 2D",
        "status": "online",
        "current_archetype": profile.archetype(),
        "total_kills": profile.total_kills,
        "walls_destroyed": profile.walls_destroyed,
        "cars_burnt": profile.cars_burnt,
        "docs": "/docs",
    }


@app.post("/api/reset")
def reset(x_session_id: Annotated[Optional[str], Header()] = None):
    session_store.reset(x_session_id)
    return {"status": "reset", "message": "Session cleared to baseline."}


@app.post("/api/telemetry/event")
def ingest_event(payload: EventPayload, x_session_id: Annotated[Optional[str], Header()] = None):
    gs = session_store.get_or_create(x_session_id)
    accumulate_event(gs.log, payload)
    profile = gs.refresh()
    return {
        "status": "ingested",
        "event_type": payload.event_type.lower(),
        "current_archetype": profile.archetype(),
    }


@app.post("/api/telemetry/batch")
def ingest_batch(payload: BatchPayload, x_session_id: Annotated[Optional[str], Header()] = None):
    gs = session_store.get_or_create(x_session_id)
    for event in payload.events:
        accumulate_event(gs.log, event)
    profile = gs.refresh()
    return {
        "status": "batch_ingested",
        "count": len(payload.events),
        "current_archetype": profile.archetype(),
    }


@app.get("/api/player/profile")
def get_profile(x_session_id: Annotated[Optional[str], Header()] = None):
    gs = session_store.get_or_create(x_session_id)
    profile = gs.refresh()
    return {
        "player_id": profile.player_id,
        "archetype": profile.archetype(),
        "stealth_ratio": round(profile.stealth_ratio, 3),
        "aggression": round(profile.aggression, 3),
        "avg_speed": round(profile.avg_speed, 3),
        "vehicle_preference": round(profile.vehicle_preference, 3),
        "destruction_index": round(profile.destruction_index, 3),
        "shortcut_ratio": round(profile.shortcut_ratio, 3),
        "total_kills": profile.total_kills,
        "stealth_kills": profile.stealth_kills,
        "walls_destroyed": profile.walls_destroyed,
        "cars_burnt": profile.cars_burnt,
    }


@app.post("/api/mission/solve")
def solve_mission(request: SolveRequest, x_session_id: Annotated[Optional[str], Header()] = None):
    gs = session_store.get_or_create(x_session_id)
    profile = gs.refresh()
    manifest = MissionService(
        get_director(), get_solver(), repository
    ).solve(profile, gs.log, mission_id=request.mission_id, seed=request.seed, session_id=x_session_id)
    gs.last_manifest = manifest
    return manifest


@app.get("/api/export/sessions")
def export_all_sessions():
    sessions = repository.list_all()
    return {"total_sessions": len(sessions), "sessions": sessions}


@app.get("/api/export/training_data")
def export_training_data(x_session_id: Annotated[Optional[str], Header()] = None):
    gs = session_store.get_or_create(x_session_id)
    profile = gs.refresh()
    if gs.last_manifest is None:
        return {"error": "No manifest generated yet. Call /api/mission/solve first."}
    manifest = gs.last_manifest
    target = manifest.get("target", {}) if isinstance(manifest, dict) else asdict(manifest.target)
    guards = manifest.get("guards", {}) if isinstance(manifest, dict) else asdict(manifest.guards)
    environment = (
        manifest.get("environment", {})
        if isinstance(manifest, dict)
        else asdict(manifest.environment)
    )
    routes = manifest.get("routes", {}) if isinstance(manifest, dict) else asdict(manifest.routes)
    archetype = (
        manifest.get("player_archetype", profile.archetype())
        if isinstance(manifest, dict)
        else manifest.player_archetype
    )
    return {
        "training_sample": {
            "inputs": {
                "stealth_ratio": profile.stealth_ratio,
                "aggression": profile.aggression,
                "avg_speed": profile.avg_speed,
                "vehicle_preference": profile.vehicle_preference,
                "destruction_index": profile.destruction_index,
                "shortcut_ratio": profile.shortcut_ratio,
            },
            "outputs": {
                "archetype": archetype,
                "target_armor": target.get("armor"),
                "target_health": target.get("health"),
                "target_location": target.get("location_id"),
                "target_indoors": target.get("is_indoors"),
                "guard_count": guards.get("count"),
                "guard_vision_range": guards.get("vision_range"),
                "guard_reaction_speed": guards.get("reaction_speed"),
                "env_visibility": environment.get("visibility"),
                "env_time_of_day": environment.get("time_of_day"),
                "primary_route": routes.get("primary_route") or routes.get("primary_route_id"),
                "blocked_routes": routes.get("blocked_routes"),
            },
        }
    }


def start():
    uvicorn.run("backend.server:app", host="127.0.0.1", port=8001, reload=False, log_level="info")


if __name__ == "__main__":
    start()
