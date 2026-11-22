#!/usr/bin/env python3
"""
Copies the part of VR Stage Lighting that ALPS fixtures are made of into Runtime/VRSL, so
ALPS does not depend on the VRSL package a world has installed.

    python scripts/vendor-vrsl.py [path to com.acchosen.vr-stage-lighting]

It takes the DMX prefabs of the create menu and everything they reference (meshes,
materials, textures, shaders and their includes, and the DMX Static script), and the
material a mesh of the world's own is made to glow with, and changes only what keeps the
copy apart from a VRSL package in the same project:

- every GUID becomes md5("alps-vrsl:" + the original), which the migration tool relies on
  to find the copy of a VRSL asset,
- shaders are named "ALPS/VRSL/..." and include each other by relative path,
- the script moves to the AdzukiSoft.ALPS.Vrsl namespace,
- references to VRSL's own DMX render textures, which ALPS replaces, are cleared,
- the volumetric materials have cone length via DMX on, which is how ALPS sets the length,
- the projections are drawn as added light (Blend One One), with the intensity of their
  materials scaled to match, so overlapping ones add up instead of multiplying,
- the multi light bar's material is in its 48 channel mode, a dimmer and a colour for each
  of its twelve segments, which ALPS drives as a fixture each,
- the material a mesh glows with reads its row as the thirteen channels of a fixture, where
  VRSL's example of it reads five,
- the lit faces shine about as much on every fixture, and far less than VRSL makes them,
- the smoke in a mover's cone thins the light by half at most, where VRSL's leaves holes,
- a cone and a projection are drawn down to a far darker colour, since ALPS dims a fixture
  by its colour,
- a static light throws no more light on what is right in front of it than on what is 25 cm
  away, and none on its own face,
- a laser's beams stand where the spin grid says, where VRSL moves them on by its own clock,
- the flasher and the strobe panel read the 13 channel row the other static lights read,
- the mirror ball reads the dimmer and the colour of that row, turns by the spin grid, which
  follows the show's time, and adds its light without dithering it.

The global shader properties and the DMX grid layout are left as they are. Running it again
replaces Runtime/VRSL with a fresh copy.
"""
import hashlib
import os
import re
import shutil
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SOURCE = os.path.abspath(sys.argv[1]) if len(sys.argv) > 1 else os.path.join(
    ROOT, "TestProject~", "Packages", "com.acchosen.vr-stage-lighting")
TARGET = os.path.join(ROOT, "Runtime", "VRSL")
SOURCE_PACKAGE = "Packages/com.acchosen.vr-stage-lighting/"

# The prefabs the create menu offers.
PREFABS = [
    "Runtime/Prefabs/DMX/Horizontal Mode/VRSL-DMX-Mover-Spotlight-H-13CH.prefab",
    "Runtime/Prefabs/DMX/Horizontal Mode/VRSL-DMX-Mover-WashLight-H-13CH.prefab",
    "Runtime/Prefabs/DMX/Horizontal Mode/VRSL-DMX-Static-ParLight-H-13CH.prefab",
    "Runtime/Prefabs/DMX/Horizontal Mode/VRSL-DMX-Static-Blinder-H-13CH.prefab",
    "Runtime/Prefabs/DMX/Horizontal Mode/VRSL-DMX-Static-LightBar-H-13CH.prefab",
    "Runtime/Prefabs/DMX/Horizontal Mode/VRSL-DMX-Static-MultiLightBar-H-15CH.prefab",
    "Runtime/Prefabs/DMX/Horizontal Mode/VRSL-DMX-Static-Laser-H-13CH.prefab",
    "Runtime/Prefabs/DMX/Horizontal Mode/VRSL-DMX-Static-Flasher-H-1CH.prefab",
    "Runtime/Prefabs/DMX/Horizontal Mode/5-Channel Statics/VRSL-DMX-Static-6x4StrobeLight-H-5CH.prefab",
    "Runtime/Prefabs/DMX/Horizontal Mode/VRSL-DMX-Static-DiscoBall-H-1CH.prefab",
]
# The material a mesh of the world's own is drawn with to glow as a fixture: VRSL's example of
# its surface shader, the shader of the lit faces. No prefab holds it, since the mesh it goes
# on is the world's.
MATERIALS = ["Runtime/Materials/Lights/DMX/VRSL-Horizontal-ExampleSurfaceShader-Cube.mat"]
# The gobo thumbnails the inspector shows.
EXTRA_FOLDERS = ["Runtime/Textures/MoverLightTextures/GOBO/IndividualGobos"]
# VRSL's own DMX reader, which ALPS replaces. References to these are cleared.
CUT = ["Runtime/Textures/RTs/", "Runtime/Materials/CRT Materials/", "Runtime/Shaders/CRTShaders/"]
# The only script the prefabs need, and its Udon program.
SCRIPTS = ["Runtime/Scripts/VRStageLighting_DMX_Static.cs", "Runtime/Scripts/VRStageLighting_DMX_Static.asset"]

