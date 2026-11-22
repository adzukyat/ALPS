using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Models the venue of the demo from code: a dark hall, a stage deck with a thrust and
/// risers, a ground supported truss roof with a ring below it, a cyclorama, the PA, the
/// backline, a barricade, the front of house riser and a crowd holding penlights.
///
/// The audience looks along +Z. The deck's front edge is at Z 0 and the hall floor at Y 0.
/// Every measure the rig hangs from is a constant here, so the fixtures land on the truss.
/// </summary>
public static class AlpsDemoVenue
{
    public const string Folder = "Assets/Demo";
    private const string MeshFolder = Folder + "/Meshes";
    private const string MaterialFolder = Folder + "/Materials";

    /// <summary>
    /// The scale of the demo's root. Everything below is measured for people about 1.7 m
    /// tall, and avatars in VRChat are about 1.3 m on average, so the root shrinks the hall,
    /// the rig and its fixtures alike and the show looks the same from an avatar's eyes.
    /// </summary>
    public const float Scale = 0.75f;

    /// <summary>Where a point measured like the venue is in the world.</summary>
    public static Vector3 World(Vector3 measured)
    {
        return measured * Scale;
    }

    // --- Hall -----------------------------------------------------------------------------
    public const float HallHalfWidth = 20f;
    public const float HallFront = -24.5f;
    public const float HallBack = 12.5f;
    public const float HallHeight = 13f;

    // --- Stage ----------------------------------------------------------------------------
    public const float DeckHalfWidth = 9f;
    public const float DeckDepth = 10f;
    public const float DeckHeight = 1.4f;
    public const float ThrustHalfWidth = 1.5f;
    public const float ThrustLength = 5f;
    public static readonly Vector3 DrumRiserMin = new Vector3(-2f, DeckHeight, 5.4f);
    public static readonly Vector3 DrumRiserMax = new Vector3(2f, DeckHeight + 0.45f, 7.9f);
    public const float CycZ = 10.9f;
    public const float CycHalfWidth = 9.6f;
    public const float CycTop = 9.6f;

    // --- Truss ----------------------------------------------------------------------------
    public const float TrussWidth = 0.4f;
    public const float TowerX = 9.6f;
    public const float FrontTrussZ = 0.5f;
    public const float MidTrussZ = 4.75f;
    public const float BackTrussZ = 9f;
    public const float RoofY = 9f;
    public const float MidTrussY = 9.6f;
    public const float RingRadius = 3f;
    public const float RingY = 7.4f;
    public static readonly float[] TotemX = { -8f, -5.6f, -3.2f, 3.2f, 5.6f, 8f };
    public static readonly float[] TotemHeight = { 3.8f, 3f, 2.2f, 2.2f, 3f, 3.8f };
    public const float TotemZ = 9.3f;

    // The lasers clamp onto the downstage side of the back truss, between its spots.
    public static readonly float[] LaserX = { -5.4f, -3.6f, -1.8f, 1.8f, 3.6f, 5.4f };
    public const float LaserY = RoofY - 0.12f;
    public const float LaserZ = BackTrussZ - TrussWidth * 0.5f - 0.22f;
    public const float LaserDepth = 0.4f;

    // --- Audience -------------------------------------------------------------------------
    public const float BarricadeZ = -6.8f;
    public const float CrowdFront = -8f;
    public const float CrowdBack = -15.5f;
    public const float CrowdHalfWidth = 11f;
    public const float FohZ = -20f;

    /// <summary>Where a fixture hangs below a truss whose middle is at <paramref name="trussY"/>.</summary>
    public static float Below(float trussY)
    {
        return trussY - TrussWidth * 0.5f - 0.03f;
    }

