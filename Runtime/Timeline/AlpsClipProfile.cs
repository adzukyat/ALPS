using UnityEngine;

namespace AdzukiSoft.ALPS
{
    /// <summary>A saved clip setup that clips can load, save to, or follow while synced.</summary>
    [CreateAssetMenu(fileName = "AlpsClipProfile", menuName = "Adzuki Live Performance System/Clip Profile", order = 400)]
    public class AlpsClipProfile : ScriptableObject
    {
        public AlpsClipEffectSet data = new AlpsClipEffectSet();

        private void OnValidate()
        {
            if (data == null)
            {
                data = new AlpsClipEffectSet();
            }

            AlpsPreviewDriver.MarkDirty();
        }
    }
}
