using System;
using System.Collections.Generic;
using UnityEngine;

namespace AdzukiSoft.ALPS
{
    /// <summary>
    /// Everything the clip inspector edits: tempo, order, shared settings, and the effect stack.
    /// The stack is always kept in <see cref="AlpsEffectCatalog.Order"/>, even before odd.
    /// </summary>
    [Serializable]
    public class AlpsClipEffectSet : ISerializationCallbackReceiver
    {
        /// <summary>
        /// The clip's own tempo, for songs that change tempo part way. Zero follows the
        /// show's tempo. Either way beats count from the clip's start.
        /// </summary>
        public float bpm;

        public AlpsOrderMode order = AlpsOrderMode.Normal;

        public bool phaseExpanded = true;
        public AlpsPhaseSettings phase = new AlpsPhaseSettings();

        public List<AlpsEffect> effects = new List<AlpsEffect>();

        public bool addCatalogExpanded;
        public bool profileExpanded;

        public AlpsClipEffectSet() { }

        /// <summary>Deep copy, used to load a clip from a profile and to save it back.</summary>
        public AlpsClipEffectSet(AlpsClipEffectSet other)
        {
            bpm = other.bpm;
            order = other.order;
            phaseExpanded = other.phaseExpanded;
            phase = new AlpsPhaseSettings(other.phase);
            effects = new List<AlpsEffect>();
            foreach (var effect in other.effects)
            {
                effects.Add(new AlpsEffect(effect));
            }

            addCatalogExpanded = other.addCatalogExpanded;
            profileExpanded = other.profileExpanded;
        }

        /// <summary>
        /// The label the add list shows for <paramref name="kind"/>, or null when the
        /// kind is fully used up (both even and odd already on the clip).
        /// </summary>
        public string GetAddLabel(AlpsEffectKind kind)
        {
            var existing = IndexOf(kind, AlpsParity.All);
            var even = IndexOf(kind, AlpsParity.Even);
            var odd = IndexOf(kind, AlpsParity.Odd);

            if (odd >= 0)
            {
                return null;
            }

            if (existing >= 0 || even >= 0)
            {
                return AlpsEffectCatalog.GetName(kind) + AlpsEffectCatalog.OddSuffix;
            }

            return AlpsEffectCatalog.GetName(kind);
        }

        public bool CanAdd(AlpsEffectKind kind)
        {
            return GetAddLabel(kind) != null;
        }

        /// <summary>
        /// Odd / even split: adding a kind that is already present demotes the existing
        /// card to even and inserts an identical odd card directly below it.
        /// </summary>
        public AlpsEffect Add(AlpsEffectKind kind)
        {
            if (!CanAdd(kind))
            {
                return null;
            }

            var existingIndex = IndexOf(kind, AlpsParity.All);
            if (existingIndex < 0)
            {
                existingIndex = IndexOf(kind, AlpsParity.Even);
            }

            if (existingIndex < 0)
            {
                var fresh = AlpsEffect.Create(kind);
                effects.Insert(InsertionIndex(kind), fresh);
                return fresh;
            }

            var existing = effects[existingIndex];
            existing.parity = AlpsParity.Even;

            var odd = new AlpsEffect(existing) { parity = AlpsParity.Odd };
            effects.Insert(existingIndex + 1, odd);
            return odd;
        }

        /// <summary>
        /// Delete button on a card header. Removing one half of an even / odd pair
        /// promotes the survivor back to an unsuffixed effect.
        /// </summary>
        public void Remove(int index)
        {
            if (index < 0 || index >= effects.Count)
            {
                return;
            }

            var kind = effects[index].kind;
            effects.RemoveAt(index);

            var remaining = new List<AlpsEffect>();
            foreach (var effect in effects)
            {
                if (effect.kind == kind)
                {
                    remaining.Add(effect);
                }
            }

            if (remaining.Count == 1)
            {
                remaining[0].parity = AlpsParity.All;
            }
        }

        /// <summary>
        /// Puts the stack back into catalog order. Clips saved before the order was fixed
        /// are normalized on load. The sort is stable, so anything unexpected keeps its place.
        /// </summary>
        public void SortEffects()
        {
            if (effects == null || IsSorted())
            {
                return;
            }

            var keyed = new List<KeyValuePair<int, AlpsEffect>>(effects.Count);
            for (var i = 0; i < effects.Count; i++)
            {
                keyed.Add(new KeyValuePair<int, AlpsEffect>(i, effects[i]));
            }

            keyed.Sort((a, b) =>
            {
                var byKey = SortKey(a.Value).CompareTo(SortKey(b.Value));
                return byKey != 0 ? byKey : a.Key.CompareTo(b.Key);
            });

            for (var i = 0; i < keyed.Count; i++)
            {
                effects[i] = keyed[i].Value;
            }
        }

        public void OnBeforeSerialize() { }

        public void OnAfterDeserialize()
        {
            SortEffects();
        }

        private bool IsSorted()
        {
            for (var i = 1; i < effects.Count; i++)
            {
                if (SortKey(effects[i - 1]) > SortKey(effects[i]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Catalog position first, then parity (all / even before odd). Nulls sink.</summary>
        private static int SortKey(AlpsEffect effect)
        {
            if (effect == null)
            {
                return int.MaxValue;
            }

            return AlpsEffectCatalog.GetSortIndex(effect.kind) * 4 + (effect.parity == AlpsParity.Odd ? 1 : 0);
        }

        /// <summary>Where a new card of <paramref name="kind"/> goes: after every card that sorts before it.</summary>
        private int InsertionIndex(AlpsEffectKind kind)
        {
            var target = AlpsEffectCatalog.GetSortIndex(kind);
            var index = 0;
            while (index < effects.Count
                   && effects[index] != null
                   && AlpsEffectCatalog.GetSortIndex(effects[index].kind) <= target)
            {
                index++;
            }

            return index;
        }

        public int IndexOf(AlpsEffectKind kind, AlpsParity parity)
        {
            for (var i = 0; i < effects.Count; i++)
            {
                if (effects[i].kind == kind && effects[i].parity == parity)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