    public static GameObject Build(Transform parent)
    {
        var venue = new GameObject("Venue");
        venue.transform.SetParent(parent, false);

        var floor = Mat("Floor", new Color(0.075f, 0.075f, 0.08f), 0f, 0.35f);
        var wall = Mat("Wall", new Color(0.045f, 0.045f, 0.05f), 0f, 0.1f);
        var deck = Mat("Deck", new Color(0.03f, 0.03f, 0.032f), 0f, 0.55f);
        var fascia = Mat("Fascia", new Color(0.02f, 0.02f, 0.02f), 0f, 0.15f);
        var truss = Mat("Truss", new Color(0.62f, 0.64f, 0.66f), 0.85f, 0.5f);
        var cyc = Mat("Cyc", new Color(0.42f, 0.42f, 0.43f), 0f, 0.05f);
        var speaker = Mat("Speaker", new Color(0.035f, 0.035f, 0.035f), 0f, 0.2f);
        var grille = Mat("Grille", new Color(0.09f, 0.09f, 0.09f), 0.6f, 0.35f);
        var steel = Mat("Steel", new Color(0.45f, 0.46f, 0.48f), 0.8f, 0.45f);
        var drum = Mat("Drum Shell", new Color(0.35f, 0.02f, 0.03f), 0.2f, 0.75f);
        var skin = Mat("Drum Head", new Color(0.7f, 0.7f, 0.66f), 0f, 0.3f);
        var brass = Mat("Cymbal", new Color(0.75f, 0.6f, 0.25f), 0.95f, 0.7f);
        var crowd = Mat("Crowd", new Color(0.025f, 0.025f, 0.03f), 0f, 0.1f);
        var screen = Mat("Console Screen", new Color(0.02f, 0.02f, 0.02f), 0f, 0.6f);
        screen.EnableKeyword("_EMISSION");
        screen.SetColor("_EmissionColor", new Color(0.05f, 0.22f, 0.3f));
        screen.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;

        Part(venue, "Hall Floor", Hall(true), floor, true);
        Part(venue, "Hall Walls", Hall(false), wall, true);
        Part(venue, "Stage Deck", Deck(), deck, true);
        Part(venue, "Stage Fascia", Fascia(), fascia, false);
        Part(venue, "Stairs", Stairs(), deck, true);
        Part(venue, "Truss", TrussRoof(), truss, false);
        Part(venue, "Rigging", Rigging(), steel, false);
        Part(venue, "Laser Housings", LaserHousings(), speaker, false);
        Part(venue, "Cyclorama", Cyc(), cyc, true);
        Part(venue, "PA", Pa(false), speaker, false);
        Part(venue, "PA Grilles", Pa(true), grille, false);
        Part(venue, "Backline", Backline(), speaker, false);
        Part(venue, "Backline Hardware", Hardware(), steel, false);
        Part(venue, "Drum Shells", Drums(0), drum, false);
        Part(venue, "Drum Heads", Drums(1), skin, false);
        Part(venue, "Cymbals", Drums(2), brass, false);
        Part(venue, "Barricade", Barricade(), steel, true);
        Part(venue, "Front Of House", Foh(false), fascia, true);
        Part(venue, "Front Of House Screens", Foh(true), screen, false);
        Part(venue, "Crowd", Crowd(out var hands), crowd, false);
        Penlights(venue, hands);
        WorkLights(venue);

        // The crowd stands in for the visitors: on VRChat's layer for players it is drawn in
        // their place in the order, so it shows what keeping the light off players does.
        var players = LayerMask.NameToLayer("Player");
        foreach (var name in new[] { "Crowd", "Penlights" })
        {
            foreach (var child in venue.transform.Find(name).GetComponentsInChildren<Transform>(true))
            {
                child.gameObject.layer = players;
            }
        }

        AssetDatabase.SaveAssets();
        return venue;
    }

    /// <summary>Where the audience's raised hands are, for the penlights and for anything aimed at them.</summary>
    public static List<Vector3> CrowdHands()
    {
        Crowd(out var hands);
        return hands;
    }

    // --- Parts ----------------------------------------------------------------------------

    private static AlpsDemoMesh Hall(bool floorOnly)
    {
        var mesh = new AlpsDemoMesh();
        if (floorOnly)
        {
            mesh.Block(new Vector3(-HallHalfWidth, -0.3f, HallFront), new Vector3(HallHalfWidth, 0f, HallBack));
            return mesh;
        }

        const float t = 0.3f;
        mesh.Block(new Vector3(-HallHalfWidth - t, 0f, HallFront), new Vector3(-HallHalfWidth, HallHeight, HallBack));
        mesh.Block(new Vector3(HallHalfWidth, 0f, HallFront), new Vector3(HallHalfWidth + t, HallHeight, HallBack));
        mesh.Block(new Vector3(-HallHalfWidth - t, 0f, HallFront - t), new Vector3(HallHalfWidth + t, HallHeight, HallFront));
        mesh.Block(new Vector3(-HallHalfWidth - t, 0f, HallBack), new Vector3(HallHalfWidth + t, HallHeight, HallBack + t));
        mesh.Block(new Vector3(-HallHalfWidth - t, HallHeight, HallFront - t), new Vector3(HallHalfWidth + t, HallHeight + t, HallBack + t));
        return mesh;
    }

