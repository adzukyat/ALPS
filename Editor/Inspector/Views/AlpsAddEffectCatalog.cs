using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>
    /// Add effect: a centred button like the inspector's Add Component, below a divider
    /// once the clip has effects, and
    /// the thumbnail grid it reveals. The button shows as checked while the grid is open.
    ///
    /// An effect already on the clip stays in the list, retitled with an odd suffix. Picking it
    /// is what splits the existing card into even / odd. A kind disappears from the list
    /// only once both halves exist.
    /// </summary>
    public class AlpsAddEffectCatalog : VisualElement
    {
        private readonly AlpsClipEffectSet _set;
        private readonly VisualElement _divider;
        private readonly VisualElement _button;
        private readonly VisualElement _list;

        public AlpsAddEffectCatalog(AlpsClipEffectSet set, Action<AlpsEffectKind> onAdd)
        {
            _set = set;
            OnAdd = onAdd;
            AddToClassList("alps-add");

            _divider = new VisualElement();
            _divider.AddToClassList("alps-divider");
            Add(_divider);

            _button = new VisualElement();
            _button.AddToClassList("alps-add__button");

            var title = new Label("効果を追加");
            title.AddToClassList("alps-add__title");
            _button.Add(title);

            _button.RegisterCallback<PointerDownEvent>(_ =>
            {
                _set.addCatalogExpanded = !_set.addCatalogExpanded;
                Rebuild();
            });

            Add(_button);

            _list = new VisualElement();
            _list.AddToClassList("alps-add__list");
            Add(_list);

            Rebuild();
        }

        private Action<AlpsEffectKind> OnAdd { get; }

        public void Rebuild()
        {
            // With no effects the divider under the shared settings already separates the
            // button, so a second one would sit right below it.
            _divider.style.display = _set.effects.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _button.EnableInClassList("alps-add__button--open", _set.addCatalogExpanded);
            _list.style.display = _set.addCatalogExpanded ? DisplayStyle.Flex : DisplayStyle.None;
            _list.Clear();

            if (!_set.addCatalogExpanded)
            {
                return;
            }

            foreach (var kind in AlpsEffectCatalog.Order)
            {
                var label = _set.GetAddLabel(kind);
                if (label == null)
                {
                    continue;
                }

                var captured = kind;
                var item = new VisualElement { tooltip = AlpsEffectCatalog.GetDescription(kind) };
                item.AddToClassList("alps-add__item");
                item.Add(AlpsEffectThumbnail.Build(kind));

                var caption = new Label(label);
                caption.AddToClassList("alps-add__label");
                item.Add(caption);

                item.RegisterCallback<PointerDownEvent>(_ => OnAdd?.Invoke(captured));
                _list.Add(item);
            }
        }
    }

    /// <summary>
    /// The 26px preview well each catalog tile carries. They are plain amber shapes rather
    /// than line icons, built from elements and painter2D paths.
    /// </summary>
    internal static class AlpsEffectThumbnail
    {
        private static readonly Color Art = new Color(0.851f, 0.643f, 0.255f);

        public static VisualElement Build(AlpsEffectKind kind)
        {
            var well = new VisualElement();
            well.AddToClassList("alps-add__thumb");

            switch (kind)
            {
                case AlpsEffectKind.Move: well.Add(BuildBeams()); break;
                case AlpsEffectKind.Cone: well.Add(BuildCone()); break;
                case AlpsEffectKind.Color: BuildSwatches(well); break;
                case AlpsEffectKind.Brightness: BuildRamp(well); break;
                case AlpsEffectKind.Flicker: BuildBars(well); break;
                case AlpsEffectKind.Gobo: well.Add(BuildGobo()); break;
            }

            return well;
        }

        /// <summary>Move: four beams fanning out of the bottom centre.</summary>
        private static VisualElement BuildBeams()
        {
            var icon = new AlpsVectorIcon { StrokeWidth = 2f, IconColor = Art };
            icon.SetViewBox(new Vector2(60f, 26f));
            icon.SetContours(new[]
            {
                Beam(-13f, -32f), Beam(-4f, -11f), Beam(4f, 11f), Beam(13f, 32f),
            });
            icon.style.width = 60f;
            icon.style.height = 26f;
            return icon;
        }

        private static Vector2[] Beam(float offset, float degrees)
        {
            const float baseY = 23f;
            const float length = 20f;
            var radians = degrees * Mathf.Deg2Rad;
            var origin = new Vector2(30f + offset, baseY);
            return new[]
            {
                origin,
                origin + new Vector2(Mathf.Sin(radians) * length, -Mathf.Cos(radians) * length),
            };
        }

        /// <summary>Cone: the filled 18x18 triangle.</summary>
        private static VisualElement BuildCone()
        {
            var icon = new AlpsVectorIcon("M9 0 L18 18 L0 18 Z", new Vector2(18f, 18f), 0f, fill: true)
            {
                IconColor = Art,
            };
            icon.style.width = 18f;
            icon.style.height = 18f;
            return icon;
        }

        private static void BuildSwatches(VisualElement well)
        {
            well.style.flexDirection = FlexDirection.Row;

            foreach (var color in new[]
                     {
                         new Color32(0xE0, 0x4F, 0x5F, 0xFF),
                         new Color32(0x3F, 0xA9, 0xF5, 0xFF),
                         new Color32(0xB5, 0x6E, 0xE8, 0xFF),
                     })
            {
                var chip = new VisualElement();
                chip.style.width = 8f;
                chip.style.height = 8f;
                chip.style.marginRight = 3f;
                SetRadius(chip, 2f);
                chip.style.backgroundColor = (Color)color;
                well.Add(chip);
            }

            well[well.childCount - 1].style.marginRight = 0f;
        }

        /// <summary>Brightness: a transparent-to-amber ramp, since UI Toolkit has no CSS gradient.</summary>
        private static void BuildRamp(VisualElement well)
        {
            well.style.flexDirection = FlexDirection.Row;

            var bar = new VisualElement();
            bar.style.flexGrow = 1;
            bar.style.height = 8f;
            bar.style.marginLeft = 6f;
            bar.style.marginRight = 6f;
            SetRadius(bar, 2f);
            bar.style.backgroundImage = new StyleBackground(RampTexture());
            well.Add(bar);
        }

        private static Texture2D _ramp;

        private static Texture2D RampTexture()
        {
            if (_ramp != null)
            {
                return _ramp;
            }

            _ramp = new Texture2D(64, 1, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            for (var x = 0; x < _ramp.width; x++)
            {
                var t = x / (float)(_ramp.width - 1);
                _ramp.SetPixel(x, 0, new Color(Art.r, Art.g, Art.b, t));
            }

            _ramp.Apply();
            return _ramp;
        }

        /// <summary>Flicker: five bars of varying heights.</summary>
        private static void BuildBars(VisualElement well)
        {
            well.style.flexDirection = FlexDirection.Row;
            well.style.alignItems = Align.FlexEnd;
            well.style.paddingBottom = 3f;

            foreach (var height in new[] { 10f, 18f, 7f, 14f, 19f })
            {
                var bar = new VisualElement();
                bar.style.width = 3f;
                bar.style.height = height;
                bar.style.marginRight = 2f;
                bar.style.backgroundColor = Art;
                well.Add(bar);
            }

            well[well.childCount - 1].style.marginRight = 0f;
        }

        /// <summary>Gobo: an 18px disc of the actual texture.</summary>
        private static VisualElement BuildGobo()
        {
            var disc = new VisualElement();
            disc.style.width = 18f;
            disc.style.height = 18f;
            SetRadius(disc, 9f);
            disc.style.overflow = Overflow.Hidden;
            disc.style.backgroundColor = new Color(0.047f, 0.047f, 0.055f);

            var texture = AlpsGoboLibrary.Load(4);
            if (texture != null)
            {
                disc.style.backgroundImage = new StyleBackground(texture);
            }

            return disc;
        }

        private static void SetRadius(VisualElement element, float radius)
        {
            element.style.borderTopLeftRadius = radius;
            element.style.borderTopRightRadius = radius;
            element.style.borderBottomLeftRadius = radius;
            element.style.borderBottomRightRadius = radius;
        }
    }
}
