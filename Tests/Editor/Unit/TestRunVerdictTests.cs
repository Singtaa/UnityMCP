using NUnit.Framework;
using UnityEditor.TestTools.TestRunner.Api;

namespace UnityMcp.Tests {
    /// <summary>
    /// The two routes to a pass that ran nothing it was asked to run: a
    /// combined EditMode and PlayMode run, which the Test Framework executes as
    /// EditMode only, and a finished run with no results at all.
    /// </summary>
    [TestFixture]
    public class TestRunVerdictTests {
        [Test]
        public void EachSingleModeParses() {
            Assert.AreEqual(TestMode.EditMode, Tools_Test.ParseRunMode("editmode"));
            Assert.AreEqual(TestMode.PlayMode, Tools_Test.ParseRunMode("playmode"));
        }

        /// <summary>
        /// "all" ran 296 EditMode cases and reported completed, with not one
        /// PlayMode entry, so it is refused rather than half run.
        /// </summary>
        [Test]
        public void AllIsNotARunMode() {
            Assert.IsNull(Tools_Test.ParseRunMode("all"));
            Assert.IsNull(Tools_Test.ParseRunMode("both"));
        }

        [Test]
        public void ARunStillGoingIsRunning() {
            Assert.AreEqual("running", Tools_Test.RunStatus(true, 0));
        }

        [Test]
        public void AFinishedRunWithResultsIsCompleted() {
            Assert.AreEqual("completed", Tools_Test.RunStatus(false, 3));
        }

        /// <summary>
        /// Whatever the filters were, a run that finished having recorded
        /// nothing did not test anything, and a caller reading only the status
        /// must not see "completed".
        /// </summary>
        [Test]
        public void AFinishedRunWithNoResultsIsEmpty() {
            Assert.AreEqual("completed_empty", Tools_Test.RunStatus(false, 0));
        }

        [Test]
        public void ACategoryNamedExactlyIsKnown() {
            CollectionAssert.IsEmpty(Tools_Test.UnknownCategoryNames(new[] { "RequiresGraphics", "Slow" }, new[] { "RequiresGraphics" }));
        }

        [Test]
        public void AMisspelledCategoryIsUnknown() {
            CollectionAssert.AreEqual(new[] { "RequiresGraphic" },
                Tools_Test.UnknownCategoryNames(new[] { "RequiresGraphics" }, new[] { "RequiresGraphic" }));
        }
    }
}
