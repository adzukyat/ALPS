using System;

namespace ManeuverForVRC.Editor
{
    /// <summary>
    /// The copy button copies a card's parameters. It does not duplicate the card. An
    /// effect kind may appear on a clip at most twice (even / odd), so a duplicate would
    /// never be legal. What is reusable is the settings.
    ///
    /// The payload lives for the editor session and holds a deep copy, so texture and
    /// gradient references survive without a serialisation round trip.
    ///
    /// Shared settings have a payload of their own, so copying them does not drop a
    /// copied effect and the other way round.
    /// </summary>
    public static class MfvEffectClipboard
    {
        private static MfvEffect _payload;
        private static MfvPhaseSettings _phasePayload;

        /// <summary>Raised when the payload changes, so open cards can show or hide paste.</summary>
        public static event Action Changed;

        public static bool HasPayload => _payload != null;

        public static MfvEffectKind? PayloadKind => _payload?.kind;

        /// <summary>Parameters can only be pasted onto the same kind of effect.</summary>
        public static bool CanPasteInto(MfvEffect target)
        {
            return _payload != null && target != null && _payload.kind == target.kind;
        }

        public static void Copy(MfvEffect effect)
        {
            _payload = effect == null ? null : new MfvEffect(effect);
            Changed?.Invoke();
        }

        /// <summary>
        /// The pasted effect: every parameter from the payload, but this card's identity.
        /// Built through the copy constructor so a field added later cannot be missed.
        /// </summary>
        public static MfvEffect Paste(MfvEffect target)
        {
            if (!CanPasteInto(target))
            {
                return null;
            }

            return new MfvEffect(_payload)
            {
                kind = target.kind,
                parity = target.parity,
                expanded = target.expanded,
            };
        }

        public static bool HasPhasePayload => _phasePayload != null;

        public static void CopyPhase(MfvPhaseSettings settings)
        {
            _phasePayload = settings == null ? null : new MfvPhaseSettings(settings);
            Changed?.Invoke();
        }

        /// <summary>A fresh copy of the copied shared settings, or null while nothing is copied.</summary>
        public static MfvPhaseSettings PastePhase()
        {
            return _phasePayload == null ? null : new MfvPhaseSettings(_phasePayload);
        }

        public static void Clear()
        {
            _payload = null;
            _phasePayload = null;
            Changed?.Invoke();
        }
    }
}
