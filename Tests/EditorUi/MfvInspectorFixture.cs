using UnityEngine;

namespace ManeuverForVRC.Tests
{
    /// <summary>
    /// A representative, fully populated clip with every effect kind, ranges, own
    /// phases, a gradient stop and an open gobo picker, so the layout tests exercise
    /// the widest rows rather than the model's defaults.
    /// </summary>
    public static class MfvInspectorFixture
    {
        public static MfvClipEffectSet Build()
        {
            var set = new MfvClipEffectSet
            {
                order = MfvOrderMode.Normal,
                phaseExpanded = true,
                addCatalogExpanded = true,
            };

            set.phase.mode = MfvPhaseMode.PingPong;
            set.phase.ease = MfvEaseType.InOutCubic;
            set.phase.pingPongRatio = 0.65f;
            set.phase.fixtureGroupSize = 2;
            set.phase.delay = 0.25f;
            set.phase.beatsPerCycle = 2f;
            set.phase.inverse = false;

            // Move: both axes ranged, Tilt as a spread range and Pan on its own Forward phase,
            // so the range frame and the spread frame are both measured.
            var move = set.Add(MfvEffectKind.Move);
            move.tilt.isRange = true;
            move.tilt.range = new Vector2(-40f, 35f);
            move.tilt.value = 10f;
            move.tilt.hasSpread = true;
            move.tilt.spreadRange = new Vector2(-10f, 20f);
            move.pan.isRange = true;
            move.pan.range = new Vector2(-90f, 90f);
            move.pan.useOwnPhase = true;
            move.pan.ownPhase.mode = MfvPhaseMode.Forward;
            move.pan.ownPhase.ease = MfvEaseType.Linear;
            move.pan.ownPhase.fixtureGroupSize = 1;
            move.pan.ownPhase.delay = 0.125f;
            move.pan.ownPhase.beatsPerCycle = 4f;
            move.pan.ownPhase.inverse = true;
            move.panTiltPhaseOffsetDegrees = 90f;

            // Move again: the odd copy turns in a circle, so those rows get measured too.
            var circle = set.Add(MfvEffectKind.Move);
            circle.moveMode = MfvMoveMode.Circle;
            circle.circleCenterTilt.value = 35f;
            circle.circleCenterPan.value = -120f;
            circle.circleCenterPan.hasSpread = true;
            circle.circleCenterPan.spread = -15f;
            circle.circleRadius.isRange = true;
            circle.circleRadius.range = new Vector2(5f, 22.5f);
            circle.circleAspect = 1.5f;

            // Cone
            var cone = set.Add(MfvEffectKind.Cone);
            cone.coneWidth.isRange = true;
            cone.coneWidth.range = new Vector2(8f, 30f);
            cone.coneLength.isRange = true;
            cone.coneLength.range = new Vector2(0f, 14f);

            // Color: palette of red / blue / gradient (selected) / black, split even / odd.
            var color = set.Add(MfvEffectKind.Color);
            color.colorStops.Clear();
            color.colorStops.Add(new MfvColorStop(new Color32(0xE0, 0x4F, 0x5F, 0xFF)));
            color.colorStops.Add(new MfvColorStop(new Color32(0x3F, 0xA9, 0xF5, 0xFF)));
            color.colorStops.Add(BuildGradientStop());
            color.colorStops.Add(new MfvColorStop(new Color32(0x10, 0x10, 0x14, 0xFF)));
            color.selectedColorStop = 2;
            color.colorPhasing.isRange = true;
            color.colorPhasing.timing = MfvTimingMode.PerCycle;

            set.Add(MfvEffectKind.Color);
            set.effects[set.IndexOf(MfvEffectKind.Color, MfvParity.Odd)].expanded = false;

            // Brightness
            var brightness = set.Add(MfvEffectKind.Brightness);
            brightness.brightness.value = 100f;
            brightness.blackoutOnReturn = true;
            brightness.blackoutFadeIn = 0.1f;
            brightness.blackoutFadeOut = 0.35f;

            // Gobo: a blink sequence (OFF appears twice) with the + picker open, so the
            // audit lays out the picker's full row of OFF plus every patterned gobo.
            var gobo = set.Add(MfvEffectKind.Gobo);
            gobo.goboStops.Add(new MfvGoboStop());
            gobo.goboStops.Add(new MfvGoboStop(2));
            gobo.goboStops.Add(new MfvGoboStop());
            gobo.goboStops.Add(new MfvGoboStop(3));
            gobo.selectedGoboStop = 1;
            gobo.goboPickerExpanded = true;
            gobo.goboPhasing.isRange = true;
            gobo.goboRotationBeats = 8f;
            gobo.goboFixtureStaggerDegrees = 45f;

            return set;
        }

        private static MfvColorStop BuildGradientStop()
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color32(0xB5, 0x6E, 0xE8, 0xFF), 0f),
                    new GradientColorKey(new Color32(0x3A, 0xE3, 0x7A, 0xFF), 1f),
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });

            return new MfvColorStop { isGradient = true, gradient = gradient };
        }
    }
}
