"""GameState: per-session telemetry log, profile, and last manifest."""

from __future__ import annotations

from typing import Optional

from ..application.profile_service import ProfileService
from ..profiler import PlayerProfile
from ..telemetry import TelemetryLog


class GameState:
    """Holds all mutable state for one gameplay session."""

    def __init__(self) -> None:
        self.reset()

    def reset(self) -> None:
        self.log: TelemetryLog = TelemetryLog()
        self.profile: Optional[PlayerProfile] = None
        self.last_manifest = None

    def refresh(self) -> PlayerProfile:
        return ProfileService().refresh(self)
