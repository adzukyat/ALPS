using System.Collections.Generic;
using UnityEngine;

namespace AdzukiSoft.ALPS
{
    /// <summary>
    /// The fixtures a timeline track drives. The list order is the fixture number i that
    /// the order, the odd and even split and every per fixture offset are based on.
    /// </summary>
    [AddComponentMenu("Adzuki Live Performance System/ALPS Fixture Group")]
    public class AlpsFixtureGroup : MonoBehaviour
    {
        public List<AlpsFixture> fixtures = new List<AlpsFixture>();

        [ContextMenu("Find Fixtures In Children")]
        public void FindFixturesInChildren()
        {
            fixtures.Clear();
            fixtures.AddRange(GetComponentsInChildren<AlpsFixture>(true));
        }

        private void Reset()
        {
            FindFixturesInChildren();
        }
    }
}
