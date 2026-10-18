using System.Collections.Generic;
using System.Linq;
using AdzukiSoft.ALPS.Editor;
using NUnit.Framework;
using UnityEngine;

namespace AdzukiSoft.ALPS.Tests
{
    /// <summary>
    /// Only one show of a scene is on VRSL's DMX grid at a time, so the shows count their rows
    /// on their own and fixtures of different shows share them.
    /// </summary>
    public class AlpsDmxRowTests
    {
        private readonly List<GameObject> _objects = new List<GameObject>();

        [TearDown]
        public void DestroyObjects()
        {
            foreach (var go in _objects)
            {
                Object.DestroyImmediate(go);
            }

            _objects.Clear();
        }

        [Test]
        public void Rows_EachShowCountsFromTheFirstRow()
        {
            var a = Fixtures(3);
            var b = Fixtures(2);

            var rows = Assign(out var errors, a, b);

            Assert.IsEmpty(errors);
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, a.Select(f => rows[f]));
            CollectionAssert.AreEqual(new[] { 0, 1 }, b.Select(f => rows[f]));
        }

        [Test]
        public void Rows_ShowsTogetherMayDriveMoreThanTheGridHolds()
        {
            var a = Fixtures(AlpsShowPlayer.GpuMaxFixtures);
            var b = Fixtures(11);

            Assign(out var errors, a, b);

            Assert.IsEmpty(errors, "129 fixtures over two shows fit, since each show fits on its own.");
        }

        [Test]
        public void Rows_AShowDrivesWhatTheGridHoldsAtMost()
        {
            Assign(out var errors, Fixtures(AlpsShowPlayer.GpuMaxFixtures + 1));

            Assert.AreEqual(1, errors.Count);
        }

        [Test]
        public void Rows_AFixtureInSeveralShowsKeepsOneRowFreeInEach()
        {
            var a = Fixtures(2);
            var b = Fixtures(1);
            var shared = Fixtures(1)[0];
            a.Add(shared);
            b.Insert(0, shared);

            var rows = Assign(out var errors, a, b);

            Assert.IsEmpty(errors);
            Assert.AreEqual(2, rows[shared]);
            Assert.AreEqual(0, rows[b[1]], "The show's own fixture takes the row the shared one left free.");
            Assert.AreEqual(a.Count, a.Select(f => rows[f]).Distinct().Count());
            Assert.AreEqual(b.Count, b.Select(f => rows[f]).Distinct().Count());
        }

        private static Dictionary<AlpsFixture, int> Assign(out List<string> errors, params List<AlpsFixture>[] shows)
        {
            errors = new List<string>();
            var names = shows.Select((_, i) => "Show " + i).ToList();
            return AlpsShowApplier.AssignDmxRows(shows.ToList(), names, errors);
        }

        private List<AlpsFixture> Fixtures(int count)
        {
            var result = new List<AlpsFixture>();
            for (var i = 0; i < count; i++)
            {
                var go = new GameObject("Fixture " + i);
                _objects.Add(go);
                result.Add(go.AddComponent<AlpsVRSLFixture>());
            }

            return result;
        }
    }
}
