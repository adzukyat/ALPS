using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>
    /// The container inspector: the shape, spacing and order on top, then the shape's size,
    /// the facing and the offsets in cards. While the shape is off only the shape shows,
    /// since nothing is laid out.
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

            _shape = new AlpsSegmentedControl("形状", "オフ", "直線", "円", "多角形", "矩形", "グリッド")
            {
                tooltip = "子オブジェクトを並べる形です。オフでは子オブジェクトを動かしません。円は角度を360°未満にすると円弧になります。",
            };
            _shape.SetValueWithoutNotify((int)settings.shape);
            _shape.RegisterValueChangedCallback(evt =>
            {
                settings.shape = (AlpsArrangementShape)evt.newValue;
                Edited();
            });
            fields.Add(_shape);

            _spacing = new AlpsSegmentedControl("配置", "端から端", "均等割り")
            {
                tooltip = "端から端: 最初と最後のオブジェクトを線の両端に置きます。均等割り: 全体をオブジェクトの数で等分し、それぞれの真ん中に置きます。",
            };
            _spacing.SetValueWithoutNotify((int)settings.spacing);
            _spacing.RegisterValueChangedCallback(evt =>
            {
                settings.spacing = (AlpsArrangementSpacing)evt.newValue;
                Edited();
            });
            fields.Add(_spacing);

            _order = new AlpsSegmentedControl("並び順", "通常", "逆順", "左右対称", "ランダム")
            {
                tooltip = "S(広がり)の値をどの順番でオブジェクトに割り当てるかです。置く場所の順番は変わりません。左右対称では前半のY回転とZ回転が反転します。",
            };
            _order.SetValueWithoutNotify((int)settings.order);
            _order.RegisterValueChangedCallback(evt =>
            {
                settings.order = (AlpsOrderMode)evt.newValue;
                Edited();
            });
            fields.Add(_order);

            _seed = new AlpsStepper("シード", string.Empty, 1f)
            {
                Minimum = 0f,
                tooltip = "ランダムな並び順を変えます。",
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
            var shapeCard = Card("形", "並べる線や円の大きさです。始点、終点、半径、幅と奥行きはシーン上のハンドルでも動かせます。", "shape");
            cards.Add(shapeCard);

            _start = Vector("始点", settings.lineStart, value => settings.lineStart = value);
            _end = Vector("終点", settings.lineEnd, value => settings.lineEnd = value);
            shapeCard.Body.Add(_start);
            shapeCard.Body.Add(_end);

            _sides = Stepper("辺の数", settings.sides, AlpsArrangementEvaluator.MinSides, AlpsArrangementEvaluator.MaxSides,
                value => settings.sides = value);
            _columns = Stepper("列数", settings.columns, 1, AlpsArrangementEvaluator.MaxSides,
                value => settings.columns = value);
            shapeCard.Body.Add(_sides);
            shapeCard.Body.Add(_columns);

            _radius = Animatable("半径", settings.radius, _defaults.radius, "m", "0.##", Meters);
            _angle = Animatable("回転", settings.angle, _defaults.angle, "°", "0.#", AlpsSnapPoints.Angles);
            _angle.tooltip = "形全体を回します。円弧では円弧の真ん中の向きです。";
            _sweep = Animatable("角度", settings.sweep, _defaults.sweep, "°", "0.#", AlpsSnapPoints.Angles);
            _sweep.tooltip = "円のうち何度分に並べるかです。360°未満で円弧になります。";
            _width = Animatable("幅", settings.width, _defaults.width, "m", "0.##", Meters);
            _depth = Animatable("奥行き", settings.depth, _defaults.depth, "m", "0.##", Meters);
            shapeCard.Body.Add(_radius);
            shapeCard.Body.Add(_angle);
            shapeCard.Body.Add(_sweep);
            shapeCard.Body.Add(_width);
            shapeCard.Body.Add(_depth);

            // --- Facing card -----------------------------------------------
            var facingCard = Card("向き", "オブジェクトの向きです。X/Y/Z回転は向きを決めた後に各オブジェクトの軸で回り、Sで1つずつ変えるとファンになります。", "facing");
            cards.Add(facingCard);

            _facing = new AlpsSegmentedControl("向き", "そのまま", "外向き", "内向き", "進行方向", "注視点")
            {
                tooltip = "そのまま: コンテナーと同じ向き。外向き/内向き: 形の外側か内側。進行方向: 並ぶ方向。注視点: 指定した点の方。どれも前(+Z)をその方向へ向け、上はできるだけ上のままにします。",
            };
            _facing.SetValueWithoutNotify((int)settings.facing);
            _facing.RegisterValueChangedCallback(evt =>
            {
                settings.facing = (AlpsArrangementFacing)evt.newValue;
                Edited();
            });
            facingCard.Body.Add(_facing);

            _target = Vector("注視点", settings.target, value => settings.target = value);
            facingCard.Body.Add(_target);

            facingCard.Body.Add(Animatable("X回転", settings.rotationX, _defaults.rotationX, "°", "0.#", AlpsSnapPoints.Angles));
            facingCard.Body.Add(Animatable("Y回転", settings.rotationY, _defaults.rotationY, "°", "0.#", AlpsSnapPoints.Angles));
            facingCard.Body.Add(Animatable("Z回転", settings.rotationZ, _defaults.rotationZ, "°", "0.#", AlpsSnapPoints.Angles));

            // --- Offset card -----------------------------------------------
            var offsetCard = Card("位置", "形の上からずらす量です。Sで1つずつ変えると、高さなら階段や螺旋、外側なら左右対称と合わせてV字になります。", "offset");
            cards.Add(offsetCard);

            offsetCard.Body.Add(Animatable("高さ", settings.height, _defaults.height, "m", "0.##", AlpsSnapPoints.Zero));
            var outward = Animatable("外側", settings.outward, _defaults.outward, "m", "0.##", AlpsSnapPoints.Zero);
            outward.tooltip = "形の外側へずらします。直線では前(+Z)側です。";
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
                spreadFirstTip: "最初のオブジェクト",
                spreadLastTip: "最後のオブジェクト");
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
