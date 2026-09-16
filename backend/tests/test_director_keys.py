"""
test_director_keys.py
---------------------
Unit tests for GeminiNarrativeDirector multi-key loading, rotation, and quota failover.
"""
import os
import unittest
from unittest.mock import Mock, patch

from backend.gemini_director import GeminiNarrativeDirector
from backend.telemetry import TelemetryLog
from backend.profiler import PlayerProfile


class TestDirectorKeyManagement(unittest.TestCase):
    def setUp(self):
        # Save original env
        self.original_env = os.environ.copy()
        for k in list(os.environ.keys()):
            if k.startswith("GEMINI_API_KEY"):
                del os.environ[k]

    def tearDown(self):
        # Restore original env
        for k in list(os.environ.keys()):
            if k.startswith("GEMINI_API_KEY"):
                del os.environ[k]
        for k, v in self.original_env.items():
            if k.startswith("GEMINI_API_KEY"):
                os.environ[k] = v

    def test_key_masking(self):
        self.assertEqual(GeminiNarrativeDirector._mask_key(""), "<empty>")
        self.assertEqual(GeminiNarrativeDirector._mask_key("short"), "***")
        self.assertEqual(
            GeminiNarrativeDirector._mask_key("AQ.Ab8RN6K8w7Y-AZ5WTQFIeKGIOxfwHf0LIKigWRFhduoID55qsg"),
            "AQ.A...5qsg"
        )

    def test_loads_single_primary_key(self):
        os.environ["GEMINI_API_KEY"] = "key_primary"
        with patch.object(GeminiNarrativeDirector, "_load_ground_truth", return_value={}):
            director = GeminiNarrativeDirector(read_env_file=False)
            keys = director._load_api_keys(read_env_file=False)
            self.assertEqual(keys, ["key_primary"])
            self.assertEqual(director.api_key, "key_primary")

    def test_loads_multiple_numbered_keys_in_order(self):
        os.environ["GEMINI_API_KEY"] = "key_0"
        os.environ["GEMINI_API_KEY_2"] = "key_2"
        os.environ["GEMINI_API_KEY_1"] = "key_1"
        os.environ["GEMINI_API_KEY_10"] = "key_10"
        with patch.object(GeminiNarrativeDirector, "_load_ground_truth", return_value={}):
            director = GeminiNarrativeDirector(read_env_file=False)
            keys = director._load_api_keys(read_env_file=False)
            self.assertEqual(keys, ["key_0", "key_1", "key_2", "key_10"])

    def test_deduplicates_and_handles_comma_separated(self):
        os.environ["GEMINI_API_KEY"] = "key_0, key_1"
        os.environ["GEMINI_API_KEY_1"] = "key_1; key_2"
        with patch.object(GeminiNarrativeDirector, "_load_ground_truth", return_value={}):
            director = GeminiNarrativeDirector(read_env_file=False)
            keys = director._load_api_keys(read_env_file=False)
            self.assertEqual(keys, ["key_0", "key_1", "key_2"])

    def test_loads_keys_from_env_file_when_enabled(self):
        # When read_env_file=True (default), it reads the active .env file
        with patch.object(GeminiNarrativeDirector, "_load_ground_truth", return_value={}):
            director = GeminiNarrativeDirector(read_env_file=True)
            self.assertTrue(len(director.api_keys) >= 4)
            self.assertTrue(director.api_key.startswith("AQ.Ab8RN6"))

    def test_key_rotation_on_rate_limit_failure(self):
        os.environ["GEMINI_API_KEY"] = "key_failing_1"
        os.environ["GEMINI_API_KEY_1"] = "key_working_2"

        with patch.object(GeminiNarrativeDirector, "_load_ground_truth", return_value={}):
            director = GeminiNarrativeDirector(read_env_file=False)

        profile = PlayerProfile(player_id="test")
        telemetry = TelemetryLog()

        calls = []

        def mock_call_gemini(prof, tele, seed=None, api_key=None):
            calls.append(api_key)
            if api_key == "key_failing_1":
                raise RuntimeError("429 RESOURCE_EXHAUSTED: Quota exceeded")
            return {"mission_id": 2, "player_archetype": "GHOST", "target": {}}

        with patch.object(director, "_call_gemini", side_effect=mock_call_gemini):
            manifest = director.generate_node_2(profile, telemetry, seed=42)

            self.assertEqual(manifest["mission_id"], 2)
            # Both keys were tried in order
            self.assertEqual(calls, ["key_failing_1", "key_working_2"])
            # Active key index updated to index 1
            self.assertEqual(director._active_key_index, 1)
            self.assertEqual(director.api_key, "key_working_2")

    def test_subsequent_request_starts_from_working_active_key(self):
        os.environ["GEMINI_API_KEY"] = "key_failing_0"
        os.environ["GEMINI_API_KEY_1"] = "key_working_1"

        with patch.object(GeminiNarrativeDirector, "_load_ground_truth", return_value={}):
            director = GeminiNarrativeDirector(read_env_file=False)

        profile = PlayerProfile(player_id="test")
        telemetry = TelemetryLog()

        calls = []

        def mock_call_gemini(prof, tele, seed=None, api_key=None):
            calls.append(api_key)
            if api_key == "key_failing_0":
                raise RuntimeError("Quota exceeded")
            return {"mission_id": 2, "player_archetype": "GHOST", "target": {}}

        with patch.object(director, "_call_gemini", side_effect=mock_call_gemini):
            # First request rotates past 0 to 1
            director.generate_node_2(profile, telemetry, seed=42)
            self.assertEqual(calls, ["key_failing_0", "key_working_1"])

            # Second request should directly try key_working_1 first!
            calls.clear()
            director.generate_node_2(profile, telemetry, seed=43)
            self.assertEqual(calls, ["key_working_1"])

    def test_falls_back_to_heuristic_when_all_keys_exhausted(self):
        os.environ["GEMINI_API_KEY"] = "key_exhausted_1"
        os.environ["GEMINI_API_KEY_1"] = "key_exhausted_2"

        with patch.object(GeminiNarrativeDirector, "_load_ground_truth", return_value={}):
            director = GeminiNarrativeDirector(read_env_file=False)

        profile = PlayerProfile(player_id="test")
        telemetry = TelemetryLog()

        calls = []

        def mock_call_gemini(prof, tele, seed=None, api_key=None):
            calls.append(api_key)
            raise RuntimeError("429 RESOURCE_EXHAUSTED")

        with patch.object(director, "_call_gemini", side_effect=mock_call_gemini):
            with patch.object(director, "_generate_fallback", return_value={"fallback": True}) as mock_fallback:
                result = director.generate_node_2(profile, telemetry, seed=42)
                self.assertEqual(calls, ["key_exhausted_1", "key_exhausted_2"])
                mock_fallback.assert_called_once()
                self.assertEqual(result, {"fallback": True})

    def test_falls_back_to_heuristic_when_no_keys(self):
        with patch.object(GeminiNarrativeDirector, "_load_ground_truth", return_value={}):
            director = GeminiNarrativeDirector(read_env_file=False)
            director.api_keys = []
            director.api_key = ""

        profile = PlayerProfile(player_id="test")
        telemetry = TelemetryLog()

        with patch.object(director, "_call_gemini") as mock_gemini:
            with patch.object(director, "_generate_fallback", return_value={"fallback": True}) as mock_fallback:
                result = director.generate_node_2(profile, telemetry, seed=42)
                mock_gemini.assert_not_called()
                mock_fallback.assert_called_once()
                self.assertEqual(result, {"fallback": True})


if __name__ == "__main__":
    unittest.main()
