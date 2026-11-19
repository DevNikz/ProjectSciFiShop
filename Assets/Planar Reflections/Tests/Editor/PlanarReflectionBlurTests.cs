using NUnit.Framework;

namespace Studio.PlanarReflections.Tests
{
    public class PlanarReflectionBlurTests
    {
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(6)]
        public void DoesIterationWriteResult_LastIterationAlwaysWritesResult(int count)
        {
            Assert.IsTrue(PlanarReflectionBlur.DoesIterationWriteResult(count - 1, count));
        }

        [Test]
        public void DoesIterationWriteResult_AlternatesBetweenResultAndTemp()
        {
            Assert.IsFalse(PlanarReflectionBlur.DoesIterationWriteResult(0, 4));
            Assert.IsTrue(PlanarReflectionBlur.DoesIterationWriteResult(1, 4));
            Assert.IsFalse(PlanarReflectionBlur.DoesIterationWriteResult(2, 4));
            Assert.IsTrue(PlanarReflectionBlur.DoesIterationWriteResult(3, 4));
        }
    }
}
