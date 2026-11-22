#!/usr/bin/env python3
"""
Copies the part of VR Stage Lighting that ALPS fixtures are made of into Runtime/VRSL, so
ALPS does not depend on the VRSL package a world has installed.

    python scripts/vendor-vrsl.py [path to com.acchosen.vr-stage-lighting]

It takes the DMX prefabs of the create menu and everything they reference (meshes,
materials, textures, shaders and their includes, and the DMX Static script), and changes
only what keeps the copy apart from a VRSL package in the same project:

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
- the lit faces shine about as much on every fixture, and far less than VRSL makes them,
- a cone and a projection are drawn down to a far darker colour, since ALPS dims a fixture
  by its colour.

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
]
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
# which the flare follows at 0.
FACE = {
    "VRSL-LightBar-FixtureMat-Horizontal.mat": {"_FixtureMaxIntensity": 0.0413},
    "VRSL-LightBar-FixtureMat-Horizontal-15CH.mat": {"_FixtureMaxIntensity": 4},
    "VRSL-ParLight-FixtureMat-Horizontal.mat": {"_FixtureMaxIntensity": 0.0307},
    "VRSL-Blinder-FixtureMat-Horizontal.mat": {"_FixtureMaxIntensity": 0.0413},
    "VRSL-Blinder-LensFlareMat-Horizontal.mat": {"_FixtureMaxIntensity": 3, "_CurveMod": 0},
    "VRSL-WashMover-HQ-FixtureMat-Horizontal.mat": {"_FixtureMaxIntensity": 0.0179},
    "VRSL-StandardMover-VolumetricMat-HQ-Horizontal.mat": {"_LensMaxBrightness": 0.0128},
}
# VRSL stops drawing a cone or a projection whose colour is under a hundredth or so, which
# saves the work on a dark fixture. ALPS dims a fixture by its colour, along the curve the
# eye reads, so that is a brightness of 10 to 17%, where the light would drop out of a fade.
# At this colour it is under 2%.
DARK_CUTOFF = re.compile(r"(all\(\s*[io]\.rgbColor\s*<=\s*(?:half4|float4)\()[0-9.]+\s*,\s*[0-9.]+\s*,\s*[0-9.]+(\s*,\s*1\s*\))")
DARK_CUTOFF_COLOUR = "0.0001"
DARK_CUTOFFS = 9
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

    queue = list(PREFABS)
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
        text = re.sub(r'^(﻿?\s*Shader\s+")VRSL/', r"\1ALPS/VRSL/", text, count=1)
        # VRSL's material inspector lives in its editor assembly, which may not be there.
        return re.sub(r'^[ \t]*CustomEditor\s+"VRSLInspector"[ \t]*\r?\n', "", text, flags=re.M)

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
    faces = set(FACE)
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
                for name, value in FACE.get(os.path.basename(relative), {}).items():
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

    if faces:
        sys.exit("No material named " + ", ".join(sorted(faces)))
    if sum(cutoffs) != DARK_CUTOFFS:
        sys.exit(f"Found {sum(cutoffs)} dark cut offs in the shaders, not {DARK_CUTOFFS}")

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
    for relative in PREFABS:
        print(f"  {new_guid(guid_of[relative])}  {os.path.basename(relative)}")
    for source, target in sorted(cleared):
        print(f"  cleared in {source}: {target}")


if __name__ == "__main__":
    main()
