using System.Collections.Generic;
using AdzukiSoft.ALPS;
using AdzukiSoft.ALPS.Editor;
using AdzukiSoft.ALPS.Vrsl;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Hangs the demo's lighting rig on the venue: 113 fixtures, 43 meshes that glow as
/// fixtures and twenty multi light bars of twelve segments each, in nineteen containers
/// below one container for the whole rig, then a mirror ball over the hall and two penlight
/// areas over the crowd. They take 237 of the DMX grid's 348 rows.
///
/// How the heads are mounted decides what tilt and pan mean in a clip. Pan turns a head
/// around the axis it is mounted on, and tilt leans the beam away from that axis:
/// - Hung movers (front wash, mid and back spots, side wash): tilt 0 is straight down, a
///   positive tilt leans at the audience and a negative one upstage.
/// - The halo's movers face out of the ring: tilt 0 is straight down, a positive tilt leans
///   outward and a negative one into the ring.
/// - Standing movers (floor and totem spots): tilt 0 is straight up, a positive tilt leans
///   at the audience and a negative one upstage.
/// - The lasers fire level at the back of the hall at tilt 0. A positive tilt raises the
///   fan and a negative one lowers it onto the crowd, and pan rolls the fan around the way
///   it fires, clockwise seen from behind the laser, so 90 stands it on end. Its cone width
///   is half the angle the fan opens by.
///
/// The pixel bars are multi light bars, whose segments are the fixtures: a clip counts 48 of
/// them from house left to house right, so a spread or a delay runs across the riser. The
/// roof pixels are sixteen more along the front truss, 192 segments counted the same way.
///
/// What a clip counts on the groups that are no row from left to right:
/// - The strobe panels go up the house left tower and down the house right one, so a
///   symmetric clip starts at the top of both.
/// - The stage's edge runs along the deck's lip from house left, round the thrust and on to
///   house right, so a symmetric clip starts at the tip of the thrust.
/// - The ring's arcs go round from upstage, clockwise seen from above.
///
/// The mirror ball hangs beside the rig, not below it, so what lights the whole rig leaves
/// it dark, and its track binds the ball itself.
/// </summary>
public static class AlpsDemoRig
{
    public const string All = "Rig";
    public const string FrontWash = "Front Wash";
    public const string Blinders = "Blinders";
    public const string MidSpots = "Mid Spots";
    public const string BackSpots = "Back Spots";
    public const string SideWash = "Side Wash";
    public const string Halo = "Halo";
    public const string FloorSpots = "Floor Spots";
    public const string TotemSpots = "Totem Spots";
    public const string TotemBars = "Totem Bars";
    public const string CycPars = "Cyc Pars";
    public const string ApronBars = "Apron Bars";
    public const string Lasers = "Lasers";
    public const string PixelBars = "Pixel Bars";
    public const string RoofPixels = "Roof Pixels";
    public const string Flashers = "Flashers";
    public const string StrobePanels = "Strobe Panels";
    public const string StageEdge = "Stage Edge";
    public const string RingGlow = "Ring Glow";
    public const string Sign = "Sign";
    public const string MirrorBall = "Mirror Ball";
    public const string PenlightSweep = "Penlight Sweep";
    public const string PenlightRipple = "Penlight Ripple";

    /// <summary>The containers in the order their tracks come in the timeline, lowest layer first.</summary>
    public static readonly string[] Containers =
    {
        CycPars, Sign, StageEdge, ApronBars, PixelBars, TotemBars, RingGlow, RoofPixels, FrontWash, SideWash, Halo, BackSpots, MidSpots,
        FloorSpots, TotemSpots, Lasers, Flashers, StrobePanels, Blinders,
    };

    /// <summary>
    /// The laser material's beams, in the measure of a laser effect: a clip that shapes the
    /// beams sets all three, so one that only sends them round asks for these again.
    /// </summary>
    public const float LaserFanFlatness = 100f;
    public const float LaserFanBeams = 16f;
    public const float LaserFanThickness = 8f;