YAML = (".prefab", ".mat", ".asset", ".controller", ".anim")
SHADER = (".shader", ".cginc", ".hlsl")
GUID = re.compile(r"guid: ([0-9a-f]{32})")
REFERENCE = re.compile(r"\{fileID: -?\d+, guid: ([0-9a-f]{32}), type: \d\}")
# VRSL's projections scale the colour already drawn by one plus the projection, so each one
# on a pixel multiplies the ones before it. The copies add their light instead.
PROJECTION_BLEND = re.compile(r"Blend\s+DstColor\s+\[_BlendDst\]")
# VRSL's intensity scales the colour drawn, so added as light it is too high by that colour's
# brightness. At this scale one projection on VRChat's default floor adds about the light it
# did. A darker floor gets more light than it did, a brighter one less.
PROJECTION_INTENSITY_SCALE = 0.1
PROJECTION_INTENSITY = re.compile(r"(- _Projection(?:Max)?Intensity: )([0-9.eE+-]+)")
# The multi light bar reads one colour and twelve dimmers until its material asks for the 48
# channel mode, which takes the keyword as well as the property. Other materials carry a
# channel mode of their own, which stays.
BAR_MATERIAL = "VRSL-LightBar-FixtureMat-Horizontal-15CH.mat"
BAR_CHANNEL_MODE = "- _ChannelMode: 0"
BAR_KEYWORD = "_CHANNEL_MODE"
KEYWORDS = re.compile(r"(m_ShaderKeywords:[^\r\n]*)")
# VRSL's example of the surface shader reads five channels from the start of its row, a
# dimmer, a colour and a strobe. ALPS lays a row out as the thirteen channels of a fixture,
# which the shader reads in its 13 channel mode, the mode of the other lit faces. Like the
# multi light bar's, the mode is a property and a keyword.
MESH_MATERIAL = "VRSL-Horizontal-ExampleSurfaceShader-Cube.mat"
MESH_CHANNEL_MODE = ("- _ChannelMode: 2", "- _ChannelMode: 3")
MESH_KEYWORD = ("_5CH_MODE", "_13CH_MODE")
# The flasher reads one channel and the strobe panel five, a dimmer, the colour and a strobe,
# from a channel layout of their own. Their shaders have the 13 channel mode the light bar
# and the par light are in, a dimmer on the sixth channel and the colour on the eighth to
# tenth, which is what ALPS writes on every row, so their materials are switched to it: the
# property and the keyword in their list of valid keywords. Each is the mode it leaves.
STATIC_13CH = {
    "VRSL-Flasher-FixtureMat-Horizontal.mat": "_1CH_MODE",
    "VRSL-LensFlareBase-Flasher-Horizontal.mat": "_1CH_MODE",
    "VRSL-Blinder-FixtureMat-Horizontal-5CH.mat": "_5CH_MODE",
}
STATIC_13CH_MODE = re.compile(r"(- _ChannelMode: )[0-9]+")
# VRSL lights the face of a fixture hundreds of times past white at full, 1450 on a light
# bar and 4300 on the spotlight's lens, which bloom turns into a glare and which washes
# every colour out to white. These are the values at which a face comes out about four
# times past white at 100%, measured on each fixture in DMX mode with the dimmer and the
# colour ALPS writes, so the same level looks about the same on all of them and a level past
# 100% still has somewhere to go. The wash mover squares its intensity, the others scale by
# it. The spotlight's lens is the end of its cone, which its cone material lights by the
# lens brightness on top of the cone's own light there. The blinder's lens flare, which is
# drawn over its face, peaks near white, and its curve modifier is 0: VRSL's 5 holds the
# flare back until the dimmer is up, and ALPS keeps the dimmer up and dims by the colour,
# which the flare follows at 0. A mesh made to glow is all face, so it shines as much. The
# strobe panel takes the blinder's value, whose shader, curve modifier and emission mask it
# shares. The flasher's face comes out four times as bright as the light bar's at the same
# value, so it takes a quarter of it. The flasher's flare takes the blinder's flare's, the
# same shader and texture, which no test measures. AlpsFaceBrightnessTests holds the faces.
FACE = {
    "VRSL-Horizontal-ExampleSurfaceShader-Cube.mat": {"_FixtureMaxIntensity": 1.247},
    "VRSL-LightBar-FixtureMat-Horizontal.mat": {"_FixtureMaxIntensity": 0.0413},
    "VRSL-LightBar-FixtureMat-Horizontal-15CH.mat": {"_FixtureMaxIntensity": 4},
    "VRSL-ParLight-FixtureMat-Horizontal.mat": {"_FixtureMaxIntensity": 0.0307},
    "VRSL-Blinder-FixtureMat-Horizontal.mat": {"_FixtureMaxIntensity": 0.0413},
    "VRSL-Blinder-LensFlareMat-Horizontal.mat": {"_FixtureMaxIntensity": 3, "_CurveMod": 0},
    "VRSL-WashMover-HQ-FixtureMat-Horizontal.mat": {"_FixtureMaxIntensity": 0.0179},
    "VRSL-StandardMover-VolumetricMat-HQ-Horizontal.mat": {"_LensMaxBrightness": 0.0063},
    "VRSL-Flasher-FixtureMat-Horizontal.mat": {"_FixtureMaxIntensity": 0.0103},
    "VRSL-LensFlareBase-Flasher-Horizontal.mat": {"_FixtureMaxIntensity": 3, "_CurveMod": 0},
    "VRSL-Blinder-FixtureMat-Horizontal-5CH.mat": {"_FixtureMaxIntensity": 0.0413},
}
# The smoke in a mover's cone is a noise that darkens it by this share at its thinnest.
# VRSL's 0.85 and 0.887 leave about a seventh of the light there, which reads as holes in
# the beam and not as smoke. At half, the thinnest smoke still shows half the light. Both the
# high quality value and the default one are set, since the quality mode picks between them.
# Thinner smoke leaves more of the light on average, about a quarter, so the cone's own
# intensity (VRSL's 0.25 and 0.2) comes down by as much and a beam is as bright as it was.
# The spotlight's lens is lit through the same smoke, which is why its brightness in FACE
# was measured with these.
SMOKE = {
    "VRSL-StandardMover-VolumetricMat-HQ-Horizontal.mat": {"_Noise2Power": 0.5, "_Noise2PowerDefault": 0.5, "_FixtureMaxIntensity": 0.195},
    "VRSL-WashMover-VolumetricMat-HQ-Horizontal.mat": {"_Noise2Power": 0.5, "_Noise2PowerDefault": 0.5, "_FixtureMaxIntensity": 0.152},
}
# VRSL stops drawing a cone or a projection whose colour is under a hundredth or so, which
# saves the work on a dark fixture. ALPS dims a fixture by its colour, along the curve the
# eye reads, so that is a brightness of 10 to 17%, where the light would drop out of a fade.
# At this colour it is under 2%.
DARK_CUTOFF = re.compile(r"(all\(\s*[io]\.rgbColor\s*<=\s*(?:half4|float4)\()[0-9.]+\s*,\s*[0-9.]+\s*,\s*[0-9.]+(\s*,\s*1\s*\))")
DARK_CUTOFF_COLOUR = "0.0001"
DARK_CUTOFFS = 9
# A par or a blinder throws light that grows by one over the distance to where it starts,
# without end, and a par's starts 5 cm behind its own face, the middle of which came out over
# a hundred times past white. At 25 cm a par still throws six times what its face shines, so
# holding the light there is not enough: nearer than that it stays what it is there and
# fades, to none at 10 cm, which the face is inside of. The distances are metres. The same
# scale also sizes the beam, which keeps VRSL's.
CLOSE_LIGHT = re.compile(r"col \* UVscale\s*\* _ProjectionMaxIntensity")
CLOSE_LIGHT_DISTANCE = 0.25
CLOSE_LIGHT_NONE = 0.1
CLOSE_LIGHT_SCALE = (
    "col * (min(UVscale, 1/(_ProjectionDistanceFallOff + ({0} * _ProjectionUVMod) + (_FeatherOffset * ({0} * {0}))))"
    " * smoothstep({1}, {0}, distanceFromOrigin)) * _ProjectionMaxIntensity").format(CLOSE_LIGHT_DISTANCE, CLOSE_LIGHT_NONE)
