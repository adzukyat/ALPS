using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace ManeuverForVRC.Editor
{
    /// <summary>
    /// The whole clip inspector: order, shared settings, the effect stack,
    /// add effect and profile.
    ///
    /// The view owns no state of its own. It renders a <see cref="MfvClipEffectSet"/> and
    /// reports every edit through <see cref="Changed"/>. The host owns undo and fills
    /// <see cref="ProfileBody"/>, since profiles live outside the effect set.
    /// </summary>
    public class MfvClipInspectorView : VisualElement
    {
        public const string StyleSheetName = "MfvInspector";

        private readonly MfvClipEffectSet _set;
        private readonly VisualElement _effectContainer;
        private readonly MfvPhaseSettingsView _phaseView;
        private readonly MfvEffectCard _phaseCard;
        private readonly MfvAddEffectCatalog _catalog;
        private readonly List<MfvEffectView> _effectViews = new List<MfvEffectView>();

        public MfvClipInspectorView(MfvClipEffectSet set)
        {
            _set = set;
            AddToClassList("mfv-root");

            var styleSheet = MfvStyleSheets.Load(StyleSheetName);
            if (styleSheet != null)
            {
                styleSheets.Add(styleSheet);
            }

            // --- Order ---------------------------------------------------
            var order = new MfvSegmentedControl("並び順", "通常", "逆順", "左右対称", "ランダム");
            order.SetValueWithoutNotify((int)set.order);
            order.RegisterValueChangedCallback(evt =>
            {
                set.order = (MfvOrderMode)evt.newValue;
                RaiseChanged();
            });
            Add(order);

            // --- Shared settings -----------------------------------------
            _phaseCard = new MfvEffectCard(
                "共通設定",
                "Rangeとパレットの動作を調整できます。",
                showActions: false);
            _phaseCard.ExpandedChanged += expanded =>
            {
                set.phaseExpanded = expanded;
                RaiseChanged();
            };

            _phaseView = new MfvPhaseSettingsView(set.phase, () =>
            {
                RefreshEffects();
                RaiseChanged();
            });
            _phaseCard.Body.Add(_phaseView);
            _phaseCard.Expanded = set.phaseExpanded;
            Add(_phaseCard);

            var divider = new VisualElement();
            divider.AddToClassList("mfv-divider");
            Add(divider);

            // --- Effects -------------------------------------------------
            _effectContainer = new VisualElement();
            _effectContainer.AddToClassList("mfv-stack");
            Add(_effectContainer);

            // --- Add effect ----------------------------------------------
            _catalog = new MfvAddEffectCatalog(set, kind =>
            {
                set.Add(kind);
                RebuildEffects();
                _catalog.Rebuild();
                RaiseChanged();
            });
            Add(_catalog);

            // --- Profile -------------------------------------------------
            var profile = new VisualElement();
            profile.AddToClassList("mfv-plainfold");
            var profileArrow = new Label(set.profileExpanded ? "▼" : "▶");
            profileArrow.AddToClassList("mfv-card__arrow");
            var profileTitle = new Label("プロファイル");
            profileTitle.AddToClassList("mfv-card__title");
            profile.Add(profileArrow);
            profile.Add(profileTitle);
            ProfileBody = new VisualElement();
            ProfileBody.AddToClassList("mfv-profile");
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

        private void RebuildEffects()
        {
            _effectContainer.Clear();
            _effectViews.Clear();

            for (var i = 0; i < _set.effects.Count; i++)
            {
                var index = i;
                var effect = _set.effects[i];

                var view = new MfvEffectView(
                    effect,
                    _set.phase,
                    RaiseChanged,
                    onPaste: () =>
                    {
                        var pasted = MfvEffectClipboard.Paste(_set.effects[index]);
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
