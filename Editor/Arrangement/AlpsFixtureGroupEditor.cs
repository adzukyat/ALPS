using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>
    /// The fixture group's inspector. While an arrangement on the same object keeps the
    /// fixture list in the children's order, the list is not shown, since an edit there
    /// would be put back. Otherwise the group shows its usual fields.
    /// </summary>
    [CustomEditor(typeof(AlpsFixtureGroup))]
    [CanEditMultipleObjects]
    public class AlpsFixtureGroupEditor : UnityEditor.Editor
    {
        /// <summary>How often the inspector checks whether an arrangement started or stopped syncing.</summary>
        private const long PollMilliseconds = 500;

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            var synced = IsSynced();
            Fill(root, synced);

            // Turning the arrangement off or on does not rebuild this inspector.
            root.schedule.Execute(() =>
            {
                var now = IsSynced();
                if (now != synced)
                {
                    synced = now;
                    Fill(root, synced);
                }
            }).Every(PollMilliseconds);

            return root;
        }

        /// <summary>True when every selected group follows an arrangement.</summary>
        public bool IsSynced()
        {
            return targets.OfType<AlpsFixtureGroup>().All(group =>
                group != null && AlpsArrangementLayout.SyncsFixtureGroup(group.GetComponent<AlpsArrangement>()));
        }

        private void Fill(VisualElement root, bool synced)
        {
            root.Clear();
            if (!synced)
            {
                InspectorElement.FillDefaultInspector(root, serializedObject, this);
                root.Bind(serializedObject);
                return;
            }

            var group = (AlpsFixtureGroup)target;
            var count = group.fixtures?.Count(fixture => fixture != null) ?? 0;
            var notice = new HelpBox(
                $"灯体の順番はALPS Arrangementの並び（子オブジェクトの順番）に同期しています（{count}台）。順番を変えるときはHierarchyで子オブジェクトを並べ替えてください。",
                HelpBoxMessageType.Info);
            notice.name = "alps-fixture-group-synced";
            root.Add(notice);
        }
    }
}
