# Architecture Improvement Plan

## Purpose

This document describes a gradual plan for improving the less mature architectural areas of AdaptiveWorld2D without changing intended gameplay behavior.

The project already follows recognizable game-development patterns:

- Unity component-based architecture
- `Update` / `FixedUpdate` simulation loops
- Event-driven communication
- Manager and coordinator objects
- Lightweight AI state machines
- Data-driven procedural world generation
- A Unity client connected to a Python/FastAPI adaptive director

The goal is not to rewrite the project into a different architecture. The goal is to improve boundaries, testability, maintainability, and reliability while preserving the current runtime behavior.

## Important Safety Principle

It is not possible to honestly guarantee that a software change will never break anything before it is implemented and tested. The safest engineering approach is therefore an incremental migration:

- preserve existing public APIs;
- preserve Unity serialized fields and scene wiring;
- add characterization tests before refactoring;
- keep compatibility paths during migration;
- make each phase independently reviewable and revertible;
- compare old and new behavior before removing old code;
- avoid combining architecture changes with gameplay tuning changes.

The central rule is:

> Refactor the internal implementation, not the external behavior.

---

# Current Less-Mature Areas

## 1. Global state and singleton dependencies

Several systems use singleton-style access:

- `AdaptiveDirectorClient.Instance`
- `TelemetryReporter.Instance`
- `MissionManager.Instance`
- `CameraFollow2D.Instance`

This is common in Unity, but it creates hidden dependencies, makes testing harder, and increases sensitivity to scene initialization order.

## 2. Mixed responsibilities inside Unity components

Some `MonoBehaviour` classes perform several unrelated jobs.

Examples:

- `PlayerController` handles input, movement, terrain detection, and telemetry aggregation.
- `AdaptiveDirectorClient` handles HTTP transport, event queueing, manifest caching, scene-load behavior, and global state.
- `WorldManifestApplier` coordinates manifest loading, fallback behavior, world generation, spawning, environment settings, routes, and extraction.
- Enemy components combine perception, state, movement, and combat behavior.

## 3. Implicit AI state machines

Enemy behavior is currently represented through combinations of fields such as:

```csharp
isDead
isAlerted
isTargetBoss
```

This is an implicit state machine. It works, but explicit states would make transitions and testing clearer.

## 4. Runtime object discovery

Some components locate dependencies dynamically:

```csharp
GameObject.FindGameObjectWithTag("Player")
FindObjectOfType<WorldManifestApplier>()
SomeManager.Instance
```

This is convenient, but scene wiring and runtime order become implicit.

## 5. Backend endpoint responsibilities

`backend/server.py` currently contains API endpoint definitions, state mutation, telemetry parsing, profile rebuilding, mission solving, and persistence coordination.

The current backend works, but its responsibilities should eventually be separated into:

```text
API layer
Application services
Domain logic
Infrastructure and persistence
```

## 6. Global backend session state

The backend currently stores state in a process-global object:

```python
state = GameState()
```

This is suitable for the current local single-player workflow, but it is not safe for multiple concurrent sessions.

---

# Migration Plan

## Phase 0 — Establish a Behavior Baseline

### Goal

Record the current behavior before making architectural changes.

### Tasks

1. Run the existing backend pipeline test in `backend/test_pipeline.py`.
2. Record expected outputs for:
   - all player archetypes;
   - telemetry ingestion;
   - profile calculation;
   - mission manifest generation;
   - room graph validity;
   - fallback behavior.
3. Manually verify Unity flows:
   - Map 1 starts normally;
   - telemetry is queued;
   - telemetry is flushed;
   - Map 2 loads;
   - an in-memory manifest is applied;
   - a disk-cached manifest is applied;
   - a network-generated manifest is applied;
   - the fallback manifest is applied when the backend is unavailable;
   - mission progression remains functional;
   - guards, target, weather, routes, and extraction remain functional.
4. Record the current public API surface of:
   - `Assets/Scripts/Director/AdaptiveDirectorClient.cs`;
   - `Assets/Scripts/Director/WorldManifestApplier.cs`;
   - `Assets/Scripts/Mission/MissionManager.cs`.

### Safety Rule

No production behavior is changed in this phase.

---

## Phase 1 — Add Characterization and Contract Tests

### Goal

Create tests that describe what the current system already does before moving code.

The existing `backend/test_pipeline.py` should remain as an end-to-end regression test. It should not be replaced by smaller tests.

