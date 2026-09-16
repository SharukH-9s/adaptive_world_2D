"""
test_target_solver.py
---------------------
Characterization and contract tests for M2TargetSolver and RoomGraphManifest validation.
"""
import unittest

from backend.profiler import PlayerProfile
from backend.target_solver import (
    M2TargetSolver, RoomGraphManifest, SectorConfig, ConnectionConfig,
    SECTOR_GRID_COORDS, SECTOR_MODULES
)


class TestTargetSolver(unittest.TestCase):
    def setUp(self):
        self.solver = M2TargetSolver()

    def _make_profile(self, archetype: str) -> PlayerProfile:
        p = PlayerProfile(player_id="test_player")
        if archetype == "GHOST":
            p.stealth_ratio = 1.0
            p.aggression = 0.1
        elif archetype == "WRECKING_BALL":
            p.destruction_index = 0.8
            p.cars_burnt = 3
        elif archetype == "SPEEDRUNNER":
            p.avg_speed = 0.9
            p.vehicle_preference = 0.8
        elif archetype == "AGGRESSOR":
            p.aggression = 0.9
            p.total_kills = 6
        elif archetype == "BALANCED":
            p.aggression = 0.2
            p.avg_speed = 0.3
        return p

    def test_deterministic_solver_output_with_fixed_seed(self):
        profile = self._make_profile("GHOST")
        m1 = self.solver.solve(profile, seed=42)
        m2 = self.solver.solve(profile, seed=42)

        self.assertEqual(m1.player_archetype, m2.player_archetype)
        self.assertEqual(m1.target.location_id, m2.target.location_id)
        self.assertEqual(m1.target.armor, m2.target.armor)
        self.assertEqual(m1.target.health, m2.target.health)
        self.assertEqual(m1.guards.count, m2.guards.count)
        self.assertEqual(m1.environment.weather, m2.environment.weather)
        self.assertEqual(m1.routes.blocked_routes, m2.routes.blocked_routes)

    def test_all_archetypes_produce_valid_room_graphs(self):
        from backend.target_solver import build_room_graph
        for arch in ["GHOST", "WRECKING_BALL", "SPEEDRUNNER", "AGGRESSOR", "BALANCED"]:
            profile = self._make_profile(arch)
            manifest = self.solver.solve(profile, seed=42)
            rg = build_room_graph(arch)

            self.assertIsNotNone(rg, f"Room graph is None for {arch}")
            errors = rg.validate()
            self.assertEqual(errors, [], f"RoomGraph validation failed for {arch}: {errors}")
            self.assertEqual(len(rg.sectors), 9)
            self.assertEqual(len(rg.connections), 12)

    def test_archetype_target_differentiation(self):
        ghost_m = self.solver.solve(self._make_profile("GHOST"), seed=42)
        wb_m = self.solver.solve(self._make_profile("WRECKING_BALL"), seed=42)
        agg_m = self.solver.solve(self._make_profile("AGGRESSOR"), seed=42)

        # Wrecking ball should have heavy/reinforced armor and high HP
        self.assertIn(wb_m.target.armor, ["heavy", "reinforced"])
        self.assertGreaterEqual(wb_m.target.health, 180)

        # Aggressor should face heavy combat defenses
        self.assertEqual(agg_m.target.armor, "reinforced")
        self.assertGreaterEqual(agg_m.guards.count, 6)

        # Ghost should face indoor or stealth-friendly targets
        self.assertTrue(ghost_m.target.is_indoors)


class TestRoomGraphValidation(unittest.TestCase):

    def _build_valid_graph(self) -> RoomGraphManifest:
        # Standard valid 9-sector grid
        sectors = [
            SectorConfig(id=sid, module="SLUM_ALLEYWAYS", center_x=coords[0], center_y=coords[1])
            for sid, coords in SECTOR_GRID_COORDS.items()
        ]
        connections = [
            ConnectionConfig("NW", "NC", "open"),
            ConnectionConfig("NC", "NE", "open"),
            ConnectionConfig("MW", "MC", "open"),
            ConnectionConfig("MC", "ME", "open"),
            ConnectionConfig("SW", "SC", "open"),
            ConnectionConfig("SC", "SE", "open"),
            ConnectionConfig("NW", "MW", "open"),
            ConnectionConfig("MW", "SW", "open"),
            ConnectionConfig("NC", "MC", "open"),
            ConnectionConfig("MC", "SC", "open"),
            ConnectionConfig("NE", "ME", "open"),
            ConnectionConfig("ME", "SE", "open"),
        ]
        return RoomGraphManifest(
            sectors=sectors,
            connections=connections,
            target_sector="MC",
            player_spawn_sector="SW",
            extraction_sector="NE"
        )

    def test_valid_graph_passes(self):
        rg = self._build_valid_graph()
        self.assertEqual(rg.validate(), [])

    def test_missing_sector_fails(self):
        rg = self._build_valid_graph()
        rg.sectors.pop()  # now 8 sectors
        errors = rg.validate()
        self.assertTrue(any("Missing sectors" in e for e in errors))

    def test_invalid_connection_count_fails(self):
        rg = self._build_valid_graph()
        rg.connections.pop()  # now 11 connections
        errors = rg.validate()
        self.assertTrue(any("Missing connections" in e for e in errors))

    def test_unreachable_target_fails(self):
        rg = self._build_valid_graph()
        # Wall off all connections to MC
        for c in rg.connections:
            if c.from_sector == "MC" or c.to_sector == "MC":
                c.status = "walled_off"
        errors = rg.validate()
        self.assertTrue(any("unreachable from spawn" in e for e in errors))

    def test_invalid_sector_id_fails(self):
        rg = self._build_valid_graph()
        rg.target_sector = "INVALID_SECTOR"
        errors = rg.validate()
        self.assertTrue(any("not a valid sector id" in e for e in errors))


if __name__ == "__main__":
    unittest.main()
