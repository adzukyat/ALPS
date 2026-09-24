# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Adzuki Live Performance System (ALPS) is a UPM package, rooted at the repository root, for Timeline based lighting shows in VRChat worlds. Shows are authored as effect clips on Timeline tracks, previewed live in the editor, and played in VRChat by an UdonSharp player that drives VR Stage Lighting (VRSL) DMX Static fixtures. Effect parameters travel to the player as data and are evaluated on the GPU into the DMX grid VRSL reads. Nothing is baked into keyframes.

Unity `2022.3.22f1`, VRChat SDK `3.10.2`, stock VRSL `2.8.4`. PC only: Quest cannot run the evaluator shader, and ALPS takes VRSL's DMX grid over, so it cannot share it with a video or other DMX source.

## Commands

All tests run Unity in batch mode against `TestProject~`, which references the package with `file:../..`. The show is evaluated on the GPU, so they run without `-nographics`, and the tests that evaluate a show ignore themselves without a graphics device. Set `UNITY_EXECUTABLE` if Unity is not at the Hub default path. Results go to `TestProject~/TestResults~/`.

```sh
scripts/test-all.sh            # metadata check, EditMode tests, UI tests
scripts/test-editmode.sh       # AdzukiSoft.ALPS.EditorTests (show maths, preview, build conversion)
scripts/test-editor-ui.sh      # AdzukiSoft.ALPS.EditorUiTests (inspector UI and layout audit)
scripts/check-package-metadata.sh
scripts/bootstrap-test-project.sh   # fetches VRChat SDK, AudioLink and VRSL
```

A single test or fixture (the scripts do not forward filters, so call Unity directly):

```sh
"/Applications/Unity/Hub/Editor/2022.3.22f1/Unity.app/Contents/MacOS/Unity" -batchmode \
  -projectPath TestProject~ -runTests -testPlatform EditMode \
  -assemblyNames AdzukiSoft.ALPS.EditorTests \
  -testFilter "AdzukiSoft.ALPS.Tests.AlpsShowTests.Circle_WalksAllTheWayRoundOncePerCycle" \
  -testResults "$TMPDIR/one.xml" -logFile "$TMPDIR/one.log"
```

Do not add `-quit`: with this Test Framework version it can exit before the results XML is written. Batch mode refuses a project that is open in the editor (`Temp/UnityLockfile`).

`DemoProject~` has an editor script, `ALPS > Demo > Rebuild Example` (batch entry `AlpsDemoBuilder.RebuildAndExit`), that regenerates the demo scene and `ExampleTimeline.playable` from code. Change the demo by editing `DemoProject~/Assets/Editor/AlpsDemoBuilder.cs`, not the assets.

## Architecture

### One evaluator on the GPU for preview and Udon

`Runtime/Gpu/AlpsEvaluator.hlsl` is the core. The compiled show travels as flat `float[]` / `int[]` arrays whose layout is `Runtime/Playback/AlpsShowLayout.cs`: clip rows (`ClipStride`, including the shared phase and 16 sampled mix curve points), effect rows (`EffectStride`, per kind scalars `EffectScalarA..`), parameter rows (`ParamStride`, one per `AlpsAnimatableValue`, including its own phase), palette rows, and a per fixture frame of 13 channels (`Frame*`). `AlpsShowPlayer.PackShowData` lays them, the fixture defaults, per fixture DMX info, the fixture on each DMX row and each fixture's aim space out as one RFloat texture of 1024 wide rows behind a header of section offsets. The shader mirrors every constant and offset, so a layout change touches `AlpsShowLayout`, the compiler encoding, `PackShowData` and the shader together. There is no C# evaluator. `AlpsPhaseCurve` (`Runtime/Model`) holds only the phase shape the inspector draws (ease, wave, random noise), and `AlpsShowTests.PhaseCurve_DrawsWhatPlays` holds it to the shader.

Each frame the player (and the preview, through the same statics) sets the time and blits three passes (`AlpsShowPlayer.RenderPasses`):
- `Hidden/ALPS/Frames` evaluates every fixture into a 4 texel wide RGBA row: the 13 channels, then `FrameAimed`. It walks the time bucket's clips (`bucketStart` / `bucketClips`, the clips overlapping each bucket in clip order), weighs and blends them, then aims a head that tracks a user.
- `Hidden/ALPS/DMX Grid` pass 0 lays the frames out as VRSL's 26 x 240 horizontal grid (bound to `_Udon_DMXGridRenderTexture`, `...Movement` and `...StrobeOutput`), pass 1 writes the gobo angle as the phase VRSL's spin timer grid holds (`_Udon_DMXGridSpinTimer`), which is how gobos turn on stock VRSL.