### Backend coverage

Add focused tests for:

- telemetry event parsing;
- batch telemetry ingestion;
- invalid event types;
- profile calculations;
- archetype threshold behavior;
- deterministic solver output;
- room graph validation;
- manifest schema and required fields;
- fallback director behavior;
- persistence behavior;
- API response compatibility.

### Unity coverage

Where practical, add Unity EditMode or PlayMode tests for:

- room graph validation;
- manifest validation;
- fallback manifest creation;
- manifest-to-component configuration;
- enemy state transitions;
- telemetry payload serialization.

### Golden manifest tests

For fixed telemetry inputs and seed `42`, compare important fields rather than raw JSON text:

```text
player_archetype
target location
target armor
target health
guard count
guard statistics
environment weather
blocked routes
room graph validity
```

This avoids false failures caused by JSON property ordering.

### Safety Rule

Tests are added before production refactoring begins.

---

## Phase 2 — Separate Pure Calculations from Unity Components

### Goal

Move calculations that do not require Unity scene objects into small, deterministic,
testable classes while preserving the current Unity lifecycle, public API, serialized
data, side effects, and fallback behavior.

This phase is an extraction only. It must not combine calculation extraction with
gameplay tuning, stricter validation policy, state-machine changes, or runtime
dependency rewiring.

### Initial extraction candidates

- Player movement calculations in `Assets/Scripts/Player/PlayerController.cs`.
- Enemy decision calculations in `Assets/Scripts/AI/EnemyPatrol.cs`.
- Room graph validation in `Assets/Scripts/World/SectorRuntimeBuilder.cs`.
- Manifest validation and normalization.

### Possible new classes

```text
PlayerMovementState
PlayerMovementModel
EnemyState
EnemyDecisionModel
RoomGraphValidator
ManifestValidator
```

The intended flow becomes:

```text
Unity input and component state
    ↓
pure calculation/model
    ↓
Unity Rigidbody2D, GameObject, and animation updates
```

### Compatibility requirements

Do not rename, remove, reorder, or change the type/default value of existing public
or serialized fields. This includes, but is not limited to:

```csharp
walkSpeed
runSpeed
driveSpeed
isDriving
isSprinting
currentVehicle
maxHealth
currentHealth
damageResistance
patrolSpeed
chaseSpeed
waypoints
waypointTolerance
feelerDistance
obstacleAvoidanceLayer
```

The existing `MonoBehaviour` remains responsible for reading Unity state, polling
input, performing physics queries, applying Rigidbody changes, changing transforms
and animations, logging, spawning decals, broadcasting alerts, and invoking mission
events. The extracted models must not access `GameObject`, `Transform`, `Rigidbody2D`,
`Physics2D`, singletons, scene objects, or Unity time.

Unity `Vector2` values are acceptable for the calculation boundary because the
models are still EditMode-testable, but the models must not introduce hidden
normalization or side effects.

### Player movement extraction

Add `PlayerMovementModel` in `AdaptiveWorld2D.Player` with deterministic operations
for:

- selecting effective speed: driving takes precedence over sprinting;
- calculating velocity from the already-normalized input direction;
- calculating kinematic displacement using the supplied fixed delta time;
- calculating the existing facing angle, including the `-90f` sprite offset;
- determining `DRIVE`, `RUN`, or `WALK` movement type;
- calculating average speed from accumulated distance and the actual interval.

`PlayerController` must retain:

- `Input.GetAxisRaw` and direct-key fallback polling in `Update`;
- input normalization exactly once;
- the `0.05f` movement threshold before rotation;
- `Update` telemetry timing and surface detection;
- `FixedUpdate` physics timing;
- the kinematic `MovePosition` branch;
- the dynamic `linearVelocity` branch and positional fallback;
- all existing telemetry event names, fields, and reset timing.

The model must not replace Rigidbody execution with displacement application.

### Enemy decision and combat extraction

Add `EnemyDecisionModel` in `AdaptiveWorld2D.AI` with deterministic operations for:

- damage mitigation using the current `Mathf.RoundToInt` equivalent;
- the existing minimum-one-damage rule;
- updated health and lethal result;
- strict boss retreat evaluation using `< 40%`, not `<= 40%`;
- the `0.85f` wounded-boss chase-speed multiplier;
- waypoint index progression with the existing modulo cycle;
- look-angle calculation using the existing `-90f` orientation offset.

