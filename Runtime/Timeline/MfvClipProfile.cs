using UnityEngine;

namespace ManeuverForVRC
{
    /// <summary>A saved clip setup that clips can load, save to, or follow while synced.</summary>
    [CreateAssetMenu(fileName = "MfvClipProfile", menuName = "Maneuver For VRC/Clip Profile", order = 400)]
    public class MfvClipProfile : ScriptableObject
    {
        public MfvClipEffectSet data = new MfvClipEffectSet();

        private void OnValidate()
        {
            if (data == null)
            {
                data = new MfvClipEffectSet();
            }

            MfvPreviewDriver.MarkDirty();
        }
    }
}
