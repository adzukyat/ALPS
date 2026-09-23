# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Adzuki Live Performance System (ALPS) is a UPM package, rooted at the repository root, for Timeline based lighting shows in VRChat worlds. Shows are authored as effect clips on Timeline tracks, previewed live in the editor, and played in VRChat by an UdonSharp player that drives VR Stage Lighting (VRSL) DMX Static fixtures. Effect parameters travel to Udon as data and are evaluated there. Nothing is baked into keyframes.

Unity `2022.3.22f1`, VRChat SDK `3.10.2`, VRSL `2.8.4`. Gobo rotation needs the VRSL shader patch on the `alps-gobo-rotation` branch of `adzukyat/VR-Stage-Lighting`, which the test bootstrap downloads at a pinned commit.

## Commands

All tests run Unity in batch mode against `TestProject~`, which references the package with `file:../..`. Set `UNITY_EXECUTABLE` if Unity is not at the Hub default path. Results go to `TestProject~/TestResults~/`.

```sh
scripts/test-all.sh            # metadata check, EditMode tests, UI tests
scripts/test-editmode.sh       # AdzukiSoft.ALPS.EditorTests (evaluator, preview, build conversion)
scripts/test-editor-ui.sh      # AdzukiSoft.ALPS.EditorUiTests (inspector UI and layout audit)
scripts/check-package-metadata.sh
scripts/bootstrap-test-project.sh   # fetches VRChat SDK, AudioLink and the patched VRSL
```

A single test or fixture (the scripts do not forward filters, so call Unity directly):

```sh
"/Applications/Unity/Hub/Editor/2022.3.22f1/Unity.app/Contents/MacOS/Unity" -batchmode -nographics \
  -projectPath TestProject~ -runTests -testPlatform EditMode \
  -assemblyNames AdzukiSoft.ALPS.EditorTests \
  -testFilter "AdzukiSoft.ALPS.Tests.AlpsShowEvaluatorTests.Circle_WalksAllTheWayRoundOncePerCycle" \
  -testResults "$TMPDIR/one.xml" -logFile "$TMPDIR/one.log"
```

Do not add `-quit`: with this Test Framework version it can exit before the results XML is written. Batch mode refuses a project that is open in the editor (`Temp/UnityLockfile`).

`DemoProject~` has an editor script, `ALPS > Demo > Rebuild Example` (batch entry `AlpsDemoBuilder.RebuildAndExit`), that regenerates the demo scene and `ExampleTimeline.playable` from code. Change the demo by editing `DemoProject~/Assets/Editor/AlpsDemoBuilder.cs`, not the assets.

## Architecture

### One evaluator for preview and Udon

`Runtime/Playback/AlpsShowEvaluator.cs` is the core. It is an UdonSharpBehaviour whose static methods take flat `float[]` / `int[]` arrays. UdonSharp inlines imported static methods into the calling program, so the editor preview and the Udon player (`AlpsShowPlayer`) run exactly the same math. Anything the show does at runtime must be expressible here.

The arrays have fixed strides defined as constants in the evaluator: clip rows (`ClipStride`, including the shared phase and 16 sampled mix curve points), effect rows (`EffectStride`, per kind scalars `EffectScalarA..`), parameter rows (`ParamStride`, one per `AlpsAnimatableValue`, including its own phase), palette rows, and a per fixture frame of 13 channels (`Frame*`). Changing the model means changing the compiler encoding, the evaluator reads, and usually these strides together.