# VRSL's laser takes a speed from the spin grid and moves its beams round the cone by that
# speed times the shader's own time, which differs from one visitor to the next and does not
# follow a seek. In DMX mode the copy takes how far the beams have gone round, in turns, from
# the spin grid as it is, so a show places them by its own time. Each of its two passes
# reads the phase and moves the beams.
LASER_SPIN = re.compile(r"(GetSpin\(dmx\s*\+\s*\(uint\)\s*6\))\s*\*\s*_ScrollOffset")
LASER_SCROLL = re.compile(r"laserUV\.x \+= _Time\.y \* scroll;")
LASER_SCROLL_PLACED = "laserUV.x += IF(isDMX() > 0, scroll, _Time.y * scroll);"
LASER_PASSES = 2
# VRSL's mirror ball reads its first channel as how bright it is, takes its colour from the
# script's tint and turns by the shader's own time. In DMX mode the copy reads the row the
# way the other fixtures do: the dimmer only says whether it is lit, the colour carries the
# level, and the spin grid on the gobo's channel says how far it has turned, as an angle of
# four times the phase, turned back for an inverted pan the way VRSL reads a gobo's, so a
# show turns it by its own time. Each of its two SubShaders reads the row, turns the ball
# and lights it.
BALL_INTERPOLATOR = re.compile(r"^([ \t]*)float2 dmxIntensity: TEXCOORD7;[ \t]*(\r?\n)", re.M)
BALL_INTERPOLATOR_ADDED = r"\1float2 dmxIntensity: TEXCOORD7;\2\1float4 dmxLight: TEXCOORD8;\2"
BALL_READ = re.compile(r"^([ \t]*)o\.dmxIntensity = IF\(_EnableCompatibilityMode == 1,[^\r\n]*\);[ \t]*(\r?\n)", re.M)
BALL_READ_ROW = (
    r"\1o.dmxIntensity = float2(dmx, getValueAtCoords(dmx + 5, _Udon_DMXGridRenderTexture));\2"
    r"\1o.dmxLight = float4(getValueAtCoords(dmx + 7, _Udon_DMXGridRenderTexture), getValueAtCoords(dmx + 8, _Udon_DMXGridRenderTexture), "
    r"getValueAtCoords(dmx + 9, _Udon_DMXGridRenderTexture), getValueAtCoordsRaw(dmx + 10, _Udon_DMXGridSpinTimer) * (checkPanInvertY() == 1 ? -4.0 : 4.0));\2")