The model must preserve the distinction between mitigated damage and applied damage
in logs and tests. It must not clamp health to zero unless the current component
already does so.

`EnemyPatrol` must retain:

- all three obstacle-avoidance raycasts and their feeler distances;
- `Physics2D.Raycast` calls and `Rigidbody2D.linearVelocity` writes in `FixedUpdate`;
- null and empty waypoint handling;
- movement-before-waypoint-advance ordering;
- `isAlerted`, `isDead`, and `_hasSummonedReinforcements` state changes;
- one-time reinforcement broadcasting;
- blood decals, collider changes, death visuals, and mission completion callbacks.

Only the decision/math expressions move to the model.

### Room graph validation extraction

Add `RoomGraphValidator` in `AdaptiveWorld2D.World` and redirect
`SectorRuntimeBuilder.Validate(roomGraph)` to it.

The first extraction must reproduce the current Unity validator's contract exactly:

- exactly 9 sectors;
- the expected `NW` through `SE` sector IDs;
- exactly 12 connections;
- allowed connection status values;
- non-empty target, player-spawn, and extraction designations.

Do not silently add adjacency, duplicate-edge, endpoint, or reachability checks in
this extraction. Those checks exist in the backend validator but are a separate
behavior change and require their own compatibility decision and tests.

The validator must return errors rather than throw for null lists, null entries, or
malformed fields. `SectorRuntimeBuilder` must retain its current logging and abort
behavior after validation.

### Manifest sanitization and validation extraction

Add `ManifestValidator` in `AdaptiveWorld2D.Director`.

`Sanitize(MissionManifest)` must preserve the current null semantics:

- a missing `room_graph` remains `null`;
- an existing `room_graph` gets non-null `sectors` and `connections` lists;
- existing sectors get non-null `guards` and `props` lists;
- an existing `routes` object gets non-null `blocked_routes` and
  `reinforced_barricades` lists;
- missing `target`, `guards`, `environment`, or `routes` objects are not invented.

`MissionManifest.Sanitize()` remains as the compatibility API and delegates to
`ManifestValidator.Sanitize(this)`.

`Validate(MissionManifest, out List<string> errors)` must initially verify structural
completeness without changing the current application path. Do not reject a
manifest, create a fallback, or partially mutate the scene merely because the new
validator reports an error until an explicit integration point and fallback policy
have been defined and tested. In particular, preserve the distinction between:

```text
no room_graph        -> existing coordinate/fallback behavior
empty room_graph     -> existing SectorRuntimeBuilder validation behavior
valid room_graph     -> existing sector build behavior
```

### Migration order

Perform the extraction in this order:

1. Add characterization tests for current formulas and edge cases.
2. Add `PlayerMovementModel` and delegate calculations without changing lifecycle code.
3. Add `EnemyDecisionModel` and delegate calculations while retaining all Unity side effects.
4. Add `RoomGraphValidator` with the current Unity rules only.
5. Add `ManifestValidator.Sanitize` and redirect the compatibility method.
6. Add manifest validation tests; defer enforcement until rejection/fallback behavior is specified.
7. Review the diff for serialized field changes and scene/prefab modifications.

Each step must be independently buildable and revertible.

### Validation

- Compare old and new calculations for identical inputs, including boundary values.
- Verify walk, sprint, driving, diagonal, zero-input, and rotation-threshold behavior.
- Verify both kinematic and dynamic Rigidbody2D paths in `FixedUpdate`.
- Verify terrain detection, telemetry interval timing, movement type, and average speed.
- Verify damage at zero, partial, full, and over-resistance values.
- Verify boss behavior at exactly 40% and below 40% health.
- Verify waypoint wrapping, empty lists, and null waypoint entries.
- Verify all obstacle raycasts remain in `EnemyPatrol`.
- Verify missing versus empty room graphs retain their existing semantics.
- Verify sanitization preserves all non-null data and only initializes the current lists.
- Run the complete backend regression suite and the Unity characterization suite.

### What must not change

- movement speeds;
- input mappings;
- rotation behavior;
- Rigidbody settings;
- telemetry event names and fields;
- telemetry interval timing;
- damage rounding and minimum-one-damage behavior;
- whether health can become negative;
- strict `< 40%` retreat threshold;
- patrol/chase obstacle avoidance and physics timing;
- room-graph validation acceptance policy;
- manifest fallback and null-object semantics;
- scene, prefab, and serialized inspector data.

