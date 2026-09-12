# Visual Upgrades & Realism Roadmap (Implementation-Ready Specification)
**Project:** AdaptiveWorld2D  
**Target Engine:** Unity 2022.3+ / Unity 6  
**Document Version:** 2.0 (Audited & Reconciled)  
**Date:** September 2026  

---

## 1. Executive Summary & Audit Findings

Following a full codebase and asset inventory audit, the visual upgrade roadmap has been reconciled with the exact state of the repository. 

### Key Audit Realities:
1. **Render Pipeline:** The project currently runs on Unity's Built-in Render Pipeline. `com.unity.render-pipelines.universal` (URP) is **not yet installed** in [`Packages/manifest.json`](file:///c:/Users/xDDDDD/AdaptiveWorld2D/Packages/manifest.json), and [`ProjectSettings/GraphicsSettings.asset`](file:///c:/Users/xDDDDD/AdaptiveWorld2D/ProjectSettings/GraphicsSettings.asset) has no pipeline asset assigned. **URP 2D setup is an explicit, gated prerequisite**, not a zero-risk cosmetic toggle.
2. **Kenney Asset Inventory:** The project already contains **524 individual, ready-to-use PNG tiles** located at [`Assets/assets/kenney_topdown_shooter/PNG/Tiles/`](file:///c:/Users/xDDDDD/AdaptiveWorld2D/Assets/assets/kenney_topdown_shooter/PNG/Tiles/) (`tile_01.png` to `tile_538.png`), as well as vehicles and character sprites. No manual sprite sheet slicing is needed.
3. **Existing Feature Foundations (Do Not Duplicate):**
   - **2D Lighting:** Reflection hooks for `UnityEngine.Rendering.Universal.Light2D` already exist in [`StreetlightController.cs`](file:///c:/Users/xDDDDD/AdaptiveWorld2D/Assets/Scripts/World/StreetlightController.cs) and [`WorldManifestApplier.cs`](file:///c:/Users/xDDDDD/AdaptiveWorld2D/Assets/Scripts/Director/WorldManifestApplier.cs).
   - **Shadow Casters:** Reflection hooks for `ShadowCaster2D` already exist in [`SectorRuntimeBuilder.TryAttachShadowCaster()`](file:///c:/Users/xDDDDD/AdaptiveWorld2D/Assets/Scripts/World/SectorRuntimeBuilder.cs#L441).
   - **Weather Systems:** Rain and fog particle systems are already generated procedurally in [`WorldManifestApplier.EnsureRainSystem()`](file:///c:/Users/xDDDDD/AdaptiveWorld2D/Assets/Scripts/Director/WorldManifestApplier.cs#L770) and `EnsureFogSystem()`.
   - **Muzzle Flash:** Dynamic illumination already exists in [`PlayerCombat.DrawMuzzleFlash()`](file:///c:/Users/xDDDDD/AdaptiveWorld2D/Assets/Scripts/Player/PlayerCombat.cs#L189).
4. **Camera Shake Architecture:** Must be implemented as an **impulse/offset layer** inside [`CameraFollow2D.cs`](file:///c:/Users/xDDDDD/AdaptiveWorld2D/Assets/Scripts/World/CameraFollow2D.cs), rather than manipulating `Camera.main.transform.position` externally, which would fight `LateUpdate` lerping.

---

## 2. Standardized Sorting Layer Architecture

To prevent Z-fighting, sprite clipping, and rendering inconsistencies across Map 1 and Map 2, all visual systems will adhere to this fixed sorting hierarchy:

| Sorting Layer Name | Sorting Order | Used By |
| :--- | :---: | :--- |
| **Background** | -100 to -10 | Road tiles, asphalt, grass, dirt, concrete foundations |
| **Decals** | -5 to -1 | Skid marks, blood stains, rubble, bullet scorch marks |
| **PropsUnder** | 0 to 5 | Curbs, manholes, dropped ammo, flattened wires |
| **Obstacles** | 10 to 20 | Concrete barricades, burnable cars, destructible walls, crates |
| **Characters** | 25 to 30 | Player, guards, Viktor (boss), civilians |
| **Projectiles** | 40 to 50 | Bullet tracers, muzzle flash quads |
| **Atmosphere** | 60 to 70 | Low-lying fog particles, rain streak particles |
| **Lighting** | 80 to 90 | Light2D shapes (if URP enabled), streetlight glow cones |
| **UI / HUD** | 100+ | Health bars, radar, objective prompts, debug overlays |

---

## 3. Tiered Implementation Roadmap

```
 ┌─────────────────────────────────────────────────────────────┐
 │ Phase 1: URP 2D Pipeline Foundation          [COMPLETED ✅] │
 └──────────────┬──────────────────────────────────────────────┘
                │
                ▼
 ┌─────────────────────────────────────────────────────────────┐
 │ Phase 2: Shared Tile & Theme Mapping (Kenney) [COMPLETED ✅]│
 └──────────────┬──────────────────────────────────────────────┘
                │
                ▼
 ┌─────────────────────────────────────────────────────────────┐
 │ Phase 3: Hardening Existing Visual Effects   [COMPLETED ✅] │
 └──────────────┬──────────────────────────────────────────────┘
                │
                ▼
 ┌─────────────────────────────────────────────────────────────┐
 │ Phase 4: Camera Impulse Shake Layer & Combat [COMPLETED ✅] │
 └──────────────┬──────────────────────────────────────────────┘
                │
                ▼
 ┌─────────────────────────────────────────────────────────────┐
 │ Phase 5: Bounded Decals & Environmental Polish[COMPLETED ✅]│
 └─────────────────────────────────────────────────────────────┘
```

---

### Phase 1: URP 2D Pipeline Foundation [GATED ⏳]

* **Current repository status:** URP is not currently installed or assigned. Do not mark this phase complete until the package, pipeline asset, and 2D renderer have been verified in the project.
* **Decision gate:** Test the Unity 6 migration on a branch or backup. Record the selected URP package version, generated assets, material/shader changes, and rollback path.
* **If URP is accepted:** Install `com.unity.render-pipelines.universal`, create a URP asset with `Renderer2DData`, assign it in `GraphicsSettings.asset`, then verify `Light2D` and `ShadowCaster2D` in both generated maps.
* **If Built-in is retained:** Keep the existing sprite/material and overlay fallbacks. This preserves gameplay compatibility, but lighting, shadows, bloom, and post-processing will have reduced fidelity and must not be described as visually identical to URP.
* **Required project setup:** Add the named sorting layers in `ProjectSettings/TagManager.asset` before code assigns them: `Background`, `Decals`, `PropsUnder`, `Obstacles`, `Characters`, `Projectiles`, `Atmosphere`, `Lighting`, and `UI / HUD`.

---

### Phase 2: Shared Tile & Theme Architecture

#### 2.1 The Problem
`AdaptiveSceneBuilder.cs` (Editor builder for Map 1) and `SectorRuntimeBuilder.cs` (Procedural runtime builder for Map 2) currently have separate, duplicated logic for creating floors and walls, and both rely on `WorldManifestApplier.GetUniversalWhiteSprite()`.

#### 2.2 The Solution: `SectorThemeCatalog.cs`
Create a centralized theme catalog backed by serialized `Sprite` references. The catalog maps sector modules and surface types to Kenney tiles without requiring runtime file-name lookup:

```csharp
[CreateAssetMenu(menuName = "AdaptiveWorld/Visuals/Sector Theme Catalog")]
public sealed class SectorThemeCatalog : ScriptableObject
{
    [Serializable]
    public sealed class ModuleTiles
    {
        public string module;
        public Sprite[] roadTiles;
    }

    public ModuleTiles[] modules;
    public Sprite wallBrickTile;
    public Sprite wallReinforcedTile;
}
```

* Create `SectorThemeCatalog.asset` with `Sprite` fields referencing `Assets/assets/kenney_topdown_shooter/PNG/Tiles/tile_*.png`.
* `AdaptiveSceneBuilder` may resolve the catalog through `AssetDatabase` in editor-only code; `SectorRuntimeBuilder` must receive the catalog as a serialized reference or initialized runtime configuration.
* Add a lookup method that returns the configured `Sprite[]` for a module and reports a clear configuration error when a module is missing; do not silently fall back to a file-name string.
* Both `AdaptiveSceneBuilder.CreateRoad()` and `SectorRuntimeBuilder.CreateSectorFloor()` will pull from this single source of truth.

---

### Phase 3: Hardening Existing Visual Effects

Instead of adding redundant systems, we harden the code already written:

#### 3.1 Rain System Hardening ([`WorldManifestApplier.EnsureRainSystem`](file:///c:/Users/xDDDDD/AdaptiveWorld2D/Assets/Scripts/Director/WorldManifestApplier.cs#L770))
* **Change:**
  * Update `ParticleSystemRenderer.renderMode` from `Billboard` to `Stretch`.
  * Set `lengthScale = 3.5f` and `velocityScale = 0.02f`.
  * Adjust particle start size to thin streaks (`x = 0.05f, y = 0.4f`).
  * Ensure sorting layer is set to `Atmosphere` (`sortingOrder = 60`).

#### 3.2 Fog System Hardening ([`WorldManifestApplier.EnsureFogSystem`](file:///c:/Users/xDDDDD/AdaptiveWorld2D/Assets/Scripts/Director/WorldManifestApplier.cs#L810))
* **Change:**
  * Increase particle lifetime to `8.0f` with low velocity (`0.3 m/s`) drifting horizontally.
  * Clamp max particle count to `100` to prevent overdraw and frame-rate drops on integrated GPUs.
  * Set alpha fade-in/fade-out curves so fog puffs do not pop into existence.

#### 3.3 Dynamic Streetlight Hardening ([`StreetlightController.cs`](file:///c:/Users/xDDDDD/AdaptiveWorld2D/Assets/Scripts/World/StreetlightController.cs))
* **Change:**
  * Enhance the fallback glow quad: if URP `Light2D` is not present, use a soft radial-gradient sprite with a transparent additive material and assign it to the `Lighting` sorting layer (`sortingOrder = 80`). `SpriteRenderer` has no direct `BlendMode` property.
  * Keep the Perlin noise flicker logic (it already works cleanly).

---

### Phase 4: Camera Impulse Layer & Combat Juice

#### 4.1 Composable Camera Shake in [`CameraFollow2D.cs`](file:///c:/Users/xDDDDD/AdaptiveWorld2D/Assets/Scripts/World/CameraFollow2D.cs)
Rather than spawning independent shake coroutines that fight `LateUpdate()`, add an impulse layer directly to `CameraFollow2D`:

```csharp
// Inside CameraFollow2D.cs
private Vector2 _shakeOffset;
private float _shakeIntensity;
private float _shakeDecay = 5.0f;
private Vector3 _basePosition;

public static CameraFollow2D Instance { get; private set; }

public void AddImpulse(float intensity)
{
    _shakeIntensity = Mathf.Clamp(_shakeIntensity + intensity, 0f, 1.2f);
}

private void LateUpdate()
{
    // 1. Follow from an unshaken base position; never lerp from
    //    transform.position because it contains the prior shake.
    Vector3 targetPos = new Vector3(target.position.x + offset.x, target.position.y + offset.y, cameraZ);
    _basePosition = Vector3.Lerp(_basePosition, targetPos, smoothSpeed * Time.deltaTime);

    // 2. Compute decaying multi-frequency noise offset
    if (_shakeIntensity > 0.01f)
    {
        float rx = (Mathf.PerlinNoise(Time.time * 25f, 0f) - 0.5f) * 2f * _shakeIntensity;
        float ry = (Mathf.PerlinNoise(0f, Time.time * 25f) - 0.5f) * 2f * _shakeIntensity;
        _shakeOffset = new Vector2(rx, ry);
        _shakeIntensity = Mathf.MoveTowards(_shakeIntensity, 0f, _shakeDecay * Time.deltaTime);
    }
    else
    {
        _shakeOffset = Vector2.zero;
    }

    transform.position = new Vector3(_basePosition.x + _shakeOffset.x, _basePosition.y + _shakeOffset.y, cameraZ);
    transform.rotation = Quaternion.identity;
}
```

#### 4.2 Impact Hooks
* **Car Explosion:** [`BurnableCar.Explode()`](file:///c:/Users/xDDDDD/AdaptiveWorld2D/Assets/Scripts/World/BurnableCar.cs#L57) ➔ calls `CameraFollow2D.Instance?.AddImpulse(0.8f)` after the explosion state is committed.
* **Wall Breached:** [`DestructibleWall.DestroyWall()`](file:///c:/Users/xDDDDD/AdaptiveWorld2D/Assets/Scripts/World/DestructibleWall.cs#L50) ➔ calls `CameraFollow2D.Instance?.AddImpulse(0.35f)`. Do not trigger this on every `DamageWall(int)` hit.
* **Player Takes Damage:** identify the actual player-health/damage owner before adding this hook. `PlayerController` currently has no damage method; do not add a speculative call there. Add the impulse at the existing player damage event once located.

---

### Phase 5: Bounded Environmental Polish & Decals

#### 5.1 Bounded Decal Pool
* **Rules:**
  * Maximum 50 decals active in scene (blood splatters from guards, rubble chunks from walls).
  * Implemented via a simple FIFO circular buffer (`Queue<GameObject>`).
  * When count exceeds 50, the oldest decal fades its alpha over 0.5s and is recycled or destroyed.
  * Decals always instantiate on `SortingLayer = "Decals"` with the project’s established camera-plane depth; use `Z = 0.05f` only after verifying it does not conflict with the active camera/sorting setup.

#### 5.2 Muzzle Flash & Shell Casings
* The existing `PlayerCombat.DrawMuzzleFlash()` quad will be assigned a distinct warm-yellow flash sprite.
* Shell casings eject with randomized initial velocity and angular momentum, coming to a halt after 0.2s on the `Decals` sorting layer.

---

## 4. Verification & Acceptance Criteria

| Area | Test / Verification Step | Success Threshold |
| :--- | :--- | :--- |
| **Compilation** | Unity compiles scripts in Editor | 0 new compiler errors or warnings attributable to this work |
| **Tileset Visuals** | Run `AdaptiveWorld/Build & Save BOTH Maps` | Map 1 & Map 2 contain themed Kenney tiles instead of plain white boxes |
| **Camera Stability** | Drive sports car, detonate cars, break walls | Camera smoothly tracks player without jitter; explosions produce proportional screen shake |
| **Weather Performance** | Trigger rain and fog in Map 2 | Particle systems run stably under 120 draw calls, maintaining 60+ FPS |
| **Memory / Pooling** | Kill 20 guards and explode 5 cars | Decal count never exceeds 50; no orphan GameObjects leaking in Hierarchy |
| **Pipeline Compatibility** | Test the selected URP configuration and the documented Built-in fallback | No `NullReferenceException` when optional URP types are unavailable; visual differences are documented |
| **Camera Impulses** | Trigger multiple explosions/impacts in quick succession | Impulses accumulate up to the configured cap, decay smoothly, and never cause camera drift |