    private const string LaserMaterialPath = AlpsDemoVenue.Folder + "/Materials/Laser Fan.mat";
    private const string MirrorBallMaterialPath = AlpsDemoVenue.Folder + "/Materials/Mirror Ball.mat";
    private const string StaticProjectionShader = "/Standard Static/Projection";
    private const string MirrorBallShader = "/Other/Discoball";

    /// <summary>Where a light bar's body sits from its origin, which is on one of its long edges.</summary>
    private static readonly Vector3 LightBarCenter = new Vector3(0f, 0.01f, 0.13f);

    /// <summary>The same for a flasher, which is a short light bar, and the size the demo hangs it at, its prefab's own.</summary>
    private static readonly Vector3 FlasherCenter = new Vector3(0f, 0.01f, 0.08f);
    private const float FlasherScale = 1.25f;

    /// <summary>A strobe panel is 37 cm wide, which a hall this size loses, so the demo's are twice that.</summary>
    private const float StrobePanelScale = 2f;

    // A VRSL mover's pan axis is its own +Z: these point it down from a truss and up from the deck,
    // with a positive tilt leaning the beam at the audience either way.
    private static readonly Quaternion Hung = Quaternion.Euler(90f, 180f, 0f);
    private static readonly Quaternion Standing = Quaternion.Euler(-90f, 0f, 0f);

