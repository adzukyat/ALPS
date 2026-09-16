using UnityEngine.Playables;

namespace ManeuverForVRC
{
    /// <summary>
    /// Hands timeline evaluation to <see cref="MfvPreviewDriver"/>. Every MFV track and
    /// layer gets one of these, and the driver evaluates the whole show once per frame.
    /// </summary>
    public class MfvTimelineMixer : PlayableBehaviour
    {
        public PlayableDirector director;

        private bool _retained;

        public override void OnGraphStart(Playable playable)
        {
            if (!_retained && director != null)
            {
                MfvPreviewDriver.Retain(director);
                _retained = true;
            }
        }

        public override void OnPlayableDestroy(Playable playable)
        {
            if (_retained)
            {
                MfvPreviewDriver.Release(director);
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
                MfvPreviewDriver.Retain(director);
                _retained = true;
            }

            MfvPreviewDriver.Evaluate(director, (float)director.time, info.frameId);
        }
    }
}
