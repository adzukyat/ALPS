using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>
    /// The whole clip inspector: order, shared settings, the effect stack,
    /// add effect and profile.
    ///
    /// The view owns no state of its own. It renders a <see cref="AlpsClipEffectSet"/> and
    /// reports every edit through <see cref="Changed"/>. The host owns undo and fills
    /// <see cref="ProfileBody"/>, since profiles live outside the effect set.
    /// </summary>
    public class AlpsClipInspectorView : VisualElement
    {
        public const string StyleSheetName = "AlpsInspector";

        private readonly AlpsClipEffectSet _set;
        private readonly AlpsMixedValues _mixed;
        private readonly VisualElement _effectContainer;
        private AlpsPhaseSettingsView _phaseView;
        private readonly AlpsEffectCard _phaseCard;
        private readonly AlpsAddEffectCatalog _catalog;
        private readonly List<AlpsEffectView> _effectViews = new List<AlpsEffectView>();

        /// <param name="mixed">Which values differ between the selected clips. Null for a single clip.</param>
        public AlpsClipInspectorView(AlpsClipEffectSet set, AlpsMixedValues mixed = null)
        {
            _set = set;
            _mixed = mixed;
            AddToClassList("alps-root");

            var styleSheet = AlpsStyleSheets.Load(StyleSheetName);
            if (styleSheet != null)
            {
                styleSheets.Add(styleSheet);
            }

            AlpsInspectorFont.Apply(this);

            // --- Order ---------------------------------------------------
            var order = new AlpsSegmentedControl("並び順", "通常", "逆順", "左右対称", "ランダム");
            order.SetValueWithoutNotify((int)set.order);
            order.RegisterValueChangedCallback(evt =>
            {
                set.order = (AlpsOrderMode)evt.newValue;
                RaiseChanged();
            });
            mixed?.Bind(order, set, nameof(AlpsClipEffectSet.order));
            Add(order);

            // --- Shared settings -----------------------------------------
            _phaseCard = new AlpsEffectCard(
                "共通設定",
                "Rangeとパレットの動作を調整できます。",
                showActions: true,
                showDelete: false);
            _phaseCard.ExpandedChanged += expanded =>
            {
                set.phaseExpanded = expanded;
                RaiseChanged();
            };

            _phaseCard.CopyRequested += () => AlpsEffectClipboard.CopyPhase(_set.phase);
            _phaseCard.PasteRequested += PastePhase;
            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                AlpsEffectClipboard.Changed += RefreshPhasePasteAvailability;
                RefreshPhasePasteAvailability();
            });
            RegisterCallback<DetachFromPanelEvent>(_ =>
                AlpsEffectClipboard.Changed -= RefreshPhasePasteAvailability);

            BuildPhaseView();
            _phaseCard.Expanded = set.phaseExpanded;
            RefreshPhasePasteAvailability();
            Add(_phaseCard);

            var divider = new VisualElement();
            divider.AddToClassList("alps-divider");
            Add(divider);

            // --- Effects -------------------------------------------------
            _effectContainer = new VisualElement();
            _effectContainer.AddToClassList("alps-stack");
            Add(_effectContainer);

            // --- Add effect ----------------------------------------------
            _catalog = new AlpsAddEffectCatalog(set, kind =>
            {
                // Another clip repeats the add only where it means the same card, so a
                // clip that already has the kind is not split into even and odd.
                var label = set.GetAddLabel(kind);
                set.Add(kind);
                RebuildEffects();
                _catalog.Rebuild();
                RaiseStructureEdited(other =>
                {
                    if (other.GetAddLabel(kind) == label)
                    {
                        other.Add(kind);
                    }
                });
                RaiseChanged();
            });
            Add(_catalog);

            // --- Profile -------------------------------------------------
            var profile = new VisualElement();
            profile.AddToClassList("alps-plainfold");
            var profileArrow = new Label(set.profileExpanded ? "▼" : "▶");
            profileArrow.AddToClassList("alps-card__arrow");
            var profileTitle = new Label("プロファイル");
            profileTitle.AddToClassList("alps-card__title");
            profile.Add(profileArrow);
            profile.Add(profileTitle);
            ProfileBody = new VisualElement();
            ProfileBody.AddToClassList("alps-profile");
            ProfileBody.style.display = set.profileExpanded ? DisplayStyle.Flex : DisplayStyle.None;
            profile.RegisterCallback<PointerDownEvent>(_ =>
            {
                set.profileExpanded = !set.profileExpanded;
                profileArrow.text = set.profileExpanded ? "▼" : "▶";
                ProfileBody.style.display = set.profileExpanded ? DisplayStyle.Flex : DisplayStyle.None;
                RaiseChanged();
            });
            Add(profile);
            Add(ProfileBody);

            RebuildEffects();
        }

        /// <summary>Shown under the profile header while it is expanded. Empty until the host fills it.</summary>
        public VisualElement ProfileBody { get; }

        /// <summary>Raised after any edit so the host can record undo and mark the asset dirty.</summary>
        public event Action Changed;

        /// <summary>
        /// Raised before <see cref="Changed"/> for an edit that adds, removes or replaces a
        /// whole part, as an action that makes the same edit on any effect set. A host
        /// editing several clips runs it on the others, since a field by field diff cannot
        /// tell which half of an even / odd pair was removed.
        /// </summary>
        public event Action<Action<AlpsClipEffectSet>> StructureEdited;

        private void RaiseChanged()
        {
            Changed?.Invoke();
        }

        private void RaiseStructureEdited(Action<AlpsClipEffectSet> edit)
        {
            StructureEdited?.Invoke(edit);
        }

        private void BuildPhaseView()
        {
            _phaseView?.RemoveFromHierarchy();
            _phaseView = new AlpsPhaseSettingsView(_set.phase, () =>
            {
                RefreshEffects();
                RaiseChanged();
            }, mixed: _mixed);
            _phaseCard.Body.Add(_phaseView);
        }

        private void RefreshPhasePasteAvailability()
        {
            _phaseCard.SetPasteAvailable(AlpsEffectClipboard.HasPhasePayload);
        }

        private void PastePhase()
        {
            var pasted = AlpsEffectClipboard.PastePhase();
            if (pasted == null)
            {
                return;
            }

            // Effect views hold the clip phase they were built with, so they are rebuilt
            // along with the shared settings view.
            _set.phase = pasted;
            BuildPhaseView();
            RebuildEffects();
            RaiseStructureEdited(other => other.phase = new AlpsPhaseSettings(pasted));
            RaiseChanged();
        }

        private void RebuildEffects()
        {
            _effectContainer.Clear();
            _effectViews.Clear();

            for (var i = 0; i < _set.effects.Count; i++)
            {
                var index = i;
                var effect = _set.effects[i];
                var kind = effect.kind;
                var parity = effect.parity;

                var view = new AlpsEffectView(
                    effect,
                    _set.phase,
                    RaiseChanged,
                    onPaste: () =>
                    {
                        var pasted = AlpsEffectClipboard.Paste(_set.effects[index]);
                        if (pasted == null)
                        {
                            return;
                        }

                        _set.effects[index] = pasted;
                        RebuildEffects();
                        RaiseStructureEdited(other =>
                        {
                            var at = other.IndexOf(kind, parity);
                            var repeated = at < 0 ? null : AlpsEffectClipboard.Paste(other.effects[at]);
                            if (repeated != null)
                            {
                                other.effects[at] = repeated;
                            }
                        });
                        RaiseChanged();
                    },
                    onDelete: () =>
                    {
                        _set.Remove(index);
                        RebuildEffects();
                        _catalog.Rebuild();
                        RaiseStructureEdited(other => other.Remove(other.IndexOf(kind, parity)));
                        RaiseChanged();
                    },
                    _mixed);

                _effectContainer.Add(view);
                _effectViews.Add(view);
            }
        }

        private void RefreshEffects()
        {
            foreach (var view in _effectViews)
            {
                view.Refresh();
            }
        }
    }
}
