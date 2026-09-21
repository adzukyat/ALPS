using System;
using System.Collections.Generic;

namespace AdzukiSoft.ALPS
{
    /// <summary>
    /// Everything the clip inspector edits: order, shared settings, and the effect stack.
    /// </summary>
    [Serializable]
    public class AlpsClipEffectSet
    {
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
                effects.Add(fresh);
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
