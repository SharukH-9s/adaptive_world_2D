"""In-memory session store: maps session IDs to independent GameState instances."""

from __future__ import annotations

from typing import Optional

from .game_state import GameState


class SessionStore:
    """Maps session IDs to GameState instances.

    - A ``None`` or empty session ID routes to the ``"default"`` session,
      preserving the existing single-player behavior for the current Unity client.
    - Unknown session IDs are created on first access with a fresh ``GameState``.
    - No session TTL or eviction is introduced in this phase.
    """

    DEFAULT_SESSION_ID = "default"

    def __init__(self) -> None:
        self._store: dict[str, GameState] = {}

    def _resolve(self, session_id: Optional[str]) -> str:
        return session_id.strip() if session_id and session_id.strip() else self.DEFAULT_SESSION_ID

    def get_or_create(self, session_id: Optional[str]) -> GameState:
        """Return the GameState for *session_id*, creating it if absent."""
        sid = self._resolve(session_id)
        if sid not in self._store:
            self._store[sid] = GameState()
        return self._store[sid]

    def reset(self, session_id: Optional[str]) -> None:
        """Reset the GameState for *session_id* only."""
        sid = self._resolve(session_id)
        if sid in self._store:
            self._store[sid].reset()
        else:
            # Materialise a fresh state so callers can immediately use it.
            self._store[sid] = GameState()

    @property
    def default(self) -> GameState:
        """The default session's GameState (used by legacy callers via ``state``)."""
        return self.get_or_create(None)
