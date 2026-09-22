using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>
    /// The value setter the ALPS fields share while they may show mixed values.
    ///
    /// On 2022.3.22 the base setter neither clears <see cref="BaseField{T}.showMixedValue"/>
    /// nor reports a value equal to the current one. A mixed field holds the shown clip's
    /// value, so picking that same value would change nothing and the other clips would
    /// never follow. Later versions do both, and clearing first keeps them harmless.
    /// </summary>
    internal static class AlpsMixedField
    {
        public static void Set<T>(BaseField<T> field, T newValue, Action<T> setBase)
        {
            if (!field.showMixedValue)
            {
                setBase(newValue);
                return;
            }

            field.showMixedValue = false;
            var previous = field.value;
            if (!EqualityComparer<T>.Default.Equals(previous, newValue))
            {
                setBase(newValue);
                return;
            }

            if (field.panel == null)
            {
                return;
            }

            using (var evt = ChangeEvent<T>.GetPooled(previous, newValue))
            {
                evt.target = field;
                field.SendEvent(evt);
            }
        }
    }
}
