using UnityEngine.Playables;

namespace AdzukiSoft.ALPS
{
    /// <summary>
    /// Hands timeline evaluation to <see cref="AlpsPreviewDriver"/>. Every ALPS track and
    /// layer gets one of these, and the driver evaluates the whole show once per frame.
    /// </summary>
    public class AlpsTimelineMixer : PlayableBehaviour
    {
        public PlayableDirector director;

        private bool _retained;

        public override void OnGraphStart(Playable playable)
        {
            if (!_retained && director != null)
            {
                AlpsPreviewDriver.Retain(director);
                _retained = true;
            }
        }

        public override void OnPlayableDestroy(Playable playable)
        {
            if (_retained)
            {
                AlpsPreviewDriver.Release(director);
                _retained = false;
            }
        }

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            if (director == null)
            {
                return;
            }

            if (!_retained)
            {
                AlpsPreviewDriver.Retain(director);
                _retained = true;
            }

            AlpsPreviewDriver.Evaluate(director, (float)director.time, info.frameId);
        }
    }
}
