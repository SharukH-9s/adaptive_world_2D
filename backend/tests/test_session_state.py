"""
test_session_state.py
---------------------
Tests verifying multi-session isolation in the FastAPI backend via X-Session-Id header,
ensuring backward compatibility for requests without session headers.
"""

import tempfile
import unittest
from unittest.mock import patch
from fastapi.testclient import TestClient

from backend.api.server import app, session_store
from backend.infrastructure.session_repository import SessionRepository


class TestSessionStateIsolation(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.client = TestClient(app)
        cls.director_patch = patch("backend.api.server.get_director")
        cls.mock_director_fn = cls.director_patch.start()
        from unittest.mock import MagicMock
        mock_dir = MagicMock()
        mock_dir.generate_node_2.return_value = {
            "mission_id": "M02_THE_HIT",
            "narrative_title": "Isolated Mission",
            "target": {"location_id": "warehouse_interior", "armor": "light", "health": 80},
            "guards": {"count": 2, "disposition": "relaxed", "patrol_intensity": "low"},
            "environment": {"time_of_day": "night", "weather": "fog", "visibility": 0.5, "ambient_light": 0.3},
            "routes": {"primary_route": "alley_south", "blocked_routes": [], "cars_available": False},
            "room_graph": None,
            "player_archetype": "GHOST",
        }
        cls.mock_director_fn.return_value = mock_dir

    @classmethod
    def tearDownClass(cls):
        cls.director_patch.stop()

    def setUp(self):
        # Reset session store for clean test isolation
        session_store._store.clear()

    def test_default_session_no_header(self):
        """Requests without X-Session-Id route to default session."""
        payload = {
            "event_type": "combat",
            "timestamp_s": 1.0,
            "data": {
                "style": "STEALTH",
                "weapon": "knife",
                "target_type": "guard",
                "lethal": True,
                "distance_m": 0.5,
            },
        }
        res = self.client.post("/api/telemetry/event", json=payload)
        self.assertEqual(res.status_code, 200)

        profile_res = self.client.get("/api/player/profile")
        self.assertEqual(profile_res.status_code, 200)
        self.assertEqual(profile_res.json()["stealth_kills"], 1)
        self.assertEqual(session_store.default.profile.stealth_kills, 1)

    def test_two_independent_sessions(self):
        """Events ingested into session A are invisible to session B."""
        event_a = {
            "event_type": "combat",
            "timestamp_s": 1.0,
            "data": {
                "style": "STEALTH",
                "weapon": "knife",
                "target_type": "guard",
                "lethal": True,
                "distance_m": 0.5,
            },
        }
        event_b = {
            "event_type": "destruction",
            "timestamp_s": 2.0,
            "data": {
                "destruction_type": "WALL_BREAK",
                "object_id": "wall_1",
            },
        }

        self.client.post(
            "/api/telemetry/event", json=event_a, headers={"X-Session-Id": "sess_A"}
        )
        self.client.post(
            "/api/telemetry/event", json=event_b, headers={"X-Session-Id": "sess_B"}
        )

        prof_a = self.client.get("/api/player/profile", headers={"X-Session-Id": "sess_A"}).json()
        prof_b = self.client.get("/api/player/profile", headers={"X-Session-Id": "sess_B"}).json()

        self.assertEqual(prof_a["stealth_kills"], 1)
        self.assertEqual(prof_a["walls_destroyed"], 0)

        self.assertEqual(prof_b["stealth_kills"], 0)
        self.assertEqual(prof_b["walls_destroyed"], 1)

    def test_reset_one_session_leaves_other_intact(self):
        """POST /api/reset with session header only clears that session."""
        event = {
            "event_type": "combat",
            "timestamp_s": 1.0,
            "data": {
                "style": "STEALTH",
                "weapon": "knife",
                "target_type": "guard",
                "lethal": True,
                "distance_m": 0.5,
            },
        }
        self.client.post("/api/telemetry/event", json=event, headers={"X-Session-Id": "sess_1"})
        self.client.post("/api/telemetry/event", json=event, headers={"X-Session-Id": "sess_2"})

        # Reset session 1 only
        reset_res = self.client.post("/api/reset", headers={"X-Session-Id": "sess_1"})
        self.assertEqual(reset_res.status_code, 200)

        prof_1 = self.client.get("/api/player/profile", headers={"X-Session-Id": "sess_1"}).json()
        prof_2 = self.client.get("/api/player/profile", headers={"X-Session-Id": "sess_2"}).json()

        self.assertEqual(prof_1["stealth_kills"], 0)
        self.assertEqual(prof_2["stealth_kills"], 1)

    def test_profile_isolation(self):
        """Profiles computed for session A and session B remain strictly isolated."""
        events_ghost = [
            {"event_type": "combat", "timestamp_s": 1.0, "data": {"style": "STEALTH", "weapon": "knife", "target_type": "guard", "lethal": True, "distance_m": 0.5}},
        ]
        events_wrecking_ball = [
            {"event_type": "destruction", "timestamp_s": 1.0, "data": {"destruction_type": "CAR_BURN", "object_id": "c1"}},
            {"event_type": "destruction", "timestamp_s": 2.0, "data": {"destruction_type": "CAR_BURN", "object_id": "c2"}},
        ]

        self.client.post("/api/telemetry/batch", json={"events": events_ghost}, headers={"X-Session-Id": "ghost_sess"})
        self.client.post("/api/telemetry/batch", json={"events": events_wrecking_ball}, headers={"X-Session-Id": "wreck_sess"})

        root_ghost = self.client.get("/", headers={"X-Session-Id": "ghost_sess"}).json()
        root_wreck = self.client.get("/", headers={"X-Session-Id": "wreck_sess"}).json()

        self.assertEqual(root_ghost["current_archetype"], "GHOST")
        self.assertEqual(root_wreck["current_archetype"], "WRECKING_BALL")

    def test_manifest_isolation(self):
        """last_manifest in session A does not affect session B."""
        # session A solves a mission
        solve_res = self.client.post(
            "/api/mission/solve",
            json={"mission_id": "M02_THE_HIT", "seed": 42},
            headers={"X-Session-Id": "sess_A"},
        )
        self.assertEqual(solve_res.status_code, 200)

        # session A can export training data
        export_a = self.client.get("/api/export/training_data", headers={"X-Session-Id": "sess_A"})
        self.assertEqual(export_a.status_code, 200)
        self.assertIn("training_sample", export_a.json())

        # session B has not solved any mission yet
        export_b = self.client.get("/api/export/training_data", headers={"X-Session-Id": "sess_B"})
        self.assertEqual(export_b.status_code, 200)
        self.assertIn("error", export_b.json())

    def test_persistence_isolation(self):
        """Session ID is written to repository when solving missions."""
        with tempfile.TemporaryDirectory() as tmpdir:
            temp_repo = SessionRepository(f"{tmpdir}/sessions.jsonl")
            with patch("backend.api.server.repository", temp_repo):
                # Solve in custom session
                self.client.post(
                    "/api/mission/solve",
                    json={"mission_id": "M02_THE_HIT", "seed": 10},
                    headers={"X-Session-Id": "persisted_session_123"},
                )
                # Solve in default session
                self.client.post(
                    "/api/mission/solve",
                    json={"mission_id": "M02_THE_HIT", "seed": 20},
                )

                records = temp_repo.list_all()
                self.assertEqual(len(records), 2)
                self.assertEqual(records[0]["session_id"], "persisted_session_123")
                self.assertNotIn("session_id", records[1])

    def test_missing_session_id_routes_to_default(self):
        """None, empty string, or whitespace-only header routes to the default session."""
        payload = {
            "event_type": "combat",
            "timestamp_s": 1.0,
            "data": {
                "style": "STEALTH",
                "weapon": "knife",
                "target_type": "guard",
                "lethal": True,
                "distance_m": 0.5,
            },
        }
        # Send with empty string header
        self.client.post("/api/telemetry/event", json=payload, headers={"X-Session-Id": ""})
        # Send with whitespace string header
        self.client.post("/api/telemetry/event", json=payload, headers={"X-Session-Id": "   "})
        # Read from default session without header
        prof = self.client.get("/api/player/profile").json()
        self.assertEqual(prof["stealth_kills"], 2)

    def test_invalid_session_id_creates_fresh_state(self):
        """Any previously unseen session ID gets a fresh, clean GameState."""
        prof = self.client.get("/api/player/profile", headers={"X-Session-Id": "fresh_unknown_id"}).json()
        self.assertEqual(prof["total_kills"], 0)
        self.assertEqual(prof["walls_destroyed"], 0)
        self.assertEqual(prof["cars_burnt"], 0)
        # Empty log has 0 kills, representing baseline undisturbed state
        self.assertEqual(prof["archetype"], "GHOST")


if __name__ == "__main__":
    unittest.main()
