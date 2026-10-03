using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>
    /// The container inspector: the shape, spacing, order and symmetric switch on top, then
    /// the shape's size, the facing and the offsets in cards. While the shape is off only the
    /// shape shows, since nothing is laid out.
    ///
    /// Like the clip view it owns no state. It edits an <see cref="AlpsArrangementSettings"/>
    /// in place and reports every edit through <see cref="Changed"/>, and the host records
    /// undo and lays the children out. Rows are the clip's parameter rows without R, since
    /// nothing moves a layout over time yet, so S spreads a value over the children.
    /// </summary>
    public class AlpsArrangementView : VisualElement
    {
        private const string CardStateKey = "AdzukiSoft.ALPS.ArrangementCard.";

        private readonly AlpsArrangementSettings _settings;
        private readonly AlpsArrangementSettings _defaults = new AlpsArrangementSettings();
        private readonly List<AlpsAnimatableView> _animatables = new List<AlpsAnimatableView>();

        private readonly AlpsSegmentedControl _shape;
        private readonly AlpsSegmentedControl _spacing;
        private readonly AlpsSegmentedControl _order;
        private readonly AlpsToggleSwitch _symmetric;
        private readonly AlpsStepper _seed;

        private readonly AlpsVectorField _start;
        private readonly AlpsVectorField _end;
        private readonly AlpsStepper _sides;
        private readonly AlpsStepper _columns;
        private readonly AlpsAnimatableView _radius;
        private readonly AlpsAnimatableView _angle;
        private readonly AlpsAnimatableView _sweep;
        private readonly AlpsAnimatableView _width;
        private readonly AlpsAnimatableView _depth;

        private readonly AlpsSegmentedControl _facing;
        private readonly AlpsVectorField _target;
        private readonly VisualElement _cards;

        public AlpsArrangementView(AlpsArrangementSettings settings)
        {
            _settings = settings;
            settings.EnsureLimits();
            AddToClassList("alps-root");

            var styleSheet = AlpsStyleSheets.Load(AlpsClipInspectorView.StyleSheetName);
            if (styleSheet != null)
            {
                styleSheets.Add(styleSheet);
            }

            AlpsInspectorFont.Apply(this);

            // --- Shape, spacing and order ----------------------------------
            var fields = new VisualElement();
            fields.AddToClassList("alps-flow9");
            Add(fields);

            _shape = new AlpsSegmentedControl(
                AlpsStrings.Tr("arrangement.shape"),
                AlpsStrings.Tr("arrangement.shape.off"),
                AlpsStrings.Tr("arrangement.shape.line"),
                AlpsStrings.Tr("arrangement.shape.circle"),
                AlpsStrings.Tr("arrangement.shape.polygon"),
                AlpsStrings.Tr("arrangement.shape.rectangle"),
                AlpsStrings.Tr("arrangement.shape.grid"))
            {
                tooltip = AlpsStrings.Tr("arrangement.shape.tip"),
            };
            _shape.SetValueWithoutNotify((int)settings.shape);
            _shape.RegisterValueChangedCallback(evt =>
            {
                settings.shape = (AlpsArrangementShape)evt.newValue;
                Edited();
            });
            fields.Add(_shape);

            _spacing = new AlpsSegmentedControl(
                AlpsStrings.Tr("arrangement.spacing"),
                AlpsStrings.Tr("arrangement.spacing.endToEnd"),
                AlpsStrings.Tr("arrangement.spacing.even"))
            {
                tooltip = AlpsStrings.Tr("arrangement.spacing.tip"),
            };
            _spacing.SetValueWithoutNotify((int)settings.spacing);
            _spacing.RegisterValueChangedCallback(evt =>
            {
                settings.spacing = (AlpsArrangementSpacing)evt.newValue;
                Edited();
            });
            fields.Add(_spacing);

            _order = new AlpsSegmentedControl(
                AlpsStrings.Tr("common.order"),
                AlpsStrings.Tr("common.order.normal"),
                AlpsStrings.Tr("common.order.reverse"),
                AlpsStrings.Tr("common.order.random"))
            {
                tooltip = AlpsStrings.Tr("arrangement.order.tip"),
            };
            _order.SetValueWithoutNotify((int)settings.order);
            _order.RegisterValueChangedCallback(evt =>
            {
                settings.order = (AlpsOrderMode)evt.newValue;
                Edited();
            });
            fields.Add(_order);

            _symmetric = new AlpsToggleSwitch(AlpsStrings.Tr("common.symmetric"))
            {
                tooltip = AlpsStrings.Tr("arrangement.symmetric.tip"),
            };
            _symmetric.SetValueWithoutNotify(settings.symmetric);
            _symmetric.RegisterValueChangedCallback(evt =>
            {
                settings.symmetric = evt.newValue;
                Edited();
            });
            fields.Add(_symmetric);

            _seed = new AlpsStepper(AlpsStrings.Tr("common.seed"), string.Empty, 1f)
            {
                Minimum = 0f,
                tooltip = AlpsStrings.Tr("arrangement.seed.tip"),
            };
            _seed.SetValueWithoutNotify(settings.seed);
            _seed.RegisterValueChangedCallback(evt =>
            {
                settings.seed = Mathf.Max(0, Mathf.RoundToInt(evt.newValue));
                Edited();
            });
            fields.Add(_seed);

            var cards = new VisualElement();
            cards.AddToClassList("alps-stack");
            Add(cards);
            _cards = cards;

            // --- Shape card ------------------------------------------------
            var shapeCard = Card(AlpsStrings.Tr("arrangement.card.shape.title"), AlpsStrings.Tr("arrangement.card.shape.desc"), "shape");
            cards.Add(shapeCard);

            _start = Vector(AlpsStrings.Tr("common.start"), settings.lineStart, value => settings.lineStart = value);
            _end = Vector(AlpsStrings.Tr("common.end"), settings.lineEnd, value => settings.lineEnd = value);
            shapeCard.Body.Add(_start);
            shapeCard.Body.Add(_end);

            _sides = Stepper(AlpsStrings.Tr("arrangement.sides"), settings.sides, AlpsArrangementEvaluator.MinSides, AlpsArrangementEvaluator.MaxSides,
                value => settings.sides = value);
            _columns = Stepper(AlpsStrings.Tr("arrangement.columns"), settings.columns, 1, AlpsArrangementEvaluator.MaxSides,
                value => settings.columns = value);
            shapeCard.Body.Add(_sides);
            shapeCard.Body.Add(_columns);

            _radius = Animatable(AlpsStrings.Tr("common.radius"), settings.radius, _defaults.radius, "m", "0.##", Meters);
            _angle = Animatable(AlpsStrings.Tr("common.rotation"), settings.angle, _defaults.angle, "°", "0.#", AlpsSnapPoints.Angles);
            _angle.tooltip = AlpsStrings.Tr("arrangement.angle.tip");
            _sweep = Animatable(AlpsStrings.Tr("arrangement.sweep"), settings.sweep, _defaults.sweep, "°", "0.#", AlpsSnapPoints.Angles);
            _sweep.tooltip = AlpsStrings.Tr("arrangement.sweep.tip");
            _width = Animatable(AlpsStrings.Tr("common.width"), settings.width, _defaults.width, "m", "0.##", Meters);
            _depth = Animatable(AlpsStrings.Tr("arrangement.depth"), settings.depth, _defaults.depth, "m", "0.##", Meters);
            shapeCard.Body.Add(_radius);
            shapeCard.Body.Add(_angle);
            shapeCard.Body.Add(_sweep);
            shapeCard.Body.Add(_width);
            shapeCard.Body.Add(_depth);

            // --- Facing card -----------------------------------------------
            var facingCard = Card(AlpsStrings.Tr("arrangement.card.facing.title"), AlpsStrings.Tr("arrangement.card.facing.desc"), "facing");
            cards.Add(facingCard);

            _facing = new AlpsSegmentedControl(
                AlpsStrings.Tr("arrangement.facing"),
                AlpsStrings.Tr("arrangement.facing.asIs"),
                AlpsStrings.Tr("arrangement.facing.outward"),
                AlpsStrings.Tr("arrangement.facing.inward"),
                AlpsStrings.Tr("arrangement.facing.forward"),
                AlpsStrings.Tr("arrangement.facing.target"))
            {
                tooltip = AlpsStrings.Tr("arrangement.facing.tip"),
            };
            _facing.SetValueWithoutNotify((int)settings.facing);
            _facing.RegisterValueChangedCallback(evt =>
            {
                settings.facing = (AlpsArrangementFacing)evt.newValue;
                Edited();
            });
            facingCard.Body.Add(_facing);

            _target = Vector(AlpsStrings.Tr("arrangement.target"), settings.target, value => settings.target = value);
            facingCard.Body.Add(_target);

            facingCard.Body.Add(Animatable(AlpsStrings.Tr("common.rotationX"), settings.rotationX, _defaults.rotationX, "°", "0.#", AlpsSnapPoints.Angles));
            facingCard.Body.Add(Animatable(AlpsStrings.Tr("common.rotationY"), settings.rotationY, _defaults.rotationY, "°", "0.#", AlpsSnapPoints.Angles));
            facingCard.Body.Add(Animatable(AlpsStrings.Tr("common.rotationZ"), settings.rotationZ, _defaults.rotationZ, "°", "0.#", AlpsSnapPoints.Angles));

            // --- Offset card -----------------------------------------------
            var offsetCard = Card(AlpsStrings.Tr("arrangement.card.offset.title"), AlpsStrings.Tr("arrangement.card.offset.desc"), "offset");
            cards.Add(offsetCard);

            offsetCard.Body.Add(Animatable(AlpsStrings.Tr("common.height"), settings.height, _defaults.height, "m", "0.##", AlpsSnapPoints.Zero));
            var outward = Animatable(AlpsStrings.Tr("arrangement.outward"), settings.outward, _defaults.outward, "m", "0.##", AlpsSnapPoints.Zero);
            outward.tooltip = AlpsStrings.Tr("arrangement.outward.tip");
            offsetCard.Body.Add(outward);

            Refresh();
        }

        /// <summary>Raised after any edit so the host can lay the children out.</summary>
        public event Action Changed;

        /// <summary>
        /// Shows the settings' values again after something other than this view changed
        /// them, such as a scene handle.
        /// </summary>
        public void Reload()
        {
            _shape.SetValueWithoutNotify((int)_settings.shape);
            _spacing.SetValueWithoutNotify((int)_settings.spacing);
            _order.SetValueWithoutNotify((int)_settings.order);
            _symmetric.SetValueWithoutNotify(_settings.symmetric);
            _seed.SetValueWithoutNotify(_settings.seed);
            _start.SetValueWithoutNotify(_settings.lineStart);
            _end.SetValueWithoutNotify(_settings.lineEnd);
            _sides.SetValueWithoutNotify(_settings.sides);
            _columns.SetValueWithoutNotify(_settings.columns);
            _facing.SetValueWithoutNotify((int)_settings.facing);
            _target.SetValueWithoutNotify(_settings.target);
            foreach (var animatable in _animatables)
            {
                animatable.Reload();
            }

            Refresh();
        }

        /// <summary>Shows the rows the current shape, facing and order use.</summary>
        public void Refresh()
        {
            var shape = _settings.shape;
            var arranges = shape != AlpsArrangementShape.Off;
            var round = shape == AlpsArrangementShape.Circle || shape == AlpsArrangementShape.Polygon;
            var boxed = shape == AlpsArrangementShape.Rectangle || shape == AlpsArrangementShape.Grid;

            AlpsPhaseSettingsView.Show(_spacing, arranges);
            AlpsPhaseSettingsView.Show(_order, arranges);
            AlpsPhaseSettingsView.Show(_symmetric, arranges);
            AlpsPhaseSettingsView.Show(_seed, arranges && _settings.order == AlpsOrderMode.Random);
            AlpsPhaseSettingsView.Show(_cards, arranges);
            AlpsPhaseSettingsView.Show(_start, shape == AlpsArrangementShape.Line);
            AlpsPhaseSettingsView.Show(_end, shape == AlpsArrangementShape.Line);
            AlpsPhaseSettingsView.Show(_sides, shape == AlpsArrangementShape.Polygon);
            AlpsPhaseSettingsView.Show(_columns, shape == AlpsArrangementShape.Grid);
            AlpsPhaseSettingsView.Show(_radius, round);
            AlpsPhaseSettingsView.Show(_angle, round);
            AlpsPhaseSettingsView.Show(_sweep, shape == AlpsArrangementShape.Circle);
            AlpsPhaseSettingsView.Show(_width, boxed);
            AlpsPhaseSettingsView.Show(_depth, boxed);
            AlpsPhaseSettingsView.Show(_target, _settings.facing == AlpsArrangementFacing.Target);

            foreach (var animatable in _animatables)
            {
                animatable.Refresh();
            }
        }

        private void Edited()
        {
            Refresh();
            Changed?.Invoke();
        }

        private static float[] Meters(Vector2 limit)
        {
            return AlpsSnapPoints.Multiples(limit, 5f);
        }

        /// <summary>A card that remembers whether it was open for the rest of the editor session.</summary>
        private static AlpsEffectCard Card(string title, string description, string key)
        {
            var card = new AlpsEffectCard(title, description, showActions: false, showDelete: false);
            card.Expanded = SessionState.GetBool(CardStateKey + key, true);
            card.ExpandedChanged += expanded => SessionState.SetBool(CardStateKey + key, expanded);
            return card;
        }

        private AlpsAnimatableView Animatable(
            string label,
            AlpsAnimatableValue model,
            AlpsAnimatableValue defaults,
            string unit,
            string format,
            Func<Vector2, float[]> snaps)
        {
            var view = new AlpsAnimatableView(
                label,
                model,
                null,
                () => Changed?.Invoke(),
                unit,
                format,
                snaps: snaps,
                defaults: defaults,
                allowRange: false,
                spreadFirstTip: AlpsStrings.Tr("arrangement.spread.first"),
                spreadLastTip: AlpsStrings.Tr("arrangement.spread.last"));
            _animatables.Add(view);
            return view;
        }

        private AlpsVectorField Vector(string label, Vector3 value, Action<Vector3> write)
        {
            var field = new AlpsVectorField(label);
            field.SetValueWithoutNotify(value);
            field.RegisterValueChangedCallback(evt =>
            {
                write(evt.newValue);
                Edited();
            });
            return field;
        }

        private AlpsStepper Stepper(string label, int value, int minimum, int maximum, Action<int> write)
        {
            var stepper = new AlpsStepper(label, string.Empty, 1f)
            {
                Minimum = minimum,
                Maximum = maximum,
            };
            stepper.SetValueWithoutNotify(value);
            stepper.RegisterValueChangedCallback(evt =>
            {
                write(Mathf.Clamp(Mathf.RoundToInt(evt.newValue), minimum, maximum));
                Edited();
            });
            return stepper;
        }
    }
}
