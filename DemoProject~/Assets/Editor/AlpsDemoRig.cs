using System.Collections.Generic;
using AdzukiSoft.ALPS;
using AdzukiSoft.ALPS.Editor;
using AdzukiSoft.ALPS.Vrsl;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Hangs the demo's lighting rig on the venue: 96 fixtures and four multi light bars of
/// twelve segments each in thirteen containers below one container for the whole rig, and
/// two penlight areas over the crowd.
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
/// them from house left to house right, so a spread or a delay runs across the riser.
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
    public const string PenlightSweep = "Penlight Sweep";
    public const string PenlightRipple = "Penlight Ripple";

    /// <summary>The containers in the order their tracks come in the timeline, lowest layer first.</summary>
    public static readonly string[] Containers =
    {
        CycPars, ApronBars, PixelBars, TotemBars, FrontWash, SideWash, Halo, BackSpots, MidSpots, FloorSpots, TotemSpots, Lasers, Blinders,
    };

    private const string LaserMaterialPath = AlpsDemoVenue.Folder + "/Materials/Laser Fan.mat";
    private const string StaticProjectionShader = "/Standard Static/Projection";

    /// <summary>Where a light bar's body sits from its origin, which is on one of its long edges.</summary>
    private static readonly Vector3 LightBarCenter = new Vector3(0f, 0.01f, 0.13f);

    // A VRSL mover's pan axis is its own +Z: these point it down from a truss and up from the deck,
    // with a positive tilt leaning the beam at the audience either way.
    private static readonly Quaternion Hung = Quaternion.Euler(90f, 180f, 0f);
    private static readonly Quaternion Standing = Quaternion.Euler(-90f, 0f, 0f);

    public static Dictionary<string, AlpsTarget> Build(Transform parent)
    {
        var targets = new Dictionary<string, AlpsTarget>();
        var rig = new GameObject(All).AddComponent<AlpsContainer>();
        rig.transform.SetParent(parent, false);
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
        targets[name] = container;
        return container;
    }

    private static GameObject Fixture(AlpsVrslPrefab prefab, AlpsContainer container, string name, int index)
    {
        var source = prefab.Load();
        if (source == null)
        {
            throw new System.InvalidOperationException($"The prefab for {prefab.Name} was not found at {prefab.Path}.");
        }

        var fixture = (GameObject)PrefabUtility.InstantiatePrefab(source, container.transform);
        fixture.name = $"{name} {index + 1}";
        fixture.transform.localPosition = Vector3.zero;
        fixture.transform.localRotation = Quaternion.identity;
        // The rig is measured out for fixtures at this scale, whatever their prefabs come with.
        fixture.transform.localScale = Vector3.one;
        var vrsl = fixture.GetComponentInChildren<VRStageLighting_DMX_Static>(true);
        fixture.AddComponent<AlpsVRSLFixture>().target = vrsl;
        foreach (var renderer in vrsl.objRenderers)
        {
            var material = renderer != null ? renderer.sharedMaterial : null;
            if (material != null && material.shader.name.EndsWith(StaticProjectionShader))
            {
                renderer.sharedMaterial = ScaledProjection(material);
            }
        }

        return fixture;
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
    /// The material of the demo's lasers. No effect drives how flat a laser's cone is, how
    /// many beams it has or how thick they are, so a show takes them from the material: this
    /// one flattens VRSL's cone into a fan of sixteen thin beams.
    /// </summary>
    private static Material LaserFan()
    {
        var source = AlpsCreateMenu.Laser.Load().GetComponentInChildren<MeshRenderer>(true).sharedMaterial;
        var material = AlpsDemoVenue.Asset(LaserMaterialPath, () => new Material(source));
        material.shader = source.shader;
        material.CopyPropertiesFromMaterial(source);
        material.SetFloat(AlpsShowPlayer.LaserFlatnessProperty, 1.999f);
        material.SetFloat(AlpsShowPlayer.LaserBeamsProperty, 16f);
        material.SetFloat(AlpsShowPlayer.LaserThicknessProperty, 0.008f);
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
