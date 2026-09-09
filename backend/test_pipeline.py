"""
test_pipeline.py
----------------
Automated end-to-end test: simulates 4 player archetypes through M1 and
verifies that M2 manifests diverge meaningfully between them.
Can run against live server (default http://127.0.0.1:8001) or automatically
in-process via FastAPI TestClient if server is not currently running.
"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
if sys.platform == "win32":
    try: sys.stdout.reconfigure(encoding="utf-8")
    except: pass

import httpx, json
from backend.server import app
from fastapi.testclient import TestClient

BASE = "http://127.0.0.1:8001"
DIV = "=" * 72
THIN = "-" * 72

def post(client, path, data):
    r = client.post(path, json=data)
    r.raise_for_status()
    return r.json()

def get(client, path):
    r = client.get(path)
    r.raise_for_status()
    return r.json()

MOCK_PLAYERS = {
    "GHOST": [
        {"event_type":"navigation","timestamp_s":5,"data":{"movement_type":"WALK","surface":"ALLEY","duration_s":60,"avg_speed":1.2,"distance_m":72}},
        {"event_type":"navigation","timestamp_s":65,"data":{"movement_type":"WALK","surface":"BUILDING_INTERIOR","duration_s":30,"avg_speed":1.0,"distance_m":30}},
        {"event_type":"combat","timestamp_s":95,"data":{"style":"STEALTH","weapon":"knife","target_type":"guard","lethal":True,"distance_m":0.5}},
    ],
    "WRECKING_BALL": [
        {"event_type":"navigation","timestamp_s":5,"data":{"movement_type":"RUN","surface":"ROAD","duration_s":30,"avg_speed":2.5,"distance_m":75}},
        {"event_type":"destruction","timestamp_s":35,"data":{"destruction_type":"WALL_BREAK","object_id":"wall_north_1"}},
        {"event_type":"destruction","timestamp_s":40,"data":{"destruction_type":"CAR_BURN","object_id":"car_03"}},
        {"event_type":"destruction","timestamp_s":45,"data":{"destruction_type":"CAR_BURN","object_id":"car_04"}},
        {"event_type":"combat","timestamp_s":50,"data":{"style":"EXPLOSIVE","weapon":"grenade","target_type":"guard","lethal":True,"distance_m":6}},
        {"event_type":"combat","timestamp_s":55,"data":{"style":"DIRECT_ASSAULT","weapon":"pistol","target_type":"guard","lethal":True,"distance_m":4}},
    ],
    "SPEEDRUNNER": [
        {"event_type":"navigation","timestamp_s":5,"data":{"movement_type":"DRIVE","surface":"ROAD","duration_s":120,"avg_speed":8.0,"distance_m":960}},
        {"event_type":"navigation","timestamp_s":125,"data":{"movement_type":"DRIVE","surface":"ROAD","duration_s":60,"avg_speed":7.5,"distance_m":450}},
    ],
    "AGGRESSOR": [
        {"event_type":"navigation","timestamp_s":5,"data":{"movement_type":"RUN","surface":"ROAD","duration_s":20,"avg_speed":2.5,"distance_m":50}},
        {"event_type":"combat","timestamp_s":25,"data":{"style":"DIRECT_ASSAULT","weapon":"pistol","target_type":"guard","lethal":True,"distance_m":5}},
        {"event_type":"combat","timestamp_s":30,"data":{"style":"DIRECT_ASSAULT","weapon":"pistol","target_type":"guard","lethal":True,"distance_m":4}},
        {"event_type":"combat","timestamp_s":35,"data":{"style":"DIRECT_ASSAULT","weapon":"pistol","target_type":"civilian","lethal":True,"distance_m":3}},
        {"event_type":"combat","timestamp_s":40,"data":{"style":"DIRECT_ASSAULT","weapon":"shotgun","target_type":"guard","lethal":True,"distance_m":2}},
        {"event_type":"combat","timestamp_s":45,"data":{"style":"DIRECT_ASSAULT","weapon":"shotgun","target_type":"guard","lethal":True,"distance_m":3}},
        {"event_type":"combat","timestamp_s":50,"data":{"style":"DIRECT_ASSAULT","weapon":"pistol","target_type":"guard","lethal":True,"distance_m":4}},
    ],
}

def run_player(client, name, events):
    print(f"\n{THIN}")
    print(f"  PLAYER: {name}")
    print(THIN)
    post(client, "/api/reset", {})
    r = post(client, "/api/telemetry/batch", {"events": events})
    print(f"  Events ingested: {r['count']}  |  Archetype: {r['current_archetype']}")
    profile = get(client, "/api/player/profile")
    print(f"  stealth={profile['stealth_ratio']:.2f}  aggression={profile['aggression']:.2f}  "
          f"destruction={profile['destruction_index']:.2f}  speed={profile['avg_speed']:.2f}")
    manifest_r = post(client, "/api/mission/solve", {"seed": 42})
    t = manifest_r["target"]
    g = manifest_r["guards"]
    e = manifest_r["environment"]
    ro = manifest_r["routes"]
    print(f"  Target: {t['location_label']} | armor={t['armor']} | hp={t['health']} | indoors={t['is_indoors']}")
    print(f"  Guards: {g['count']} units | vision={g['vision_range']}m | reaction={g['reaction_speed']}x")
    print(f"  Env   : {e['ambient_label']} (vis={e['visibility']})")
    print(f"  Route : {ro['primary_label']} | blocked={ro['blocked_routes']}")
    return manifest_r

def main():
    print(f"\n{DIV}")
    print("  ADAPTIVE WORLD 2D - PIPELINE TEST")
    print(DIV)
    
    # Try connecting to live server first, fallback to in-process TestClient
    use_live = False
    try:
        live_client = httpx.Client(base_url=BASE, timeout=2.0)
        h = live_client.get("/").json()
        if h.get("status") == "online":
            client = live_client
            use_live = True
            print(f"  Connected to live server at {BASE}")
    except Exception:
        pass

    if not use_live:
        print("  [INFO] Running in-process test via FastAPI TestClient (Fast & Self-contained)")
        client = TestClient(app)

    h = get(client, "/")
    assert h["status"] == "online", f"Server not online: {h}"
    print(f"  [OK] Server online | {h['service']}")

    results = {}
    for name, events in MOCK_PLAYERS.items():
        results[name] = run_player(client, name, events)

    # Verify manifests differ
    print(f"\n{DIV}")
    print("  ADAPTATION VERIFICATION")
    print(DIV)
    archetypes = {n: r["player_archetype"] for n, r in results.items()}
    armors     = {n: r["target"]["armor"] for n, r in results.items()}
    guards     = {n: r["guards"]["count"] for n, r in results.items()}
    locations  = {n: r["target"]["location_id"] for n, r in results.items()}

    print(f"\n  {'Player':<16} {'Archetype':<16} {'Armor':<14} {'Guards':<8} {'Location'}")
    print(f"  {'-'*70}")
    for n in MOCK_PLAYERS:
        print(f"  {n:<16} {archetypes[n]:<16} {armors[n]:<14} {guards[n]:<8} {locations[n]}")

    unique_archetypes = len(set(archetypes.values()))
    unique_locations  = len(set(locations.values()))
    assert unique_archetypes >= 3, "Not enough archetype diversity!"
    assert unique_locations >= 3,  "Locations not adapting!"
    print(f"\n  [OK] {unique_archetypes}/4 unique archetypes")
    print(f"  [OK] {unique_locations}/4 unique target locations")
    print(f"\n{DIV}")
    print("  ALL PIPELINE TESTS PASSED")
    print(DIV)

if __name__ == "__main__":
    main()
