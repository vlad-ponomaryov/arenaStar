using NUnit.Framework;
using UnityEngine.TestTools.Constraints; // ConstraintExtensions.AllocatingGCMemory for Is.Not
using AllocIs = UnityEngine.TestTools.Constraints.Is;

namespace Arena.Tests.EditMode.Core
{
    /// <summary>
    /// Zero-allocation checks (D-033, D-047). Uses the Test Framework's profiler-recorder constraint: it counts every
    /// GC.Alloc sample on the current thread. <c>GC.GetAllocatedBytesForCurrentThread()</c> is not used — under Unity's Mono
    /// it does not reliably see small allocations (verified in S2.1: allocating a 4 KB array gave a smaller delta).
    /// </summary>
    public static class AllocationAssert
    {
        public static void NoGcAllocations(TestDelegate action, string message = null) =>
            Assert.That(action, AllocIs.Not.AllocatingGCMemory(), message);

        public static void Allocates(TestDelegate action, string message = null) =>
            Assert.That(action, AllocIs.AllocatingGCMemory(), message);
    }
}