    public static Dictionary<string, AlpsTarget> Build(Transform parent)
    {
        var targets = new Dictionary<string, AlpsTarget>();
        var rig = new GameObject(All).AddComponent<AlpsContainer>();
        rig.transform.SetParent(parent, false);
        // One switch per scene, which the inspector sets on every container, so here too.
        rig.keepLightOffPlayers = true;
        targets[All] = rig;

        // --- Front truss: wash movers laid out by the container, blinders between them ---------
        var frontWash = Container(rig, FrontWash, targets);
        frontWash.transform.localPosition = new Vector3(0f, AlpsDemoVenue.Below(AlpsDemoVenue.RoofY), AlpsDemoVenue.FrontTrussZ);
        for (var i = 0; i < 8; i++)
        {
            Fixture(AlpsCreateMenu.MoverWashLight, frontWash, FrontWash, i);
        }

        frontWash.settings.shape = AlpsArrangementShape.Line;
        frontWash.settings.lineStart = new Vector3(-7f, 0f, 0f);
        frontWash.settings.lineEnd = new Vector3(7f, 0f, 0f);
        frontWash.settings.rotationX.value = 90f;
        frontWash.settings.rotationY.value = 180f;
        Arrange(frontWash);

        var blinders = Container(rig, Blinders, targets);
        var towardCrowd = new Vector3(0f, -0.32f, -1f).normalized;
        var blinderX = new[] { -6f, -4f, -2f, 2f, 4f, 6f };
        for (var i = 0; i < blinderX.Length; i++)
        {
            var blinder = Fixture(AlpsCreateMenu.Blinder, blinders, "Blinder", i);
            blinder.transform.localRotation = Aim(towardCrowd, Vector3.right);
            blinder.transform.localPosition = new Vector3(blinderX[i], AlpsDemoVenue.Below(AlpsDemoVenue.RoofY) - 0.12f, AlpsDemoVenue.FrontTrussZ - 0.1f);
        }

        // --- Mid and back truss: spot movers ----------------------------------------------------
        Row(rig, MidSpots, "Mid Spot", AlpsCreateMenu.MoverSpotlight, 10, 8.1f, AlpsDemoVenue.Below(AlpsDemoVenue.MidTrussY), AlpsDemoVenue.MidTrussZ, Hung, targets);
        Row(rig, BackSpots, "Back Spot", AlpsCreateMenu.MoverSpotlight, 10, 8.1f, AlpsDemoVenue.Below(AlpsDemoVenue.RoofY), AlpsDemoVenue.BackTrussZ, Hung, targets);

        // --- Side trusses: wash movers, listed back to front then front to back, so the two
        // sides mirror each other when a clip is symmetric ---------------------------------------
        var sideWash = Container(rig, SideWash, targets);
        var sideZ = new[] { 7f, 4.75f, 2.5f, 2.5f, 4.75f, 7f };
        for (var i = 0; i < sideZ.Length; i++)
        {
            var wash = Fixture(AlpsCreateMenu.MoverWashLight, sideWash, "Side Wash", i);
            wash.transform.localRotation = Hung;
            wash.transform.localPosition = new Vector3(i < 3 ? -AlpsDemoVenue.TowerX : AlpsDemoVenue.TowerX, AlpsDemoVenue.Below(AlpsDemoVenue.RoofY), sideZ[i]);
        }

        // --- The ring: wash movers on a circle, each facing out of it ---------------------------
        var halo = Container(rig, Halo, targets);
        halo.transform.localPosition = new Vector3(0f, AlpsDemoVenue.Below(AlpsDemoVenue.RingY), AlpsDemoVenue.MidTrussZ);
        for (var i = 0; i < 12; i++)
        {
            Fixture(AlpsCreateMenu.MoverWashLight, halo, Halo, i);
        }

        halo.settings.shape = AlpsArrangementShape.Circle;
        halo.settings.radius.value = AlpsDemoVenue.RingRadius;
        halo.settings.facing = AlpsArrangementFacing.Outward;
        halo.settings.rotationX.value = 90f;
        Arrange(halo);

        // --- The deck: spots on an arc behind the band, and one on top of every totem -----------
        var floor = Container(rig, FloorSpots, targets);
        floor.transform.localPosition = new Vector3(0f, AlpsDemoVenue.DeckHeight, 2f);
        for (var i = 0; i < 8; i++)
        {
            Fixture(AlpsCreateMenu.MoverSpotlight, floor, "Floor Spot", i);
        }

        floor.settings.shape = AlpsArrangementShape.Circle;
        floor.settings.radius.value = 7f;
        floor.settings.sweep.value = 120f;
        floor.settings.rotationX.value = -90f;
        Arrange(floor);

        var totemSpots = Container(rig, TotemSpots, targets);
        var totemBars = Container(rig, TotemBars, targets);
        for (var i = 0; i < AlpsDemoVenue.TotemX.Length; i++)
        {
            var top = AlpsDemoVenue.DeckHeight + AlpsDemoVenue.TotemHeight[i];
            var spot = Fixture(AlpsCreateMenu.MoverSpotlight, totemSpots, "Totem Spot", i);
            spot.transform.localRotation = Standing;
            spot.transform.localPosition = new Vector3(AlpsDemoVenue.TotemX[i], top + 0.03f, AlpsDemoVenue.TotemZ);

            // The bar stands up the front of the totem, facing the audience.
            var bar = Fixture(AlpsCreateMenu.LightBar, totemBars, "Totem Bar", i);
            var rotation = Aim(Vector3.back, Vector3.up);
            bar.transform.localRotation = rotation;
            bar.transform.localPosition = new Vector3(AlpsDemoVenue.TotemX[i], top - 0.85f, AlpsDemoVenue.TotemZ - 0.28f) - rotation * LightBarCenter;
        }

        // --- Pars along the back of the deck, grazing up the cyclorama --------------------------
        var cyc = Container(rig, CycPars, targets);
        cyc.transform.localPosition = new Vector3(0f, AlpsDemoVenue.DeckHeight + 0.12f, AlpsDemoVenue.DeckDepth - 0.15f);
        for (var i = 0; i < 10; i++)
        {
            Fixture(AlpsCreateMenu.ParLight, cyc, "Cyc Par", i);
        }

        cyc.settings.shape = AlpsArrangementShape.Line;
        cyc.settings.lineStart = new Vector3(-8.1f, 0f, 0f);
        cyc.settings.lineEnd = new Vector3(8.1f, 0f, 0f);
        // A par looks along its own -Y, so this tips it up and a little back at the cloth.
        cyc.settings.rotationX.value = -168f;
        Arrange(cyc);

        // --- Light bars along the lip of the deck, either side of the thrust --------------------
        var apron = Container(rig, ApronBars, targets);
        var apronX = new[] { -7.07f, -5.53f, -3.99f, -2.45f, 2.45f, 3.99f, 5.53f, 7.07f };
        for (var i = 0; i < apronX.Length; i++)
        {
            var bar = Fixture(AlpsCreateMenu.LightBar, apron, "Apron Bar", i);
            var rotation = Aim(Vector3.back, Vector3.right);
            bar.transform.localRotation = rotation;
            bar.transform.localPosition = new Vector3(apronX[i], AlpsDemoVenue.DeckHeight - 0.3f, -0.08f) - rotation * LightBarCenter;
        }

        // --- Multi light bars across the front of the drum riser, twelve pixels each ------------
        var pixels = Container(rig, PixelBars, targets);
        var riserY = (AlpsDemoVenue.DrumRiserMin.y + AlpsDemoVenue.DrumRiserMax.y) * 0.5f;
        for (var i = 0; i < 4; i++)
        {
            // A multi light bar shines along its own +Z and counts its segments from its +X end,
            // so turned half round it faces the audience and counts from house left.
            var bar = MultiLightBar(pixels, "Pixel Bar", i);
            bar.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            bar.transform.localPosition = new Vector3((i - 1.5f) * 1.05f, riserY, AlpsDemoVenue.DrumRiserMin.z - 0.14f);
        }

        // --- Sixteen more along the audience side of the front truss, tipped down at the crowd ---
        var roof = Container(rig, RoofPixels, targets);
        for (var i = 0; i < 16; i++)
        {
            var bar = MultiLightBar(roof, "Roof Pixel Bar", i);
            bar.transform.localRotation = Quaternion.Euler(15f, 180f, 0f);
            bar.transform.localPosition = new Vector3((i - 7.5f) * 1.05f, AlpsDemoVenue.RoofY, AlpsDemoVenue.FrontTrussZ - AlpsDemoVenue.TrussWidth * 0.5f - 0.14f);
        }

        // --- Flashers on the audience side of the mid truss, between its spots -------------------
        var flashers = Container(rig, Flashers, targets);
        var atCrowd = new Vector3(0f, -0.35f, -1f).normalized;
        for (var i = 0; i < 9; i++)
        {
            var flasher = Fixture(AlpsCreateMenu.Flasher, flashers, "Flasher", i, FlasherScale);
            var rotation = Aim(atCrowd, Vector3.right);
            flasher.transform.localRotation = rotation;
            flasher.transform.localPosition = new Vector3((i - 4f) * 1.8f, AlpsDemoVenue.MidTrussY, AlpsDemoVenue.MidTrussZ - AlpsDemoVenue.TrussWidth * 0.5f - 0.12f)
                                              - rotation * (FlasherCenter * FlasherScale);
        }

        // --- Strobe panels up the two downstage towers, turned in a little at the crowd ----------
        var strobes = Container(rig, StrobePanels, targets);
        var heights = AlpsDemoVenue.StrobeY;
        for (var i = 0; i < heights.Length * 2; i++)
        {
            // Up the house left tower, then down the house right one.
            var left = i < heights.Length;
            var y = heights[left ? i : heights.Length * 2 - 1 - i];
            var panel = Fixture(AlpsCreateMenu.StrobePanel, strobes, "Strobe Panel", i, StrobePanelScale);
            panel.transform.localRotation = Aim(new Vector3(left ? 0.3f : -0.3f, -0.12f, -1f), Vector3.right);
            panel.transform.localPosition = new Vector3(left ? -AlpsDemoVenue.TowerX : AlpsDemoVenue.TowerX, y, AlpsDemoVenue.FrontTrussZ - AlpsDemoVenue.TrussWidth * 0.5f - 0.3f);
        }

        // --- Meshes of the venue that glow: every one is a fixture of its own --------------------
        Edge(Container(rig, StageEdge, targets));

        var ring = Container(rig, RingGlow, targets);
        ring.transform.localPosition = new Vector3(0f, AlpsDemoVenue.RingY, AlpsDemoVenue.MidTrussZ);
        var arc = AlpsDemoVenue.RingArc();
        for (var i = 0; i < AlpsDemoVenue.RingArcs; i++)
        {
            Glow(ring, "Ring Arc", i, arc, Vector3.zero, Quaternion.Euler(0f, i * 360f / AlpsDemoVenue.RingArcs, 0f), 1f);
        }

        var sign = Container(rig, Sign, targets);
        sign.transform.localPosition = AlpsDemoVenue.SignCenter;
        var text = AlpsDemoVenue.SignText;
        for (var i = 0; i < text.Length; i++)
        {
            var letter = Glow(sign, "Letter", i, AlpsDemoVenue.SignLetter(text[i]), new Vector3((i - (text.Length - 1) * 0.5f) * AlpsDemoVenue.SignSpacing, 0f, 0f), Quaternion.identity, 1f);
            letter.name = $"Letter {text[i]}";
        }

        // --- Lasers on the back truss, turned round to fire over the crowd -----------------------
        var lasers = Container(rig, Lasers, targets);
        var fan = LaserFan();
        for (var i = 0; i < AlpsDemoVenue.LaserX.Length; i++)
        {
            var laser = Fixture(AlpsCreateMenu.Laser, lasers, "Laser", i);
            // The prefab's own rotation fires along +Z with a positive tilt raising the fan.
            laser.transform.localRotation = Quaternion.Euler(0f, 180f, 0f) * AlpsCreateMenu.Laser.Load().transform.localRotation;
            laser.transform.localPosition = new Vector3(AlpsDemoVenue.LaserX[i], AlpsDemoVenue.LaserY, AlpsDemoVenue.LaserZ - AlpsDemoVenue.LaserDepth * 0.5f - 0.01f);
            foreach (var renderer in laser.GetComponentInChildren<VRStageLighting_DMX_Static>(true).objRenderers)
            {
                renderer.sharedMaterial = fan;
            }
        }

        // --- The mirror ball over the hall, beside the rig -----------------------------------------
        var ball = Fixture(AlpsCreateMenu.MirrorBall, parent, MirrorBall, AlpsDemoVenue.MirrorBallScale);
        ball.transform.localPosition = AlpsDemoVenue.MirrorBall;
        targets[MirrorBall] = ball.GetComponent<AlpsVRSLFixture>();

        // --- Penlights: one area across the crowd, one spreading out from the thrust ------------
        var crowdMiddle = (AlpsDemoVenue.CrowdFront + AlpsDemoVenue.CrowdBack) * 0.5f;
        targets[PenlightSweep] = Penlight(parent, PenlightSweep, new Vector3(0f, 1.9f, crowdMiddle), AlpsPenlightShape.Line, AlpsDemoVenue.CrowdHalfWidth * 2f + 1f, 24);
        targets[PenlightRipple] = Penlight(parent, PenlightRipple, new Vector3(0f, 1.9f, -AlpsDemoVenue.ThrustLength), AlpsPenlightShape.Radial, 18f, 16);
        return targets;
    }

