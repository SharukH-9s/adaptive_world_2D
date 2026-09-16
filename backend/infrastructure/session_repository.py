"""Persistence adapter for gameplay session records."""

import json
import os
from typing import Any, Optional


class SessionRepository:
    """Reads and appends session records in the existing JSONL format.

    The ``save`` method now accepts an optional *session_id* that is stored
    alongside the record.  Existing rows without a ``session_id`` field are
    still loaded correctly — the field is simply absent in those records.
    """

    def __init__(self, path: str | None = None):
        default_path = os.path.join(
            os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
            "data",
            "sessions.jsonl",
        )
        self.path = path or default_path

    def save(self, record: dict[str, Any], session_id: Optional[str] = None) -> None:
        """Append *record* to the JSONL store.

        If *session_id* is supplied it is written as ``record["session_id"]``
        so the record can be correlated with a specific session later.
        Records saved without a *session_id* belong to the legacy default
        session and are written without the field to preserve backward
        compatibility with readers that pre-date Phase 8.
        """
        if session_id is not None:
            record = {**record, "session_id": session_id}
        directory = os.path.dirname(self.path)
        if directory:
            os.makedirs(directory, exist_ok=True)
        with open(self.path, "a", encoding="utf-8") as sessions_file:
            sessions_file.write(json.dumps(record) + "\n")

    def list_all(self) -> list[dict[str, Any]]:
        if not os.path.exists(self.path):
            return []

        sessions: list[dict[str, Any]] = []
        skipped = 0
        with open(self.path, "r", encoding="utf-8") as sessions_file:
            for line in sessions_file:
                line = line.strip()
                if not line:
                    continue
                try:
                    sessions.append(json.loads(line))
                except json.JSONDecodeError:
                    skipped += 1
        if skipped:
            print(f"[WARN] SessionRepository: skipped {skipped} malformed line(s)")
        return sessions
