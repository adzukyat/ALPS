using System.Collections.Generic;
using UnityEngine;

namespace AdzukiSoft.ALPS
{
    /// <summary>
    /// What an ALPS track can be bound to: an <see cref="AlpsContainer"/> that holds its
    /// objects as children, or a single <see cref="AlpsFixture"/>. A track reads the fixtures
    /// through <see cref="CollectFixtures"/> and does not need to know which one it got.
    /// </summary>
    public abstract class AlpsTarget : MonoBehaviour
    {
        /// <summary>
        /// Adds the fixtures a lighting track bound here drives, in the order the order modes,
        /// the odd and even split and every per fixture offset count them.
        /// </summary>
        public abstract void CollectFixtures(List<AlpsFixture> into);

        /// <summary>The fixtures <see cref="CollectFixtures"/> adds, as a new list.</summary>
        public List<AlpsFixture> Fixtures()
        {
            var fixtures = new List<AlpsFixture>();
            CollectFixtures(fixtures);
            return fixtures;
        }
    }
}
