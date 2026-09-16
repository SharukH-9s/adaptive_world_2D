"""
test_golden_manifests.py
-------------------------
Golden manifest characterization tests:
Validates that M2TargetSolver output at seed 42 matches recorded golden benchmarks
on semantic fields (target armor, HP, guards, environment, routes, archetype).
"""
import os
import json
import unittest

from backend.profiler import build_profile
from backend.target_solver import M2TargetSolver
from backend.test_pipeline import MOCK_PLAYERS
from backend.api.server import EventPayload
from backend.application.telemetry_service import accumulate_event
from backend.telemetry import TelemetryLog


class TestGoldenManifests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.solver = M2TargetSolver()
        cls.golden_dir = os.path.join(os.path.dirname(__file__), "golden_manifests")

    def _load_golden(self, name: str) -> dict:
        path = os.path.join(self.golden_dir, f"{name.lower()}_seed42.json")
        with open(path, "r", encoding="utf-8") as f:
            return json.load(f)

    def _get_live_manifest(self, events: list) -> dict:
        log = TelemetryLog()
        for ev in events:
            accumulate_event(log, EventPayload(**ev))
        profile = build_profile(log)
        manifest = self.solver.solve(profile, seed=42)
        import dataclasses
        return dataclasses.asdict(manifest)

    def test_golden_ghost(self):
        golden = self._load_golden("GHOST")
        live = self._get_live_manifest(MOCK_PLAYERS["GHOST"])

        self.assertEqual(live["player_archetype"], golden["player_archetype"])
        self.assertEqual(live["target"]["location_id"], golden["target"]["location_id"])
        self.assertEqual(live["target"]["armor"], golden["target"]["armor"])
        self.assertEqual(live["target"]["health"], golden["target"]["health"])
        self.assertEqual(live["target"]["is_indoors"], golden["target"]["is_indoors"])
        self.assertEqual(live["guards"]["count"], golden["guards"]["count"])
        self.assertAlmostEqual(live["guards"]["vision_range"], golden["guards"]["vision_range"], places=2)
        self.assertEqual(live["environment"]["weather"], golden["environment"]["weather"])
        self.assertEqual(live["routes"]["blocked_routes"], golden["routes"]["blocked_routes"])

    def test_golden_wrecking_ball(self):
        golden = self._load_golden("WRECKING_BALL")
        live = self._get_live_manifest(MOCK_PLAYERS["WRECKING_BALL"])

        self.assertEqual(live["player_archetype"], golden["player_archetype"])
        self.assertEqual(live["target"]["location_id"], golden["target"]["location_id"])
        self.assertEqual(live["target"]["armor"], golden["target"]["armor"])
        self.assertEqual(live["target"]["health"], golden["target"]["health"])
        self.assertEqual(live["guards"]["count"], golden["guards"]["count"])
        self.assertEqual(live["environment"]["weather"], golden["environment"]["weather"])
        self.assertEqual(live["routes"]["blocked_routes"], golden["routes"]["blocked_routes"])

    def test_golden_speedrunner(self):
        golden = self._load_golden("SPEEDRUNNER")
        live = self._get_live_manifest(MOCK_PLAYERS["SPEEDRUNNER"])

        self.assertEqual(live["player_archetype"], golden["player_archetype"])
        self.assertEqual(live["target"]["location_id"], golden["target"]["location_id"])
        self.assertEqual(live["target"]["armor"], golden["target"]["armor"])
        self.assertEqual(live["target"]["health"], golden["target"]["health"])
        self.assertEqual(live["guards"]["count"], golden["guards"]["count"])
        self.assertEqual(live["environment"]["weather"], golden["environment"]["weather"])
        self.assertEqual(live["routes"]["blocked_routes"], golden["routes"]["blocked_routes"])

    def test_golden_aggressor(self):
        golden = self._load_golden("AGGRESSOR")
        live = self._get_live_manifest(MOCK_PLAYERS["AGGRESSOR"])

        self.assertEqual(live["player_archetype"], golden["player_archetype"])
        self.assertEqual(live["target"]["location_id"], golden["target"]["location_id"])
        self.assertEqual(live["target"]["armor"], golden["target"]["armor"])
        self.assertEqual(live["target"]["health"], golden["target"]["health"])
        self.assertEqual(live["guards"]["count"], golden["guards"]["count"])
        self.assertEqual(live["environment"]["weather"], golden["environment"]["weather"])
        self.assertEqual(live["routes"]["blocked_routes"], golden["routes"]["blocked_routes"])

    def test_golden_balanced(self):
        golden = self._load_golden("BALANCED")
        live = self._get_live_manifest(MOCK_PLAYERS["BALANCED"])

        self.assertEqual(live["player_archetype"], golden["player_archetype"])
        self.assertEqual(live["target"]["location_id"], golden["target"]["location_id"])
        self.assertEqual(live["target"]["armor"], golden["target"]["armor"])
        self.assertEqual(live["target"]["health"], golden["target"]["health"])
        self.assertEqual(live["guards"]["count"], golden["guards"]["count"])
        self.assertEqual(live["environment"]["weather"], golden["environment"]["weather"])


if __name__ == "__main__":
    unittest.main()
