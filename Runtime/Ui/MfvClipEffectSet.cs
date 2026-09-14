using System;
using System.Collections.Generic;

namespace ManeuverForVRC.Ui
{
    /// <summary>
    /// Everything the clip inspector edits: order, shared settings, and the effect stack.
    /// </summary>
    [Serializable]
    public class MfvClipEffectSet
    {
        public MfvOrderMode order = MfvOrderMode.Normal;

        public bool phaseExpanded = true;
        public MfvPhaseSettings phase = new MfvPhaseSettings();

        public List<MfvEffect> effects = new List<MfvEffect>();

        public bool addCatalogExpanded;
        public bool profileExpanded;

        /// <summary>
        /// The label the add list shows for <paramref name="kind"/>, or null when the
        /// kind is fully used up (both even and odd already on the clip).
        /// </summary>
        public string GetAddLabel(MfvEffectKind kind)
        {
            var existing = IndexOf(kind, MfvParity.All);
            var even = IndexOf(kind, MfvParity.Even);
            var odd = IndexOf(kind, MfvParity.Odd);

            if (odd >= 0)
            {
                return null;
            }

            if (existing >= 0 || even >= 0)
            {
                return MfvEffectCatalog.GetName(kind) + MfvEffectCatalog.OddSuffix;
            }

            return MfvEffectCatalog.GetName(kind);
        }

        public bool CanAdd(MfvEffectKind kind)
        {
            return GetAddLabel(kind) != null;
        }

        /// <summary>
        /// Odd / even split: adding a kind that is already present demotes the existing
        /// card to even and inserts an identical odd card directly below it.
        /// </summary>
        public MfvEffect Add(MfvEffectKind kind)
        {
            if (!CanAdd(kind))
            {
                return null;
            }

            var existingIndex = IndexOf(kind, MfvParity.All);
            if (existingIndex < 0)
            {
                existingIndex = IndexOf(kind, MfvParity.Even);
            }

            if (existingIndex < 0)
            {
                var fresh = MfvEffect.Create(kind);
                effects.Add(fresh);
                return fresh;
            }

            var existing = effects[existingIndex];
            existing.parity = MfvParity.Even;

            var odd = new MfvEffect(existing) { parity = MfvParity.Odd };
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

            var remaining = new List<MfvEffect>();
            foreach (var effect in effects)
            {
                if (effect.kind == kind)
                {
                    remaining.Add(effect);
                }
            }

            if (remaining.Count == 1)
            {
                remaining[0].parity = MfvParity.All;
            }
        }

        public int IndexOf(MfvEffectKind kind, MfvParity parity)
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
