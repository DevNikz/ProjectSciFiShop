using System.Collections.Generic;
using NUnit.Framework;

namespace Studio.PlanarReflections.Tests
{
    public class PlanarReflectionPoolTests
    {
        sealed class FakeEntry : IPlanarReflectionPoolEntry
        {
            public int LastUsedFrame { get; set; }
            public bool IsDisposed;
            public void Dispose() { IsDisposed = true; }
        }

        [Test]
        public void Key_SameIds_AreEqualAndHashEqually()
        {
            var a = new PlanarReflectionKey(3, 7);
            var b = new PlanarReflectionKey(3, 7);

            Assert.IsTrue(a.Equals(b));
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
            Assert.IsFalse(a.Equals(new PlanarReflectionKey(3, 8)));
        }

        [Test]
        public void ReleaseReflector_RemovesOnlyMatchingEntries()
        {
            var keep = new FakeEntry();
            var dropA = new FakeEntry();
            var dropB = new FakeEntry();
            var entries = new Dictionary<PlanarReflectionKey, FakeEntry>
            {
                { new PlanarReflectionKey(1, 10), dropA },
                { new PlanarReflectionKey(1, 11), dropB },
                { new PlanarReflectionKey(2, 10), keep }
            };

            PlanarReflectionPoolUtility.ReleaseReflector(entries, 1);

            Assert.AreEqual(1, entries.Count);
            Assert.IsTrue(dropA.IsDisposed);
            Assert.IsTrue(dropB.IsDisposed);
            Assert.IsFalse(keep.IsDisposed);
        }

        [Test]
        public void PruneUnused_RemovesOnlyEntriesOlderThanMaxAge()
        {
            var stale = new FakeEntry { LastUsedFrame = 100 };
            var fresh = new FakeEntry { LastUsedFrame = 950 };
            var entries = new Dictionary<PlanarReflectionKey, FakeEntry>
            {
                { new PlanarReflectionKey(1, 1), stale },
                { new PlanarReflectionKey(2, 1), fresh }
            };

            PlanarReflectionPoolUtility.PruneUnused(entries, 1000, 600);

            Assert.AreEqual(1, entries.Count);
            Assert.IsTrue(stale.IsDisposed);
            Assert.IsFalse(fresh.IsDisposed);
        }
    }
}
