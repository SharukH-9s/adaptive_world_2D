import urllib.request
import json

url = 'http://127.0.0.1:8001/api/mission/solve'
body = json.dumps({"seed": 42}).encode()
req = urllib.request.Request(url, data=body, headers={'Content-Type': 'application/json'}, method='POST')
res = urllib.request.urlopen(req)
raw = res.read().decode()
data = json.loads(raw)

print("=== TOP LEVEL KEYS ===")
print(list(data.keys()))

print("\n=== guards ===")
print(json.dumps(data.get("guards"), indent=2))

print("\n=== target ===")
print(json.dumps(data.get("target"), indent=2))

print("\n=== room_graph keys ===")
rg = data.get("room_graph")
if rg:
    print(list(rg.keys()))
    print("sectors count:", len(rg.get("sectors", [])))
    print("connections count:", len(rg.get("connections", [])))
else:
    print("room_graph is MISSING from response!")

print("\n=== Full JSON (first 2000 chars) ===")
print(raw[:2000])