### Phase 2 exit criteria

Phase 2 is complete only when:

1. All extracted-model tests pass.
2. Existing Unity characterization tests pass without changing expected outcomes.
3. Backend tests and golden manifest benchmarks pass unchanged.
4. A serialized-field diff shows no renamed, removed, or type-changed fields.
5. A scene/prefab diff shows no unintended asset modifications.
6. Manual verification confirms player movement, vehicle enter/exit, enemy patrol,
   damage, boss retreat, manifest application, and backend-unavailable fallback.
7. Any stricter validation or fallback-policy change is documented as a later,
   separately reviewed phase rather than being hidden in the extraction.

---

## Phase 3 — Introduce an Explicit AI State Model

### Goal

Make enemy states explicit without changing AI tuning or behavior.

### Proposed states

```text
Patrolling
Investigating
Alerted
Chasing
Retreating
Dead
```

### Migration approach

Do not rewrite the AI all at once. First introduce an internal state representation that mirrors the current fields:

```text
Existing isAlerted/isDead values
    ↓
state adapter
    ↓
explicit internal state
    ↓
existing patrol and chase logic
```

After the mapping is verified, transitions can be moved into explicit methods.

### Preserve exactly

- vision range;
- vision angle;
- hearing range;
- alert threshold;
- awareness decay;
- patrol speed;
- chase speed;
- boss retreat threshold;
- reinforcement broadcast behavior;
- death behavior;
- `AlertSystem` interactions.

### Validation scenarios

Test each scenario independently:

1. Enemy patrols with no player present.
2. Player enters the vision cone.
3. Player leaves the vision cone before the alert threshold.
4. Enemy hears a noise.
5. Enemy becomes alerted.
6. Boss falls below 40% health.
7. Boss retreats and summons reinforcements.
8. Enemy dies.
9. A dead enemy receives another hit.
10. A sector alert reaches the correct guards.

### Safety Rule

Do not change AI values while changing the state representation.

---

## Phase 4 — Replace Runtime Discovery Gradually

### Goal

Make dependencies explicit while retaining current lookup behavior as a compatibility fallback.

### Safe migration pattern

Use an assigned reference when present:

```text
serialized or injected reference
    ↓ if assigned
use it

otherwise
    ↓ compatibility fallback
existing tag lookup or singleton lookup
```

For example:

```text
EnemyDetection.playerTransform
    ↓ if null
GameObject.FindGameObjectWithTag("Player")
```

### Recommended order

1. Player references for enemy detection and patrol.
2. Mission manager references for player combat and interaction.
3. Telemetry reporter references.
4. World manifest applier references.
5. Camera target references.

### Validation

Test both configurations:

- dependencies assigned in the scene or prefab;
- dependencies missing, proving the old fallback behavior still works.

Also verify that:

- prefabs do not lose serialized references;
- scene loading still works;
- objects created at runtime still receive required dependencies;
- no duplicate manager is introduced.

### Safety Rule

Do not remove singleton access until all callers have migrated and usage has been verified.

---

## Phase 5 — Split `AdaptiveDirectorClient` Internally

### Goal

Separate transport, queueing, caching, and coordination while preserving the current public API.

### Current responsibilities

`AdaptiveDirectorClient` currently manages:

- singleton lifetime;
- scene-load handling;
- telemetry queueing;
- telemetry batching;
- HTTP requests;
- response parsing;
- archetype events;
- mission manifest requests;
- manifest caching;
- reset requests;
- callback and event behavior.

### Target internal structure

```text
AdaptiveDirectorClient
    compatibility facade

TelemetryEventQueue
    event queue and batch construction

DirectorTransport
    UnityWebRequest operations

ManifestCache
    disk read and write operations

DirectorResponseParser
    response deserialization and sanitization

DirectorSessionCoordinator
    scene and mission lifecycle coordination
```

### Compatibility requirement

Existing callers must continue to work unchanged:

```csharp
AdaptiveDirectorClient.Instance.QueueEvent(...)
AdaptiveDirectorClient.Instance.RequestMission2Manifest(...)
AdaptiveDirectorClient.latestManifest
```

The existing class initially becomes a facade that delegates internally to the new services.

### Preserve