    private static AlpsDemoMesh Deck()
    {
        var mesh = new AlpsDemoMesh();
        const float top = 0.12f;
        mesh.Block(new Vector3(-DeckHalfWidth, DeckHeight - top, 0f), new Vector3(DeckHalfWidth, DeckHeight, DeckDepth));
        mesh.Block(new Vector3(-ThrustHalfWidth, DeckHeight - top, -ThrustLength), new Vector3(ThrustHalfWidth, DeckHeight, 0f));
        mesh.Block(DrumRiserMin, DrumRiserMax);
        return mesh;
    }

    private static AlpsDemoMesh Fascia()
    {
        var mesh = new AlpsDemoMesh();
        const float inset = 0.06f;
        var top = DeckHeight - 0.12f;
        mesh.Block(new Vector3(-DeckHalfWidth + inset, 0f, inset), new Vector3(DeckHalfWidth - inset, top, DeckDepth - inset));
        mesh.Block(new Vector3(-ThrustHalfWidth + inset, 0f, -ThrustLength + inset), new Vector3(ThrustHalfWidth - inset, top, inset));
        return mesh;
    }

    /// <summary>Steps down from both wings of the deck, wide enough to walk.</summary>
    private static AlpsDemoMesh Stairs()
    {
        var mesh = new AlpsDemoMesh();
        const int steps = 8;
        const float run = 0.3f;
        var rise = DeckHeight / steps;
        for (var side = -1; side <= 1; side += 2)
        {
            for (var i = 0; i < steps; i++)
            {
                // Each step is a block down to the floor, so the flight is solid to walk on.
                var x0 = side * (DeckHalfWidth + i * run);
                var x1 = side * (DeckHalfWidth + (i + 1) * run);
                var height = DeckHeight - (i + 1) * rise;
                mesh.Block(
                    new Vector3(Mathf.Min(x0, x1), 0f, 1.2f),
                    new Vector3(Mathf.Max(x0, x1), Mathf.Max(height, 0.02f), 3f));
            }
        }

        return mesh;
    }

    private static AlpsDemoMesh TrussRoof()
    {
        var mesh = new AlpsDemoMesh();
        var towerTop = RoofY + TrussWidth * 0.5f;
        foreach (var x in new[] { -TowerX, TowerX })
        {
            foreach (var z in new[] { FrontTrussZ, BackTrussZ })
            {
                mesh.Truss(new Vector3(x, 0f, z), new Vector3(x, towerTop + 0.6f, z), TrussWidth);
                // The sleeve block the roof rides on.
                mesh.Box(new Vector3(x, RoofY, z), new Vector3(TrussWidth + 0.12f, TrussWidth + 0.12f, TrussWidth + 0.12f));
                mesh.Box(new Vector3(x, 0.03f, z), new Vector3(1.1f, 0.06f, 1.1f));
            }

            mesh.Truss(new Vector3(x, RoofY, FrontTrussZ + 0.26f), new Vector3(x, RoofY, BackTrussZ - 0.26f), TrussWidth);
        }

        var inner = TowerX - 0.26f;
        mesh.Truss(new Vector3(-inner, RoofY, FrontTrussZ), new Vector3(inner, RoofY, FrontTrussZ), TrussWidth);
        mesh.Truss(new Vector3(-inner, RoofY, BackTrussZ), new Vector3(inner, RoofY, BackTrussZ), TrussWidth);

        // The mid truss sits a little higher on short legs standing on the side trusses.
        mesh.Truss(new Vector3(-inner, MidTrussY, MidTrussZ), new Vector3(inner, MidTrussY, MidTrussZ), TrussWidth);
        foreach (var x in new[] { -TowerX, TowerX })
        {
            mesh.Box(new Vector3(x, (RoofY + MidTrussY) * 0.5f, MidTrussZ), new Vector3(TrussWidth, MidTrussY - RoofY + TrussWidth, TrussWidth));
        }

        mesh.RingTruss(new Vector3(0f, RingY, MidTrussZ), RingRadius, TrussWidth);

        for (var i = 0; i < TotemX.Length; i++)
        {
            var x = TotemX[i];
            mesh.Truss(new Vector3(x, DeckHeight, TotemZ), new Vector3(x, DeckHeight + TotemHeight[i], TotemZ), 0.3f, 0.04f, 0.018f);
            mesh.Box(new Vector3(x, DeckHeight + 0.02f, TotemZ), new Vector3(0.7f, 0.04f, 0.7f));
            mesh.Box(new Vector3(x, DeckHeight + TotemHeight[i] + 0.015f, TotemZ), new Vector3(0.36f, 0.03f, 0.36f));
        }

        return mesh;
    }

