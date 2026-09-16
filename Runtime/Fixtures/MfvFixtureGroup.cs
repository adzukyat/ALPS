using System.Collections.Generic;
using UnityEngine;

namespace ManeuverForVRC
{
    /// <summary>
    /// The fixtures a timeline track drives. The list order is the fixture number i that
    /// the order, the odd and even split and every per fixture offset are based on.
    /// </summary>
    [AddComponentMenu("Maneuver For VRC/MFV Fixture Group")]
    public class MfvFixtureGroup : MonoBehaviour
    {
        public List<MfvFixture> fixtures = new List<MfvFixture>();

        [ContextMenu("Find Fixtures In Children")]
        public void FindFixturesInChildren()
        {
            fixtures.Clear();
            fixtures.AddRange(GetComponentsInChildren<MfvFixture>(true));
        }

        private void Reset()
        {
            FindFixturesInChildren();
        }
    }
}
