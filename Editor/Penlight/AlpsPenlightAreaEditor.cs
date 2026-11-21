using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>
    /// The shape, size and slot count of a penlight area, in the look of the container's
    /// inspector. Every edit keeps the hidden slots at the count and has the preview draw
    /// the area again.
    /// </summary>
    [CustomEditor(typeof(AlpsPenlightArea))]
    public class AlpsPenlightAreaEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var area = (AlpsPenlightArea)target;
            var host = new VisualElement();

            // An area added by Add Component or a script has no slots yet.
            AlpsPenlightSlots.Sync(area, recordUndo: true);

            void Rebuild()
            {
                host.Clear();
                if (area != null)
                {
                    host.Add(BuildView(area));
                }
            }

            host.RegisterCallback<AttachToPanelEvent>(_ => Undo.undoRedoPerformed += Rebuild);
            host.RegisterCallback<DetachFromPanelEvent>(_ => Undo.undoRedoPerformed -= Rebuild);
            Rebuild();
            return host;
        }

        private static VisualElement BuildView(AlpsPenlightArea area)
        {
            var view = new VisualElement { name = "penlight-area" };
            view.AddToClassList("alps-root");
            view.AddToClassList("alps-root--component");
            var styleSheet = AlpsStyleSheets.Load(AlpsClipInspectorView.StyleSheetName);
            if (styleSheet != null)
            {
                view.styleSheets.Add(styleSheet);
            }

            AlpsInspectorFont.Apply(view);
            view.RegisterCallback<PointerDownEvent>(_ => Snapshot(area), TrickleDown.TrickleDown);
            view.RegisterCallback<FocusInEvent>(_ => Snapshot(area), TrickleDown.TrickleDown);

            var fields = new VisualElement();
            fields.AddToClassList("alps-flow9");
            view.Add(fields);

            var size = new AlpsStepper(AlpsStrings.Tr("penlight.size.length"), "m", 0.5f)
            {
                name = "penlight-size",
                Minimum = 0.01f,
                tooltip = AlpsStrings.Tr("penlight.size.tip"),
            };

            void ShowSize()
            {
                size.label = AlpsStrings.Tr(area.shape == AlpsPenlightShape.Radial ? "common.radius" : "penlight.size.length");
                size.style.display = area.shape == AlpsPenlightShape.Angle ? DisplayStyle.None : DisplayStyle.Flex;
            }

            var shape = new AlpsSegmentedControl(
                AlpsStrings.Tr("arrangement.shape"),
                AlpsStrings.Tr("penlight.shape.line"),
                AlpsStrings.Tr("penlight.shape.radial"),
                AlpsStrings.Tr("penlight.shape.angle"))
            {
                name = "penlight-shape",
                tooltip = AlpsStrings.Tr("penlight.shape.tip"),
            };
            shape.SetValueWithoutNotify((int)area.shape);
            shape.RegisterValueChangedCallback(evt =>
            {
                area.shape = (AlpsPenlightShape)evt.newValue;
                ShowSize();
                Edited(area);
            });
            fields.Add(shape);

            size.SetValueWithoutNotify(area.size);
            size.RegisterValueChangedCallback(evt =>
            {
                area.size = Mathf.Max(0.01f, evt.newValue);
                Edited(area);
            });
            fields.Add(size);
            ShowSize();

            var slots = new AlpsStepper(AlpsStrings.Tr("penlight.slots"), string.Empty, 1f)
            {
                name = "penlight-slots",
                Minimum = 1f,
                Maximum = AlpsShowPlayer.GpuMaxPenlightSlots,
                tooltip = AlpsStrings.Tr("penlight.slots.tip"),
            };
            slots.SetValueWithoutNotify(area.slots);
            slots.RegisterValueChangedCallback(evt =>
            {
                area.slots = Mathf.Clamp(Mathf.RoundToInt(evt.newValue), 1, AlpsShowPlayer.GpuMaxPenlightSlots);
                AlpsPenlightSlots.Sync(area, recordUndo: true);
                Edited(area);
            });
            fields.Add(slots);

            var help = new Label(AlpsStrings.Tr("penlight.help")) { name = "penlight-help" };
            help.style.whiteSpace = WhiteSpace.Normal;
            help.style.opacity = 0.7f;
            view.Add(help);
            return view;
        }

        private static void Snapshot(AlpsPenlightArea area)
        {
            Undo.RegisterCompleteObjectUndo(area, "Edit Penlight Area");
        }

        private static void Edited(AlpsPenlightArea area)
        {
            EditorUtility.SetDirty(area);
            AlpsPreviewDriver.MarkDirty();
            SceneView.RepaintAll();
        }
    }
}
