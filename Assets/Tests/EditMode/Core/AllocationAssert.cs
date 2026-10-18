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

        /// <summary>
        /// Runs the class initialization of every type in the assemblies (D-058): under Unity's Mono the first use of a class
        /// allocates once (its runtime data), which is not a per-tick allocation. Long scenarios whose code paths appear late
        /// (the first ult, the first broken wall) call this before measuring the steady state.
        /// </summary>
        public static void InitializeTypes(params System.Reflection.Assembly[] assemblies)
        {
            for (int a = 0; a < assemblies.Length; a++)
            {
                System.Type[] types = assemblies[a].GetTypes();
                for (int i = 0; i < types.Length; i++)
                {
                    if (types[i].IsGenericTypeDefinition) continue;
                    System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(types[i].TypeHandle);
                }
            }
        }
    }
}
