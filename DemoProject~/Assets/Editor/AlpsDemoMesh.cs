using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Collects boxes, tubes and truss into one mesh, so a whole part of the venue is one object.
/// Everything is in the space of the object the mesh goes on. The meshes carry no UVs, since
/// the venue's materials are plain colours.
/// </summary>
public sealed class AlpsDemoMesh
{
    private readonly List<Vector3> _vertices = new List<Vector3>();
    private readonly List<Vector3> _normals = new List<Vector3>();
    private readonly List<int> _triangles = new List<int>();

    public bool IsEmpty => _vertices.Count == 0;

    /// <summary>A box of <paramref name="size"/> around <paramref name="center"/>.</summary>
    public void Box(Vector3 center, Vector3 size)
    {
        Box(center, size, Quaternion.identity);
    }

    public void Box(Vector3 center, Vector3 size, Quaternion rotation)
    {
        var half = size * 0.5f;
        Face(center, rotation, Vector3.right, Vector3.up, Vector3.forward, half.x, half.y, half.z);
        Face(center, rotation, Vector3.left, Vector3.up, Vector3.back, half.x, half.y, half.z);
        Face(center, rotation, Vector3.up, Vector3.forward, Vector3.right, half.y, half.z, half.x);
        Face(center, rotation, Vector3.down, Vector3.forward, Vector3.left, half.y, half.z, half.x);
        Face(center, rotation, Vector3.forward, Vector3.up, Vector3.left, half.z, half.y, half.x);
        Face(center, rotation, Vector3.back, Vector3.up, Vector3.right, half.z, half.y, half.x);
    }

    /// <summary>A box given by its lowest and highest corner.</summary>
    public void Block(Vector3 min, Vector3 max)
    {
        Box((min + max) * 0.5f, max - min);
    }

    /// <summary>
    /// A tube from <paramref name="a"/> to <paramref name="b"/> with open ends, square unless
    /// <paramref name="sides"/> says otherwise. Truss is made of thousands of these, and their
    /// ends always sit inside another one.
    /// </summary>
    public void Beam(Vector3 a, Vector3 b, float thickness, int sides = 4)
    {
        Tube(a, b, thickness * 0.7071f, thickness * 0.7071f, sides, false, Mathf.PI / sides);
    }

    /// <summary>A round tube from <paramref name="a"/> to <paramref name="b"/>, capped at both ends.</summary>
    public void Cylinder(Vector3 a, Vector3 b, float radius, int sides = 12)
    {
        Tube(a, b, radius, radius, sides, true, 0f);
    }

    /// <summary>A capped tube whose radius runs from <paramref name="radiusA"/> to <paramref name="radiusB"/>.</summary>
    public void Cone(Vector3 a, Vector3 b, float radiusA, float radiusB, int sides = 12)
    {
        Tube(a, b, radiusA, radiusB, sides, true, 0f);
    }

    private void Tube(Vector3 a, Vector3 b, float radiusA, float radiusB, int sides, bool caps, float twist)
    {
        var axis = b - a;
        if (axis.sqrMagnitude < 1e-10f)
        {
            return;
        }

        var rotation = Quaternion.LookRotation(axis, UpFor(axis));
        var forward = axis.normalized;
        for (var i = 0; i < sides; i++)
        {
            var angle0 = twist + i * Mathf.PI * 2f / sides;
            var angle1 = twist + (i + 1) * Mathf.PI * 2f / sides;
            var d0 = rotation * new Vector3(Mathf.Cos(angle0), Mathf.Sin(angle0), 0f);
            var d1 = rotation * new Vector3(Mathf.Cos(angle1), Mathf.Sin(angle1), 0f);
            var normal = (d0 + d1).normalized;
            Quad(a + d0 * radiusA, a + d1 * radiusA, b + d1 * radiusB, b + d0 * radiusB, normal);
            if (caps && radiusA > 0f)
            {
                Triangle(a, a + d1 * radiusA, a + d0 * radiusA, -forward);
            }

            if (caps && radiusB > 0f)
            {
                Triangle(b, b + d0 * radiusB, b + d1 * radiusB, forward);
            }
        }
    }

    /// <summary>
    /// Box truss from <paramref name="a"/> to <paramref name="b"/>: four chords, a diagonal on
    /// every face of every bay and a frame where the bays meet.
    /// </summary>
    public void Truss(Vector3 a, Vector3 b, float width = 0.4f, float chord = 0.05f, float brace = 0.022f)
    {
        var axis = b - a;
        var length = axis.magnitude;
        if (length < 1e-4f)
        {
            return;
        }

        var forward = axis / length;
        var up = UpFor(axis);
        var right = Vector3.Cross(up, forward).normalized;
        up = Vector3.Cross(forward, right).normalized;
        var half = width * 0.5f - chord * 0.5f;
        var corners = new[]
        {
            right * half + up * half,
            -right * half + up * half,
            -right * half - up * half,
            right * half - up * half,
        };

        foreach (var corner in corners)
        {
            Beam(a + corner, b + corner, chord);
        }

        var bays = Mathf.Max(1, Mathf.RoundToInt(length / width));
        for (var bay = 0; bay <= bays; bay++)
        {
            var at = a + forward * (length * bay / bays);
            for (var c = 0; c < 4; c++)
            {
                Beam(at + corners[c], at + corners[(c + 1) % 4], brace, 3);
            }

            if (bay == bays)
            {
                break;
            }

            var next = a + forward * (length * (bay + 1) / bays);
            for (var c = 0; c < 4; c++)
            {
                var from = bay % 2 == 0 ? c : (c + 1) % 4;
                var to = bay % 2 == 0 ? (c + 1) % 4 : c;
                Beam(at + corners[from], next + corners[to], brace, 3);
            }
        }
    }

