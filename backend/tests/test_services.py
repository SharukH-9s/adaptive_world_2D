import json
import os
import tempfile
import unittest
from unittest.mock import Mock

from fastapi import HTTPException

from backend.api.server import EventPayload
from backend.application.mission_service import MissionService
from backend.application.profile_service import ProfileService
from backend.application.telemetry_service import accumulate_event
from backend.infrastructure.session_repository import SessionRepository
from backend.profiler import PlayerProfile
from backend.telemetry import TelemetryLog


class TestTelemetryService(unittest.TestCase):
    def test_event_types_and_duration(self):
        log = TelemetryLog()
        for event in (
            EventPayload(event_type="navigation", timestamp_s=5, data={}),
            EventPayload(event_type="combat", timestamp_s=10, data={}),
            EventPayload(event_type="destruction", timestamp_s=15, data={}),
        ):
            accumulate_event(log, event)
        self.assertEqual(len(log.navigation_events), 1)
        self.assertEqual(len(log.combat_events), 1)
        self.assertEqual(len(log.destruction_events), 1)
        self.assertEqual(log.session_duration_s, 15)

    def test_invalid_events_raise_400(self):
        with self.assertRaises(HTTPException):
            accumulate_event(TelemetryLog(), EventPayload(event_type="unknown", data={}))
        with self.assertRaises(HTTPException):
            accumulate_event(
                TelemetryLog(),
                EventPayload(event_type="navigation", data={"duration_s": "bad"}),
            )


class TestProfileService(unittest.TestCase):
    def test_builds_profile(self):
        profile = ProfileService().build(TelemetryLog(player_id="test"))
        self.assertIsInstance(profile, PlayerProfile)
        self.assertEqual(profile.player_id, "test")


class TestMissionService(unittest.TestCase):
    def test_generates_and_persists_manifest(self):
        director = Mock()
        director.generate_node_2.return_value = {
            "target": {},
            "guards": {},
            "environment": {},
            "routes": {},
            "room_graph": {},
            "player_archetype": "GHOST",
        }
        repository = Mock()
        profile = PlayerProfile(player_id="test")
        manifest = MissionService(director, Mock(), repository).solve(
            profile, TelemetryLog(), seed=42
        )
        director.generate_node_2.assert_called_once()
        repository.save.assert_called_once()
        self.assertIn("target", manifest)
        self.assertIn("room_graph", manifest)


class TestSessionRepository(unittest.TestCase):
    def test_save_and_list_jsonl(self):
        with tempfile.TemporaryDirectory() as directory:
            path = os.path.join(directory, "sessions.jsonl")
            repository = SessionRepository(path)
            record = {"profile": {"player_id": "test"}}
            repository.save(record)
            self.assertEqual(repository.list_all(), [record])
            with open(path, encoding="utf-8") as sessions_file:
                self.assertEqual(json.loads(sessions_file.readline()), record)

    def test_save_with_session_id(self):
        with tempfile.TemporaryDirectory() as directory:
            path = os.path.join(directory, "sessions.jsonl")
            repository = SessionRepository(path)
            record = {"profile": {"player_id": "test"}}
            repository.save(record, session_id="session_xyz")
            records = repository.list_all()
            self.assertEqual(len(records), 1)
            self.assertEqual(records[0]["session_id"], "session_xyz")

    def test_missing_file_is_empty(self):
        with tempfile.TemporaryDirectory() as directory:
            self.assertEqual(SessionRepository(os.path.join(directory, "missing.jsonl")).list_all(), [])


class TestSessionStore(unittest.TestCase):
    def test_get_or_create_default(self):
        from backend.application.session_store import SessionStore
        store = SessionStore()
        s1 = store.get_or_create(None)
        s2 = store.get_or_create("")
        s3 = store.default
        self.assertIs(s1, s2)
        self.assertIs(s1, s3)

    def test_get_or_create_distinct_sessions(self):
        from backend.application.session_store import SessionStore
        store = SessionStore()
        s_a = store.get_or_create("session_a")
        s_b = store.get_or_create("session_b")
        self.assertIsNot(s_a, s_b)
        self.assertIs(store.get_or_create("session_a"), s_a)

    def test_reset_session(self):
        from backend.application.session_store import SessionStore
        store = SessionStore()
        s_a = store.get_or_create("session_a")
        s_b = store.get_or_create("session_b")
        s_a.log.navigation_events.append(Mock())
        s_b.log.navigation_events.append(Mock())

        store.reset("session_a")
        self.assertEqual(len(s_a.log.navigation_events), 0)
        self.assertEqual(len(s_b.log.navigation_events), 1)


if __name__ == "__main__":
    unittest.main()