    /// <summary>The steel the ring hangs on, and the PA's flying frames.</summary>
    private static AlpsDemoMesh Rigging()
    {
        var mesh = new AlpsDemoMesh();
        for (var i = 0; i < 4; i++)
        {
            var out2 = AlpsDemoMesh.Radial(45f + i * 90f) * RingRadius;
            var at = new Vector3(out2.x, RingY + TrussWidth * 0.5f, MidTrussZ + out2.z);
            // Two points pick up from the mid truss, the other two from spreader pipes.
            mesh.Cylinder(at, new Vector3(at.x, MidTrussY + 0.9f, at.z), 0.012f, 6);
        }

        foreach (var z in new[] { MidTrussZ - RingRadius * 0.7071f, MidTrussZ + RingRadius * 0.7071f })
        {
            mesh.Cylinder(new Vector3(-TowerX, MidTrussY + 0.9f, z), new Vector3(TowerX, MidTrussY + 0.9f, z), 0.03f, 8);
            foreach (var x in new[] { -TowerX, TowerX })
            {
                mesh.Cylinder(new Vector3(x, RoofY + 0.2f, z), new Vector3(x, MidTrussY + 0.9f, z), 0.03f, 8);
            }
        }

        foreach (var side in new[] { -1f, 1f })
        {
            var x = side * (TowerX + 2.2f);
            mesh.Box(new Vector3(x, 10.1f, -0.4f), new Vector3(1.3f, 0.12f, 0.9f));
            mesh.Cylinder(new Vector3(x, 10.1f, -0.7f), new Vector3(x, HallHeight, -0.7f), 0.012f, 6);
            mesh.Cylinder(new Vector3(x, 10.1f, -0.1f), new Vector3(x, HallHeight, -0.1f), 0.012f, 6);
        }

        return mesh;
    }

    /// <summary>A box for every laser, which is only its beams, and the arm that holds it off the back truss.</summary>
    private static AlpsDemoMesh LaserHousings()
    {
        var mesh = new AlpsDemoMesh();
        foreach (var x in LaserX)
        {
            mesh.Box(new Vector3(x, LaserY, LaserZ), new Vector3(0.3f, 0.2f, LaserDepth));
            mesh.Box(new Vector3(x, LaserY, BackTrussZ - TrussWidth * 0.5f), new Vector3(0.08f, 0.08f, 0.12f));
        }

        return mesh;
    }

    private static AlpsDemoMesh Cyc()
    {
        var mesh = new AlpsDemoMesh();
        mesh.Block(new Vector3(-CycHalfWidth, DeckHeight - 0.1f, CycZ), new Vector3(CycHalfWidth, CycTop, CycZ + 0.08f));
        return mesh;
    }

    /// <summary>Two flown line arrays and the subs under them, the boxes or their grilles.</summary>
    private static AlpsDemoMesh Pa(bool grilles)
    {
        var mesh = new AlpsDemoMesh();
        foreach (var side in new[] { -1f, 1f })
        {
            var x = side * (TowerX + 2.2f);
            var top = new Vector3(x, 9.85f, -0.4f);
            var angle = 2f;
            for (var i = 0; i < 9; i++)
            {
                // Each box hangs off the one above and tips a little further down.
                var rotation = Quaternion.Euler(-angle, 0f, 0f);
                var center = top + rotation * new Vector3(0f, -0.2f, 0f);
                if (grilles)
                {
                    mesh.Box(center + rotation * new Vector3(0f, 0f, -0.36f), new Vector3(1.12f, 0.36f, 0.02f), rotation);
                }
                else
                {
                    mesh.Box(center, new Vector3(1.2f, 0.4f, 0.7f), rotation);
                }

                top += rotation * new Vector3(0f, -0.41f, 0f);
                angle += 2.2f;
            }

            for (var column = 0; column < 3; column++)
            {
                for (var row = 0; row < 2; row++)
                {
                    var center = new Vector3(x + (column - 1) * 1.25f * side, 0.3f + row * 0.6f, -1.2f);
                    if (grilles)
                    {
                        mesh.Box(center + new Vector3(0f, 0f, -0.46f), new Vector3(1.1f, 0.5f, 0.02f));
                    }
                    else
                    {
                        mesh.Box(center, new Vector3(1.2f, 0.58f, 0.9f));
                    }
                }
            }
        }

        return mesh;
    }

