using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>
    /// The grey header + panel body every effect (and the shared settings) sits in: disclosure arrow,
    /// title, description, and the copy / delete actions on the right of the header.
    /// Collapsed cards show a row of mini chips summarising their palette.
    /// </summary>
    public class AlpsEffectCard : VisualElement
    {
        private readonly VisualElement _header;
        private readonly Label _arrow;
        private readonly Label _title;
        private readonly Label _description;
        private readonly VisualElement _chips;
        private readonly VisualElement _actions;
        private readonly VisualElement _pasteAction;

        private bool _expanded = true;

        public AlpsEffectCard(string title, string description, bool showActions, bool showDelete = true)
        {
            AddToClassList("alps-card");

            _header = new VisualElement();
            _header.AddToClassList("alps-card__header");

            var headerLine = new VisualElement();
            headerLine.AddToClassList("alps-card__headerline");

            _arrow = new Label("▼");
            _arrow.AddToClassList("alps-card__arrow");

            _title = new Label(title);
            _title.AddToClassList("alps-card__title");

            _chips = new VisualElement();
            _chips.AddToClassList("alps-card__chips");

            _actions = new VisualElement();
            _actions.AddToClassList("alps-card__actions");

            headerLine.Add(_arrow);
            headerLine.Add(_title);
            headerLine.Add(_chips);
            headerLine.Add(_actions);

            _description = new Label(description);
            _description.AddToClassList("alps-card__desc");

            _header.Add(headerLine);
            _header.Add(_description);
            Add(_header);

            Body = new VisualElement();
            Body.AddToClassList("alps-card__body");
            Body.AddToClassList("alps-flow9");
            Add(Body);

            if (showActions)
            {
                _pasteAction = BuildAction(AlpsIcons.Paste, "パラメーターを貼り付け",
                    new Color(0.918f, 0.918f, 0.918f), RequestPaste);
                _pasteAction.style.display = DisplayStyle.None;
                _actions.Add(_pasteAction);

                _actions.Add(BuildAction(AlpsIcons.Copy, "パラメーターをコピー",
                    new Color(0.918f, 0.918f, 0.918f), RequestCopy));
                if (showDelete)
                {
                    _actions.Add(BuildAction(AlpsIcons.Trash, "削除", new Color(0.910f, 0.647f, 0.647f),
                        () => DeleteRequested?.Invoke()));
                }
            }

            // The whole header toggles, including the title, description, and the empty
            // space around them. Only the action icons are excluded.
            _header.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.target is VisualElement element && element.GetFirstAncestorOfType<Button>() == null &&
                    !IsInsideActions(element))
                {
                    Expanded = !Expanded;
                }
            });

            Expanded = true;
        }

        public VisualElement Body { get; }

        public bool Expanded
        {
            get => _expanded;
            set
            {
                _expanded = value;
                _arrow.text = value ? "▼" : "▶";
                Body.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
                _header.EnableInClassList("alps-card__header--collapsed", !value);
                _chips.style.display = value ? DisplayStyle.None : DisplayStyle.Flex;
                ExpandedChanged?.Invoke(value);
            }
        }

        public string Title
        {
            get => _title.text;
            set => _title.text = value;
        }

        public event Action CopyRequested;
        public event Action PasteRequested;
        public event Action DeleteRequested;
        public event Action<bool> ExpandedChanged;

        /// <summary>Paste only exists while the clipboard holds a compatible payload.</summary>
        public bool PasteAvailable { get; private set; }

        public void SetPasteAvailable(bool available)
        {
            PasteAvailable = available;
            if (_pasteAction != null)
            {
                _pasteAction.style.display = available ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        /// <summary>Runs the copy action, the same entry point the header icon uses.</summary>
        public void RequestCopy()
        {
            CopyRequested?.Invoke();
        }

        /// <summary>Runs the paste action. Does nothing while nothing compatible is copied.</summary>
        public void RequestPaste()
        {
            if (PasteAvailable)
            {
                PasteRequested?.Invoke();
            }
        }

        /// <summary>Mini palette chips shown while the card is collapsed.</summary>
        public void SetChips(IEnumerable<Color> colors)
        {
            _chips.Clear();
            if (colors == null)
            {
                return;
            }

            foreach (var color in colors)
            {
                var chip = new VisualElement();
                chip.AddToClassList("alps-card__chip");
                chip.style.backgroundColor = color;
                _chips.Add(chip);
            }
        }

        private bool IsInsideActions(VisualElement element)
        {
            for (var current = element; current != null; current = current.parent)
            {
                if (current == _actions)
                {
                    return true;
                }
            }

            return false;
        }

        private static AlpsVectorIcon BuildAction(string[] paths, string tooltip, Color color, Action onClick)
        {
            var icon = new AlpsVectorIcon { tooltip = tooltip, IconColor = color, StrokeWidth = 2f };
            icon.SetPaths(paths);
            icon.AddToClassList("alps-iconbutton");
            icon.pickingMode = PickingMode.Position;
            icon.RegisterCallback<PointerDownEvent>(evt =>
            {
                onClick();
                evt.StopPropagation();
            });
            return icon;
        }
    }
}
