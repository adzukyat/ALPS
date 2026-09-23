using UnityEngine;

namespace AdzukiSoft.ALPS.Tests
{
    /// <summary>
    /// A representative, fully populated clip with every effect kind, ranges, own
    /// phases, a gradient stop and an open gobo picker, so the layout tests exercise
    /// the widest rows rather than the model's defaults.
    /// </summary>
    public static class AlpsInspectorFixture
    {
        public static AlpsClipEffectSet Build()
        {
            var set = new AlpsClipEffectSet
            {
                order = AlpsOrderMode.Normal,
                phaseExpanded = true,
                addCatalogExpanded = true,
            };

            set.phase.mode = AlpsPhaseMode.Wave;
            set.phase.ease = AlpsEaseType.InOutCubic;
            // Every part of the wave has a share, so each gets its percentage laid out.
            set.phase.SetShares(0.4f, 0.2f, 0.25f);
            set.phase.fixtureGroupSize = 2;
            set.phase.spread = 1.25f;
            set.phase.beatsPerCycle = 2f;
            set.phase.inverse = false;

            // Move: both axes ranged, Tilt moving between two spreads and Pan on its own Forward
            // phase, so the stacked spread sliders and the range frame are both measured.
            var move = set.Add(AlpsEffectKind.Move);
            move.tilt.isRange = true;
            move.tilt.range = new Vector2(-40f, 35f);
            move.tilt.value = 10f;
            move.tilt.hasSpread = true;
            move.tilt.spreadRange = new Vector2(-10f, 20f);
            move.tilt.spreadRangeEnd = new Vector2(35f, -40f);
            move.pan.isRange = true;
            move.pan.range = new Vector2(-90f, 90f);
            move.pan.useOwnPhase = true;
            move.pan.ownPhase.SetShares(1f, 0f, 0f);
            move.pan.ownPhase.ease = AlpsEaseType.Linear;
            move.pan.ownPhase.fixtureGroupSize = 1;
            move.pan.ownPhase.spread = 0.5f;
            move.pan.ownPhase.spreadInBeats = true;
            move.pan.ownPhase.spreadBeats = 2f;
            move.pan.ownPhase.beatsPerCycle = 4f;
            move.pan.ownPhase.inverse = true;
            move.panTiltPhaseOffsetDegrees = 90f;

            // Move again: the odd copy turns in a circle, so those rows get measured too.
            var circle = set.Add(AlpsEffectKind.Move);
            circle.moveMode = AlpsMoveMode.Circle;
            circle.circleCenterTilt.value = 35f;
            circle.circleCenterPan.value = -120f;
            circle.circleCenterPan.hasSpread = true;
            circle.circleCenterPan.spreadRange = new Vector2(-120f, -165f);
            circle.circleRadius.isRange = true;
            circle.circleRadius.range = new Vector2(5f, 22.5f);
            circle.circleAspect = 1.5f;

            // Cone
            var cone = set.Add(AlpsEffectKind.Cone);
            cone.coneWidth.isRange = true;
            cone.coneWidth.range = new Vector2(8f, 30f);
            cone.coneLength.isRange = true;
            cone.coneLength.range = new Vector2(0f, 14f);

            // Color: palette of red / blue / gradient (selected) / black, split even / odd.
            var color = set.Add(AlpsEffectKind.Color);
            color.colorStops.Clear();
            color.colorStops.Add(new AlpsColorStop(new Color32(0xE0, 0x4F, 0x5F, 0xFF)));
            color.colorStops.Add(new AlpsColorStop(new Color32(0x3F, 0xA9, 0xF5, 0xFF)));
            color.colorStops.Add(BuildGradientStop());
            color.colorStops.Add(new AlpsColorStop(new Color32(0x10, 0x10, 0x14, 0xFF)));
            color.selectedColorStop = 2;
            color.colorPhasing.isRange = true;
            color.colorPhasing.timing = AlpsTimingMode.PerCycle;

            set.Add(AlpsEffectKind.Color);
            set.effects[set.IndexOf(AlpsEffectKind.Color, AlpsParity.Odd)].expanded = false;

            // Brightness
            var brightness = set.Add(AlpsEffectKind.Brightness);
            brightness.brightness.value = 100f;
            brightness.blackoutOnReturn = true;
            brightness.blackoutFadeIn = 0.1f;
            brightness.blackoutFadeOut = 0.35f;
            brightness.phaseOffset = 0.25f;

            // Gobo: a blink sequence (OFF appears twice) with the + picker open, so the
            // audit lays out the picker's full row of OFF plus every patterned gobo.
            var gobo = set.Add(AlpsEffectKind.Gobo);
            gobo.goboStops.Add(new AlpsGoboStop());
            gobo.goboStops.Add(new AlpsGoboStop(2));
            gobo.goboStops.Add(new AlpsGoboStop());
            gobo.goboStops.Add(new AlpsGoboStop(3));
            gobo.selectedGoboStop = 1;
            gobo.goboPickerExpanded = true;
            gobo.goboPhasing.isRange = true;
            gobo.goboRotationBeats = 8f;
            gobo.goboFixtureStaggerDegrees = 45f;

            return set;
        }

        /// <summary>
        /// A clip whose cards end on a parameter row: nothing on them follows a phase, so
        /// the phase offset row below the parameters stays hidden. The cone ends on a spread.
        /// </summary>
        public static AlpsClipEffectSet BuildPlain()
        {
            var set = new AlpsClipEffectSet();
            var cone = set.Add(AlpsEffectKind.Cone);
            cone.coneLength.hasSpread = true;
            cone.coneLength.spreadRange = new Vector2(2f, 6f);
            set.Add(AlpsEffectKind.Brightness);
            return set;
        }

        /// <summary>
        /// An arrangement of <paramref name="shape"/> with its widest rows showing: a random
        /// order with its seed, a target to face, and spreads on values of every kind.
        /// </summary>
        public static AlpsArrangementSettings BuildArrangement(AlpsArrangementShape shape)
        {
            var settings = new AlpsArrangementSettings
            {
                shape = shape,
                spacing = AlpsArrangementSpacing.Centered,
                order = AlpsOrderMode.Random,
                seed = 12,
                facing = AlpsArrangementFacing.Target,
                sides = 8,
                columns = 6,
                lineStart = new Vector3(-12.5f, 3.25f, -0.125f),
                lineEnd = new Vector3(12.5f, 3.25f, 10.875f),
                target = new Vector3(0f, -4.5f, 12.75f),
            };

            settings.radius.hasSpread = true;
            settings.radius.spreadRange = new Vector2(3f, 12.5f);
            settings.sweep.value = 135f;
            settings.angle.value = -45f;
            settings.width.hasSpread = true;
            settings.width.spreadRange = new Vector2(24f, 8f);
            settings.depth.value = 12.25f;
            settings.height.hasSpread = true;
            settings.height.spreadRange = new Vector2(-2.5f, 4f);
            settings.rotationY.hasSpread = true;
            settings.rotationY.spreadRange = new Vector2(-135f, 90f);
            settings.rotationX.value = -35.5f;

            // The last row of a card with its spread open, so the card's end is measured
            // below a spread slider as well as below a value slider.
            settings.rotationZ.hasSpread = true;
            settings.rotationZ.spreadRange = new Vector2(15f, -15f);
            return settings;
        }

        private static AlpsColorStop BuildGradientStop()
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color32(0xB5, 0x6E, 0xE8, 0xFF), 0f),
                    new GradientColorKey(new Color32(0x3A, 0xE3, 0x7A, 0xFF), 1f),
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });

            return new AlpsColorStop { isGradient = true, gradient = gradient };
        }
    }
}
