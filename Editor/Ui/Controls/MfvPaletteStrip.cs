using System;
using System.Collections.Generic;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace ManeuverForVRC.Ui.Editor
{
    /// <summary>
    /// Shared behaviour for the color and gobo palettes: a strip of swatches that is
    /// itself the range, followed by + (add a stop) and a trash tile (delete the selected one).
    /// A palette may be empty, which is how an effect is OFF.
    ///
    /// A swatch is selected by clicking it and reordered by dragging it. Both gestures
    /// share one pointer sequence, so nothing is rebuilt until the pointer is released.
    /// Rebuilding on press would detach the very element holding the pointer capture.
    /// </summary>
    public abstract class MfvPaletteStrip<TStop> : VisualElement where TStop : class
    {
        private const float DragThreshold = 4f;

        private readonly List<TStop> _stops;
        private readonly Func<int> _getSelected;
        private readonly Action<int> _setSelected;
        private readonly List<VisualElement> _swatches = new List<VisualElement>();

        private VisualElement _pressed;
        private int _pressedIndex = -1;
        private Vector2 _pressStart;
        private bool _dragging;

        protected MfvPaletteStrip(
            string label,
            List<TStop> stops,
            Func<int> getSelected,
            Action<int> setSelected)
        {
            _stops = stops;
            _getSelected = getSelected;
            _setSelected = setSelected;

            AddToClassList("mfv-palette-strip");

            var header = new VisualElement();
            header.AddToClassList("mfv-row");
            header.AddToClassList("mfv-row--top");

            var labelElement = new Label(label);
            labelElement.AddToClassList("mfv-row__label");
            header.Add(labelElement);

            Strip = new VisualElement();
            Strip.AddToClassList("mfv-palette");
            header.Add(Strip);
            Add(header);

            Editor = new VisualElement();
            Editor.AddToClassList("mfv-palette__editor");
            Add(Editor);

            Extras = new VisualElement();
            Extras.AddToClassList("mfv-palette__extras");
            Add(Extras);
        }

        protected VisualElement Strip { get; }

        /// <summary>The row under the strip that edits whichever stop is selected.</summary>
        protected VisualElement Editor { get; }

        /// <summary>Below the editor, for anything a palette adds of its own (the gobo picker).</summary>
        protected VisualElement Extras { get; }

        protected IReadOnlyList<TStop> Stops => _stops;

        /// <summary>Raised whenever the palette contents or selection change.</summary>
        public event Action Changed;

        public int SelectedIndex => _stops.Count == 0 ? -1 : Mathf.Clamp(_getSelected(), 0, _stops.Count - 1);

        /// <summary>Whether delete applies. Anything selected can go, down to an empty palette.</summary>
        public bool CanDelete => SelectedIndex >= 0;

        /// <summary>The swatch's contents for one stop. Selection and drag are added by the base.</summary>
        protected abstract VisualElement CreateSwatch(TStop stop);

        /// <summary>A deep copy, so a duplicated stop can be edited independently.</summary>
        protected abstract TStop CloneStop(TStop stop);

        /// <summary>What + adds by default.</summary>
        protected abstract TStop CreateDefaultStop();

        protected virtual string AddTooltip => "要素を追加";

        /// <summary>Shows + as pressed, for a palette whose + opens something.</summary>
        protected virtual bool AddActive => false;

        /// <summary>+ adds <see cref="CreateDefaultStop"/> unless a palette offers a choice.</summary>
        protected virtual void OnAddRequested()
        {
            AddStop(CreateDefaultStop());
        }

        /// <summary>Fills <see cref="Extras"/>. The default adds nothing.</summary>
        protected virtual void BuildExtras()
        {
        }

        /// <summary>Fills <see cref="Editor"/> for the selected stop. The default has no editor.</summary>
        protected virtual void BuildEditor(TStop selected)
        {
        }

        /// <summary>Called before swatches are recreated, for resources tied to the old ones.</summary>
        protected virtual void OnRebuilding()
        {
        }

        public void Rebuild()
        {
            RefreshSwatches();

            Editor.Clear();
            Editor.style.display = DisplayStyle.None;
            if (SelectedIndex >= 0)
            {
                BuildEditor(_stops[SelectedIndex]);
            }

            Extras.Clear();
            BuildExtras();
        }

        /// <summary>
        /// Redraws the strip only. Value edits must use this instead of <see cref="Rebuild"/>:
        /// Unity's colour and gradient pickers are separate windows bound to the field that
        /// opened them, and once that field is replaced they keep editing a detached copy
        /// whose changes never arrive.
        /// </summary>
        protected void RefreshSwatches()
        {
            EndPress();
            Strip.Clear();
            _swatches.Clear();
            OnRebuilding();

            var selected = SelectedIndex;
            for (var i = 0; i < _stops.Count; i++)
            {
                var swatch = CreateSwatch(_stops[i]);
                swatch.AddToClassList("mfv-swatch");
                swatch.EnableInClassList("mfv-swatch--selected", i == selected);
                RegisterGestures(swatch, i);
                _swatches.Add(swatch);
                Strip.Add(swatch);
            }

            Strip.Add(CreateAddTile());
            Strip.Add(CreateDeleteTile());
        }

        public void Select(int index)
        {
            if (index < 0 || index >= _stops.Count)
            {
                return;
            }

            _setSelected(index);
            Rebuild();
            RaiseChanged();
        }

        /// <summary>What pressing + does.</summary>
        public void RequestAdd()
        {
            OnAddRequested();
        }

        /// <summary>Inserts a stop right after the selection (or first, when empty) and selects it.</summary>
        public void AddStop(TStop stop)
        {
            var index = SelectedIndex + 1;
            _stops.Insert(index, stop);
            _setSelected(index);
            Rebuild();
            RaiseChanged();
        }

        /// <summary>Duplicate: a copy of the selected stop, inserted right after it and selected.</summary>
        public void DuplicateSelected()
        {
            var index = SelectedIndex;
            if (index >= 0)
            {
                AddStop(CloneStop(_stops[index]));
            }
        }

        /// <summary>Delete: removes the selected stop, and the selection moves to its neighbour.</summary>
        public void DeleteSelected()
        {
            if (!CanDelete)
            {
                return;
            }

            DeleteAt(SelectedIndex);
        }

        /// <summary>Moves a stop so it ends up at <paramref name="to"/>, and the selection follows it.</summary>
        public void Reorder(int from, int to)
        {
            if (from < 0 || from >= _stops.Count)
            {
                return;
            }

            to = Mathf.Clamp(to, 0, _stops.Count - 1);
            if (from != to)
            {
                var stop = _stops[from];
                _stops.RemoveAt(from);
                _stops.Insert(to, stop);
            }

            _setSelected(to);
            Rebuild();
            RaiseChanged();
        }

        /// <summary>
        /// Where a dragged swatch belongs among the others, given their bounds in reading
        /// order (the strip wraps, so rows are compared before x).
        /// </summary>
        public static int SlotAt(IReadOnlyList<Rect> others, Vector2 pointer)
        {
            var slot = 0;
            for (var i = 0; i < others.Count; i++)
            {
                var rect = others[i];
                if (pointer.y < rect.yMin)
                {
                    break;
                }

                if (pointer.y > rect.yMax || pointer.x > rect.center.x)
                {
                    slot = i + 1;
                    continue;
                }

                break;
            }

            return slot;
        }

        protected void RaiseChanged()
        {
            Changed?.Invoke();
        }

        /// <summary>The fixed-width spacer that lines the editor row up with the controls.</summary>
        protected static VisualElement CreateEditorGutter()
        {
            var spacer = new Label(string.Empty);
            spacer.AddToClassList("mfv-row__label");
            return spacer;
        }

        private void DeleteAt(int index)
        {
            if (index < 0 || index >= _stops.Count)
            {
                return;
            }

            _stops.RemoveAt(index);
            _setSelected(_stops.Count == 0 ? -1 : Mathf.Clamp(index, 0, _stops.Count - 1));
            Rebuild();
            RaiseChanged();
        }

        private VisualElement CreateAddTile()
        {
            var tile = new Label("＋") { tooltip = AddTooltip };
            tile.AddToClassList("mfv-swatch");
            tile.AddToClassList("mfv-swatch--action");
            tile.EnableInClassList("mfv-swatch--active", AddActive);
            tile.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button == 0)
                {
                    RequestAdd();
                }
            });
            return tile;
        }

        private VisualElement CreateDeleteTile()
        {
            var tile = new VisualElement
            {
                tooltip = CanDelete ? "選択中の要素を削除" : "要素がありません",
            };
            tile.AddToClassList("mfv-swatch");
            tile.AddToClassList("mfv-swatch--action");
            tile.EnableInClassList("mfv-swatch--disabled", !CanDelete);

            var icon = new MfvVectorIcon
            {
                IconColor = new Color(0.910f, 0.647f, 0.647f),
                StrokeWidth = 2f,
                pickingMode = PickingMode.Ignore,
            };
            icon.SetPaths(MfvIcons.Trash);
            icon.AddToClassList("mfv-swatch__icon");
            tile.Add(icon);

            tile.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button == 0)
                {
                    DeleteSelected();
                }
            });
            return tile;
        }

        private void RegisterGestures(VisualElement swatch, int index)
        {
            swatch.AddManipulator(new ContextualMenuManipulator(evt =>
            {
                evt.menu.AppendAction(
                    "複製",
                    _ =>
                    {
                        _setSelected(index);
                        DuplicateSelected();
                    });
                evt.menu.AppendAction("削除", _ => DeleteAt(index));
            }));

            swatch.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0)
                {
                    return;
                }

                _pressed = swatch;
                _pressedIndex = index;
                _pressStart = evt.position;
                _dragging = false;

                // Show the selection immediately, but commit it on release.
                foreach (var other in _swatches)
                {
                    other.EnableInClassList("mfv-swatch--selected", other == swatch);
                }

                swatch.CapturePointer(evt.pointerId);
                evt.StopPropagation();
            });

            swatch.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (_pressed != swatch || !swatch.HasPointerCapture(evt.pointerId))
                {
                    return;
                }

                var position = (Vector2)evt.position;
                if (!_dragging)
                {
                    if ((position - _pressStart).sqrMagnitude < DragThreshold * DragThreshold)
                    {
                        return;
                    }

                    _dragging = true;
                    swatch.AddToClassList("mfv-swatch--dragging");
                }

                // Live reorder: the swatch moves through the strip as it is dragged.
                var others = new List<Rect>(_swatches.Count);
                var ordered = new List<VisualElement>(_swatches.Count);
                foreach (var child in Strip.Children())
                {
                    if (child != swatch && _swatches.Contains(child))
                    {
                        ordered.Add(child);
                        others.Add(child.worldBound);
                    }
                }

                var slot = SlotAt(others, position);
                if (Strip.IndexOf(swatch) != slot)
                {
                    Strip.Insert(slot, swatch);
                }

                evt.StopPropagation();
            });

            swatch.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (_pressed != swatch || evt.button != 0)
                {
                    return;
                }

                var from = _pressedIndex;
                var to = Strip.IndexOf(swatch);
                var dragged = _dragging;

                // Clear the press first: releasing capture raises PointerCaptureOut,
                // which would otherwise treat this as an aborted gesture.
                EndPress();
                swatch.ReleasePointer(evt.pointerId);
                evt.StopPropagation();

                if (dragged)
                {
                    Reorder(from, to);
                }
                else
                {
                    Select(from);
                }
            });

            // Capture lost mid-gesture (window lost focus, etc.): put everything back.
            swatch.RegisterCallback<PointerCaptureOutEvent>(_ =>
            {
                if (_pressed == swatch)
                {
                    EndPress();
                    Rebuild();
                }
            });
        }

        private void EndPress()
        {
            _pressed?.RemoveFromClassList("mfv-swatch--dragging");
            _pressed = null;
            _pressedIndex = -1;
            _dragging = false;
        }
    }

    /// <summary>Color palette, where a stop may be a solid colour or a gradient.</summary>
    public class MfvColorPalette : MfvPaletteStrip<MfvColorStop>
    {
        public const string SolidLabel = "単色";
        public const string GradientLabel = "グラデーション";

        /// <summary>
        /// Gradient swatches are backed by a texture built per rebuild. They are not
        /// assets, so nothing else will ever collect them.
        /// </summary>
        private readonly List<Texture2D> _previews = new List<Texture2D>();

        public MfvColorPalette(
            string label,
            List<MfvColorStop> stops,
            Func<int> getSelected,
            Action<int> setSelected)
            : base(label, stops, getSelected, setSelected)
        {
            RegisterCallback<DetachFromPanelEvent>(_ => ReleasePreviews());
            Rebuild();
        }

        /// <summary>What the type dropdown does: turn the selected stop into a solid or a gradient.</summary>
        public void ChangeSelectedStopType(bool gradient)
        {
            var index = SelectedIndex;
            if (index < 0 || Stops[index].isGradient == gradient)
            {
                return;
            }

            Stops[index].isGradient = gradient;
            Rebuild();
            RaiseChanged();
        }

        protected override VisualElement CreateSwatch(MfvColorStop stop)
        {
            var swatch = new VisualElement { tooltip = stop.isGradient ? GradientLabel : SolidLabel };
            if (stop.isGradient)
            {
                var preview = MfvGradientPreview.Build(stop.gradient);
                _previews.Add(preview);
                swatch.style.backgroundImage = new StyleBackground(preview);
            }
            else
            {
                swatch.style.backgroundColor = stop.color;
            }

            return swatch;
        }

        protected override MfvColorStop CloneStop(MfvColorStop stop)
        {
            return new MfvColorStop(stop);
        }

        protected override MfvColorStop CreateDefaultStop()
        {
            return new MfvColorStop(Color.white);
        }

        protected override string AddTooltip => "白の単色を追加";

        protected override void OnRebuilding()
        {
            ReleasePreviews();
        }

        /// <summary>The type dropdown (solid | gradient) plus the picker for whichever is chosen.</summary>
        protected override void BuildEditor(MfvColorStop stop)
        {
            Editor.style.display = DisplayStyle.Flex;
            Editor.Add(CreateEditorGutter());

            var type = new DropdownField(new List<string> { SolidLabel, GradientLabel }, stop.isGradient ? 1 : 0)
            {
                tooltip = "このパレット項目の種類",
            };
            type.AddToClassList("mfv-palette__type");
            type.RegisterValueChangedCallback(evt => ChangeSelectedStopType(evt.newValue == GradientLabel));
            Editor.Add(type);

            if (stop.isGradient)
            {
                var field = new GradientField { value = stop.gradient };
                field.AddToClassList("mfv-palette__value");
                field.RegisterValueChangedCallback(evt =>
                {
                    stop.gradient = evt.newValue;
                    RefreshSwatches();
                    RaiseChanged();
                });
                Editor.Add(field);
            }
            else
            {
                var field = new ColorField { value = stop.color, showAlpha = false, showEyeDropper = false };
                field.AddToClassList("mfv-palette__value");
                field.RegisterValueChangedCallback(evt =>
                {
                    stop.color = evt.newValue;
                    RefreshSwatches();
                    RaiseChanged();
                });
                Editor.Add(field);
            }
        }

        private void ReleasePreviews()
        {
            foreach (var preview in _previews)
            {
                if (preview != null)
                {
                    UnityEngine.Object.DestroyImmediate(preview);
                }
            }

            _previews.Clear();
        }
    }

    /// <summary>
    /// Gobo palette, where the OFF tile is a stop with no texture. + opens a picker of
    /// OFF and every gobo VRSL ships with. It stays open so a sequence can be built by
    /// clicking tiles one after another.
    /// </summary>
    public class MfvGoboPalette : MfvPaletteStrip<MfvGoboStop>
    {
        private readonly Func<bool> _getPickerOpen;
        private readonly Action<bool> _setPickerOpen;
        private List<MfvGoboStop> _catalog;

        public MfvGoboPalette(
            string label,
            List<MfvGoboStop> stops,
            Func<int> getSelected,
            Action<int> setSelected,
            Func<bool> getPickerOpen,
            Action<bool> setPickerOpen)
            : base(label, stops, getSelected, setSelected)
        {
            _getPickerOpen = getPickerOpen;
            _setPickerOpen = setPickerOpen;
            Rebuild();
        }

        public bool PickerOpen => _getPickerOpen != null && _getPickerOpen();

        protected override string AddTooltip => PickerOpen ? "ゴボの一覧を閉じる" : "ゴボを追加";

        protected override bool AddActive => PickerOpen;

        /// <summary>OFF followed by the built-in gobos, loaded once per palette.</summary>
        private List<MfvGoboStop> Catalog
        {
            get
            {
                if (_catalog == null)
                {
                    _catalog = new List<MfvGoboStop> { new MfvGoboStop() };
                    foreach (var texture in MfvGoboLibrary.LoadBuiltIn())
                    {
                        _catalog.Add(new MfvGoboStop(texture));
                    }
                }

                return _catalog;
            }
        }

        protected override void OnAddRequested()
        {
            // Opening the picker is not an edit, so nothing is reported as changed.
            _setPickerOpen(!PickerOpen);
            Rebuild();
        }

        protected override void BuildExtras()
        {
            if (!PickerOpen)
            {
                return;
            }

            var row = new VisualElement();
            row.AddToClassList("mfv-row");
            row.AddToClassList("mfv-row--top");
            row.Add(CreateEditorGutter());

            var picker = new VisualElement();
            picker.AddToClassList("mfv-palette");
            picker.AddToClassList("mfv-gobo-picker");
            foreach (var entry in Catalog)
            {
                var tile = CreateSwatch(entry);
                tile.AddToClassList("mfv-swatch");
                tile.tooltip = entry.IsOff ? "OFF を追加" : entry.texture.name + " を追加";
                tile.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button == 0)
                    {
                        AddStop(new MfvGoboStop(entry));
                        evt.StopPropagation();
                    }
                });
                picker.Add(tile);
            }

            row.Add(picker);
            Extras.Add(row);
        }

        /// <summary>The tiles in the + picker, in order.</summary>
        public IReadOnlyList<MfvGoboStop> CatalogStops => Catalog;

        protected override VisualElement CreateSwatch(MfvGoboStop stop)
        {
            var swatch = new VisualElement();
            swatch.AddToClassList("mfv-swatch--gobo");

            if (stop.IsOff)
            {
                swatch.AddToClassList("mfv-swatch--off");
                swatch.Add(new Label("OFF") { pickingMode = PickingMode.Ignore });
            }
            else
            {
                swatch.tooltip = stop.texture.name;
                var image = new Image
                {
                    image = stop.texture,
                    scaleMode = ScaleMode.ScaleToFit,
                    pickingMode = PickingMode.Ignore,
                };
                image.AddToClassList("mfv-swatch__image");
                swatch.Add(image);
            }

            return swatch;
        }

        protected override MfvGoboStop CloneStop(MfvGoboStop stop)
        {
            return new MfvGoboStop(stop);
        }

        protected override MfvGoboStop CreateDefaultStop()
        {
            return new MfvGoboStop();
        }
    }

    /// <summary>Renders a Gradient into a small texture so a swatch can show it.</summary>
    internal static class MfvGradientPreview
    {
        private const int Width = 32;

        public static Texture2D Build(Gradient gradient)
        {
            var texture = new Texture2D(Width, 1, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            for (var x = 0; x < Width; x++)
            {
                var t = x / (float)(Width - 1);
                texture.SetPixel(x, 0, gradient?.Evaluate(t) ?? Color.white);
            }

            texture.Apply();
            return texture;
        }
    }
}