- endpoint URLs;
- HTTP methods;
- request payload formats;
- event names;
- timestamp behavior;
- flush interval;
- cache path;
- event order;
- callback behavior;
- manifest sanitization;
- error logging;
- fallback behavior.

### Validation

Test:

- successful telemetry POST;
- failed telemetry POST;
- queue behavior across multiple flushes;
- successful mission manifest request;
- malformed manifest response;
- disk cache load;
- cache deletion during fresh Map 1 loading;
- reset request;
- multiple `OnManifestReceived` subscribers;
- multiple `OnArchetypeChanged` subscribers;
- scene transitions between Map 1 and Map 2.

### Safety Rule

Do not change public method signatures or payload schemas in this phase.

---

## Phase 6 — Split `WorldManifestApplier` into World Systems

### Goal

Reduce the number of responsibilities inside `WorldManifestApplier` while preserving manifest application order.

### Target services

```text
ManifestCoordinator
ManifestValidator
TargetSpawner
GuardSpawner
EnvironmentApplier
RouteApplier
ExtractionZoneBuilder
WorldGraphApplier
FallbackManifestProvider
```

The existing `WorldManifestApplier` remains the coordinator:

```text
manifest received
    ↓
validate manifest
    ↓
apply environment
    ↓
build world graph
    ↓
spawn target
    ↓
spawn guards
    ↓
apply routes
    ↓
create extraction zone
```

### Extraction order

Extract one responsibility at a time:

1. Manifest validation.
2. Environment and weather application.
3. Route and barricade application.
4. Target spawning.
5. Guard spawning.
6. Extraction setup.
7. Runtime sector construction.

Do not parallelize operations unless the existing implementation already permits it. Unity object creation order can affect gameplay.

### Validation

For every supported manifest archetype, verify:

- target location;
- target health and armor;
- guard count;
- guard vision and hearing values;
- guard reaction speed;
- weather and lighting;
- blocked routes;
- room graph;
- extraction behavior;
- fallback behavior.

### Safety Rule

Preserve the current call order and keep `WorldManifestApplier` as the stable scene-facing component during migration.

---

## Phase 7 — Add Backend Application and Repository Boundaries

### Goal

Separate FastAPI routing from application logic, domain calculations, and persistence.

### Target structure

```text
api/
    server.py

application/
    telemetry_service.py
    mission_service.py
    profile_service.py

domain/
    telemetry.py
    profiler.py
    target_solver.py
    manifest models

infrastructure/
    session_repository.py
    director_provider.py
```

### Target request flow

Telemetry:

```text
POST /api/telemetry/batch
    ↓
TelemetryService.ingest_batch()
    ↓
TelemetryLog
    ↓
ProfileService.build_profile()
    ↓
response DTO
```

Mission generation:

```text
POST /api/mission/solve
    ↓
MissionService.solve()
    ↓
profile
    ↓
deterministic solver or Gemini director
    ↓
SessionRepository.save()
    ↓
MissionManifest
```

### Safe migration

Keep all existing endpoint paths and JSON response shapes unchanged:

```text
/api/telemetry/event
/api/telemetry/batch
/api/player/profile
/api/mission/solve
/api/reset
```

Initially, `server.py` should instantiate and call the new services while preserving the existing external API.

### Persistence abstraction

Introduce a repository around the existing JSONL storage:

```python
class SessionRepository:
    def save(self, record):
        ...

    def list_all(self):
        ...
```

Do not replace JSONL with a database as part of this phase. The repository boundary can be introduced without changing storage.

### Validation

Run:

- the existing end-to-end pipeline test;
- API compatibility tests;
- profile calculation tests;
- mission manifest tests;
- persistence tests;
- deterministic seed tests.

### Safety Rule

No Unity client changes are required or allowed during the first backend layering migration.

---

## Phase 8 — Improve Backend Session State Safely

### Goal

Prepare the backend for multiple sessions without breaking the current single-player behavior.

### Migration sequence

1. Add an internal `session_id` field to the session model.
2. Keep the current default-session behavior.
3. Continue accepting requests without a session ID.
4. Route requests without a session ID to the existing default session.
5. Add explicit session lookup and storage.
6. Add explicit session IDs to Unity only after backend support is tested.

### Compatibility requirement

Requests from the current Unity client must continue to work exactly as before.

### Validation

Test:

- default session behavior;
- two independent session IDs;
- reset of one session without affecting another;
- profile isolation;
- manifest isolation;
- persistence isolation;
- missing or invalid session IDs.

