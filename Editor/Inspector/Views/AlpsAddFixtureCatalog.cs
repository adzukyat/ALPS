using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>
    /// Add fixture: the container inspector's counterpart of the clip's add effect list. A
    /// centred button reveals a thumbnail grid of the VR Stage Lighting fixtures the
    /// Hierarchy's ALPS menu offers, and picking one adds it under the container. The grid
    /// starts open while the container holds no fixture, since that is the next step there.
    /// </summary>
    public class AlpsAddFixtureCatalog : VisualElement
    {
        private readonly AlpsContainer _container;
        private readonly Action<GameObject> _onAdded;
        private readonly VisualElement _button;
        private readonly VisualElement _list;

        public AlpsAddFixtureCatalog(AlpsContainer container, Action<GameObject> onAdded)
        {
            _container = container;
            _onAdded = onAdded;
            Expanded = container.Fixtures().Count == 0;
            AddToClassList("alps-add");

            var divider = new VisualElement();
            divider.AddToClassList("alps-divider");
            Add(divider);

            _button = new VisualElement();
            _button.AddToClassList("alps-add__button");
            var title = new Label(AlpsStrings.Tr("addFixture.title"));
            title.AddToClassList("alps-add__title");
            _button.Add(title);
            _button.RegisterCallback<PointerDownEvent>(_ =>
            {
                Expanded = !Expanded;
                Rebuild();
            });
            Add(_button);

            _list = new VisualElement();
            _list.AddToClassList("alps-add__list");
            Add(_list);

            Rebuild();
        }

        /// <summary>True while the grid is open.</summary>
        public bool Expanded { get; private set; }

        /// <summary>Adds <paramref name="fixture"/> under the container, the same entry point a tile uses.</summary>
        public GameObject RequestAdd(AlpsVrslPrefab fixture)
        {
            var created = AlpsCreateMenu.CreateFixture(fixture, _container.gameObject);
            if (created != null)
            {
                _onAdded?.Invoke(created);
            }

            return created;
        }

        private void Rebuild()
        {
            _button.EnableInClassList("alps-add__button--open", Expanded);
            _list.style.display = Expanded ? DisplayStyle.Flex : DisplayStyle.None;
            _list.Clear();
            if (!Expanded)
            {
                return;
            }

            foreach (var fixture in AlpsCreateMenu.Fixtures)
            {
                var captured = fixture;
                var available = fixture.Load() != null;
                var item = new VisualElement { tooltip = available ? string.Empty : AlpsStrings.Tr("addFixture.missing") };
                item.AddToClassList("alps-add__item");
                item.Add(AlpsFixtureThumbnail.Build(fixture));

                var caption = new Label(AlpsStrings.Tr(fixture.LabelKey));
                caption.AddToClassList("alps-add__label");
                item.Add(caption);

                item.SetEnabled(available);
                item.RegisterCallback<PointerDownEvent>(_ => RequestAdd(captured));
                _list.Add(item);
            }
        }
    }

    /// <summary>
    /// The preview well of each fixture tile: amber outlines in the style of the effect
    /// thumbnails, a moving head with a narrow or a wide beam, the face of a par can, the four
    /// cells of a blinder, a bar of lamps, a bar of segments and a fan of laser lines.
    /// </summary>
    internal static class AlpsFixtureThumbnail
    {
        private const float Width = 60f;
        private const float Height = 26f;

        public static VisualElement Build(AlpsVrslPrefab fixture)
        {
            var well = new VisualElement();
            well.AddToClassList("alps-add__thumb");

            var icon = new AlpsVectorIcon { StrokeWidth = 1.5f, IconColor = AlpsEffectThumbnail.Art };
            icon.SetViewBox(new Vector2(Width, Height));
            icon.SetContours(Contours(fixture));
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