BALL_ROTATION = re.compile(r"float4 Rotation\(float4 vertPos\)")
BALL_ROTATION_TURNED = "float4 Rotation(float4 vertPos, float turn)"
BALL_ANGLE = re.compile(r"float angleY = radians\(_Time\.y \* _RotationSpeed\);")
BALL_ANGLE_PLACED = "float angleY = _EnableDMX == 1 ? turn : radians(_Time.y * _RotationSpeed);"
BALL_TURN = re.compile(r"Rotation\(float4\(projPos, 0\)\)")
BALL_TURN_PLACED = "Rotation(float4(projPos, 0), i.dmxLight.w)"
# VRSL squares the ball's light, its colour and its intensity with it, which would dim the
# ball by the level to the power of 4.4. The copy squares the pattern and how it falls off
# with the distance, which is the look of VRSL's ball, and takes the colour once, so the ball
# dims by the curve of every other fixture. The light grows toward the ball without end and
# lands on the ball itself, so nearer than a share of its cube it stays what it is there and
# fades, to none just outside the ball, in DMX mode and out of it. The distances are in the
# cube's own space, where the prefab's 25 m cube puts 1 m at 0.04 and the ball's surface at
# 0.0095.
BALL_LIGHT = re.compile(
    r"^([ \t]*)col = IF\(_EnableDMX == 1, col \* i\.dmxIntensity\.y, col\);[ \t]*\r?\n"
    r"[ \t]*col = \(col \* _Multiplier\)\*\(\(col \* getGlobalIntensity\(\)\) \* getFinalIntensity\(\)\);[ \t]*(\r?\n)", re.M)
