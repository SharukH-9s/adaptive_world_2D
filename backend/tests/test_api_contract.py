"""
test_api_contract.py
--------------------
FastAPI HTTP API contract tests verifying schemas, status codes,
and response structure using FastAPI TestClient.
"""
import unittest
from fastapi.testclient import TestClient

from backend.api.server import app, session_store


from unittest.mock import patch, MagicMock


class TestApiContract(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.client = TestClient(app)
        cls.director_patch = patch("backend.api.server.get_director")
        cls.mock_director_fn = cls.director_patch.start()
        mock_dir = MagicMock()
        mock_dir.generate_node_2.return_value = {
            "mission_id": "M02_THE_HIT",
            "narrative_title": "Contract Mission",
            "target": {"location_id": "warehouse_interior", "armor": "light", "health": 80},
            "guards": {"count": 2, "disposition": "relaxed", "patrol_intensity": "low"},
            "environment": {"time_of_day": "night", "weather": "fog", "visibility": 0.5, "ambient_light": 0.3},
            "routes": {"primary_route": "alley_south", "blocked_routes": [], "cars_available": False},
            "room_graph": {"target_sector": "NW"},
            "player_archetype": "GHOST",
        }
        cls.mock_director_fn.return_value = mock_dir

    @classmethod
    def tearDownClass(cls):
        cls.director_patch.stop()

    def setUp(self):
        session_store.reset(None)

    def test_root_endpoint(self):
        res = self.client.get("/")
        self.assertEqual(res.status_code, 200)
        data = res.json()
        self.assertEqual(data["status"], "online")
        self.assertIn("service", data)
        self.assertIn("current_archetype", data)
        self.assertIn("total_kills", data)
        self.assertIn("walls_destroyed", data)
        self.assertIn("cars_burnt", data)

    def test_reset_endpoint(self):
        res = self.client.post("/api/reset")
        self.assertEqual(res.status_code, 200)
        data = res.json()
        self.assertEqual(data["status"], "reset")

    def test_telemetry_event_single(self):
        payload = {
            "event_type": "combat",
            "timestamp_s": 5.0,
            "data": {
                "style": "STEALTH",
                "weapon": "knife",
                "target_type": "guard",
                "lethal": True,
                "distance_m": 0.5
            }
        }
        res = self.client.post("/api/telemetry/event", json=payload)
        self.assertEqual(res.status_code, 200)
        data = res.json()
        self.assertEqual(data["status"], "ingested")
        self.assertEqual(data["event_type"], "combat")
        self.assertEqual(data["current_archetype"], "GHOST")

    def test_telemetry_batch(self):
        events = [
            {
                "event_type": "navigation",
                "timestamp_s": 5.0,
                "data": {"movement_type": "WALK", "surface": "ALLEY", "duration_s": 10.0, "avg_speed": 1.2, "distance_m": 12.0}
            },
            {
                "event_type": "combat",
                "timestamp_s": 15.0,
                "data": {"style": "STEALTH", "weapon": "knife", "target_type": "guard", "lethal": True, "distance_m": 0.5}
            }
        ]
        res = self.client.post("/api/telemetry/batch", json={"events": events})
        self.assertEqual(res.status_code, 200)
        data = res.json()
        self.assertEqual(data["status"], "batch_ingested")
        self.assertEqual(data["count"], 2)
        self.assertEqual(data["current_archetype"], "GHOST")

    def test_get_profile_contract(self):
        res = self.client.get("/api/player/profile")
        self.assertEqual(res.status_code, 200)
        p = res.json()
        expected_keys = [
            "player_id", "archetype", "stealth_ratio", "aggression",
            "avg_speed", "vehicle_preference", "destruction_index",
            "shortcut_ratio", "total_kills", "stealth_kills",
            "walls_destroyed", "cars_burnt"
        ]
        for k in expected_keys:
            self.assertIn(k, p, f"Missing key in profile response: {k}")

    def test_mission_solve_contract(self):
        # Ingest events first
        self.client.post("/api/telemetry/batch", json={
            "events": [
                {
                    "event_type": "destruction",
                    "timestamp_s": 10.0,
                    "data": {"destruction_type": "WALL_BREAK", "object_id": "w1"}
                }
            ]
        })
        res = self.client.post("/api/mission/solve", json={"mission_id": "M02_THE_HIT", "seed": 42})
        self.assertEqual(res.status_code, 200)
        m = res.json()
        self.assertIn("target", m)
        self.assertIn("guards", m)
        self.assertIn("environment", m)
        self.assertIn("routes", m)
        self.assertIn("room_graph", m)
        self.assertIn("player_archetype", m)

    def test_export_sessions_contract(self):
        res = self.client.get("/api/export/sessions")
        self.assertEqual(res.status_code, 200)
        data = res.json()
        self.assertIn("total_sessions", data)
        self.assertIn("sessions", data)
        self.assertIsInstance(data["sessions"], list)

    def test_export_training_data_contract(self):
        # First solve a mission so last_manifest exists
        self.client.post("/api/mission/solve", json={"seed": 42})
        res = self.client.get("/api/export/training_data")
        self.assertEqual(res.status_code, 200)
        data = res.json()
        self.assertIn("training_sample", data)
        sample = data["training_sample"]
        self.assertIn("inputs", sample)
        self.assertIn("outputs", sample)


if __name__ == "__main__":
    unittest.main()
