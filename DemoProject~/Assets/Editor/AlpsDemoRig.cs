using System.Collections.Generic;
using AdzukiSoft.ALPS;
using AdzukiSoft.ALPS.Editor;
using AdzukiSoft.ALPS.Vrsl;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Hangs the demo's lighting rig on the venue: 90 fixtures in eleven containers below one
/// container for the whole rig, and two penlight areas over the crowd.
///
/// How the heads are mounted decides what tilt and pan mean in a clip. Pan turns a head
/// around the axis it is mounted on, and tilt leans the beam away from that axis:
/// - Hung movers (front wash, mid and back spots, side wash): tilt 0 is straight down, a
///   positive tilt leans at the audience and a negative one upstage.
/// - The halo's movers face out of the ring: tilt 0 is straight down, a positive tilt leans
///   outward and a negative one into the ring.
/// - Standing movers (floor and totem spots): tilt 0 is straight up, a positive tilt leans
///   at the audience and a negative one upstage.
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
    public const string PenlightSweep = "Penlight Sweep";
    public const string PenlightRipple = "Penlight Ripple";

    /// <summary>The containers in the order their tracks come in the timeline, lowest layer first.</summary>
    public static readonly string[] Containers =
    {
        CycPars, ApronBars, TotemBars, FrontWash, SideWash, Halo, BackSpots, MidSpots, FloorSpots, TotemSpots, Blinders,
    };

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
        // The create menu gives every fixture this scale too.
        fixture.transform.localScale = Vector3.one;
        fixture.AddComponent<AlpsVRSLFixture>().target = fixture.GetComponentInChildren<VRStageLighting_DMX_Static>(true);
        return fixture;
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
