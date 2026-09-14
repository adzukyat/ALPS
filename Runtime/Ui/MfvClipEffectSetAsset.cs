using UnityEngine;

namespace ManeuverForVRC.Ui
{
    /// <summary>
    /// Authoring host for a <see cref="MfvClipEffectSet"/>.
    ///
    /// Keeping this inspector on its own asset lets the new UI and model be
    /// edited and tested for real without disturbing the existing
    /// StageLightTimelineClip / SlmProperty pipeline. Attaching the set to the timeline
    /// clip itself is the next step and is a model migration, not a UI change.
    /// </summary>
    [CreateAssetMenu(
        fileName = "MfvClipEffectSet",
        menuName = "Maneuver For VRC/Clip Effect Set",
        order = 400)]
    public class MfvClipEffectSetAsset : ScriptableObject
    {
        public MfvClipEffectSet data = new MfvClipEffectSet();
    }
}
