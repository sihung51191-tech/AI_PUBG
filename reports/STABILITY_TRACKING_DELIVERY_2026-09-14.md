# Stability and tracking delivery — 2026-09-14

## What changed

- Removed the unreachable `NotImplementedException` control factories.
- Converted recognition scanning internals from `async void` to `Task`, retaining cancellation and generation rejection.
- Added an allocation-free four-state constant-velocity Kalman tracker: `[x, y, vx, vy]` in physical screen space.
- Added stable `TargetTrackId` assignment in Sticky Aim. A new target gets a new ID; a matched head/body detection keeps the same ID.
- Expanded Sticky Aim association with predicted-position, movement-direction and confidence terms in addition to its existing class, head/body, IoU, center-distance and size checks.
- Reset tracking through target ID and reset generation when slot, model, capture identity/method/box, image size or aim activation changes.
- Reset WGC frame ordering at a capture-context boundary so a recreated session can safely restart its frame counter.
- Rejected duplicate/old timestamps, invalid values and stale frames; reset after a long frame gap; bounded missing-frame extrapolation and confidence decay.
- Prediction uses Kalman velocity only after three observations, scales lead by confidence and frame age, adds a standing-target dead zone, and clamps lead to 150 ms and a target-size/configured distance.
- Kalman smoothing remains active when Prediction is off. Prediction remains off by default.
- Preserved the custom mouse response curve and all recoil code. The filtered physical offset is converted through the existing per-slot capture-to-response gain exactly once.
- Fixed WGC resize double-dispose and surfaced DirectX reinitialization errors through rate-limited capture reporting.
- Added Windows GitHub Actions for restore, Release x64 build and CPU-only pipeline checks. GPU/WGC/TensorRT/native-driver/PUBG checks are explicitly manual integration tests.

## GUI and defaults

The Predictions panel now exposes bilingual tooltips for:

- `Enable Kalman Filter`: default On.
- `Predictions`: default Off.
- `Prediction Time`: 0–150 ms, default 35 ms, step 5 ms.
- `Kalman Smoothness`: 0–100, default 55.
- `Maximum Missing Frames`: default 3.
- `Maximum Frame Age`: default 150 ms.
- `Maximum Prediction Distance`: default 0, which derives the cap from target-box size.

`Sticky Aim` is On for a new/default configuration. Existing config values continue to win when loaded, so the migration does not silently overwrite a user's saved choice.

## Verification performed

- Release x64 build after edits: PASS, 0 errors / 284 warnings.
- Pipeline harness: 74 PASS and 2 measured entries.
- Coordinate matrix: 48/48 PASS across model sizes 160, 224, 256, 288, 320, 416, 512 and 640; primary, negative-origin and secondary-monitor origins; stretch and letterbox transforms.
- Local CPU execution: YOLO11 and YOLO26 fixed/dynamic ONNX models PASS.
- Kalman microbenchmark: 0 allocated bytes in the measured loop and approximately 0.0006 ms/frame on the final run, below the 0.2 ms/frame target.
- Postprocess measurement: allocation decreased from about 42,656 to 33,600 bytes/frame versus the legacy comparison harness. Runtime in this synthetic 300-detection benchmark changed from about 0.048 to 0.082 ms/frame; this is not an end-to-end FPS measurement.

## Existing custom capabilities retained

The repository inventory and source show substantial custom functionality absent from the referenced upstream snapshot: WGC capture, DirectX duplication fallback, two independent model slots, YOLO11/YOLO26 metadata and output canonicalization, TensorRT engine support, weapon/scope recognition with template/feature/OCR paths, per-slot recoil profiles, Vietnamese UI, display-aware overlays, capture-specific sensitivity, performance helper and Alt+Tab recovery hooks. None of these were replaced with upstream files.

## Model/provider and recognition status

- ONNX validation already checks input/output metadata, FP16 conversion, static/dynamic dimensions and rejects ambiguous layouts instead of guessing.
- Provider factory records the actual provider in model metadata and falls back to CPU after a requested provider fails.
- Historical local reports contain CPU/DirectML model checks and TensorRT engine validation, but they were not regenerated in this run and do not prove compatibility on another GPU.
- Recognition already separates weapon/scope regions and template libraries, supports multiple templates, quality analysis, descriptor caching, debounce and generation-based stale-result rejection. This turn changed only scan task lifetime; recognition accuracy was not measured from PUBG screenshots.

## Not verified here

- No integration test was run inside PUBG.
- No live fullscreen/borderless/windowed WGC, DPI 125/150%, resize, monitor move, device removal or Alt+Tab sequence was executed.
- No mouse movement was emitted; SendInput, `mouse_event`, Logitech, Razer and ddxoft behavior remain unverified on hardware.
- No TensorRT engine was built or loaded on a GPU in this run.
- FPS, capture/inference/processing/end-to-end latency, CPU/GPU/RAM and long-run leak behavior require a live benchmark session and cannot be inferred from unit checks.
- Recoil feeling and five-shot/continuous behavior were intentionally not changed and still require direct weapon/scope testing.

## Manual PUBG checklist

1. Test fullscreen, borderless and windowed with WGC; Alt+Tab repeatedly and change resolution/display.
2. Verify overlay and aim point on the primary display, a negative-origin display and DPI 100/125/150%.
3. Cross two targets, overlap head/body boxes, briefly occlude the locked target and verify track ID/hysteresis behavior.
4. Start with Prediction Off; enable it at 35 ms only after Kalman smoothing is stable, then tune the distance cap if overshoot appears.
5. Exercise model/slot/provider/capture changes while aiming and while recognition scanning.
6. Validate each mouse backend's actual connection/fallback and confirm no simultaneous movement loops.
7. Re-run the complete recoil matrix without changing saved profiles.
8. Run a 30–60 minute resource test while switching model and capture backends and record RAM/GPU memory growth.

## Repository cleanup recommendation

Slot 1 and Slot 2 contain byte-identical ONNX files by SHA-256 in the existing inventory. Future cleanup should reference one model store from both slots, move large models to Git LFS or Releases, and keep runtime configuration/templates out of ordinary source commits. Do not rewrite history or delete current runtime assets without a separate reviewed migration.

## Rollback

- Safe rollback after commits: use `git revert <commit>` for the delivery commits.
- To leave the work untouched and return to the original source: switch back to `main` at `ab528e3`.
- User runtime configs and the two untracked `holo` templates were not included in the source commits and should not be deleted during rollback.