    /// <summary>Amp stacks, wedges, a keyboard and the drum throne.</summary>
    private static AlpsDemoMesh Backline()
    {
        var mesh = new AlpsDemoMesh();
        foreach (var side in new[] { -1f, 1f })
        {
            var x = side * 6.6f;
            mesh.Box(new Vector3(x, DeckHeight + 0.38f, 7.6f), new Vector3(0.76f, 0.76f, 0.36f));
            mesh.Box(new Vector3(x, DeckHeight + 1.14f, 7.6f), new Vector3(0.76f, 0.76f, 0.36f));
            mesh.Box(new Vector3(x, DeckHeight + 1.66f, 7.6f), new Vector3(0.72f, 0.26f, 0.3f));
            mesh.Box(new Vector3(x + side * 0.85f, DeckHeight + 0.38f, 7.6f), new Vector3(0.76f, 0.76f, 0.36f));
        }

        // Wedges along the front of the deck, tipped back at the players.
        foreach (var x in new[] { -5.2f, -2.6f, 2.6f, 5.2f })
        {
            mesh.Box(new Vector3(x, DeckHeight + 0.17f, 1f), new Vector3(0.6f, 0.34f, 0.45f), Quaternion.Euler(-28f, 0f, 0f));
        }

        mesh.Box(new Vector3(0f, DeckHeight + 0.17f, -3.9f), new Vector3(0.6f, 0.34f, 0.45f), Quaternion.Euler(-28f, 0f, 0f));

        // Keyboard, stage right.
        mesh.Box(new Vector3(-5.4f, DeckHeight + 0.95f, 3.6f), new Vector3(1.4f, 0.1f, 0.38f), Quaternion.Euler(0f, 25f, 0f));
        mesh.Box(new Vector3(-5.4f, DeckHeight + 1.2f, 3.75f), new Vector3(1.1f, 0.08f, 0.3f), Quaternion.Euler(0f, 25f, 0f));

        // Drum throne.
        mesh.Cylinder(new Vector3(0f, DrumRiserMax.y + 0.5f, 7.3f), new Vector3(0f, DrumRiserMax.y + 0.58f, 7.3f), 0.18f, 12);
        return mesh;
    }

    /// <summary>Stands: microphones, cymbals, the keyboard and the drum hardware.</summary>
    private static AlpsDemoMesh Hardware()
    {
        var mesh = new AlpsDemoMesh();
        foreach (var at in new[] { new Vector3(0f, 0f, -3f), new Vector3(-3.6f, 0f, 1.6f), new Vector3(3.6f, 0f, 1.6f) })
        {
            var foot = new Vector3(at.x, DeckHeight, at.z);
            mesh.Cylinder(foot, foot + Vector3.up * 0.03f, 0.14f, 12);
            mesh.Cylinder(foot, foot + Vector3.up * 1.5f, 0.012f, 6);
            mesh.Cylinder(foot + Vector3.up * 1.5f, foot + new Vector3(0f, 1.58f, -0.14f), 0.022f, 8);
        }

        // Keyboard stand.
        var keys = new Vector3(-5.4f, DeckHeight, 3.6f);
        mesh.Beam(keys + new Vector3(-0.45f, 0f, -0.2f), keys + new Vector3(0.45f, 0.92f, 0.2f), 0.03f);
        mesh.Beam(keys + new Vector3(0.45f, 0f, -0.2f), keys + new Vector3(-0.45f, 0.92f, 0.2f), 0.03f);

        // Cymbal stands and the throne's post.
        var riser = DrumRiserMax.y;
        foreach (var stand in CymbalStands)
        {
            mesh.Cylinder(new Vector3(stand.x, riser, stand.z), stand, 0.012f, 6);
        }

        mesh.Cylinder(new Vector3(0f, riser, 7.3f), new Vector3(0f, riser + 0.5f, 7.3f), 0.02f, 6);
        mesh.Cylinder(new Vector3(-0.5f, riser, 6.5f), new Vector3(-0.5f, riser + 0.6f, 6.5f), 0.015f, 6);
        return mesh;
    }

