"""Application-wide solver and narrative director instances."""

from ..gemini_director import GeminiNarrativeDirector
from ..target_solver import M2TargetSolver


solver = M2TargetSolver()
director = GeminiNarrativeDirector()


def get_solver() -> M2TargetSolver:
    return solver


def get_director() -> GeminiNarrativeDirector:
    return director