BALL_CLOSE_DISTANCE = 0.04
BALL_CLOSE_NONE = 0.012
BALL_LIGHT_ADDED = (
    r"\1float ballNear = length(projPos);\2"
    r"\1float ballHeld = saturate(ballNear / {0});\2"
    r"\1col = (_EnableDMX == 1\2"
    r"\1    ? (col * col) * float4(i.dmxLight.rgb, 1) * (_Multiplier * getGlobalIntensity() * getFinalIntensity())\2"
    r"\1    : (col * _Multiplier) * ((col * getGlobalIntensity()) * getFinalIntensity()))\2"
    r"\1    * (ballHeld * ballHeld * smoothstep({1}, {0}, ballNear));\2").format(BALL_CLOSE_DISTANCE, BALL_CLOSE_NONE)
BALL_PASSES = 2
# The mirror ball's material, drawn as added light like the static projections: no dither
# that let the colour drawn show through where the multiplied light was dark, no alpha to
# coverage and the static projections' queue. VRSL's multiplier squared a tint of 2 and
# scaled the colour drawn, so its 4.55 comes to 4.55 * 4 / 2.02 * PROJECTION_INTENSITY_SCALE
# with ALPS's colour of 2.02 for white at 100%, taken once.
BALL_MATERIAL = "VRSL-Discoball-Horizontal.mat"
BALL_VALUES = {"_Multiplier": 0.9, "_AlphaToCoverage": 0, "_RenderMode": 1, "_BlendDst": 1}
BALL_DITHER = re.compile(r"m_ValidKeywords:[ \t]*(\r?\n)[ \t]*- _ALPHATEST_ON[ \t]*\r?\n")
BALL_QUEUE = re.compile(r"m_CustomRenderQueue: -?[0-9]+")
BALL_QUEUE_ADDED = "m_CustomRenderQueue: 3001"
INCLUDE = re.compile(r'(#include\s+")([^"]+)(")')


def new_guid(old):
    return hashlib.md5(("alps-vrsl:" + old).encode()).hexdigest()


def rel(path):
    return os.path.relpath(path, SOURCE).replace("\\", "/")


def read(path):
    with open(path, "rb") as handle:
        return handle.read().decode("utf-8")


def write(path, text):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "wb") as handle:
        handle.write(text.encode("utf-8"))


def is_cut(relative):
    if any(relative.startswith(prefix) for prefix in CUT):
        return True
    return relative.startswith("Runtime/Scripts/") and relative not in SCRIPTS


