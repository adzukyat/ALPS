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

The arrays have fixed strides defined as constants in the evaluator: clip rows (`ClipStride`, including the shared phase and 16 sampled mix curve points), effect rows (`EffectStride`, per kind scalars `EffectScalarA..`), parameter rows (`ParamStride`, one per `AlpsAnimatableValue`, including its own phase), palette rows, and a per fixture frame of 12 channels (`Frame*`). Changing the model means changing the compiler encoding, the evaluator reads, and usually these strides together.

Key evaluation rules that span files:
- Order position `k` comes from `OrderPosition` (normal, reverse, symmetric counting outward from the middle, seeded random). Symmetric also mirrors the final pan of the first half (`IsMirrored`).
- A parameter value is `value or range(phase) + spread or spreadRange(phase) * k`. Ranges follow the parameter's own phase if set, otherwise the clip's shared phase, and respect timing (within cycle or per cycle).
- Composition: clips are sorted by layer (track order, override tracks after their parent). Clips inside a layer blend by weight, each layer covers lower ones per channel by its total weight, and gobo and track effect are discrete. Undriven channels keep the fixture default, except brightness, which is dark unless a brightness effect lights it, so a fixture no clip covers is dark too.
- Each clip has its own fade in / fade out in beats (`ClipFade`, counted at the clip's tempo). It multiplies the clip's Timeline weight, so a fade behaves like blending with an empty clip on every channel. The Timeline window draws its slopes (`AlpsTimelineClipEditor`).
- Move has three modes: angle, circle (a ring around a center direction computed on the sphere, so it never folds into a figure eight), and track user (resolved by the player through `VRCPlayerApi`).

### From Timeline to arrays

`AlpsTimelineTrack` binds an `AlpsFixtureGroup`. `AlpsTimelineClip` holds an `AlpsClipEffectSet` (the model in `Runtime/Model`), or follows an `AlpsClipProfile`. `AlpsShowCompiler.Compile(director)` walks the tracks and produces an `AlpsCompiledShow` (arrays plus fixture list, errors, warnings). `CompileStandalone` builds a show without a Timeline and is what Level 1 tests use. The compiler lives in the Runtime assembly because the preview needs it.

### Preview

`AlpsTimelineMixer` forwards `ProcessFrame` to the static `AlpsPreviewDriver`, which compiles once per director, evaluates once per frame, and writes frames through `AlpsFixture.ApplyFrame`. Clip edits call `AlpsPreviewDriver.MarkDirty()`. Restoring fixtures after preview relies on Timeline's animation mode: `AlpsTimelineTrack.GatherProperties` registers the VRSL fields, and `AlpsPreviewSessionWatcher` refreshes fixtures when animation mode ends. Captured defaults survive graph rebuilds on purpose.

### Build and play mode

There is no manual bake. `Editor/Build/AlpsBuildHooks.cs` validates and generates build Timelines before a VRChat build (`IVRCSDKBuildRequestedCallback`), and `IProcessSceneWithReport` (order -100, before UdonSharp strips proxies) runs `AlpsShowApplier.Apply` on the build scene copy and on entering play mode. Apply compiles the show into the inactive `ALPS Show Player` (created by `ALPS > Set Up Show Player`), swaps the director to a generated Timeline without ALPS tracks with other bindings carried over, and strips `AlpsFixture` / `AlpsFixtureGroup`. The authoring scene and Timeline are never modified.

### Fixtures

`AlpsFixture` is the adapter seam (`AdapterKind`, capture default, apply frame, preview property gathering). Only `AlpsVRSLFixture` exists. The Udon side cannot call the MonoBehaviour adapters, so the VRSL mapping lives as statics on `AlpsShowPlayer` (`ApplyVRSL`, `CaptureVRSL`) and the fixture delegates to them. Gobo rotation is written into each renderer's MaterialPropertyBlock after VRSL rebuilds its own block.

### Inspector

The clip UI is UI Toolkit (`Editor/Inspector`, root view `AlpsClipInspectorView`, styles in `Resources/AlpsInspector.uss`). The look follows the IMGUI inspector, and `AlpsInspectorFont` gives the view the same fonts IMGUI uses (Inter plus the OS font IMGUI falls back to for Japanese), because UI Toolkit's own fallback picks a different Japanese font. Timeline's clip inspector only calls `OnInspectorGUI`, so `AlpsTimelineClipInspector` draws nothing in IMGUI and inserts the UI Toolkit view after the running IMGUI container (found through `AlpsImguiHost`, which reads an internal UIElements stack). The insert only happens on Repaint because the hierarchy is locked during layout. Views mutate the model directly, so undo is registered explicitly on pointer down.

## Constraints and conventions

- UdonSharp parses every file in `Runtime/` as C# 7.3: no `??=`, and U# classes cannot use user structs, generics, `List`, or `ref`/`out`. Editor code is not restricted.
- Every importable file and folder needs a `.meta` (the metadata check and a test enforce it). Folders ending in `~` and dot folders are not imported. When moving files, move the `.meta` with them to keep GUIDs.
- Enum order is load bearing: `AlpsOrderMode`, `AlpsMoveMode` and similar enums are indexes of segmented controls and are mirrored by integer constants in `AlpsShowEvaluator`. Keep all three in sync.
- UI text is Japanese. Code comments are English.
- Test levels: Level 1 is evaluator math on compiled arrays (`AlpsShowEvaluatorTests`). Level 3 previews the real `PreviewSmoke` Timeline in edit mode. Level 4 applies the build conversion and checks the compiled Udon player matches the preview. The smoke scene is regenerated by `ALPS > Tests > Regenerate Preview Smoke Fixture`.
- IMGUI handlers and `EditorApplication.delayCall` do not run in batch tests, and `ChangeEvent` only fires on elements attached to a panel. UI tests that need value callbacks host the view in a real `EditorWindow`.
