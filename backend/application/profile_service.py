"""Player profile application service."""

from ..profiler import PlayerProfile, build_profile
from ..telemetry import TelemetryLog


class ProfileService:
    def build(self, log: TelemetryLog) -> PlayerProfile:
        return build_profile(log)

    def refresh(self, state) -> PlayerProfile:
        state.profile = self.build(state.log)
        return state.profile


def build_player_profile(log: TelemetryLog) -> PlayerProfile:
    return ProfileService().build(log)
