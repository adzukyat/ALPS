using UnityEditor;

namespace AdzukiSoft.ALPS.Editor
{
    /// <summary>
    /// Notices when the editor leaves animation mode, which is how the Timeline window ends a
    /// preview. By then the fixture properties are reverted, so the preview driver refreshes
    /// the fixtures and forgets the defaults it captured.
    /// </summary>
    [InitializeOnLoad]
    internal static class AlpsPreviewSessionWatcher
    {
        private static bool _wasInAnimationMode;

        static AlpsPreviewSessionWatcher()
        {
            EditorApplication.update += Poll;
        }

        internal static void Poll()
        {
            var inAnimationMode = AnimationMode.InAnimationMode();
            if (_wasInAnimationMode && !inAnimationMode)
            {
                AlpsPreviewDriver.EndPreviewSession();
            }

            _wasInAnimationMode = inAnimationMode;
        }
    }
}