    private static void Row(AlpsContainer rig, string name, string fixtureName, AlpsVrslPrefab prefab, int count, float halfWidth, float y, float z, Quaternion rotation, Dictionary<string, AlpsTarget> targets)
    {
        var container = Container(rig, name, targets);
        for (var i = 0; i < count; i++)
        {
            var fixture = Fixture(prefab, container, fixtureName, i);
            fixture.transform.localRotation = rotation;
            fixture.transform.localPosition = new Vector3(Mathf.Lerp(-halfWidth, halfWidth, i / (count - 1f)), y, z);
        }
    }

    private static AlpsContainer Container(AlpsContainer rig, string name, Dictionary<string, AlpsTarget> targets)
    {
        var container = new GameObject(name).AddComponent<AlpsContainer>();
        container.transform.SetParent(rig.transform, false);
        container.keepLightOffPlayers = true;
        targets[name] = container;
        return container;
    }

    /// <summary>
    /// The strips along the stage's edge, a fixture each: the deck's lip from house left, down
    /// the thrust's side, across its tip, back up the other side and on to house right.
    /// </summary>
    private static void Edge(AlpsContainer container)
    {
        var strip = AlpsDemoVenue.EdgeStrip();
        var deck = AlpsDemoVenue.DeckHalfWidth;
        var thrust = AlpsDemoVenue.ThrustHalfWidth;
        var tip = AlpsDemoVenue.ThrustLength;
        var y = AlpsDemoVenue.EdgeStripY;
        var corners = new[]
        {
            new Vector3(-deck, y, 0f), new Vector3(-thrust, y, 0f), new Vector3(-thrust, y, -tip),
            new Vector3(thrust, y, -tip), new Vector3(thrust, y, 0f), new Vector3(deck, y, 0f),
        };

        var index = 0;
        for (var run = 0; run < corners.Length - 1; run++)
        {
            var along = corners[run + 1] - corners[run];
            var count = Mathf.Max(1, Mathf.FloorToInt(along.magnitude + 0.01f));
            var step = along / count;
            // The stage is to the left of the way the run goes, seen from above.
            var outward = Vector3.Cross(Vector3.up, along.normalized);
            var rotation = Quaternion.LookRotation(outward, Vector3.up);
            for (var i = 0; i < count; i++)
            {
                // A run that is no whole number of metres stretches its strips to fill it.
                Glow(container, "Edge Strip", index++, strip, corners[run] + step * (i + 0.5f) + outward * AlpsDemoVenue.EdgeStripOut, rotation, step.magnitude);
            }
        }
    }

