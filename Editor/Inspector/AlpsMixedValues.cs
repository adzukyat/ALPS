using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>
    /// Which values differ between the selected clips, so the controls showing them can
    /// read as mixed, the way Unity's own inspector shows a dash.
    ///
    /// The shown clip's model objects are the keys, since those are what the views hold.
    /// Each is paired with the matching object on every other clip. Effects are matched
    /// by kind and parity like <see cref="AlpsClipEditSync"/>, and an effect another clip
    /// does not have has nothing to compare, so it never reads as mixed.
    ///
    /// A field committed while mixed fires a change even when the value did not move (the
    /// ALPS fields make sure of it through <see cref="AlpsMixedField"/>), and the diff sees
    /// nothing then. <see cref="CarryEdited"/> copies such a field to the
    /// other clips as a whole, which is how picking a value on a mixed control unifies it.
    /// </summary>
    public sealed class AlpsMixedValues
    {
        /// <summary>The dash a mixed control shows, the same glyph Unity uses.</summary>
        public const string MixedText = "—";

        private readonly AlpsClipEffectSet _shown;
        private readonly IReadOnlyList<AlpsClipEffectSet> _others;
        private readonly Dictionary<object, HashSet<string>> _mixed = new Dictionary<object, HashSet<string>>();
        private readonly Dictionary<object, List<object>> _counterparts = new Dictionary<object, List<object>>();
        private readonly List<Binding> _bindings = new List<Binding>();

        public AlpsMixedValues(AlpsClipEffectSet shown, IReadOnlyList<AlpsClipEffectSet> others)
        {
            _shown = shown;
            _others = others;
            Compute();
        }

        private sealed class Binding
        {
            public VisualElement Element;
            public Func<bool> IsShownMixed;
            public Action<bool> Show;
            public object Owner;
            public string[] Fields;
            public bool Carries;
            public bool WasMixed;
        }

        /// <summary>True when <paramref name="field"/> of <paramref name="owner"/> differs on any other clip.</summary>
        public bool IsMixed(object owner, params string[] fields)
        {
            if (owner == null || !_mixed.TryGetValue(owner, out var names))
            {
                return false;
            }

            foreach (var field in fields)
            {
                if (names.Contains(field))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Shows <paramref name="control"/> as mixed while any of <paramref name="fields"/>
        /// differ. Committing it while mixed carries those fields to every clip.
        /// </summary>
        public void Bind<T>(BaseField<T> control, object owner, params string[] fields)
        {
            Add(new Binding
            {
                Element = control,
                IsShownMixed = () => control.showMixedValue,
                Show = mixed => control.showMixedValue = mixed,
                Owner = owner,
                Fields = fields,
                Carries = true,
            });
        }

        /// <summary>
        /// Shows mixed through <paramref name="show"/> only. For a control that edits part of
        /// a value, such as one stop of a palette, where carrying the whole value would
        /// overwrite what the other clips keep.
        /// </summary>
        public void BindDisplay(VisualElement element, Action<bool> show, object owner, params string[] fields)
        {
            Add(new Binding
            {
                Element = element,
                Show = show,
                Owner = owner,
                Fields = fields,
            });
        }

        private void Add(Binding binding)
        {
            binding.WasMixed = IsMixed(binding.Owner, binding.Fields);
            binding.Show(binding.WasMixed);
            _bindings.Add(binding);
        }

        /// <summary>
        /// Copies every bound field whose control was committed since the last
        /// <see cref="Recompute"/> from the shown clip to the others.
        /// </summary>
        public void CarryEdited()
        {
            foreach (var binding in _bindings)
            {
                if (!binding.Carries || !binding.WasMixed || binding.IsShownMixed())
                {
                    continue;
                }

                if (!_counterparts.TryGetValue(binding.Owner, out var counterparts))
                {
                    continue;
                }

                foreach (var name in binding.Fields)
                {
                    var field = binding.Owner.GetType().GetField(name);
                    if (field == null)
                    {
                        continue;
                    }

                    var value = field.GetValue(binding.Owner);
                    foreach (var counterpart in counterparts)
                    {
                        field.SetValue(counterpart, AlpsClipEditSync.Copy(field.FieldType, value));
                    }
                }
            }
        }

        /// <summary>
        /// Compares the clips again and updates every control still on screen. Controls a
        /// rebuild took down are dropped.
        /// </summary>
        public void Recompute()
        {
            Compute();
            _bindings.RemoveAll(binding => binding.Element.panel == null);
            foreach (var binding in _bindings)
            {
                binding.WasMixed = IsMixed(binding.Owner, binding.Fields);
                binding.Show(binding.WasMixed);
            }
        }

        private void Compute()
        {
            _mixed.Clear();
            _counterparts.Clear();
            if (_shown == null || _others == null)
            {
                return;
            }

            foreach (var other in _others)
            {
                if (other != null && !ReferenceEquals(other, _shown))
                {
                    Walk(_shown, other);
                }
            }
        }

        private void Walk(object shown, object other)
        {
            if (!_counterparts.TryGetValue(shown, out var counterparts))
            {
                counterparts = new List<object>();
                _counterparts[shown] = counterparts;
            }

            counterparts.Add(other);

            foreach (var field in AlpsClipEditSync.Fields(shown.GetType()))
            {
                var a = field.GetValue(shown);
                var b = field.GetValue(other);

                if (a is List<AlpsEffect> effects)
                {
                    if (b is List<AlpsEffect> otherEffects)
                    {
                        foreach (var effect in effects)
                        {
                            var match = AlpsClipEditSync.Find(otherEffects, effect);
                            if (match != null)
                            {
                                Walk(effect, match);
                            }
                        }
                    }

                    continue;
                }

                if (AlpsClipEditSync.IsModel(field.FieldType) && a != null && b != null)
                {
                    Walk(a, b);
                    continue;
                }

                if (!AlpsClipEditSync.Same(field.FieldType, a, b))
                {
                    if (!_mixed.TryGetValue(shown, out var names))
                    {
                        names = new HashSet<string>();
                        _mixed[shown] = names;
                    }

                    names.Add(field.Name);
                }
            }
        }
    }
}