    /// <summary>A ring of box truss lying flat around <paramref name="center"/>.</summary>
    public void RingTruss(Vector3 center, float radius, float width = 0.4f, int segments = 40, float chord = 0.05f, float brace = 0.022f)
    {
        var half = width * 0.5f - chord * 0.5f;
        for (var i = 0; i < segments; i++)
        {
            var d0 = Radial(i * 360f / segments);
            var d1 = Radial((i + 1) * 360f / segments);
            for (var side = -1; side <= 1; side += 2)
            {
                for (var level = -1; level <= 1; level += 2)
                {
                    var lift = Vector3.up * (level * half);
                    Beam(center + d0 * (radius + side * half) + lift, center + d1 * (radius + side * half) + lift, chord);
                }

                // The diagonal of the inner and the outer face.
                var low = i % 2 == 0 ? -half : half;
                Beam(center + d0 * (radius + side * half) + Vector3.up * low, center + d1 * (radius + side * half) - Vector3.up * low, brace, 3);
            }

            // The frame between two segments, and the diagonals of the top and the bottom.
            Beam(center + d0 * (radius - half) + Vector3.up * half, center + d0 * (radius + half) + Vector3.up * half, brace, 3);
            Beam(center + d0 * (radius - half) - Vector3.up * half, center + d0 * (radius + half) - Vector3.up * half, brace, 3);
            Beam(center + d0 * (radius - half) - Vector3.up * half, center + d0 * (radius - half) + Vector3.up * half, brace, 3);
            Beam(center + d0 * (radius + half) - Vector3.up * half, center + d0 * (radius + half) + Vector3.up * half, brace, 3);
            var inner = i % 2 == 0 ? -half : half;
            Beam(center + d0 * (radius + inner) + Vector3.up * half, center + d1 * (radius - inner) + Vector3.up * half, brace, 3);
            Beam(center + d0 * (radius + inner) - Vector3.up * half, center + d1 * (radius - inner) - Vector3.up * half, brace, 3);
        }
    }

    /// <summary>The horizontal unit direction at <paramref name="degrees"/>, 0 along +Z.</summary>
    public static Vector3 Radial(float degrees)
    {
        var radians = degrees * Mathf.Deg2Rad;
        return new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians));
    }

    public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
    {
        var start = _vertices.Count;
        _vertices.Add(a);
        _vertices.Add(b);
        _vertices.Add(c);
        _vertices.Add(d);
        for (var i = 0; i < 4; i++)
        {
            _normals.Add(normal);
        }

        AddTriangle(start, start + 1, start + 2, normal);
        AddTriangle(start, start + 2, start + 3, normal);
    }

    public void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3 normal)
    {
        var start = _vertices.Count;
        _vertices.Add(a);
        _vertices.Add(b);
        _vertices.Add(c);
        for (var i = 0; i < 3; i++)
        {
            _normals.Add(normal);
        }

        AddTriangle(start, start + 1, start + 2, normal);
    }

    public Mesh ToMesh(string name)
    {
        var mesh = new Mesh { name = name };
        if (_vertices.Count > 65000)
        {
            mesh.indexFormat = IndexFormat.UInt32;
        }

        mesh.SetVertices(_vertices);
        mesh.SetNormals(_normals);
        mesh.SetTriangles(_triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>Winds the triangle so it faces along <paramref name="normal"/>, whichever way its corners were listed.</summary>
    private void AddTriangle(int a, int b, int c, Vector3 normal)
    {
        var facing = Vector3.Cross(_vertices[b] - _vertices[a], _vertices[c] - _vertices[a]);
        if (Vector3.Dot(facing, normal) >= 0f)
        {
            _triangles.Add(a);
            _triangles.Add(b);
            _triangles.Add(c);
        }
        else
        {
            _triangles.Add(a);
            _triangles.Add(c);
            _triangles.Add(b);
        }
    }

    private void Face(Vector3 center, Quaternion rotation, Vector3 normal, Vector3 up, Vector3 right, float distance, float halfUp, float halfRight)
    {
        var n = rotation * normal;
        var u = rotation * up * halfUp;
        var r = rotation * right * halfRight;
        var middle = center + n * distance;
        Quad(middle - r - u, middle + r - u, middle + r + u, middle - r + u, n);
    }

    private static Vector3 UpFor(Vector3 axis)
    {
        return Mathf.Abs(Vector3.Dot(axis.normalized, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up;
    }
}