Key evaluation rules that span files:
- Order position `k` comes from `OrderPosition` (normal, reverse, symmetric counting outward from the middle, seeded random). Symmetric also mirrors the final pan of the first half (`IsMirrored`).
- A phase is a wave or random. A wave cycle is four shares, rise, high hold, fall and the low hold they leave over (`AlpsShowEvaluator.Wave`). The rise and the fall each have their own ease (`ease` and `fallEase`), both read forwards in time, and invert flips the eased wave. The return leg (`IsReturnLeg`, blackout on return) is the fall plus the low hold.
- Each effect has a `phaseOffset` that runs every value on it late by that share of a cycle. It travels as `EffectPhaseOffset` and reaches the phase as the `extraCycles` term, so ranges, palettes, circles and blackout all shift together.
- A parameter value is `value` or `range(phase)`. With spread on it is `spreadRange` instead, the values at the first and last order position, or `spreadRange` to `spreadRangeEnd` by phase when also ranged. The compiler turns each spread into its first value plus a step per position (`StepFromSpread`, divided by `SpreadPositions` like the phase delay), so the evaluator computes `first(phase) + step(phase) * k`. Ranges follow the parameter's own phase if set, otherwise the clip's shared phase, and respect timing (within cycle or per cycle).
- Composition: clips are sorted by layer (track order, override tracks after their parent). Clips inside a layer blend by weight, each layer covers lower ones per channel by its total weight, and gobo and track effect are discrete. Undriven channels keep the fixture default, except brightness, which is dark unless a brightness effect lights it, so a fixture no clip covers is dark too.
- Each clip has its own fade in / fade out in beats (`ClipFade`, counted at the clip's tempo). It multiplies the clip's Timeline weight, so a fade behaves like blending with an empty clip on every channel. The Timeline window draws its slopes (`AlpsTimelineClipEditor`).
- Move has three modes: angle, circle (a ring around a center direction computed on the sphere, so it never folds into a figure eight), and track user (resolved by the player through `VRCPlayerApi`).

### From Timeline to arrays

`AlpsTimelineTrack` binds an `AlpsTarget`: an `AlpsContainer`, which drives every `AlpsFixture` below it in hierarchy order, or one fixture on its own (`CollectFixtures`). A Tween track for moving objects is planned and will bind containers the same way. `AlpsTimelineClip` holds an `AlpsClipEffectSet` (the model in `Runtime/Model`), or follows an `AlpsClipProfile`. `AlpsShowCompiler.Compile(director)` walks the tracks and produces an `AlpsCompiledShow` (arrays plus fixture list, errors, warnings). `CompileStandalone` builds a show without a Timeline and is what Level 1 tests use. The compiler lives in the Runtime assembly because the preview needs it.

### Preview

`AlpsTimelineMixer` forwards `ProcessFrame` to the static `AlpsPreviewDriver`, which compiles once per director, evaluates once per frame, and writes frames through `AlpsFixture.ApplyFrame`. Clip edits call `AlpsPreviewDriver.MarkDirty()`. Restoring fixtures after preview relies on Timeline's animation mode: `AlpsTimelineTrack.GatherProperties` registers the VRSL fields, and `AlpsPreviewSessionWatcher` refreshes fixtures when animation mode ends. Captured defaults survive graph rebuilds on purpose.

### Build and play mode

There is no manual bake. `Editor/Build/AlpsBuildHooks.cs` validates and generates build Timelines before a VRChat build (`IVRCSDKBuildRequestedCallback`), and `IProcessSceneWithReport` (order -100, before UdonSharp strips proxies) runs `AlpsShowApplier.Apply` on the build scene copy and on entering play mode. Apply compiles the show into the inactive `ALPS Show Player` (created by `ALPS > Set Up Show Player`), swaps the director to a generated Timeline without ALPS tracks with other bindings carried over, and strips every `AlpsTarget` (containers and fixtures). The strip runs on every scene, with a show or without one. The authoring scene and Timeline are never modified.

### Fixtures

`AlpsFixture` is the adapter seam (`AdapterKind`, capture default, apply frame, preview property gathering). Only `AlpsVRSLFixture` exists. The Udon side cannot call the MonoBehaviour adapters, so the VRSL mapping lives as statics on `AlpsShowPlayer` (`ApplyVRSL`, `CaptureVRSL`) and the fixture delegates to them. Gobo rotation is written into each renderer's MaterialPropertyBlock after VRSL rebuilds its own block.

### Containers and arrangement

`AlpsContainer` (`Runtime/Fixtures`) replaced both the fixture group and the separate arrangement component. It kept the fixture group's script GUID, so Timeline bindings survived, and the group's `fixtures` list loads into a hidden `legacyFixtures` that `AlpsArrangementWatcher.MigrateScene` turns into the children's order when a scene opens (`AlpsArrangementLayout.MigrateFixtureOrder`), then clears. Settings of the old `AlpsArrangement` component were not carried over.

A container can lay its direct children out in hierarchy order, inactive ones included, on a line, circle or arc, polygon, rectangle or grid, from an `AlpsArrangementSettings` whose sizes, offsets and rotations are `AlpsAnimatableValue`s. The shape defaults to `Off`, which lays nothing out. The layout is editor only for now: nothing moves it over time, so its rows offer S and not R, and a range saved on a value is ignored. The geometry is `AlpsArrangementEvaluator` (`Runtime/Playback`), statics over a layout row and one resolved value per `Value*` index, kept inside what UdonSharp compiles so a Timeline driven layout can run it in Udon later. Values are resolved in the model with the same math as `ResolveScalar` without a range, and a test holds the two together.

`Editor/Arrangement` holds the rest. `AlpsArrangementLayout` writes only children whose pose differs, which keeps prefab instances free of empty overrides and is what stops the layout from looping. `AlpsArrangementWatcher` lays containers out again from `ObjectChangeEvents` (children added, removed, reordered, reparented or moved, fixtures added below, the component added or reset) and marks the preview dirty, since those can change which fixtures a track drives. It records into the current undo group, never increments it, skips the publish that follows an undo or redo, and waits while a handle is held. The first layout (`Initialize`) runs when a shape is first picked and starts the line on the first and last child.

### Inspector

The clip UI is UI Toolkit (`Editor/Inspector`, root view `AlpsClipInspectorView`, styles in `Resources/AlpsInspector.uss`). The look follows the IMGUI inspector, and `AlpsInspectorFont` gives the view the same fonts IMGUI uses (Inter plus the OS font IMGUI falls back to for Japanese), because UI Toolkit's own fallback picks a different Japanese font. Timeline's clip inspector only calls `OnInspectorGUI`, so `AlpsTimelineClipInspector` draws nothing in IMGUI and inserts the UI Toolkit view after the running IMGUI container (found through `AlpsImguiHost`, which reads an internal UIElements stack). The insert only happens on Repaint because the hierarchy is locked during layout. Views mutate the model directly, so undo is registered explicitly on pointer down.

## Constraints and conventions

- UdonSharp parses every file in `Runtime/` as C# 7.3: no `??=`, and U# classes cannot use user structs, generics, `List`, or `ref`/`out`. Editor code is not restricted.
- Every importable file and folder needs a `.meta` (the metadata check and a test enforce it). Folders ending in `~` and dot folders are not imported. When moving files, move the `.meta` with them to keep GUIDs.
- Enum order is load bearing: `AlpsOrderMode`, `AlpsMoveMode` and similar enums are indexes of segmented controls and are mirrored by integer constants in `AlpsShowEvaluator`. `AlpsArrangementShape`, `AlpsArrangementSpacing` and `AlpsArrangementFacing` are mirrored in `AlpsArrangementEvaluator` the same way. Keep all three in sync.
- `AlpsPhaseSettings` carries a private serialized `version`. Anything saved before version 1 (forward / ping-pong / random modes, the Step ease) is converted in `OnAfterDeserialize`, and before version 2 the one shared ease is split into a rise and a fall ease that play the same (`AlpsEase.Reversed`, swapped when inverted). Changing its serialized layout again means bumping the version and converting from the previous one. `AlpsAnimatableValue` carries its own `version` the same way: before version 1 the spread was a step per order position, which cannot be converted without the fixture count, so it loads closed on the value. `JsonUtility` ignores `FormerlySerializedAs`, so renamed fields can only be tested through a real asset import.
- UI text is Japanese. Code comments are English.
- Test levels: Level 1 is evaluator math on compiled arrays (`AlpsShowEvaluatorTests`, and `AlpsArrangementEvaluatorTests` for arrangement geometry). Level 3 previews the real `PreviewSmoke` Timeline in edit mode. Level 4 applies the build conversion and checks the compiled Udon player matches the preview. The smoke scene is regenerated by `ALPS > Tests > Regenerate Preview Smoke Fixture`.
- IMGUI handlers and `EditorApplication.delayCall` do not run in batch tests, and `ChangeEvent` only fires on elements attached to a panel. UI tests that need value callbacks host the view in a real `EditorWindow`.
