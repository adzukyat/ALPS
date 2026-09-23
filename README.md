# Adzuki Live Performance System

Adzuki Live Performance System (ALPS) is a Timeline based lighting show tool for VRChat worlds. Shows are authored as effect cues on Timeline tracks, previewed live in the Unity Editor, and played back in VRChat by an Udon player that drives VR Stage Lighting (VRSL) fixtures.

The effect parameters themselves travel to the Udon player. Nothing is sampled into keyframes, so editor preview and VRChat run the same evaluator on the same data.

## Installation

This repository is a UPM package at the repository root. Add it to a VCC world project with the normal Git URL workflow, without a `?path=` suffix.

The project must already resolve these packages:

- VR Stage Lighting `com.acchosen.vr-stage-lighting` `2.8.4`
- VRChat SDK Base `com.vrchat.base` `3.10.2`
- VRChat SDK Worlds `com.vrchat.worlds` `3.10.2`

Gobo rotation needs VRSL shaders that accept a script driven gobo angle. The patch lives on the `alps-gobo-rotation` branch of [adzukyat/VR-Stage-Lighting](https://github.com/adzukyat/VR-Stage-Lighting/tree/alps-gobo-rotation). With stock VRSL everything else works and gobos simply do not turn.

## Setup

### 1. Fixtures

Add `ALPS VRSL Fixture` (`Adzuki Live Performance System > ALPS VRSL Fixture`) to each VRSL DMX Static fixture, for example `VRSL-DMX-Mover-Spotlight-H-13CH`. Its `Target` is found in children when left empty.

New fixtures can also come from the Hierarchy: right click (or the + button, or the GameObject menu) and pick `ALPS > Mover Spotlight`, `Mover Wash Light`, `Par Light`, `Blinder`, `Light Bar`, `Multi Light Bar` or `Laser`. Each is the VRSL horizontal mode prefab with `ALPS VRSL Fixture` already on it. It goes under the object you clicked, or next to it when that object is a fixture, so repeating the menu on the last fixture keeps filling the same container.

### 2. Containers

Put fixtures under an object with `ALPS Container` (`Adzuki Live Performance System > ALPS Container`), or create one from the Hierarchy with `ALPS > Container`. A container holds every fixture below it, inactive ones and ones nested deeper included, and their order in the Hierarchy is the fixture number used by the order, the odd and even split, and every per fixture offset. Reorder them in the Hierarchy to change it. Containers can be nested, so one for the whole rig can hold one per truss.

A container that was an `ALPS Fixture Group` in an older version keeps its Timeline bindings. When its scene opens, the children are reordered to follow the group's old fixture list. Fixtures the list held from outside the container are reported in the console, since only fixtures below it are driven now.

### 3. Arrangement (optional)

A container can also lay its direct children out for you. Its 形状 starts at オフ, which leaves the children where they are. Pick a shape and the children are kept laid out while you edit: changing a value or dragging a handle in the Scene view moves them at once, adding, removing or reordering children in the Hierarchy lays them out again, and a child moved by hand goes back. The first shape starts its line on the first and last child and keeps a rotation they all share, so a hand placed row stays where it is. Set 形状 back to オフ to adjust children by hand. Fixtures and any other objects work, and inactive children keep their place.

- **形状**: オフ, 直線 between a start and an end point, 円 with a radius (an arc when 角度 is under 360°), 多角形 with a number of sides, 矩形 with a width and depth, or グリッド with a column count. Everything is in the object's local space, with planar shapes on its XZ plane, so rotate the object to stand a shape up. The start and end points, the radius, the width and the depth have handles in the Scene view.
- **配置**: 端から端 puts the first and last child on the ends, 均等割り gives every child an equal share and sits it in the middle of it.
- **向き**: keep the object's rotation, or turn each child's front (+Z) outward, inward, along the path or toward a point. X/Y/Z回転 turn each child further about its own axes after that.
- **位置**: 高さ lifts children and 外側 pushes them out of the shape.
- **S (spread)** works as on clips: a value runs from the first child to the last, in the 並び順 order. A Y回転 spread fans children out, 左右対称 mirrors the first half so the fan opens both ways, 外側 with 左右対称 makes a V, and 高さ on a circle makes a spiral. R is not offered yet, since nothing moves a layout over time.

### 4. Timeline

Add an `AlpsTimelineTrack` to a Timeline and bind it to a container, or to a single fixture to drive it alone. Tracks lower in the Timeline are layers above the ones before them: a later track only overrides the channels its effects drive. Override tracks nest the same way.

Other Timeline tracks such as `ActivationTrack`, `AnimationTrack` or `ControlTrack` are left untouched.

### 5. Clips

Select an ALPS clip to edit it in the Inspector:

- **Tempo**: 全体BPM is the tempo of the whole Timeline, and editing it from any clip changes it for every clip. BPMオーバーライド gives one clip its own tempo, for songs that change tempo part way. Every clip counts beats from its own start, so its first cycle begins where the clip begins. Place clips on the beat of the song to keep them in time.
- **Order**: normal, reverse, symmetric or random fixture order. Symmetric counts outward from the middle and mirrors pan, so a pan spread opens into a fan. Each parameter row has R for a range and S for a spread. With S on the slider sets the first and the last fixture in the order, and the fixtures between are spaced evenly whatever their count. Drag one thumb past the other to make the spread run downward. With R on as well a second spread slider appears below, and the range moves from the upper spread to the lower one. The shared delay can be negative to run the other way.
- **Shared settings**: the phase every ranged parameter and palette follows (mode, distribution, easing, fixture grouping, delay, speed in beats, invert). A wave splits each cycle into a rise, a hold at the top, a fall and a hold at the bottom, and the distribution row sets how long each part lasts: a rise over the whole cycle is a sawtooth, and a rise and fall of zero make a square wave. Random wanders smoothly instead. The delay is read over the whole group in percent, so 100% walks the wave across every fixture in one cycle whatever the fixture count, and the marks on its rail sit where that trip lands on a whole number of beats. The 拍 flag next to it switches the delay to beats, which then stays that many beats when the speed changes.
- **Effects**: move, cone, color, brightness, flicker and gobo. Move aims by angle, turns the beam in a circle around a center direction, or follows a user. Adding an effect that is already on the clip splits it into an even and an odd copy. Each effect has a phase offset that runs it that share of a cycle late, so setting the odd copy to 50% makes the two sides take turns.
- **Profile**: save the clip to a profile asset, load it into other clips, or let a clip follow a profile.

## Preview

Scrub or play the Timeline. ALPS writes the evaluated show to the VRSL fixtures. When the Timeline window stops previewing, the fixture properties are reverted to their authored values and the scene is not marked dirty.

## Building and play mode

Nothing needs to be baked by hand.

- **VRChat Build & Test or Upload**: before the build starts, ALPS validates every open show and writes a build copy of each Timeline without the ALPS tracks to `Assets/ALPS/Generated`. While Unity processes the scene copy for the build, an `ALPS Show Player` is added under every director whose Timeline has ALPS tracks and the show is compiled into it, the director is pointed at the build Timeline with all other bindings carried over, and the fixture components are removed. Any other component in the scene that references the authoring Timeline, such as a player that assigns it to a director itself or an UdonBehaviour variable, is switched to the build copy too, and a director that holds bindings for the authoring Timeline gets the same bindings for the build copy. Containers are removed from every scene as well, and the children stay where they were laid out. The authoring scene and Timeline are never modified.
- **Play mode and ClientSim**: the same conversion runs on the play mode scene, so what plays there is what VRChat plays.

A build stops with an error when a fixture has no target.

## Current limitations

- Only VRSL DMX Static fixtures are driven. The fixture and adapter split is ready for other fixture types.
- Tracking a user (`Move > Track user`) only runs in VRChat and ClientSim. Its pan and tilt mapping still needs checking against real fixtures.
- Clips do not snap to beats yet.

## Validation

`TestProject~` is the committed Unity test harness and references this package with `file:../..`.

```sh
scripts/test-all.sh
```

This runs the metadata check, the EditMode tests (`AdzukiSoft.ALPS.EditorTests`) and the UI tests (`AdzukiSoft.ALPS.EditorUiTests`) with Unity `2022.3.22f1`. Set `UNITY_EXECUTABLE` when Unity is installed elsewhere. Results are written to `TestProject~/TestResults~/`.

`scripts/bootstrap-test-project.sh` downloads the VRChat SDK, AudioLink, and the patched VRSL into the harness.

Test levels:

- Level 1: evaluator math on compiled arrays (order, phase, ranges, palettes, layers, blending), and where arrangements place each child.
- Level 3: the real `PreviewSmoke` Timeline previewed in edit mode.
- Level 4: the build conversion applied to that scene, with the Udon player matching the preview and other tracks kept.

`ALPS > Tests > Regenerate Preview Smoke Fixture` rebuilds the test scene, and `ALPS > Demo > Rebuild Example` in `DemoProject~` rebuilds the demo show.

## Third party notices

See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
