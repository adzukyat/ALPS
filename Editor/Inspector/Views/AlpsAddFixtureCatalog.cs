using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>
    /// Add fixture: the container inspector's counterpart of the clip's add effect list, an
    /// always open grid of the VR Stage Lighting fixtures the Hierarchy's ALPS menu offers,
    /// led by a container, which shows that containers nest. Picking one adds it under this
    /// container and leaves the container selected, so several can be added in a row. The
    /// last tile adds a cube that glows, a placeholder for a mesh of the world's own.
    /// </summary>
    public class AlpsAddFixtureCatalog : VisualElement
    {
        private readonly AlpsContainer _container;
        private readonly Action<GameObject> _onAdded;

        public AlpsAddFixtureCatalog(AlpsContainer container, Action<GameObject> onAdded)
        {
            _container = container;
            _onAdded = onAdded;
            AddToClassList("alps-add");

            var divider = new VisualElement();
            divider.AddToClassList("alps-divider");
            Add(divider);

            var list = AlpsCatalogPanel.Build(this, AlpsStrings.Tr("addFixture.title"));

            var nested = AlpsCatalogPanel.Item(AlpsFixtureThumbnail.BuildContainer(), AlpsStrings.Tr("addFixture.container"), AlpsStrings.Tr("addFixture.container.tip"));
            nested.RegisterCallback<PointerDownEvent>(_ => RequestAddContainer());
            list.Add(nested);

            foreach (var fixture in AlpsCreateMenu.Fixtures)
            {
                var captured = fixture;
                var available = fixture.Load() != null;
                var item = AlpsCatalogPanel.Item(
                    AlpsFixtureThumbnail.Build(fixture),
                    AlpsStrings.Tr(fixture.LabelKey),
                    available ? string.Empty : AlpsStrings.Tr("addFixture.missing"));
                item.SetEnabled(available);
                item.RegisterCallback<PointerDownEvent>(_ => RequestAdd(captured));
                list.Add(item);
            }

            // Last, since it is no light of its own: a cube that glows, a placeholder for any
            // mesh of the world's own.
            var canGlow = AlpsEmissiveMesh.LoadMaterial() != null;
            var mesh = AlpsCatalogPanel.Item(
                AlpsFixtureThumbnail.BuildEmissiveMesh(),
                AlpsStrings.Tr("addFixture.emissiveMesh"),
                canGlow ? AlpsStrings.Tr("addFixture.emissiveMesh.tip") : AlpsStrings.Tr("addFixture.missing"));
            mesh.SetEnabled(canGlow);
            mesh.RegisterCallback<PointerDownEvent>(_ => RequestAddEmissiveMesh());
            list.Add(mesh);
        }

        /// <summary>Adds <paramref name="fixture"/> under the container, the same entry point a tile uses.</summary>
        public GameObject RequestAdd(AlpsVrslPrefab fixture)
        {
            return Added(KeepSelection(() => AlpsCreateMenu.CreateFixture(fixture, _container.gameObject)));
        }

        /// <summary>Adds a container under this one, the same entry point its tile uses.</summary>
        public GameObject RequestAddContainer()
        {
            return Added(KeepSelection(() => AlpsCreateMenu.CreateContainer(_container.gameObject)));
        }

        /// <summary>Adds a cube that glows under the container, the same entry point its tile uses.</summary>
        public GameObject RequestAddEmissiveMesh()
        {
            return Added(KeepSelection(() => AlpsEmissiveMesh.Create(_container.gameObject)));
        }

        private GameObject Added(GameObject created)
        {
            if (created != null)
            {
                _onAdded?.Invoke(created);
            }

            return created;
        }

        /// <summary>
        /// Placing a new object selects it, which would swap this inspector for the new
        /// object's, so the selection goes back to what it was.
        /// </summary>
        private static GameObject KeepSelection(Func<GameObject> create)
        {
            var selection = Selection.objects;
            var created = create();
            Selection.objects = selection;
            return created;
        }
    }

    /// <summary>
    /// The preview well of each fixture tile: amber outlines in the style of the effect
    /// thumbnails, a moving head with a narrow or a wide beam, the face of a par can, the four
    /// cells of a blinder, a bar of lamps, a bar of segments, a fan of laser lines, the one lamp
    /// of a flasher, the cells of a strobe panel, a hanging mirror ball, and a frame holding
    /// lamps for a container.
    /// </summary>
    internal static class AlpsFixtureThumbnail
    {
        private const float Width = 60f;
        private const float Height = 26f;

        public static VisualElement Build(AlpsVrslPrefab fixture)
        {
            return Well(Contours(fixture));
        }

        /// <summary>A container: a frame holding three lamps.</summary>
        public static VisualElement BuildContainer()
        {
            var contours = new List<Vector2[]> { Box(15f, 4f, 45f, 22f) };
            for (var i = 0; i < 3; i++)
            {
                contours.Add(Circle(22f + i * 8f, 13f, 2.5f));
            }

            return Well(contours);
        }

        /// <summary>An emissive mesh: a cube seen from above its front.</summary>
        public static VisualElement BuildEmissiveMesh()
        {
            return Well(new List<Vector2[]>
            {
                Box(21f, 9f, 35f, 23f),
                new[] { new Vector2(21f, 9f), new Vector2(27f, 3f), new Vector2(41f, 3f), new Vector2(35f, 9f) },
                new[] { new Vector2(41f, 3f), new Vector2(41f, 17f), new Vector2(35f, 23f) },
            });
        }

        private static VisualElement Well(List<Vector2[]> contours)
        {
            var well = new VisualElement();
            well.AddToClassList("alps-add__thumb");

            var icon = new AlpsVectorIcon { StrokeWidth = 1.5f, IconColor = AlpsEffectThumbnail.Art };
            icon.SetViewBox(new Vector2(Width, Height));
            icon.SetContours(contours);
            icon.style.width = Width;
            icon.style.height = Height;
            well.Add(icon);
            return well;
        }

        private static List<Vector2[]> Contours(AlpsVrslPrefab fixture)
        {
            var contours = new List<Vector2[]>();
            if (fixture == AlpsCreateMenu.MoverSpotlight || fixture == AlpsCreateMenu.MoverWashLight)
            {
                // A head with its beam: the wash light's opens wide.
                var spread = fixture == AlpsCreateMenu.MoverWashLight ? 11f : 3f;
                contours.Add(Box(26f, 17f, 34f, 23f));
                contours.Add(new[] { new Vector2(28f, 17f), new Vector2(28f - spread, 3f) });
                contours.Add(new[] { new Vector2(32f, 17f), new Vector2(32f + spread, 3f) });
            }
            else if (fixture == AlpsCreateMenu.ParLight)
            {
                contours.Add(Circle(30f, 13f, 9f));
                contours.Add(Circle(30f, 13f, 4f));
            }
            else if (fixture == AlpsCreateMenu.Blinder)
            {
                foreach (var x in new[] { 24f, 36f })
                {
                    foreach (var y in new[] { 7f, 19f })
                    {
                        contours.Add(Circle(x, y, 4.5f));
                    }
                }
            }
            else if (fixture == AlpsCreateMenu.LightBar)
            {
                contours.Add(Box(9f, 8f, 51f, 18f));
                for (var i = 0; i < 5; i++)
                {
                    contours.Add(Circle(15f + i * 7.5f, 13f, 2f));
                }
            }
            else if (fixture == AlpsCreateMenu.MultiLightBar)
            {
                contours.Add(Box(9f, 8f, 51f, 18f));
                for (var i = 1; i < 7; i++)
                {
                    var x = 9f + i * 6f;
                    contours.Add(new[] { new Vector2(x, 8f), new Vector2(x, 18f) });
                }
            }
            else if (fixture == AlpsCreateMenu.Laser)
            {
                for (var i = 0; i < 7; i++)
                {
                    contours.Add(new[] { new Vector2(30f, 23f), new Vector2(12f + i * 6f, 3f) });
                }
            }
            else if (fixture == AlpsCreateMenu.Flasher)
            {
                // A short housing round one long lamp.
                contours.Add(Box(16f, 7f, 44f, 19f));
                contours.Add(Box(19f, 11f, 41f, 15f));
            }
            else if (fixture == AlpsCreateMenu.StrobePanel)
            {
                // Six cells across and four down.
                contours.Add(Box(14f, 3f, 46f, 23f));
                for (var x = 0; x < 6; x++)
                {
                    for (var y = 0; y < 4; y++)
                    {
                        contours.Add(Box(16.5f + x * 4.75f, 5f + y * 4.25f, 19.5f + x * 4.75f, 8f + y * 4.25f));
                    }
                }
            }
            else if (fixture == AlpsCreateMenu.MirrorBall)
            {
                // A ball on its line, its facets in rings, and a few flecks it throws.
                contours.Add(new[] { new Vector2(30f, 0f), new Vector2(30f, 5f) });
                contours.Add(Circle(30f, 14f, 9f));
                contours.Add(new[] { new Vector2(21.5f, 11f), new Vector2(38.5f, 11f) });
                contours.Add(new[] { new Vector2(21.5f, 17f), new Vector2(38.5f, 17f) });
                contours.Add(new[] { new Vector2(30f, 5f), new Vector2(26f, 14f), new Vector2(30f, 23f) });
                contours.Add(new[] { new Vector2(30f, 5f), new Vector2(34f, 14f), new Vector2(30f, 23f) });
                foreach (var fleck in new[] { new Vector2(10f, 6f), new Vector2(14f, 20f), new Vector2(47f, 8f), new Vector2(50f, 19f) })
                {
                    contours.Add(Circle(fleck.x, fleck.y, 1f));
                }
            }

            return contours;
        }

        /// <summary>A closed rectangle as a polyline back to its start.</summary>
        private static Vector2[] Box(float left, float top, float right, float bottom)
        {
            return new[]
            {
                new Vector2(left, top), new Vector2(right, top), new Vector2(right, bottom),
                new Vector2(left, bottom), new Vector2(left, top),
            };
        }

        private static Vector2[] Circle(float x, float y, float radius)
        {
            const int segments = 20;
            var points = new Vector2[segments + 1];
            for (var i = 0; i <= segments; i++)
            {
                var angle = i * 2f * Mathf.PI / segments;
                points[i] = new Vector2(x + Mathf.Cos(angle) * radius, y + Mathf.Sin(angle) * radius);
            }

            return points;
        }
    }
}