    private static readonly Vector3[] CymbalStands =
    {
        new Vector3(-1.05f, DeckHeight + 0.45f + 1.25f, 6.55f),
        new Vector3(1.05f, DeckHeight + 0.45f + 1.3f, 6.55f),
        new Vector3(1.45f, DeckHeight + 0.45f + 1.15f, 7.1f),
        new Vector3(-1.4f, DeckHeight + 0.45f + 0.95f, 7f),
    };

    /// <summary>The kit on the drum riser: shells, heads or cymbals.</summary>
    private static AlpsDemoMesh Drums(int part)
    {
        var mesh = new AlpsDemoMesh();
        var riser = DrumRiserMax.y;
        // Kick lies on its side facing the audience, the rest face up.
        Drum(mesh, part, new Vector3(0f, riser + 0.3f, 6.45f), Vector3.back, 0.29f, 0.45f);
        Drum(mesh, part, new Vector3(-0.5f, riser + 0.68f, 6.5f), Vector3.up, 0.18f, 0.14f);
        Drum(mesh, part, new Vector3(-0.28f, riser + 0.88f, 6.25f), new Vector3(0f, 1f, -0.25f), 0.14f, 0.2f);
        Drum(mesh, part, new Vector3(0.28f, riser + 0.88f, 6.25f), new Vector3(0f, 1f, -0.25f), 0.16f, 0.22f);
        Drum(mesh, part, new Vector3(0.75f, riser + 0.5f, 6.85f), Vector3.up, 0.21f, 0.4f);
        if (part == 2)
        {
            foreach (var stand in CymbalStands)
            {
                mesh.Cone(stand, stand + Vector3.up * 0.03f, 0.24f, 0.03f, 16);
                mesh.Cone(stand + Vector3.down * 0.005f, stand, 0.03f, 0.24f, 16);
            }
        }

        return mesh;
    }

    private static void Drum(AlpsDemoMesh mesh, int part, Vector3 center, Vector3 axis, float radius, float depth)
    {
        axis.Normalize();
        var a = center - axis * depth * 0.5f;
        var b = center + axis * depth * 0.5f;
        if (part == 0)
        {
            mesh.Cylinder(a, b, radius, 18);
        }
        else if (part == 1)
        {
            mesh.Cylinder(b, b + axis * 0.012f, radius * 0.94f, 18);
        }
    }

    /// <summary>A crowd barrier across the hall in front of the thrust.</summary>
    private static AlpsDemoMesh Barricade()
    {
        var mesh = new AlpsDemoMesh();
        const float height = 1.1f;
        const float panel = 1.1f;
        var count = Mathf.RoundToInt(HallHalfWidth * 2f / panel) - 8;
        var start = -count * panel * 0.5f;
        for (var i = 0; i < count; i++)
        {
            var x0 = start + i * panel;
            var x1 = x0 + panel;
            mesh.Block(new Vector3(x0 + 0.02f, 0.08f, BarricadeZ - 0.015f), new Vector3(x1 - 0.02f, height - 0.05f, BarricadeZ + 0.015f));
            mesh.Beam(new Vector3(x0, height, BarricadeZ), new Vector3(x1, height, BarricadeZ), 0.06f);
            mesh.Beam(new Vector3(x0, 0f, BarricadeZ), new Vector3(x0, height, BarricadeZ), 0.05f);
            // The foot plate the crowd stands on, and the brace behind it.
            mesh.Block(new Vector3(x0 + 0.02f, 0f, BarricadeZ - 0.9f), new Vector3(x1 - 0.02f, 0.03f, BarricadeZ + 0.5f));
            mesh.Beam(new Vector3(x0 + 0.05f, height * 0.8f, BarricadeZ), new Vector3(x0 + 0.05f, 0.03f, BarricadeZ + 0.5f), 0.04f);
        }

        return mesh;
    }

