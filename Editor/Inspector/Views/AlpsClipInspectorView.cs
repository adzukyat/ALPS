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
        private readonly VisualElement _effectContainer;
        private AlpsPhaseSettingsView _phaseView;
        private readonly AlpsEffectCard _phaseCard;
        private readonly AlpsAddEffectCatalog _catalog;
        private readonly List<AlpsEffectView> _effectViews = new List<AlpsEffectView>();

        public AlpsClipInspectorView(AlpsClipEffectSet set)
        {
            _set = set;
            AddToClassList("alps-root");

            var styleSheet = AlpsStyleSheets.Load(StyleSheetName);
            if (styleSheet != null)
            {
                styleSheets.Add(styleSheet);
            }

            // --- Order ---------------------------------------------------
            var order = new AlpsSegmentedControl("並び順", "通常", "逆順", "左右対称", "ランダム");
            order.SetValueWithoutNotify((int)set.order);
            order.RegisterValueChangedCallback(evt =>
            {
                set.order = (AlpsOrderMode)evt.newValue;
                RaiseChanged();
            });
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
                set.Add(kind);
                RebuildEffects();
                _catalog.Rebuild();
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

        private void RaiseChanged()
        {
            Changed?.Invoke();
        }

        private void BuildPhaseView()
        {
            _phaseView?.RemoveFromHierarchy();
            _phaseView = new AlpsPhaseSettingsView(_set.phase, () =>
            {
                RefreshEffects();
                RaiseChanged();
            });
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
                        RaiseChanged();
                    },
                    onDelete: () =>
                    {
                        _set.Remove(index);
                        RebuildEffects();
                        _catalog.Rebuild();
                        RaiseChanged();
                    });

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
