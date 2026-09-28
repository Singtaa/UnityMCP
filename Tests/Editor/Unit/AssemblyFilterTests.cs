using NUnit.Framework;

namespace UnityMcp.Tests {
    [TestFixture]
    public class AssemblyFilterTests {
        static readonly string[] Known = { "OneJS.Tests", "OneJSContainer.Tests.PlayMode" };

        [Test]
        public void AnAssemblyNamedExactlyIsKnown() {
            CollectionAssert.IsEmpty(Tools_Test.UnknownAssemblyNames(Known, new[] { "OneJS.Tests", "OneJSContainer.Tests.PlayMode" }));
        }

        /// <summary>
        /// The name that ran nothing and passed: a prefix of a real assembly's
        /// name is not that assembly, because the runner compares exactly.
        /// </summary>
        [Test]
        public void APrefixOfARealNameIsUnknown() {
            CollectionAssert.AreEqual(new[] { "OneJSContainer.Tests" },
                Tools_Test.UnknownAssemblyNames(Known, new[] { "OneJS.Tests", "OneJSContainer.Tests" }));
        }
    }
}
