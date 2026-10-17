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
        /// The layout <see cref="version"/> marks. Sets saved before version 1 had symmetric
        /// as an order of its own, between reverse and random. Before version 2 the fade
        /// belonged to the clip and scaled its weight, and it now belongs to the brightness
        /// effect. Before version 3 blackout on return belonged to the brightness effect, and
        /// it now belongs to the move. <see cref="OnAfterDeserialize"/> converts all three.
        /// </summary>
        private const int CurrentVersion = 3;

        /// <summary>
        /// The clip's own tempo, for songs that change tempo part way. Zero follows the
        /// show's tempo. Either way beats count from the clip's start.
        /// </summary>
        public float bpm;

        public AlpsOrderMode order = AlpsOrderMode.Normal;

        /// <summary>
        /// Folds the fixtures onto one half, so both halves share the order and the first half
        /// mirrors its pan. See <see cref="AlpsShowLayout.OrderPosition"/>.
        /// </summary>
        public bool symmetric;

        /// <summary>
        /// Shuffles the random order. The order depends on it alone, so clips with the same
        /// seed share their order wherever they sit on the timeline.
        /// </summary>
        public int orderSeed;

        /// <summary>
        /// Missing from anything saved before <see cref="CurrentVersion"/>, so it reads as 0
        /// there. Every save writes the current one.
        /// </summary>
        [SerializeField, HideInInspector] private int version;

        /// <summary>
        /// The clip's fade from before version 2, kept under its old names only while the set
        /// has no brightness effect to take it. The first brightness effect added takes it
        /// (<see cref="Add"/>), and until then the clip does not fade.
        /// </summary>
        [SerializeField, HideInInspector] private float fadeInBeats;
        [SerializeField, HideInInspector] private float fadeOutBeats;

        public bool phaseExpanded = true;
        public AlpsPhaseSettings phase = new AlpsPhaseSettings();

        public List<AlpsEffect> effects = new List<AlpsEffect>();

        public bool profileExpanded;

        public AlpsClipEffectSet() { }

        /// <summary>
        /// What a clip or profile made in the editor starts from. Its phase is in / out with
        /// linear eases and a delay in beats, where the model's own defaults stay the wave,
        /// the sine and the share of the cycle everything older was saved against.
        /// </summary>
        public static AlpsClipEffectSet CreateNew()
        {
            var set = new AlpsClipEffectSet();
            set.phase.mode = AlpsPhaseMode.InOut;
            set.phase.ease = AlpsEaseType.Linear;
            set.phase.fallEase = AlpsEaseType.Linear;
            set.phase.spreadInBeats = true;
            return set;
        }

        /// <summary>Deep copy, used to load a clip from a profile and to save it back.</summary>
        public AlpsClipEffectSet(AlpsClipEffectSet other)
        {
            bpm = other.bpm;
            order = other.order;
            symmetric = other.symmetric;
            orderSeed = other.orderSeed;
            fadeInBeats = other.fadeInBeats;
            fadeOutBeats = other.fadeOutBeats;
            phaseExpanded = other.phaseExpanded;
            phase = new AlpsPhaseSettings(other.phase);
            effects = new List<AlpsEffect>();
            foreach (var effect in other.effects)
            {
                effects.Add(new AlpsEffect(effect));
            }

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
                return AlpsEffectCatalog.GetName(kind) + AlpsEffectCatalog.GetParitySuffix(AlpsParity.Odd);
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
                if (kind == AlpsEffectKind.Brightness)
                {
                    TakeLegacyFade(fresh);
                }

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

        public void OnBeforeSerialize()
        {
            version = CurrentVersion;
        }

        public void OnAfterDeserialize()
        {
            if (version < 1)
            {
                symmetric = AlpsLegacyOrder.WasSymmetric(order);
                order = AlpsLegacyOrder.Upgrade(order);
            }

            if (version < 2)
            {
                MoveFadeToBrightness();
            }

            if (version < 3)
            {
                MoveBlackoutToMove();
            }

            version = CurrentVersion;
            SortEffects();
        }

        /// <summary>
        /// Hands every brightness effect's blackout on return to the moves that turn the same
        /// fixtures, where the move goes back on the same phase, so the clip looks the same on
        /// its own. The move now darkens the composed brightness, so a layer above that lights
        /// the same fixtures goes dark with it. A blackout no move can take unchanged stays on
        /// the brightness effect, which still plays it.
        /// </summary>
        private void MoveBlackoutToMove()
        {
            if (effects == null)
            {
                return;
            }

            foreach (var brightness in effects)
            {
                if (brightness == null || brightness.kind != AlpsEffectKind.Brightness || !brightness.blackoutOnReturn
                    || brightness.brightness.useOwnPhase)
                {
                    continue;
                }

                var takers = new List<AlpsEffect>();
                var covered = 0;
                foreach (var move in effects)
                {
                    if (move == null || move.kind != AlpsEffectKind.Move || !Overlaps(move.parity, brightness.parity))
                    {
                        continue;
                    }

                    if (!ReturnsOnClipPhase(move) || !Mathf.Approximately(move.phaseOffset, brightness.phaseOffset))
                    {
                        covered = -1;
                        break;
                    }

                    takers.Add(move);
                    covered |= Positions(move.parity);
                }

                if (covered < 0 || (covered & Positions(brightness.parity)) != Positions(brightness.parity))
                {
                    continue;
                }

                foreach (var move in takers)
                {
                    move.blackoutOnReturn = true;
                    move.blackoutFadeIn = brightness.blackoutFadeIn;
                    move.blackoutFadeOut = brightness.blackoutFadeOut;
                }

                brightness.blackoutOnReturn = false;
            }
        }

        /// <summary>
        /// True when the move's blackout on return follows the clip's shared phase: a circle
        /// always, an angle move through tilt while tilt sweeps, else through pan.
        /// </summary>
        private static bool ReturnsOnClipPhase(AlpsEffect move)
        {
            if (move.moveMode == AlpsMoveMode.TrackUser)
            {
                return false;
            }

            return move.moveMode == AlpsMoveMode.Circle || !AlpsEffect.ReturnValue(move).useOwnPhase;
        }

        /// <summary>Order positions a parity reaches: 1 for even, 2 for odd.</summary>
        private static int Positions(AlpsParity parity)
        {
            return parity == AlpsParity.Even ? 1 : parity == AlpsParity.Odd ? 2 : 3;
        }

        private static bool Overlaps(AlpsParity a, AlpsParity b)
        {
            return (Positions(a) & Positions(b)) != 0;
        }

        /// <summary>
        /// True while the set carries a fade from before version 2 that no brightness effect
        /// took, so the clip no longer fades.
        /// </summary>
        public bool HasLegacyFade => fadeInBeats > 0f || fadeOutBeats > 0f;

        /// <summary>
        /// Gives the clip's fade to every brightness effect on it. The fade scaled the clip's
        /// weight and now scales the brightness, which looks the same on a clip alone, but a
        /// fading clip now covers the layers below where it used to blend with them. A set
        /// without brightness keeps it for the first brightness effect added.
        /// </summary>
        private void MoveFadeToBrightness()
        {
            if (!HasLegacyFade || effects == null)
            {
                return;
            }

            var moved = false;
            foreach (var effect in effects)
            {
                if (effect != null && effect.kind == AlpsEffectKind.Brightness)
                {
                    effect.fadeInBeats = fadeInBeats;
                    effect.fadeOutBeats = fadeOutBeats;
                    moved = true;
                }
            }

            if (moved)
            {
                fadeInBeats = 0f;
                fadeOutBeats = 0f;
            }
        }

        private void TakeLegacyFade(AlpsEffect brightness)
        {
            brightness.fadeInBeats = fadeInBeats;
            brightness.fadeOutBeats = fadeOutBeats;
            fadeInBeats = 0f;
            fadeOutBeats = 0f;
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