    /// <summary>The mix position: a riser with desks, or the screens on them.</summary>
    private static AlpsDemoMesh Foh(bool screens)
    {
        var mesh = new AlpsDemoMesh();
        if (screens)
        {
            foreach (var x in new[] { -1.9f, -0.65f, 0.65f, 1.9f })
            {
                mesh.Box(new Vector3(x, 1.72f, FohZ + 0.48f), new Vector3(0.74f, 0.42f, 0.012f), Quaternion.Euler(-12f, 0f, 0f));
            }

            return mesh;
        }

        mesh.Block(new Vector3(-3.4f, 0f, FohZ - 1.6f), new Vector3(3.4f, 0.4f, FohZ + 1.6f));
        mesh.Block(new Vector3(-3.4f, 0.2f, FohZ - 2f), new Vector3(3.4f, 0.21f, FohZ - 1.6f));
        mesh.Block(new Vector3(-3.4f, 0f, FohZ - 2f), new Vector3(3.4f, 0.2f, FohZ - 1.6f));
        mesh.Block(new Vector3(-2.9f, 0.4f, FohZ + 0.4f), new Vector3(2.9f, 1.25f, FohZ + 1.2f));
        foreach (var x in new[] { -1.9f, -0.65f, 0.65f, 1.9f })
        {
            mesh.Box(new Vector3(x, 1.34f, FohZ + 0.7f), new Vector3(1.1f, 0.12f, 0.62f), Quaternion.Euler(-10f, 0f, 0f));
            mesh.Box(new Vector3(x, 1.72f, FohZ + 0.5f), new Vector3(0.8f, 0.48f, 0.03f), Quaternion.Euler(-12f, 0f, 0f));
        }

        // Rails around the riser, open at the back where the steps are.
        foreach (var x in new[] { -3.4f, 3.4f })
        {
            mesh.Beam(new Vector3(x, 1.4f, FohZ - 1.6f), new Vector3(x, 1.4f, FohZ + 1.6f), 0.05f);
            for (var i = 0; i <= 4; i++)
            {
                var z = FohZ - 1.6f + i * 0.8f;
                mesh.Beam(new Vector3(x, 0.4f, z), new Vector3(x, 1.4f, z), 0.04f);
            }
        }

        return mesh;
    }

    /// <summary>
    /// Rows of standing figures between the barricade and the mix position, with the hand
    /// each one raises. Seeded, so every rebuild gives the same crowd.
    /// </summary>
    private static AlpsDemoMesh Crowd(out List<Vector3> hands)
    {
        var mesh = new AlpsDemoMesh();
        hands = new List<Vector3>();
        var random = new System.Random(20261122);
        float Next() => (float)random.NextDouble();

        var row = 0;
        for (var z = CrowdFront; z >= CrowdBack; z -= 1.15f, row++)
        {
            for (var x = -CrowdHalfWidth + (row % 2) * 0.55f; x <= CrowdHalfWidth; x += 1.1f)
            {
                // The thinner the further back, and nobody inside the mix position.
                if (Next() < 0.08f + 0.02f * row || (Mathf.Abs(x) < 4.2f && z < FohZ + 3.2f))
                {
                    continue;
                }

                var at = new Vector3(x + (Next() - 0.5f) * 0.5f, 0f, z + (Next() - 0.5f) * 0.5f);
                var height = 1.55f + Next() * 0.3f;
                var turn = Quaternion.Euler(0f, (Next() - 0.5f) * 30f, 0f);
                mesh.Box(at + Vector3.up * (height * 0.26f), new Vector3(0.3f, height * 0.52f, 0.2f), turn);
                mesh.Box(at + Vector3.up * (height * 0.68f), new Vector3(0.42f, height * 0.34f, 0.22f), turn);
                mesh.Cylinder(at + Vector3.up * (height * 0.87f), at + Vector3.up * height, 0.095f, 8);

                var side = Next() < 0.5f ? -1f : 1f;
                var shoulder = at + turn * new Vector3(side * 0.22f, height * 0.82f, 0f);
                var hand = shoulder + turn * new Vector3(side * 0.08f, 0.42f + Next() * 0.12f, 0.16f);
                mesh.Beam(shoulder, hand, 0.08f);
                hands.Add(hand);
            }
        }

        return mesh;
    }

