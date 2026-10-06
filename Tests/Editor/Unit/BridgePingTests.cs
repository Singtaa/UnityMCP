using NUnit.Framework;

namespace UnityMcp.Tests {
    /// <summary>
    /// unity_bridge_ping is answered on the TCP thread, so a bare "pong" only
    /// says the socket is up. After a domain reload it arrived about two
    /// seconds before the main thread ran anything, which read as ready while
    /// every tool call was still waiting.
    /// </summary>
    [TestFixture]
    public class BridgePingTests {
        [Test]
        public void ARunningMainThreadIsAPlainPong() {
            Assert.AreEqual("pong", MainThreadDispatcher.PingReply(16, 0));
            Assert.AreEqual("pong", MainThreadDispatcher.PingReply(MainThreadDispatcher.StalledAfterMs - 1, 2));
        }

        [Test]
        public void AStalledMainThreadIsSaidToBe() {
            var reply = MainThreadDispatcher.PingReply(1896, 4);
            StringAssert.StartsWith("pong", reply, "Callers matching on \"pong\" still see one.");
            StringAssert.Contains("1896 ms", reply);
            StringAssert.Contains("4 calls waiting", reply);
        }

        [Test]
        public void AMainThreadThatNeverRanIsSaidToBe() {
            StringAssert.Contains("has not run yet", MainThreadDispatcher.PingReply(-1, 1));
        }
    }
}
