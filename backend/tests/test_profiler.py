"""
test_profiler.py
----------------
Unit and characterization tests for profiler calculations, metrics normalization,
and archetype classification thresholds.
"""
import unittest

from backend.telemetry import (
    TelemetryLog, NavigationEvent, CombatEvent, DestructionEvent,
    MovementType, CombatStyle, SurfaceType, DestructionType
)
from backend.profiler import build_profile, PlayerProfile


class TestProfilerCalculations(unittest.TestCase):

    def test_empty_log_defaults_to_ghost_baseline(self):
        log = TelemetryLog(player_id="test_player")
        p = build_profile(log)

        self.assertEqual(p.player_id, "test_player")
        self.assertEqual(p.total_kills, 0)
        self.assertEqual(p.stealth_kills, 0)
        self.assertEqual(p.stealth_ratio, 1.0)
        self.assertEqual(p.aggression, 0.0)
        self.assertEqual(p.avg_speed, 0.0)
        self.assertEqual(p.vehicle_preference, 0.0)
        self.assertEqual(p.destruction_index, 0.0)
        self.assertEqual(p.archetype(), "GHOST")

    def test_wrecking_ball_by_cars_burnt(self):
        log = TelemetryLog()
        log.session_duration_s = 60.0
        log.destruction_events.append(DestructionEvent(1.0, DestructionType.CAR_BURN, "car_1"))
        log.destruction_events.append(DestructionEvent(2.0, DestructionType.CAR_BURN, "car_2"))
        
        p = build_profile(log)
        self.assertEqual(p.cars_burnt, 2)
        self.assertEqual(p.destruction_index, 0.4)
        self.assertEqual(p.archetype(), "WRECKING_BALL")

    def test_wrecking_ball_by_destruction_index_threshold(self):
        log = TelemetryLog()
        log.session_duration_s = 60.0
        # 3 wall breaks = 3/5 = 0.6 > 0.5
        for i in range(3):
            log.destruction_events.append(DestructionEvent(float(i), DestructionType.WALL_BREAK, f"wall_{i}"))

        p = build_profile(log)
        self.assertAlmostEqual(p.destruction_index, 0.6)
        self.assertEqual(p.archetype(), "WRECKING_BALL")

    def test_speedrunner_thresholds(self):
        log = TelemetryLog()
        log.session_duration_s = 60.0
        # Driving 60s at 7.0 m/s -> avg_speed = 7/8 = 0.875 > 0.6, vehicle_preference = 1.0 > 0.5
        log.navigation_events.append(NavigationEvent(
            timestamp_s=5.0,
            movement_type=MovementType.DRIVE,
            surface=SurfaceType.ROAD,
            duration_s=60.0,
            avg_speed=7.0,
            distance_m=420.0
        ))

        p = build_profile(log)
        self.assertGreater(p.avg_speed, 0.6)
        self.assertGreater(p.vehicle_preference, 0.5)
        self.assertEqual(p.archetype(), "SPEEDRUNNER")

    def test_aggressor_by_kill_volume(self):
        log = TelemetryLog()
        log.session_duration_s = 300.0  # 5 minutes
        # 4 lethal kills
        for i in range(4):
            log.combat_events.append(CombatEvent(
                timestamp_s=float(i * 10),
                style=CombatStyle.DIRECT_ASSAULT,
                weapon="pistol",
                target_type="guard",
                lethal=True,
                distance_m=5.0
            ))

        p = build_profile(log)
        self.assertEqual(p.total_kills, 4)
        self.assertEqual(p.archetype(), "AGGRESSOR")

    def test_aggressor_by_aggression_rate(self):
        log = TelemetryLog()
        log.session_duration_s = 60.0  # 1 minute
        # 4 kills in 1 minute -> 4 / 1 / 5 = 0.8 > 0.65
        for i in range(4):
            log.combat_events.append(CombatEvent(
                timestamp_s=float(i * 5),
                style=CombatStyle.DIRECT_ASSAULT,
                weapon="shotgun",
                target_type="guard",
                lethal=True,
                distance_m=3.0
            ))

        p = build_profile(log)
        self.assertGreater(p.aggression, 0.65)
        self.assertEqual(p.archetype(), "AGGRESSOR")

    def test_ghost_archetype(self):
        log = TelemetryLog()
        log.session_duration_s = 180.0  # 3 minutes
        # 1 stealth kill in 3 minutes -> aggression = 1 / 3 / 5 = 0.067 < 0.3, stealth_ratio = 1.0 > 0.65
        log.combat_events.append(CombatEvent(
            timestamp_s=60.0,
            style=CombatStyle.STEALTH,
            weapon="knife",
            target_type="guard",
            lethal=True,
            distance_m=0.5
        ))

        p = build_profile(log)
        self.assertEqual(p.stealth_ratio, 1.0)
        self.assertLess(p.aggression, 0.3)
        self.assertEqual(p.archetype(), "GHOST")

    def test_balanced_archetype(self):
        log = TelemetryLog()
        log.session_duration_s = 60.0
        # 1 direct assault kill, 1 walk nav event -> doesn't hit any high specialization threshold
        log.navigation_events.append(NavigationEvent(
            timestamp_s=10.0,
            movement_type=MovementType.WALK,
            surface=SurfaceType.ROAD,
            duration_s=40.0,
            avg_speed=2.0,
            distance_m=80.0
        ))
        log.combat_events.append(CombatEvent(
            timestamp_s=30.0,
            style=CombatStyle.DIRECT_ASSAULT,
            weapon="pistol",
            target_type="guard",
            lethal=True,
            distance_m=5.0
        ))

        p = build_profile(log)
        self.assertEqual(p.stealth_ratio, 0.0)
        self.assertEqual(p.total_kills, 1)
        self.assertLess(p.aggression, 0.65)
        self.assertEqual(p.archetype(), "BALANCED")

    def test_shortcut_ratio_calculation(self):
        log = TelemetryLog()
        log.session_duration_s = 100.0
        # 30s road, 50s alley, 20s interior -> shortcut ratio = (50 + 20) / 100 = 0.70
        log.navigation_events.append(NavigationEvent(1.0, MovementType.WALK, SurfaceType.ROAD, 30.0, 2.0, 60.0))
        log.navigation_events.append(NavigationEvent(2.0, MovementType.WALK, SurfaceType.ALLEY, 50.0, 2.0, 100.0))
        log.navigation_events.append(NavigationEvent(3.0, MovementType.WALK, SurfaceType.BUILDING_INTERIOR, 20.0, 1.0, 20.0))

        p = build_profile(log)
        self.assertAlmostEqual(p.shortcut_ratio, 0.70)


if __name__ == "__main__":
    unittest.main()
