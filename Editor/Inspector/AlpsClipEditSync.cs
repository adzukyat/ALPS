using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>
    /// Carries an edit made on the shown clip over to the other selected clips.
    ///
    /// Only what the edit changed is carried. Every other value on the other clips stays
    /// as it was, the way Unity's own multi object editing works. The change is found by
    /// comparing the shown clip before and after the edit, field by field through
    /// reflection, so a field added to the model later is carried without touching this.
    ///
    /// Effects are matched by kind and parity rather than by index, since the stacks of
    /// two clips differ. An effect the other clip does not have is skipped. Adding,
    /// removing and pasting whole parts are not found here. The view reports those
    /// through <see cref="AlpsClipInspectorView.StructureEdited"/> and the host repeats
    /// them on every clip, which leaves nothing for the diff to carry afterwards.
    /// </summary>
    public static class AlpsClipEditSync
    {
        /// <summary>
        /// Inspector state rather than show content, plus the identity of an effect.
        /// A selected stop could also point past the end of a shorter palette.
        /// </summary>
        private static readonly HashSet<string> SkippedFields = new HashSet<string>
        {
            nameof(AlpsClipEffectSet.phaseExpanded),
            nameof(AlpsClipEffectSet.addCatalogExpanded),
            nameof(AlpsClipEffectSet.profileExpanded),
            nameof(AlpsEffect.kind),
            nameof(AlpsEffect.parity),
            nameof(AlpsEffect.expanded),
            nameof(AlpsEffect.selectedColorStop),
            nameof(AlpsEffect.selectedGoboStop),
            nameof(AlpsEffect.goboPickerExpanded),
        };

        private static readonly Dictionary<Type, FieldInfo[]> FieldCache = new Dictionary<Type, FieldInfo[]>();

        /// <summary>
        /// Writes into <paramref name="target"/> every value that differs between
        /// <paramref name="before"/> and <paramref name="after"/>.
        /// </summary>
        public static void Apply(AlpsClipEffectSet before, AlpsClipEffectSet after, AlpsClipEffectSet target)
        {
            if (before == null || after == null || target == null || ReferenceEquals(after, target))
            {
                return;
            }

            ApplyObject(before, after, target);
        }

        private static void ApplyObject(object before, object after, object target)
        {
            foreach (var field in Fields(after.GetType()))
            {
                var b = field.GetValue(before);
                var a = field.GetValue(after);
                var t = field.GetValue(target);

                if (a is List<AlpsEffect> afterEffects)
                {
                    ApplyEffects(b as List<AlpsEffect>, afterEffects, t as List<AlpsEffect>);
                    continue;
                }

                var carried = Carry(field.FieldType, b, a, t);
                if (!ReferenceEquals(carried, t))
                {
                    field.SetValue(target, carried);
                }
            }
        }

        private static void ApplyEffects(List<AlpsEffect> before, List<AlpsEffect> after, List<AlpsEffect> target)
        {
            if (before == null || target == null)
            {
                return;
            }

            foreach (var effect in after)
            {
                var b = Find(before, effect);
                var t = Find(target, effect);
                if (b != null && t != null)
                {
                    ApplyObject(b, effect, t);
                }
            }
        }

        internal static AlpsEffect Find(List<AlpsEffect> effects, AlpsEffect key)
        {
            if (key == null)
            {
                return null;
            }

            foreach (var effect in effects)
            {
                if (effect != null && effect.kind == key.kind && effect.parity == key.parity)
                {
                    return effect;
                }
            }

            return null;
        }

        /// <summary>The value <paramref name="target"/> should hold after the edit. The same instance when nothing changed.</summary>
        private static object Carry(Type type, object before, object after, object target)
        {
            if (IsModel(type) && before != null && after != null && target != null)
            {
                ApplyObject(before, after, target);
                return target;
            }

            if (IsList(type) && before != null && after != null && target != null)
            {
                CarryList(ElementType(type), (IList)before, (IList)after, (IList)target);
                return target;
            }

            return Same(type, before, after) ? target : Copy(type, after);
        }

        /// <summary>
        /// Same length carries stop by stop. One stop inserted or removed is repeated at the
        /// same place. Any other change of length carries the whole list.
        /// </summary>
        private static void CarryList(Type element, IList before, IList after, IList target)
        {
            if (before.Count == after.Count)
            {
                for (var i = 0; i < after.Count && i < target.Count; i++)
                {
                    var carried = Carry(element, before[i], after[i], target[i]);
                    if (!ReferenceEquals(carried, target[i]))
                    {
                        target[i] = carried;
                    }
                }

                return;
            }

            if (after.Count == before.Count + 1)
            {
                var inserted = SingleDifference(element, after, before);
                if (inserted >= 0)
                {
                    target.Insert(Math.Min(inserted, target.Count), Copy(element, after[inserted]));
                    return;
                }
            }

            if (before.Count == after.Count + 1)
            {
                var removed = SingleDifference(element, before, after);
                if (removed >= 0)
                {
                    if (removed < target.Count)
                    {
                        target.RemoveAt(removed);
                    }

                    return;
                }
            }

            target.Clear();
            foreach (var item in after)
            {
                target.Add(Copy(element, item));
            }
        }

        /// <summary>
        /// The index at which <paramref name="longer"/> has one extra item and otherwise
        /// equals <paramref name="shorter"/>, or -1 when the two differ in some other way.
        /// </summary>
        private static int SingleDifference(Type element, IList longer, IList shorter)
        {
            var index = 0;
            while (index < shorter.Count && Same(element, longer[index], shorter[index]))
            {
                index++;
            }

            for (var i = index; i < shorter.Count; i++)
            {
                if (!Same(element, longer[i + 1], shorter[i]))
                {
                    return -1;
                }
            }

            return index;
        }

        internal static bool Same(Type type, object a, object b)
        {
            if (a == null || b == null)
            {
                return a == null && b == null;
            }

            if (IsModel(type))
            {
                foreach (var field in Fields(type))
                {
                    if (!Same(field.FieldType, field.GetValue(a), field.GetValue(b)))
                    {
                        return false;
                    }
                }

                return true;
            }

            if (IsList(type))
            {
                var listA = (IList)a;
                var listB = (IList)b;
                if (listA.Count != listB.Count)
                {
                    return false;
                }

                var element = ElementType(type);
                for (var i = 0; i < listA.Count; i++)
                {
                    if (!Same(element, listA[i], listB[i]))
                    {
                        return false;
                    }
                }

                return true;
            }

            if (a is Gradient gradientA && b is Gradient gradientB)
            {
                return gradientA.mode == gradientB.mode
                       && SameKeys(gradientA.colorKeys, gradientB.colorKeys)
                       && SameKeys(gradientA.alphaKeys, gradientB.alphaKeys);
            }

            return a.Equals(b);
        }

        private static bool SameKeys<T>(T[] a, T[] b)
        {
            if (a.Length != b.Length)
            {
                return false;
            }

            for (var i = 0; i < a.Length; i++)
            {
                if (!a[i].Equals(b[i]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// A copy the target can own. Model types go through their copy constructor, the same
        /// way the clipboard copies, so nothing is shared between clips.
        /// </summary>
        internal static object Copy(Type type, object value)
        {
            if (value == null)
            {
                return null;
            }

            if (IsModel(type))
            {
                return Activator.CreateInstance(type, value);
            }

            if (IsList(type))
            {
                var element = ElementType(type);
                var copy = (IList)Activator.CreateInstance(type);
                foreach (var item in (IList)value)
                {
                    copy.Add(Copy(element, item));
                }

                return copy;
            }

            if (value is Gradient gradient)
            {
                var copy = new Gradient { mode = gradient.mode };
                copy.SetKeys(gradient.colorKeys, gradient.alphaKeys);
                return copy;
            }

            // Value types and strings, which cannot be shared by mistake.
            return value;
        }

        internal static bool IsModel(Type type)
        {
            return type.IsClass && type.Namespace == typeof(AlpsClipEffectSet).Namespace && type.IsSerializable;
        }

        private static bool IsList(Type type)
        {
            return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>);
        }

        private static Type ElementType(Type listType)
        {
            return listType.GetGenericArguments()[0];
        }

        internal static FieldInfo[] Fields(Type type)
        {
            if (FieldCache.TryGetValue(type, out var cached))
            {
                return cached;
            }

            var fields = new List<FieldInfo>();
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!field.IsNotSerialized && !SkippedFields.Contains(field.Name))
                {
                    fields.Add(field);
                }
            }

            cached = fields.ToArray();
            FieldCache[type] = cached;
            return cached;
        }
    }
}