    /// <summary>One penlight per raised hand, each its own object so the shader knows where it is.</summary>
    private static void Penlights(GameObject venue, List<Vector3> hands)
    {
        var shader = Shader.Find("ALPS/Penlight");
        if (shader == null)
        {
            Debug.LogError("[ALPS Demo] The ALPS/Penlight shader was not found.");
            return;
        }

        var material = Asset(MaterialFolder + "/Penlight.mat", () => new Material(shader));
        material.shader = shader;
        material.SetColor("_Color", new Color(0.02f, 0.02f, 0.02f));
        // Without a show the sticks glow a faint white of their own.
        material.SetColor("_EmissionColor", new Color(0.05f, 0.05f, 0.05f));
        material.SetFloat("_ShowIntensity", 3f);
        material.enableInstancing = true;
        EditorUtility.SetDirty(material);

        var stick = new AlpsDemoMesh();
        stick.Cylinder(new Vector3(0f, -0.04f, 0f), new Vector3(0f, 0.24f, 0f), 0.016f, 8);
        var mesh = SaveMesh(stick.ToMesh("Penlight"));

        var root = new GameObject("Penlights");
        root.transform.SetParent(venue.transform, false);
        var random = new System.Random(7);
        for (var i = 0; i < hands.Count; i++)
        {
            var penlight = new GameObject("Penlight");
            penlight.transform.SetParent(root.transform, false);
            penlight.transform.localPosition = hands[i];
            penlight.transform.localRotation = Quaternion.Euler(
                -15f + (float)random.NextDouble() * 30f, 0f, -20f + (float)random.NextDouble() * 40f);
            penlight.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = penlight.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
    }

    /// <summary>
    /// A little light that is always on, so the stage keeps its shape between cues: a faint
    /// cold wash from the house and a warm one over the deck.
    /// </summary>
    private static void WorkLights(GameObject venue)
    {
        var house = new GameObject("House Light").AddComponent<Light>();
        house.transform.SetParent(venue.transform, false);
        house.transform.localRotation = Quaternion.Euler(38f, 12f, 0f);
        house.type = LightType.Directional;
        house.color = new Color(0.55f, 0.65f, 1f);
        house.intensity = 0.06f;
        house.shadows = LightShadows.None;

        var deck = new GameObject("Deck Work Light").AddComponent<Light>();
        deck.transform.SetParent(venue.transform, false);
        deck.transform.localPosition = new Vector3(0f, 7f, 5f);
        deck.type = LightType.Point;
        deck.color = new Color(1f, 0.8f, 0.6f);
        deck.intensity = 0.25f;
        // A light's range is in the world's measure, whatever the scale above it.
        deck.range = 16f * Scale;
        deck.shadows = LightShadows.None;
    }

    // --- Assets ---------------------------------------------------------------------------

    private static void Part(GameObject venue, string name, AlpsDemoMesh builder, Material material, bool walkable)
    {
        if (builder.IsEmpty)
        {
            return;
        }

        var mesh = SaveMesh(builder.ToMesh(name));
        var part = new GameObject(name);
        part.transform.SetParent(venue.transform, false);
        part.isStatic = true;
        part.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = part.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        if (walkable)
        {
            part.AddComponent<MeshCollider>().sharedMesh = mesh;
        }
    }

    /// <summary>
    /// Writes the mesh over the one the last build saved under its name, so the asset and
    /// every reference to it stay the same.
    /// </summary>
    private static Mesh SaveMesh(Mesh built)
    {
        var path = $"{MeshFolder}/{built.name}.asset";
        var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (saved == null)
        {
            EnsureFolder(MeshFolder);
            AssetDatabase.CreateAsset(built, path);
            return built;
        }

        EditorUtility.CopySerialized(built, saved);
        saved.name = built.name;
        EditorUtility.SetDirty(saved);
        Object.DestroyImmediate(built);
        return saved;
    }

    private static Material Mat(string name, Color color, float metallic, float smoothness)
    {
        var material = Asset($"{MaterialFolder}/{name}.mat", () => new Material(Shader.Find("Standard")));
        material.shader = Shader.Find("Standard");
        material.color = color;
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Glossiness", smoothness);
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>The asset at <paramref name="path"/>, made on the first build and kept after, so its GUID holds.</summary>
    public static T Asset<T>(string path, System.Func<T> create) where T : Object
    {
        var asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset != null)
        {
            return asset;
        }

        EnsureFolder(System.IO.Path.GetDirectoryName(path).Replace('\\', '/'));
        asset = create();
        AssetDatabase.CreateAsset(asset, path);
        return asset;
    }

    public static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder))
        {
            return;
        }

        var parent = System.IO.Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(folder));
    }
}