### Safety Rule

Do not change the Unity request payload until the backend supports both old and new request forms.

---

## Phase 9 — Remove Compatibility Paths Only After Verification

### Goal

Remove obsolete global lookups, facades, and transitional adapters only after all callers and tests have migrated.

### Removal criteria

A compatibility path may be removed only when:

1. Search confirms there are no remaining callers.
2. Scene and prefab references use the new path.
3. Focused tests cover the new path.
4. The end-to-end regression test passes.
5. Manual Map 1 and Map 2 verification passes.
6. Offline, cache, and fallback behavior has been checked.
7. The removal is independently reviewable and revertible.

---

# Validation Strategy

Every phase should use the smallest test or verification set that covers the changed behavior, followed by broader regression validation where required.

## Required checks after each phase

1. The changed code compiles.
2. Existing backend tests pass.
3. New focused tests pass.
4. Existing API payloads remain compatible.
5. Unity scenes and prefabs load without missing references.
6. Map 1 behavior remains unchanged.
7. Map 2 behavior remains unchanged.
8. Offline and fallback paths still work.
9. Disk-cache behavior still works.
10. The change can be reverted independently.

## Behavior comparison

For refactored calculations and manifest application, compare:

```text
same input
    ↓
old implementation result
    vs
new implementation result
```

The comparison should cover meaningful outputs rather than implementation details.

## Regression scenarios

The following scenarios must remain functional:

### Unity startup

- Director client initializes once.
- Duplicate singleton instances are handled as before.
- Map 1 loads normally.
- Map 2 loads normally.

### Telemetry

- Movement telemetry is recorded.
- Combat telemetry is recorded.
- Destruction telemetry is recorded.
- Events are queued.
- Batches are flushed.
- Network errors are surfaced through existing logging.

### Adaptive mission generation

- Current player profile is rebuilt.
- Archetype is calculated.
- Mission 2 manifest is generated.
- Manifest is cached.
- Manifest is applied after scene loading.
- Fallback is used when no manifest arrives.

### World application

- Room graph is validated.
- Sectors are built.
- Target is spawned.
- Guards are spawned.
- Guard configuration is applied.
- Weather and environment are applied.
- Routes and barricades are applied.
- Extraction zone is created.

### AI

- Guards patrol.
- Guards detect the player.
- Guards hear noise.
- Guards chase after alert.
- Boss retreat behavior works.
- Reinforcements are broadcast.
- Dead guards stop acting.

---

# Changes to Avoid

To minimize risk, do not:

- rewrite the Unity project into ECS;
- replace all singletons in one change;
- change API response schemas;
- rename serialized Unity fields;
- convert every `MonoBehaviour` into pure C# immediately;
- change AI tuning values during refactoring;
- replace JSONL with a database as part of this architecture work;
- introduce a dependency-injection framework prematurely;
- change scene or prefab wiring in the same change as broad code extraction;
- combine gameplay changes with architecture changes;
- optimize performance without profiling evidence;
- remove fallback behavior before the new path is proven.

---

# Recommended Implementation Order

```text
1. Establish the behavior baseline
2. Add characterization and contract tests
3. Extract pure validation and calculation code
4. Add an explicit AI state representation
5. Add explicit references with fallback lookup
6. Split AdaptiveDirectorClient internally behind a compatibility facade
7. Split WorldManifestApplier one responsibility at a time
8. Add backend service and repository boundaries
9. Add optional session identity
10. Remove compatibility paths only after usage is verified
```

---

# Definition of Done

The architecture improvement effort is complete only when:

- gameplay behavior remains equivalent;
- all existing API endpoints remain compatible;
- Unity scene and prefab wiring remains valid;
- Map 1 and Map 2 flows work;
- telemetry and adaptive manifest flows work;
- cache and fallback flows work;
- AI behavior remains equivalent;
- focused tests cover extracted logic;
- the end-to-end backend pipeline test passes;
- no compatibility path is removed without proof that it is unused;
- each architectural change is small enough to review and revert.

## Final Principle

The project does not need a risky architectural rewrite. It needs controlled separation of responsibilities:

```text
Unity gameplay
    ↔
telemetry and adaptation application logic
    ↔
HTTP and persistence infrastructure
```

This plan preserves the current Unity architecture while making it more testable, explicit, and scalable.
