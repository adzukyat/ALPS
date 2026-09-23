using UnityEngine;
using UnityEngine.UIElements;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>
    /// A point in space: label | X box | Y box | Z box. Unity's Vector3Field would pick up
    /// the inspector's label column rule on its axis labels and overrun the row, so this
    /// lines up three of the usual value boxes instead.
    /// </summary>
    public class AlpsVectorField : BaseField<Vector3>
    {
        public new static readonly string ussClassName = "alps-vector";

        private static readonly string[] Axes = { "X", "Y", "Z" };

        private readonly AlpsNumberBox[] _boxes = new AlpsNumberBox[3];

        public AlpsVectorField(string label, string unit = "m", string format = "0.###")
            : base(label, new VisualElement())
        {
            AddToClassList(ussClassName);

            var container = this.Q(className: BaseField<Vector3>.inputUssClassName);
            container.AddToClassList(ussClassName + "__input");

            for (var i = 0; i < Axes.Length; i++)
            {
                var axis = i;
                var axisLabel = new Label(Axes[i]);
                axisLabel.AddToClassList(ussClassName + "__axis");
                if (i > 0)
                {
                    axisLabel.AddToClassList(ussClassName + "__axis--gap");
                }

                var box = new AlpsNumberBox(unit, format) { tooltip = Axes[i] };
                box.AddToClassList(ussClassName + "__box");
                box.RegisterValueChangedCallback(evt =>
                {
                    // The box's own change event bubbles past this field, so only this
                    // field's change is left for the view to hear.
                    evt.StopPropagation();
                    var vector = value;
                    vector[axis] = evt.newValue;
                    value = vector;
                });

                container.Add(axisLabel);
                container.Add(box);
                _boxes[i] = box;
            }

            SetValueWithoutNotify(Vector3.zero);
        }

        public override void SetValueWithoutNotify(Vector3 newValue)
        {
            base.SetValueWithoutNotify(newValue);
            for (var i = 0; i < _boxes.Length; i++)
            {
                _boxes[i].SetValueWithoutNotify(newValue[i]);
            }
        }
    }
}
