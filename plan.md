# Visual Upgrades Plan Review and Recommended Implementation Plan

## Problem and proposed approach

`VISUAL_UPGRADES_PLAN.md` is directionally strong, but it mixes foundation work, already-landed behavior, asset assumptions, and optional polish without explicit prerequisites or acceptance criteria. The recommended approach is to revise execution around the actual repository state:

1. Establish and validate URP 2D as an explicit project prerequisite.
2. Correct the asset inventory and introduce a reusable tile/theme mapping instead of hard-coding sprite picks in both builders.
3. Preserve existing compatibility fallbacks while adding visual features behind configurable components.
4. Implement feedback effects through shared services and bounded pools/lifetimes.
5. Validate each tier in Unity with performance and gameplay-regression checks before advancing.

## Findings to incorporate

- `Packages/manifest.json` and `ProjectSettings/GraphicsSettings.asset` do not currently declare or activate URP, despite scripts using reflection-based `Light2D` and `ShadowCaster2D` fallbacks.
- The plan references `Assets/assets/kenney_topdown_shooter/Tilesheet/tilesheet_complete_2X.png`, but the repository currently contains individual `PNG/Tiles/tile_*.png` assets and no matching tilesheet at that path.
- `ShadowCaster2D` attachment is already attempted in `SectorRuntimeBuilder`; the work should verify behavior and geometry rather than blindly add duplicate components.
- Muzzle-flash lighting already exists in `PlayerCombat`; rain/fog systems already exist in `WorldManifestApplier`; streetlight behavior already exists in `StreetlightController`.
- Camera follow is a simple custom component, so camera shake needs a composable offset/impulse layer rather than replacing follow logic.
- The plan needs explicit treatment for post-processing package support, sorting/layer conventions, effect cleanup/pooling, mobile/performance budgets, and testable acceptance criteria.

## Recommended todos

1. Audit and revise the roadmap against the current Unity project and asset inventory.
2. Add and configure URP 2D only after confirming the target Unity editor/version and render-pipeline migration scope.
3. Build a reusable tile/theme asset mapping and wire both scene and runtime builders to it.
4. Harden existing lighting, shadow, rain, fog, muzzle-flash, and destruction hooks without duplicating components.
5. Add camera impulse/shake integration that preserves `CameraFollow2D` smoothing and supports multiple concurrent impacts.
6. Add decals, impacts, vision-cone visuals, and splash effects with explicit lifetime, sorting, and pooling limits.
7. Defer character animation and normal-map work until the foundation is stable; define source assets and acceptance screenshots first.
8. Validate in Unity play mode and generated Map 1/Map 2 scenes, checking visuals, console errors, frame time, and gameplay behavior.

## Notes and trade-offs

- URP migration is not zero-risk and should be a gated prerequisite, not bundled with camera shake/rain as one “zero risk” step.
- Prefer ScriptableObject/configuration-driven visual themes over direct asset references duplicated across builders.
- Keep reflection/fallback paths only if the project must remain compatible with non-URP configurations; otherwise replace them with typed URP references after the package migration.
- Bloom, vignette, and film grain should be treated as optional quality settings with platform-safe defaults.
- The final roadmap should define measurable acceptance criteria per tier, including no duplicate runtime effects, bounded particle counts, and no new compile/runtime errors.
