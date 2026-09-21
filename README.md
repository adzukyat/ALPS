# Maneuver For VRC

Maneuver For VRC (MFV) is a Timeline based lighting show tool for VRChat worlds. Shows are authored as effect cues on Timeline tracks, previewed live in the Unity Editor, and played back in VRChat by an Udon player that drives VR Stage Lighting (VRSL) fixtures.

The effect parameters themselves travel to the Udon player. Nothing is sampled into keyframes, so editor preview and VRChat run the same evaluator on the same data.

## Installation

This repository is a UPM package at the repository root. Add it to a VCC world project with the normal Git URL workflow, without a `?path=` suffix.

The project must already resolve these packages:

- VR Stage Lighting `com.acchosen.vr-stage-lighting` `2.8.4`
- VRChat SDK Base `com.vrchat.base` `3.10.2`
- VRChat SDK Worlds `com.vrchat.worlds` `3.10.2`

Gobo rotation needs VRSL shaders that accept a script driven gobo angle. The patch lives on the `mfv-gobo-rotation` branch of [adzukyat/VR-Stage-Lighting](https://github.com/adzukyat/VR-Stage-Lighting/tree/mfv-gobo-rotation). With stock VRSL everything else works and gobos simply do not turn.

## Setup

### 1. Fixtures

Add `MFV VRSL Fixture` (`Maneuver For VRC > MFV VRSL Fixture`) to each VRSL DMX Static fixture, for example `VRSL-DMX-Mover-Spotlight-H-13CH`. Its `Target` is found in children when left empty.

### 2. Fixture groups

Add `MFV Fixture Group` to a parent object and list its fixtures. The list order is the fixture number used by the order, the odd and even split, and every per fixture offset. `Find Fixtures In Children` in the component menu fills the list.

### 3. Timeline

Add an `MfvTimelineTrack` to a Timeline and bind it to a fixture group. Set the track's `Bpm` and `Beat Origin` so cycles line up with the song. Tracks lower in the Timeline are layers above the ones before them: a later track only overrides the channels its effects drive. Override tracks nest the same way.

Other Timeline tracks such as `ActivationTrack`, `AnimationTrack` or `ControlTrack` are left untouched.

### 4. Clips

Select an MFV clip to edit it in the Inspector:

- **Order**: normal, reverse, symmetric or random fixture order. Symmetric counts outward from the middle and mirrors pan, so a pan spread opens into a fan. Each parameter row has R for a range and S for a spread. With S on the value becomes a fixed offset and R ranges the spread instead. Spread and the shared delay can be negative to run the other way.
- **Shared settings**: the phase every ranged parameter and palette follows (mode, easing, ping-pong ratio, fixture grouping, delay, speed in beats, invert). The delay is read over the whole group in percent, so 100% walks the wave across every fixture in one cycle whatever the fixture count, and the marks on its rail sit where that trip lands on a whole number of beats. The 拍 flag next to it switches the delay to beats, which then stays that many beats when the speed changes.
- **Effects**: move, cone, color, brightness, flicker and gobo. Move aims by angle, turns the beam in a circle around a center direction, or follows a user. Adding an effect that is already on the clip splits it into an even and an odd copy.
- **Profile**: save the clip to a profile asset, load it into other clips, or let a clip follow a profile.

### 5. Show player

Select the `PlayableDirector` and run `ManeuverForVRC > Set Up Show Player`. This adds an inactive `MFV Show Player` under the director. It stays empty in the authoring scene.

## Preview

Scrub or play the Timeline. MFV writes the evaluated show to the VRSL fixtures. When the Timeline window stops previewing, the fixture properties are reverted to their authored values and the scene is not marked dirty.

## Building and play mode

Nothing needs to be baked by hand.

- **VRChat Build & Test or Upload**: before the build starts, MFV validates every open show and writes a build copy of each Timeline without the MFV tracks to `Assets/ManeuverForVRC/Generated`. While Unity processes the scene copy for the build, the show is compiled into the player, the player is switched on, the director is pointed at the build Timeline with all other bindings carried over, and the fixture components are removed. The authoring scene and Timeline are never modified.
- **Play mode and ClientSim**: the same conversion runs on the play mode scene, so what plays there is what VRChat plays.

A build stops with an error when a director has MFV tracks but no show player, or when a fixture has no target.

## Current limitations

- Only VRSL DMX Static fixtures are driven. The fixture and adapter split is ready for other fixture types.
- Tracking a user (`Move > Track user`) only runs in VRChat and ClientSim. Its pan and tilt mapping still needs checking against real fixtures.
- Clips do not snap to beats yet.

## Validation

`TestProject~` is the committed Unity test harness and references this package with `file:../..`.

```sh
scripts/test-all.sh
```

This runs the metadata check, the EditMode tests (`ManeuverForVRC.EditorTests`) and the UI tests (`ManeuverForVRC.EditorUiTests`) with Unity `2022.3.22f1`. Set `UNITY_EXECUTABLE` when Unity is installed elsewhere. Results are written to `TestProject~/TestResults~/`.

`scripts/bootstrap-test-project.sh` downloads the VRChat SDK, AudioLink, and the patched VRSL into the harness.

Test levels:

- Level 1: evaluator math on compiled arrays (order, phase, ranges, palettes, layers, blending).
- Level 3: the real `PreviewSmoke` Timeline previewed in edit mode.
- Level 4: the build conversion applied to that scene, with the Udon player matching the preview and other tracks kept.

`ManeuverForVRC > Tests > Regenerate Preview Smoke Fixture` rebuilds the test scene, and `ManeuverForVRC > Demo > Rebuild Example` in `DemoProject~` rebuilds the demo show.

## Third party notices

See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
