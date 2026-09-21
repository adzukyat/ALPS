using System;
using System.Collections.Generic;
using UnityEngine;

namespace AdzukiSoft.ALPS
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
    public class AlpsEffect
    {
        public AlpsEffectKind kind;
        public AlpsParity parity = AlpsParity.All;
        public bool expanded = true;

        // --- Move ---
        public AlpsMoveMode moveMode = AlpsMoveMode.Angle;
        public AlpsAnimatableValue tilt = new AlpsAnimatableValue(0f, new Vector2(-90f, 90f));
        public AlpsAnimatableValue pan = new AlpsAnimatableValue(0f, new Vector2(-180f, 180f));
        /// <summary>Phase offset. Only meaningful while both Tilt and Pan are ranges.</summary>
        public float panTiltPhaseOffsetDegrees = 90f;
        public string trackUserName = string.Empty;
        public float trackSpeed = 5.5f;

        // --- Move: circle ---
        /// <summary>Direction the circle turns around, in the same angles as Tilt and Pan.</summary>
        public AlpsAnimatableValue circleCenterTilt = new AlpsAnimatableValue(30f, new Vector2(-90f, 90f));
        public AlpsAnimatableValue circleCenterPan = new AlpsAnimatableValue(0f, new Vector2(-180f, 180f));
        /// <summary>Opening angle from the center direction to the traced ring.</summary>
        public AlpsAnimatableValue circleRadius = new AlpsAnimatableValue(10f, new Vector2(0f, 90f));
        /// <summary>Width over height. 1 is a circle, anything else an ellipse.</summary>
        public float circleAspect = 1f;

        // --- Cone ---
        public AlpsAnimatableValue coneWidth = new AlpsAnimatableValue(15f, new Vector2(0f, 90f));
        public AlpsAnimatableValue coneLength = new AlpsAnimatableValue(14f, new Vector2(0f, 50f));

        // --- Color ---
        /// <summary>
        /// The palette is the range: no stops means OFF (nothing to play), one stop is a
        /// static value, and two or more are walked by the phase. So a new effect starts
        /// empty rather than pre-filled, because a pre-filled palette would animate immediately.
        /// </summary>
        public List<AlpsColorStop> colorStops = new List<AlpsColorStop>();
        public int selectedColorStop;
        /// <summary>Carries timing / own phase for the palette.</summary>
        public AlpsAnimatableValue colorPhasing = new AlpsAnimatableValue(0f, new Vector2(0f, 1f));

        // --- Brightness ---
        /// <summary>Stored on the 0–200 scale the inspector shows. 100 is white at a tint of 1.</summary>
        public AlpsAnimatableValue brightness = new AlpsAnimatableValue(100f, new Vector2(0f, 200f)) { range = new Vector2(0f, 100f) };
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
        public List<AlpsGoboStop> goboStops = new List<AlpsGoboStop>();
        public int selectedGoboStop;
        /// <summary>Whether the + picker under the palette is open (UI state, like <see cref="expanded"/>).</summary>
        public bool goboPickerExpanded;
        public AlpsAnimatableValue goboPhasing = new AlpsAnimatableValue(0f, new Vector2(0f, 1f));
        /// <summary>Rotation speed in beats per full turn.</summary>
        public float goboRotationBeats = 8f;
        public float goboFixtureStaggerDegrees = 45f;

        public AlpsEffect() { }

        public AlpsEffect(AlpsEffect other)
        {
            kind = other.kind;
            parity = other.parity;
            expanded = other.expanded;

            moveMode = other.moveMode;
            tilt = new AlpsAnimatableValue(other.tilt);
            pan = new AlpsAnimatableValue(other.pan);
            panTiltPhaseOffsetDegrees = other.panTiltPhaseOffsetDegrees;
            trackUserName = other.trackUserName;
            trackSpeed = other.trackSpeed;
            circleCenterTilt = new AlpsAnimatableValue(other.circleCenterTilt);
            circleCenterPan = new AlpsAnimatableValue(other.circleCenterPan);
            circleRadius = new AlpsAnimatableValue(other.circleRadius);
            circleAspect = other.circleAspect;

            coneWidth = new AlpsAnimatableValue(other.coneWidth);
            coneLength = new AlpsAnimatableValue(other.coneLength);

            colorStops = new List<AlpsColorStop>();
            foreach (var stop in other.colorStops)
            {
                colorStops.Add(new AlpsColorStop(stop));
            }

            selectedColorStop = other.selectedColorStop;
            colorPhasing = new AlpsAnimatableValue(other.colorPhasing);

            brightness = new AlpsAnimatableValue(other.brightness);
            blackoutOnReturn = other.blackoutOnReturn;
            blackoutFadeIn = other.blackoutFadeIn;
            blackoutFadeOut = other.blackoutFadeOut;

            flickerSpeed = other.flickerSpeed;
            flickerStrength = other.flickerStrength;
            flickerFixtureStagger = other.flickerFixtureStagger;

            goboStops = new List<AlpsGoboStop>();
            foreach (var stop in other.goboStops)
            {
                goboStops.Add(new AlpsGoboStop(stop));
            }

            selectedGoboStop = other.selectedGoboStop;
            goboPickerExpanded = other.goboPickerExpanded;
            goboPhasing = new AlpsAnimatableValue(other.goboPhasing);
            goboRotationBeats = other.goboRotationBeats;
            goboFixtureStaggerDegrees = other.goboFixtureStaggerDegrees;
        }

        /// <summary>Both Tilt and Pan ranged, the condition for showing phase offset.</summary>
        public bool ShowPanTiltPhaseOffset => tilt.isRange && pan.isRange;

        public static AlpsEffect Create(AlpsEffectKind kind)
        {
            var effect = new AlpsEffect { kind = kind };
            switch (kind)
            {
                case AlpsEffectKind.Move:
                    effect.tilt.range = new Vector2(-40f, 35f);
                    effect.pan.range = new Vector2(-90f, 90f);
                    break;
                case AlpsEffectKind.Cone:
                    effect.coneWidth.range = new Vector2(8f, 30f);
                    effect.coneLength.range = new Vector2(0f, 14f);
                    break;
                case AlpsEffectKind.Color:
                    effect.colorPhasing.isRange = true;
                    break;
                case AlpsEffectKind.Brightness:
                    effect.brightness.value = 100f;
                    break;
                case AlpsEffectKind.Gobo:
                    effect.goboPhasing.isRange = true;
                    break;
            }

            return effect;
        }
    }
}
