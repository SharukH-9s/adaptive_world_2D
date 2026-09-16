"""
test_telemetry.py
-----------------
Characterization and contract tests for telemetry events, log accumulation,
and event serialization.
"""
import unittest
from fastapi import HTTPException

from backend.api.server import EventPayload
from backend.application.telemetry_service import accumulate_event
from backend.telemetry import (
    TelemetryLog, NavigationEvent, CombatEvent, DestructionEvent,
    MovementType, CombatStyle, SurfaceType, DestructionType
)


class TestTelemetryAccumulation(unittest.TestCase):
    def setUp(self):
        self.log = TelemetryLog()

    def test_navigation_event_parsing(self):
        payload = EventPayload(
            event_type="navigation",
            timestamp_s=12.5,
            data={
                "movement_type": "RUN",
                "surface": "ALLEY",
                "duration_s": 15.0,
                "avg_speed": 3.2,
                "distance_m": 48.0
            }
        )
        accumulate_event(self.log, payload)

        self.assertEqual(len(self.log.navigation_events), 1)
        ev = self.log.navigation_events[0]
        self.assertEqual(ev.timestamp_s, 12.5)
        self.assertEqual(ev.movement_type, MovementType.RUN)
        self.assertEqual(ev.surface, SurfaceType.ALLEY)
        self.assertEqual(ev.duration_s, 15.0)
        self.assertEqual(ev.avg_speed, 3.2)
        self.assertEqual(ev.distance_m, 48.0)
        self.assertEqual(self.log.session_duration_s, 12.5)

    def test_combat_event_parsing(self):
        payload = EventPayload(
            event_type="combat",
            timestamp_s=25.0,
            data={
                "style": "STEALTH",
                "weapon": "knife",
                "target_type": "sentry",
                "lethal": True,
                "distance_m": 1.2
            }
        )
        accumulate_event(self.log, payload)

        self.assertEqual(len(self.log.combat_events), 1)
        ev = self.log.combat_events[0]
        self.assertEqual(ev.timestamp_s, 25.0)
        self.assertEqual(ev.style, CombatStyle.STEALTH)
        self.assertEqual(ev.weapon, "knife")
        self.assertEqual(ev.target_type, "sentry")
        self.assertTrue(ev.lethal)
        self.assertEqual(ev.distance_m, 1.2)
        self.assertEqual(self.log.session_duration_s, 25.0)

    def test_destruction_event_parsing(self):
        payload = EventPayload(
            event_type="destruction",
            timestamp_s=40.0,
            data={
                "destruction_type": "WALL_BREAK",
                "object_id": "wall_north_01"
            }
        )
        accumulate_event(self.log, payload)

        self.assertEqual(len(self.log.destruction_events), 1)
        ev = self.log.destruction_events[0]
        self.assertEqual(ev.timestamp_s, 40.0)
        self.assertEqual(ev.destruction_type, DestructionType.WALL_BREAK)
        self.assertEqual(ev.object_id, "wall_north_01")
        self.assertEqual(self.log.session_duration_s, 40.0)

    def test_case_insensitivity(self):
        payload = EventPayload(
            event_type="NAVIGATION",
            timestamp_s=5.0,
            data={"movement_type": "walk", "surface": "road"}
        )
        accumulate_event(self.log, payload)
        self.assertEqual(len(self.log.navigation_events), 1)
        self.assertEqual(self.log.navigation_events[0].movement_type, MovementType.WALK)
        self.assertEqual(self.log.navigation_events[0].surface, SurfaceType.ROAD)

    def test_unknown_event_type_raises_400(self):
        payload = EventPayload(
            event_type="invalid_action",
            timestamp_s=1.0,
            data={"foo": "bar"}
        )
        with self.assertRaises(HTTPException) as ctx:
            accumulate_event(self.log, payload)
        self.assertEqual(ctx.exception.status_code, 400)
        self.assertIn("Unknown event_type", ctx.exception.detail)

    def test_malformed_number_raises_400(self):
        payload = EventPayload(
            event_type="navigation",
            timestamp_s=1.0,
            data={"duration_s": "not_a_float"}
        )
        with self.assertRaises(HTTPException) as ctx:
            accumulate_event(self.log, payload)
        self.assertEqual(ctx.exception.status_code, 400)
        self.assertIn("Parse error", ctx.exception.detail)


if __name__ == "__main__":
    unittest.main()
