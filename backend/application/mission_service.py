"""Mission generation and persistence application service."""

from dataclasses import asdict
from datetime import datetime, timezone
from typing import Any, Optional


class MissionService:
    def __init__(self, director, solver, repository):
        self.director = director
        self.solver = solver
        self.repository = repository

    def solve(
        self,
        profile,
        telemetry,
        mission_id: str = "M02_THE_HIT",
        seed=None,
        session_id: Optional[str] = None,
    ) -> dict[str, Any]:
        manifest = self.director.generate_node_2(profile, telemetry, seed=seed)
        record = {
            "timestamp": datetime.now(timezone.utc).replace(tzinfo=None).isoformat(),
            "profile": asdict(profile),
            "manifest": manifest,
        }
        self.repository.save(record, session_id=session_id)
        return manifest


def solve_mission(profile, telemetry, director, solver, repository, mission_id="M02_THE_HIT", seed=None, session_id=None):
    return MissionService(director, solver, repository).solve(
        profile, telemetry, mission_id=mission_id, seed=seed, session_id=session_id
    )