    /// <summary>
    /// A mesh of the venue made a fixture the way the Hierarchy's Make Selection Emissive
    /// makes one: it takes the one material ALPS lights a mesh with, and a row of its own.
    /// </summary>
    private static GameObject Glow(AlpsContainer container, string name, int index, Mesh mesh, Vector3 position, Quaternion rotation, float length)
    {
        var prop = AlpsDemoVenue.Prop(container.transform, $"{name} {index + 1}", mesh);
        prop.transform.localPosition = position;
        prop.transform.localRotation = rotation;
        prop.transform.localScale = new Vector3(length, 1f, 1f);
        if (AlpsEmissiveMesh.Convert(prop) == null)
        {
            throw new System.InvalidOperationException($"{prop.name} could not be made an emissive mesh.");
        }

        return prop;
    }

    private static GameObject Fixture(AlpsVrslPrefab prefab, AlpsContainer container, string name, int index, float scale = 1f)
    {
        return Fixture(prefab, container.transform, $"{name} {index + 1}", scale);
    }

    private static GameObject Fixture(AlpsVrslPrefab prefab, Transform parent, string name, float scale)
    {
        var source = prefab.Load();
        if (source == null)
        {
            throw new System.InvalidOperationException($"The prefab for {prefab.Name} was not found at {prefab.Path}.");
        }

        var fixture = (GameObject)PrefabUtility.InstantiatePrefab(source, parent);
        fixture.name = name;
        fixture.transform.localPosition = Vector3.zero;
        fixture.transform.localRotation = Quaternion.identity;
        // The rig is measured out for fixtures at this scale, whatever their prefabs come with.
        fixture.transform.localScale = Vector3.one * scale;
        var vrsl = fixture.GetComponentInChildren<VRStageLighting_DMX_Static>(true);
        fixture.AddComponent<AlpsVRSLFixture>().target = vrsl;
        foreach (var renderer in vrsl.objRenderers)
        {
            var material = renderer != null ? renderer.sharedMaterial : null;
            if (material != null && material.shader.name.EndsWith(StaticProjectionShader))
            {
                renderer.sharedMaterial = ScaledProjection(material);
            }
            else if (material != null && material.shader.name.EndsWith(MirrorBallShader))
            {
                renderer.sharedMaterial = ScaledMirrorBall(material);
            }
        }

        return fixture;
    }