The shader compiles in seconds only because each value, phase and ease sits at one place in the code: an effect lists the values it reads as slots and one `[loop]` resolves them (`AlpsResolve`), the wave calls its ease once, and the three bounce eases share one bounce. Calling those from every effect again has the compiler inline them over and over, which takes minutes. The compiler still precomputes what does not change over time, the positions table (`ClipPositionStart`, the order position and mirror flag of every group member, shared by clips with the same group, order and grouping) and the time index.

`AlpsPlaybackBenchmark` (explicit, enters play mode) times the compiled Udon player on 48 VRSL movers and writes `TestResults~/benchmark.txt`. `Capture` renders a moment of a small show into `TestResults~/capture.png`. Run them by name with `-testFilter`.

Key evaluation rules that span files:
- Order position `k` comes from `OrderPosition`, run by the compiler into the positions table (normal, reverse, symmetric counting outward from the middle, seeded random). Symmetric also mirrors the final pan of the first half (`IsMirrored`).
- A phase is a wave or random. A wave cycle is four shares, rise, high hold, fall and the low hold they leave over (`AlpsPhaseCurve.Wave`). The rise and the fall each have their own ease (`ease` and `fallEase`), both read forwards in time, and invert flips the eased wave. The return leg (`IsReturnLeg`, blackout on return) is the fall plus the low hold. Random is a value noise (`AlpsPhaseCurve.Noise`, `AlpsNoise` in the shader).
- Each effect has a `phaseOffset` that runs every value on it late by that share of a cycle. It travels as `EffectPhaseOffset` and reaches the phase as the extra cycles of a slot, so ranges, palettes, circles and blackout all shift together.
- A parameter value is `value` or `range(phase)`. With spread on it is `spreadRange` instead, the values at the first and last order position, or `spreadRange` to `spreadRangeEnd` by phase when also ranged. The compiler turns each spread into its first value plus a step per position (`StepFromSpread`, divided by `SpreadPositions` like the phase delay), so the shader computes `first(phase) + step(phase) * k`. Ranges follow the parameter's own phase if set, otherwise the clip's shared phase, and respect timing (within cycle or per cycle).
- Composition: clips are sorted by layer (track order, override tracks after their parent). Clips inside a layer blend by weight, each layer covers lower ones per channel by its total weight, and gobo and track effect are discrete. Undriven channels keep the fixture default, except brightness, which is dark unless a brightness effect lights it, so a fixture no clip covers is dark too.
- Each clip has its own fade in / fade out in beats (`ClipFadeIn` / `ClipFadeOut`, counted at the clip's tempo). It multiplies the clip's Timeline weight, so a fade behaves like blending with an empty clip on every channel. The Timeline window draws its slopes (`AlpsTimelineClipEditor`).
- Move has three modes: angle, circle (a ring around a center direction computed on the sphere, so it never folds into a figure eight), and track user. Tracking runs in the frames pass: the player hands it each tracked user's head (`_AlpsTrackTarget0..7`, at most `GpuMaxTrackedUsers`), the show data holds each fixture's world to aim matrix taken when the player starts, and the head follows at the effect's speed from the previous frames target, `frames` and `framesPrevious` taken in turn. The preview has no users, so a tracking head keeps what the layers below give it.

### From Timeline to arrays

`AlpsTimelineTrack` binds an `AlpsTarget`: an `AlpsContainer`, which drives every `AlpsFixture` below it in hierarchy order, or one fixture on its own (`CollectFixtures`). A Tween track for moving objects is planned and will bind containers the same way. `AlpsTimelineClip` holds an `AlpsClipEffectSet` (the model in `Runtime/Model`), or follows an `AlpsClipProfile`. `AlpsShowCompiler.Compile(director)` walks the tracks and produces an `AlpsCompiledShow` (arrays plus fixture list, errors, warnings). `CompileStandalone` builds a show without a Timeline and is what Level 1 tests use. The compiler lives in the Runtime assembly because the preview needs it.

### Preview

`AlpsTimelineMixer` forwards `ProcessFrame` to the static `AlpsPreviewDriver`, which compiles once per director, puts its fixtures in DMX mode on rows in their own order (`AlpsFixture.ConfigureGpu`), and renders the passes once per frame with materials and targets of its own. `GetFrames` hands tests the frames it drew. Clip edits call `AlpsPreviewDriver.MarkDirty()`. Restoring fixtures after preview relies on Timeline's animation mode: `AlpsTimelineTrack.GatherProperties` registers every VRSL field the DMX setup writes, `AlpsFixture.RestoreAuthored` puts them back when the graph goes away, and `AlpsPreviewSessionWatcher` refreshes fixtures when animation mode ends. Captured defaults survive graph rebuilds on purpose, since the fixtures then hold the DMX setup, not the authored look.

The Timeline window's clip strip (`AlpsClipStrip`) compiles the clip on its own and renders it through `Hidden/ALPS/Clip Strip` (`Editor/Timeline`), which evaluates clip 0 alone per lane and sample, and reads it back.

### Build and play mode

There is no manual bake. `Editor/Build/AlpsBuildHooks.cs` validates and generates build Timelines before a VRChat build (`IVRCSDKBuildRequestedCallback`), and `IProcessSceneWithReport` (order -100, before UdonSharp strips proxies) runs `AlpsShowApplier.Apply` on the build scene copy and on entering play mode. Apply creates an `ALPS Show Player` under each show director on the scene copy (`AlpsShowSetup.GetOrCreatePlayer`, reusing one an older version left in the scene), compiles the show into it, swaps the director to a generated Timeline without ALPS tracks with other bindings carried over, and strips every `AlpsTarget` (containers and fixtures). Directors sharing a Timeline share one build copy. `AlpsBuildReferences` then points every other serialized reference to an authoring Timeline (component fields, UnityEvent arguments, UdonBehaviour public variables) at its build copy, and gives any director holding bindings for the authoring tracks the same bindings on the build tracks. The Udon program asset and the GPU assets (`AlpsGpuAssets`, under `Assets/ALPS/Gpu`) are made by the build preflight, and before play mode by `AlpsPlayModePreflight`, since neither Udon nor a running build can make them. The strip runs on every scene, with a show or without one. The authoring scene and Timeline are never modified.

- Every show of a scene shares VRSL's grid, so Apply hands out one DMX row per fixture across all of them (`AssignDmxRows`, at most `GpuMaxFixtures` = 118, which is what the grid holds), and `player.dmxRows` tells the player each fixture's row. A player draws only when its time moves, or every frame while its show tracks a user, and rows its show does not drive come out dark, so the show whose time moves last is the one on the grid.
- The shared materials and the two frames targets, the grid and the spin grid are assets. Each player sets its own show data on the shared materials before it blits.
- Fixture on row n reads absolute DMX channel 13n + 1 (`ConfigureVRSLDmx`). VRSL reads channel offset o of row n from texel (2o + 1, 2n + 1): its `IndustryRead` takes the row as an int, which drops the fraction of channel / 13. `AlpsGpuEvaluatorTests.Grid_ReadsBackThroughVrslsOwnFunctions` checks this through VRSL's own `getValueAtCoords` (the `Hidden/ALPS/Tests/VRSL Probe` shader), and `Diagnose_WhereVrslReads` dumps the texel of every channel.
- Brightness and colour are laid out to look the way VRSL's static path (the tint and the global intensity ALPS drove before DMX) showed them, measured against it on the stock movers. The colour goes through the gamma to linear conversion Unity gave the tint, brightness past 100% included, and up to 100% the dimmer takes the cube root of the level and the colour the rest, since VRSL's DMX path scales the floor by the dimmer once and the cone about by its sixth power. The dimmer is 1.26 and the colour 2.02 at 100% (`ALPS_DIMMER_AT_FULL`, `ALPS_COLOUR_AT_FULL`), and `AlpsGpuEvaluatorTests.GridDimmer` / `GridColour` mirror the mapping. Pan and tilt ride on base rotations of 0 and 90.
- Cone length rides on the fine pan channel, which is free with fine channels off: VRSL's volumetric mesh adds 4 per unit of it to `_MaxConeLength` when its material's `_EnableExtraChannels` ("Enable Cone Length Via DMX") is on. The model length scales the fixture's own mesh (`ModelConeLengthLimit` = 50 is the mesh itself), and the fade along the cone stays fully open (`coneLength` 10). A build points the fixtures' renderers at copies of their volumetric materials with the option on (`AlpsGpuAssets.UseConeLengthMaterials`), since a property block value outside VRSL's instanced ones could stop the renderers from being drawn together. The preview sets it per renderer block instead.

### Fixtures

`AlpsFixture` is the adapter seam (`AdapterKind`, capture default, DMX setup for the preview, restore, preview property gathering). Only `AlpsVRSLFixture` exists. The Udon side cannot call the MonoBehaviour adapters, so the VRSL mapping lives as statics on `AlpsShowPlayer` (`CaptureVRSL`, `CaptureVRSLDmxInfo`, `ConfigureVRSLDmx`) and the fixture delegates to them. Nothing writes VRSL's fields while a show plays: they are set once for DMX mode, and every channel travels through the grid.

### Containers and arrangement

`AlpsContainer` (`Runtime/Fixtures`) replaced both the fixture group and the separate arrangement component. It kept the fixture group's script GUID, so Timeline bindings survived, and the group's `fixtures` list loads into a hidden `legacyFixtures` that `AlpsArrangementWatcher.MigrateScene` turns into the children's order when a scene opens (`AlpsArrangementLayout.MigrateFixtureOrder`), then clears. Settings of the old `AlpsArrangement` component were not carried over.

A container can lay its direct children out in hierarchy order, inactive ones included, on a line, circle or arc, polygon, rectangle or grid, from an `AlpsArrangementSettings` whose sizes, offsets and rotations are `AlpsAnimatableValue`s. The shape defaults to `Off`, which lays nothing out. The layout is editor only for now: nothing moves it over time, so its rows offer S and not R, and a range saved on a value is ignored. The geometry is `AlpsArrangementEvaluator` (`Runtime/Playback`), statics over a layout row and one resolved value per `Value*` index, kept inside what UdonSharp compiles so a Timeline driven layout can run it in Udon later. Values are resolved in the model with the same maths the show uses for a value without a range, and a test holds the two together against the GPU evaluator.

`Editor/Arrangement` holds the rest. `AlpsArrangementLayout` writes only children whose pose differs, which keeps prefab instances free of empty overrides and is what stops the layout from looping. `AlpsArrangementWatcher` lays containers out again from `ObjectChangeEvents` (children added, removed, reordered, reparented or moved, fixtures added below, the component added or reset) and marks the preview dirty, since those can change which fixtures a track drives. It records into the current undo group, never increments it, skips the publish that follows an undo or redo, and waits while a handle is held. The first layout (`Initialize`) runs when a shape is first picked and starts the line on the first and last child.

### Inspector

The clip UI is UI Toolkit (`Editor/Inspector`, root view `AlpsClipInspectorView`, styles in `Resources/AlpsInspector.uss`). The look follows the IMGUI inspector, and `AlpsInspectorFont` gives the view the same fonts IMGUI uses (Inter plus the OS font IMGUI falls back to for Japanese), because UI Toolkit's own fallback picks a different Japanese font. Timeline's clip inspector only calls `OnInspectorGUI`, so `AlpsTimelineClipInspector` draws nothing in IMGUI and inserts the UI Toolkit view after the running IMGUI container (found through `AlpsImguiHost`, which reads an internal UIElements stack). The insert only happens on Repaint because the hierarchy is locked during layout. Views mutate the model directly, so undo is registered explicitly on pointer down.

## Constraints and conventions

- UdonSharp parses every file in `Runtime/` as C# 7.3: no `??=`, and U# classes cannot use user structs, generics, `List`, or `ref`/`out`. Editor code is not restricted.
- Shader comments follow the comment rules too, and HLSL does not short circuit `&&` and `||`, so both sides of a guard are always evaluated.
- Every importable file and folder needs a `.meta` (the metadata check and a test enforce it). Folders ending in `~` and dot folders are not imported. When moving files, move the `.meta` with them to keep GUIDs.
- Enum order is load bearing: `AlpsOrderMode`, `AlpsMoveMode` and similar enums are indexes of segmented controls and are mirrored by integer constants in `AlpsShowLayout` and the shader. `AlpsArrangementShape`, `AlpsArrangementSpacing` and `AlpsArrangementFacing` are mirrored in `AlpsArrangementEvaluator` the same way. Keep all three in sync.
- `AlpsPhaseSettings` carries a private serialized `version`. Anything saved before version 1 (forward / ping-pong / random modes, the Step ease) is converted in `OnAfterDeserialize`, and before version 2 the one shared ease is split into a rise and a fall ease that play the same (`AlpsEase.Reversed`, swapped when inverted). Changing its serialized layout again means bumping the version and converting from the previous one. `AlpsAnimatableValue` carries its own `version` the same way: before version 1 the spread was a step per order position, which cannot be converted without the fixture count, so it loads closed on the value. `JsonUtility` ignores `FormerlySerializedAs`, so renamed fields can only be tested through a real asset import.
- UI text is Japanese. Code comments are English.
- Test levels: Level 1 is the show's maths on compiled arrays, evaluated on the GPU and read back (`AlpsShowTests` through `AlpsGpuShow`, `AlpsGpuEvaluatorTests` for the grid, rows and tracking, and `AlpsArrangementEvaluatorTests` for arrangement geometry). Level 3 previews the real `PreviewSmoke` Timeline in edit mode and reads back the frames the preview drew. Level 4 applies the build conversion and checks the compiled Udon player draws the same frames as the preview. The smoke scene is regenerated by `ALPS > Tests > Regenerate Preview Smoke Fixture`.
- IMGUI handlers and `EditorApplication.delayCall` do not run in batch tests, and `ChangeEvent` only fires on elements attached to a panel. UI tests that need value callbacks host the view in a real `EditorWindow`.
