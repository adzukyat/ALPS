using UnityEngine;

namespace AdzukiSoft.ALPS
{
    /// <summary>A saved clip setup that clips can load, save to, or follow while synced.</summary>
    [CreateAssetMenu(fileName = "AlpsClipProfile", menuName = "ALPS/Clip Profile", order = 400)]
    public class AlpsClipProfile : ScriptableObject
    {
        public AlpsClipEffectSet data = AlpsClipEffectSet.CreateNew();

        private void OnValidate()
        {
            if (data == null)
            {
                data = AlpsClipEffectSet.CreateNew();
            }

            AlpsPreviewDriver.MarkDirty();
        }
    }
}