    /// <summary>
    /// The material a mirror ball throws its light with, for a venue at the demo's scale.
    /// That light falls off with the square of the distance in the world's measure, so in a
    /// scaled down venue it lands brighter. This copy takes that back.
    /// </summary>
    private static Material ScaledMirrorBall(Material source)
    {
        var material = AlpsDemoVenue.Asset(MirrorBallMaterialPath, () => new Material(source));
        material.shader = source.shader;
        material.CopyPropertiesFromMaterial(source);
        material.SetFloat("_Multiplier", source.GetFloat("_Multiplier") * AlpsDemoVenue.Scale * AlpsDemoVenue.Scale);
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>
    /// The material a static light throws its light with, for a venue at the demo's scale.
    /// VRSL dims that light and lets it reach by the distance in the world's measure, so in a
    /// scaled down venue a par lights the cloth above it brighter and further up. This copy
    /// counts the distance in the venue's measure, and the light lands as it does at full size.
    /// </summary>
    private static Material ScaledProjection(Material source)
    {
        var material = AlpsDemoVenue.Asset($"{AlpsDemoVenue.Folder}/Materials/{source.name} Scaled.mat", () => new Material(source));
        material.shader = source.shader;
        material.CopyPropertiesFromMaterial(source);
        material.SetFloat("_ProjectionUVMod", source.GetFloat("_ProjectionUVMod") / AlpsDemoVenue.Scale);
        material.SetFloat("_Fade", source.GetFloat("_Fade") * AlpsDemoVenue.Scale);
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>A multi light bar at its prefab's own size, with the segments a show drives.</summary>
    private static GameObject MultiLightBar(AlpsContainer container, string name, int index)
    {
        var source = AlpsCreateMenu.MultiLightBar.Load();
        if (source == null)
        {
            throw new System.InvalidOperationException($"The prefab for {AlpsCreateMenu.MultiLightBar.Name} was not found at {AlpsCreateMenu.MultiLightBar.Path}.");
        }

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(source, container.transform);
        instance.name = $"{name} {index + 1}";
        var bar = instance.AddComponent<AlpsVRSLMultiLightBar>();
        bar.target = instance.GetComponentInChildren<VRStageLighting_DMX_Static>(true);
        AlpsMultiLightBarSegments.Sync(bar, recordUndo: false);
        return instance;
    }

    /// <summary>
    /// The material of the demo's lasers. A laser no laser effect drives takes how flat its
    /// cone is, how many beams it has and how thick they are from its material: this one
    /// flattens VRSL's cone into a fan of sixteen thin beams, which the show's laser effects
    /// then bend, thin out and send round.
    /// </summary>
    private static Material LaserFan()
    {
        var source = AlpsCreateMenu.Laser.Load().GetComponentInChildren<MeshRenderer>(true).sharedMaterial;
        var material = AlpsDemoVenue.Asset(LaserMaterialPath, () => new Material(source));
        material.shader = source.shader;
        material.CopyPropertiesFromMaterial(source);
        material.SetFloat(AlpsShowPlayer.LaserFlatnessProperty, AlpsShowPlayer.LaserMaxFlatness * LaserFanFlatness / 100f);
        material.SetFloat(AlpsShowPlayer.LaserBeamsProperty, LaserFanBeams);
        material.SetFloat(AlpsShowPlayer.LaserThicknessProperty, AlpsShowPlayer.LaserMaxThickness * LaserFanThickness / 100f);
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>Lays the container's children out the way its inspector does once a shape is picked.</summary>
    private static void Arrange(AlpsContainer container)
    {
        container.Initialized = true;
        AlpsArrangementLayout.Apply(container, recordUndo: false);
        EditorUtility.SetDirty(container);
    }

    /// <summary>
    /// The rotation of a static fixture, which shines along its own -Y, that looks along
    /// <paramref name="direction"/> with its long side along <paramref name="along"/>.
    /// </summary>
    private static Quaternion Aim(Vector3 direction, Vector3 along)
    {
        var up = -direction.normalized;
        var forward = Vector3.Cross(along, up).normalized;
        return Quaternion.LookRotation(forward, up);
    }

    private static AlpsPenlightArea Penlight(Transform parent, string name, Vector3 position, AlpsPenlightShape shape, float size, int slots)
    {
        var area = new GameObject(name).AddComponent<AlpsPenlightArea>();
        area.transform.SetParent(parent, false);
        area.transform.localPosition = position;
        area.shape = shape;
        area.size = size;
        area.slots = slots;
        AlpsPenlightSlots.Sync(area, recordUndo: false);
        return area;
    }
}
