using System;
using System.Collections.Generic;
using UnityEngine;

namespace ManeuverForVRC
{
    /// <summary>
    /// Effect: one entry of a clip's effect stack.
    ///
    /// The effect catalog is fixed and small, so this is one concrete serializable type
    /// discriminated by <see cref="kind"/> rather than a [SerializeReference] hierarchy.
    /// That keeps SerializedProperty binding in UI Toolkit straightforward (no polymorphic
    /// property paths) and makes copy / delete / reorder plain list operations.
    /// </summary>
    [Serializable]
    public class MfvEffect
    {
        public MfvEffectKind kind;
        public MfvParity parity = MfvParity.All;
        public bool expanded = true;

        // --- Move ---
        public MfvMoveMode moveMode = MfvMoveMode.Angle;
        public MfvAnimatableValue tilt = new MfvAnimatableValue(0f, new Vector2(-90f, 90f));
        public MfvAnimatableValue pan = new MfvAnimatableValue(0f, new Vector2(-180f, 180f));
        /// <summary>Phase offset. Only meaningful while both Tilt and Pan are ranges.</summary>
        public float panTiltPhaseOffsetDegrees = 90f;
        public string trackUserName = string.Empty;
        public float trackSpeed = 5.5f;

        // --- Move: circle ---
        /// <summary>Direction the circle turns around, in the same angles as Tilt and Pan.</summary>
        public MfvAnimatableValue circleCenterTilt = new MfvAnimatableValue(30f, new Vector2(-90f, 90f));
        public MfvAnimatableValue circleCenterPan = new MfvAnimatableValue(0f, new Vector2(-180f, 180f));
        /// <summary>Opening angle from the center direction to the traced ring.</summary>
        public MfvAnimatableValue circleRadius = new MfvAnimatableValue(10f, new Vector2(0f, 90f));
        /// <summary>Width over height. 1 is a circle, anything else an ellipse.</summary>
        public float circleAspect = 1f;

        // --- Cone ---
        public MfvAnimatableValue coneWidth = new MfvAnimatableValue(15f, new Vector2(0f, 90f));
        public MfvAnimatableValue coneLength = new MfvAnimatableValue(14f, new Vector2(0f, 50f));

        // --- Color ---
        /// <summary>
        /// The palette is the range: no stops means OFF (nothing to play), one stop is a
        /// static value, and two or more are walked by the phase. So a new effect starts
        /// empty rather than pre-filled, because a pre-filled palette would animate immediately.
        /// </summary>
        public List<MfvColorStop> colorStops = new List<MfvColorStop>();
        public int selectedColorStop;
        /// <summary>Carries timing / own phase for the palette.</summary>
        public MfvAnimatableValue colorPhasing = new MfvAnimatableValue(0f, new Vector2(0f, 1f));

        // --- Brightness ---
        /// <summary>Stored on the 0–100 scale the inspector shows.</summary>
        public MfvAnimatableValue brightness = new MfvAnimatableValue(100f, new Vector2(0f, 100f));
        /// <summary>
        /// Blackout on return: brightness drops to 0 while the phase driving it is on a
        /// ping-pong return leg, so the beams fly out one way only.
        /// </summary>
        public bool blackoutOnReturn;
        /// <summary>Fade in at the start of the outbound leg, as a fraction of that leg (0 to 0.5).</summary>
        [Range(0f, 0.5f)] public float blackoutFadeIn;
        /// <summary>Fade out at the end of the outbound leg, as a fraction of that leg (0 to 0.5).</summary>
        [Range(0f, 0.5f)] public float blackoutFadeOut;

        // --- Flicker ---
        public float flickerSpeed = 12f;
        [Range(0f, 1f)] public float flickerStrength = 0.3f;
        public float flickerFixtureStagger = 0.6f;

        // --- Gobo ---
        /// <summary>Same semantics as <see cref="colorStops"/>. OFF is an ordinary stop.</summary>
        public List<MfvGoboStop> goboStops = new List<MfvGoboStop>();
        public int selectedGoboStop;
        /// <summary>Whether the + picker under the palette is open (UI state, like <see cref="expanded"/>).</summary>
        public bool goboPickerExpanded;
        public MfvAnimatableValue goboPhasing = new MfvAnimatableValue(0f, new Vector2(0f, 1f));
        /// <summary>Rotation speed in beats per full turn.</summary>
        public float goboRotationBeats = 8f;
        public float goboFixtureStaggerDegrees = 45f;

        public MfvEffect() { }

        public MfvEffect(MfvEffect other)
        {
            kind = other.kind;
            parity = other.parity;
            expanded = other.expanded;

            moveMode = other.moveMode;
            tilt = new MfvAnimatableValue(other.tilt);
            pan = new MfvAnimatableValue(other.pan);
            panTiltPhaseOffsetDegrees = other.panTiltPhaseOffsetDegrees;
            trackUserName = other.trackUserName;
            trackSpeed = other.trackSpeed;
            circleCenterTilt = new MfvAnimatableValue(other.circleCenterTilt);
            circleCenterPan = new MfvAnimatableValue(other.circleCenterPan);
            circleRadius = new MfvAnimatableValue(other.circleRadius);
            circleAspect = other.circleAspect;

            coneWidth = new MfvAnimatableValue(other.coneWidth);
            coneLength = new MfvAnimatableValue(other.coneLength);

            colorStops = new List<MfvColorStop>();
            foreach (var stop in other.colorStops)
            {
                colorStops.Add(new MfvColorStop(stop));
            }

            selectedColorStop = other.selectedColorStop;
            colorPhasing = new MfvAnimatableValue(other.colorPhasing);

            brightness = new MfvAnimatableValue(other.brightness);
            blackoutOnReturn = other.blackoutOnReturn;
            blackoutFadeIn = other.blackoutFadeIn;
            blackoutFadeOut = other.blackoutFadeOut;

            flickerSpeed = other.flickerSpeed;
            flickerStrength = other.flickerStrength;
            flickerFixtureStagger = other.flickerFixtureStagger;

            goboStops = new List<MfvGoboStop>();
            foreach (var stop in other.goboStops)
            {
                goboStops.Add(new MfvGoboStop(stop));
            }

            selectedGoboStop = other.selectedGoboStop;
            goboPickerExpanded = other.goboPickerExpanded;
            goboPhasing = new MfvAnimatableValue(other.goboPhasing);
            goboRotationBeats = other.goboRotationBeats;
            goboFixtureStaggerDegrees = other.goboFixtureStaggerDegrees;
        }

        /// <summary>Both Tilt and Pan ranged, the condition for showing phase offset.</summary>
        public bool ShowPanTiltPhaseOffset => tilt.isRange && pan.isRange;

        public static MfvEffect Create(MfvEffectKind kind)
        {
            var effect = new MfvEffect { kind = kind };
            switch (kind)
            {
                case MfvEffectKind.Move:
                    effect.tilt.range = new Vector2(-40f, 35f);
                    effect.pan.range = new Vector2(-90f, 90f);
                    break;
                case MfvEffectKind.Cone:
                    effect.coneWidth.range = new Vector2(8f, 30f);
                    effect.coneLength.range = new Vector2(0f, 14f);
                    break;
                case MfvEffectKind.Color:
                    effect.colorPhasing.isRange = true;
                    break;
                case MfvEffectKind.Brightness:
                    effect.brightness.value = 100f;
                    break;
                case MfvEffectKind.Gobo:
                    effect.goboPhasing.isRange = true;
                    break;
            }

            return effect;
        }
    }
}