def resolve_include(including, include):
    """The VRSL file an #include names, or None for Unity's and other packages' files."""
    if include.startswith(SOURCE_PACKAGE):
        candidate = os.path.join(SOURCE, include[len(SOURCE_PACKAGE):])
    elif include.startswith("Packages/"):
        return None
    else:
        candidate = os.path.join(os.path.dirname(os.path.join(SOURCE, including)), include)
    candidate = os.path.normpath(candidate)
    return rel(candidate) if os.path.isfile(candidate) else None


def main():
    if not os.path.isfile(os.path.join(SOURCE, "package.json")):
        sys.exit(f"No VRSL package at {SOURCE}")

    by_guid = {}
    for folder, _, files in os.walk(SOURCE):
        for name in files:
            if name.endswith(".meta"):
                path = os.path.join(folder, name)
                match = re.search(r"^guid: ([0-9a-f]{32})", read(path), re.M)
                if match and os.path.isfile(path[:-5]):
                    by_guid[match.group(1)] = rel(path[:-5])
    guid_of = {path: guid for guid, path in by_guid.items()}

    queue = list(PREFABS) + list(MATERIALS)
    for folder in EXTRA_FOLDERS:
        for name in sorted(os.listdir(os.path.join(SOURCE, folder))):
            if not name.endswith(".meta"):
                queue.append(folder + "/" + name)

    taken = []
    seen = set()
    while queue:
        relative = queue.pop()
        if relative in seen:
            continue
        seen.add(relative)
        if relative not in guid_of:
            sys.exit(f"{relative} has no .meta")
        taken.append(relative)
        path = os.path.join(SOURCE, relative)
        referenced = set(GUID.findall(read(path + ".meta")))
        if relative.endswith(YAML):
            referenced.update(GUID.findall(read(path)))
        if relative.endswith(SHADER):
            for _, include, _ in INCLUDE.findall(read(path)):
                resolved = resolve_include(relative, include)
                if resolved and not is_cut(resolved):
                    queue.append(resolved)
        for guid in referenced:
            target = by_guid.get(guid)
            if target and not is_cut(target):
                queue.append(target)

    light_shaders = {relative for relative in taken
                     if relative.endswith(".shader") and PROJECTION_BLEND.search(read(os.path.join(SOURCE, relative)))}

    def scale_intensity(match):
        return match.group(1) + format(float(match.group(2)) * PROJECTION_INTENSITY_SCALE, ".7g")

    remap = {guid_of[relative]: new_guid(guid_of[relative]) for relative in taken}
    cutoffs = []
    close_lights = []
    laser_spins = []
    laser_scrolls = []
    balls = {name: [] for name in ("interpolator", "read", "rotation", "angle", "turn", "light")}
    cleared = set()

    def remap_references(text, relative):
        def reference(match):
            guid = match.group(1)
            if guid in remap:
                return match.group(0).replace(guid, remap[guid])
            if guid in by_guid:
                cleared.add((relative, by_guid[guid]))
                return "{fileID: 0}"
            return match.group(0)

        text = REFERENCE.sub(reference, text)
        return GUID.sub(lambda m: "guid: " + remap.get(m.group(1), m.group(1)), text)

    def convert_shader(text, relative):
        def include(match):
            resolved = resolve_include(relative, match.group(2))
            if resolved is None or not match.group(2).startswith(SOURCE_PACKAGE):
                return match.group(0)
            local = os.path.relpath(resolved, os.path.dirname(relative)).replace("\\", "/")
            return match.group(1) + local + match.group(3)

        text = INCLUDE.sub(include, text)
        text = PROJECTION_BLEND.sub("Blend One One", text)
        text, found = DARK_CUTOFF.subn(
            lambda match: match.group(1) + ", ".join([DARK_CUTOFF_COLOUR] * 3) + match.group(2), text)
        cutoffs.append(found)
        text, found = CLOSE_LIGHT.subn(CLOSE_LIGHT_SCALE, text)
        close_lights.append(found)
        text, found = LASER_SPIN.subn(r"\1", text)
        laser_spins.append(found)
        text, found = LASER_SCROLL.subn(LASER_SCROLL_PLACED, text)
        laser_scrolls.append(found)
        for name, pattern, replacement in (
                ("interpolator", BALL_INTERPOLATOR, BALL_INTERPOLATOR_ADDED), ("read", BALL_READ, BALL_READ_ROW),
                ("rotation", BALL_ROTATION, BALL_ROTATION_TURNED), ("angle", BALL_ANGLE, BALL_ANGLE_PLACED),
                ("turn", BALL_TURN, BALL_TURN_PLACED), ("light", BALL_LIGHT, BALL_LIGHT_ADDED)):
            text, found = pattern.subn(replacement, text)
            balls[name].append(found)
        text = re.sub(r'^(﻿?\s*Shader\s+")VRSL/', r"\1ALPS/VRSL/", text, count=1)
        # VRSL's material inspectors live in its editor assembly, which may not be there.
        return re.sub(r'^[ \t]*CustomEditor\s+"VRSL[^"]*"[ \t]*\r?\n', "", text, flags=re.M)

    def convert_script(text):
        # The script names editor assemblies it never uses, which the ALPS runtime assembly
        # does not reference.
        text = re.sub(r"^using (UdonSharpEditor|VRC\.Udon\.Common(\.Interfaces)?);[ \t]*\r?\n", "", text, flags=re.M)
        return text.replace("namespace VRSL", "namespace AdzukiSoft.ALPS.Vrsl", 1)

    def convert_program(text):
        # The serialized program belongs to VRSL's own script. UdonSharp makes this one's.
        return re.sub(r"serializedUdonProgramAsset: \{[^}]*\}", "serializedUdonProgramAsset: {fileID: 0}", text)

    if os.path.isdir(TARGET):
        shutil.rmtree(TARGET)

    def out_path(relative):
        return os.path.join(TARGET, relative[len("Runtime/"):])

    folders = set()
    faces = set(FACE) | set(SMOKE)
    statics = set(STATIC_13CH) | {BALL_MATERIAL}
    size = 0
    for relative in sorted(taken):
        source = os.path.join(SOURCE, relative)
        target = out_path(relative)
        os.makedirs(os.path.dirname(target), exist_ok=True)
        if relative.endswith(YAML + SHADER + (".cs",)):
            text = read(source)
            if relative.endswith(YAML):
                text = remap_references(text, relative)
            if relative.endswith(".mat"):
                text = text.replace("- _EnableExtraChannels: 0", "- _EnableExtraChannels: 1")
                if os.path.basename(relative) == BAR_MATERIAL:
                    if BAR_CHANNEL_MODE not in text:
                        sys.exit(f"{relative} has no channel mode to set")
                    text = text.replace(BAR_CHANNEL_MODE, "- _ChannelMode: 1")
                    text = KEYWORDS.sub(lambda match: match.group(1).rstrip() + " " + BAR_KEYWORD, text, count=1)
                if os.path.basename(relative) == MESH_MATERIAL:
                    for old, new in (MESH_CHANNEL_MODE, MESH_KEYWORD):
                        if text.count(old) != 1:
                            sys.exit(f"{relative} has no {old} to replace")
                        text = text.replace(old, new)
                mode = STATIC_13CH.get(os.path.basename(relative))
                if mode:
                    text, found = STATIC_13CH_MODE.subn(r"\g<1>3", text)
                    text, keywords = re.subn(r"^([ \t]*- )" + mode + r"([ \t]*\r?\n)", r"\g<1>_13CH_MODE\2", text, flags=re.M)
                    if found != 1 or keywords != 1:
                        sys.exit(f"{relative} has {found} channel modes and {keywords} {mode} keywords to set, not one of each")
                    statics.discard(os.path.basename(relative))
                values = dict(FACE.get(os.path.basename(relative), {}), **SMOKE.get(os.path.basename(relative), {}))
                if os.path.basename(relative) == BALL_MATERIAL:
                    values.update(BALL_VALUES)
                    text, dithers = BALL_DITHER.subn(r"m_ValidKeywords: []\1", text)
                    text, queues = BALL_QUEUE.subn(BALL_QUEUE_ADDED, text)
                    if dithers != 1 or queues != 1:
                        sys.exit(f"{relative} has {dithers} dither keywords and {queues} queues to set, not one of each")
                    statics.discard(BALL_MATERIAL)
                for name, value in values.items():
                    text, found = re.subn(r"(- " + name + r": )[0-9.eE+-]+", lambda match: match.group(1) + format(value, ".7g"), text)
                    if found != 1:
                        sys.exit(f"{relative} has no {name} to set")
                    faces.discard(os.path.basename(relative))
                shader = re.search(r"m_Shader: \{fileID: \d+, guid: ([0-9a-f]{32})", read(source))
                if shader and by_guid.get(shader.group(1)) in light_shaders:
                    text = PROJECTION_INTENSITY.sub(scale_intensity, text)
            if relative.endswith(SHADER):
                text = convert_shader(text, relative)
            if relative.endswith(".cs"):
                text = convert_script(text)
            if relative in SCRIPTS and relative.endswith(".asset"):
                text = convert_program(text)
            write(target, text)
        else:
            shutil.copyfile(source, target)
        write(target + ".meta", remap_references(read(source + ".meta"), relative + ".meta"))
        size += os.path.getsize(target)

        folder = os.path.dirname(relative)
        while folder and folder != "Runtime":
            folders.add(folder)
            folder = os.path.dirname(folder)

    for folder in sorted(folders) + [None]:
        guid = hashlib.md5(("alps-vrsl-folder:" + (folder or "")).encode()).hexdigest()
        write((out_path(folder) if folder else TARGET) + ".meta",
              f"fileFormatVersion: 2\nguid: {guid}\nfolderAsset: yes\nDefaultImporter:\n"
              "  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n")

    if faces or statics:
        sys.exit("No material named " + ", ".join(sorted(faces | statics)))
    if sum(cutoffs) != DARK_CUTOFFS:
        sys.exit(f"Found {sum(cutoffs)} dark cut offs in the shaders, not {DARK_CUTOFFS}")
    if sum(close_lights) != 1:
        sys.exit(f"Found {sum(close_lights)} static projections to keep off their own face, not 1")
    if sum(laser_spins) != LASER_PASSES or sum(laser_scrolls) != LASER_PASSES:
        sys.exit(f"Found {sum(laser_spins)} laser spin phases and {sum(laser_scrolls)} scrolls to place, not {LASER_PASSES} of each")
    for name, found in balls.items():
        if sum(found) != BALL_PASSES:
            sys.exit(f"Found {sum(found)} places for the mirror ball's {name} in the shaders, not {BALL_PASSES}")

    version = re.search(r'"version":\s*"([^"]+)"', read(os.path.join(SOURCE, "package.json"))).group(1)
    license_text = read(os.path.join(SOURCE, "LICENSE"))
    notice = (
        "# VR Stage Lighting\n\n"
        f"The fixtures in this folder are copied from VR Stage Lighting {version} "
        "(https://github.com/AcChosen/VR-Stage-Lighting) by `scripts/vendor-vrsl.py`, which lists "
        "what it changes. Do not edit them by hand.\n\n" + license_text)
    write(os.path.join(TARGET, "LICENSE.md"), notice)
    write(os.path.join(TARGET, "LICENSE.md.meta"),
          f"fileFormatVersion: 2\nguid: {hashlib.md5(b'alps-vrsl-license').hexdigest()}\nTextScriptImporter:\n"
          "  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n")

    print(f"Copied {len(taken)} files, {size / 1e6:.1f} MB, from VRSL {version} into {TARGET}")
    for relative in PREFABS + MATERIALS:
        print(f"  {new_guid(guid_of[relative])}  {os.path.basename(relative)}")
    for source, target in sorted(cleared):
        print(f"  cleared in {source}: {target}")


if __name__ == "__main__":
    main()
