using System;
using FFXIManager.Utilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FFXIManager.Tests.Utilities
{
    [TestClass]
    public class ThrottleTests
    {
        [TestMethod]
        public void ShouldRun_RespectsInterval()
        {
            var key = Guid.NewGuid().ToString();
            var t0 = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var interval = TimeSpan.FromSeconds(5);

            // First run should be allowed
            Assert.IsTrue(Throttle.ShouldRun(key, interval, t0));
            // Within interval should be blocked
            Assert.IsFalse(Throttle.ShouldRun(key, interval, t0.AddSeconds(1)));
            Assert.IsFalse(Throttle.ShouldRun(key, interval, t0.AddSeconds(4.9)));
            // After interval should be allowed
            Assert.IsTrue(Throttle.ShouldRun(key, interval, t0.AddSeconds(5.001)));
        }

        [TestMethod]
        public void Reset_AllowsImmediateRun()
        {
            var key = Guid.NewGuid().ToString();
            var interval = TimeSpan.FromSeconds(10);
            var t0 = DateTime.UnixEpoch;

            Assert.IsTrue(Throttle.ShouldRun(key, interval, t0));
            Assert.IsFalse(Throttle.ShouldRun(key, interval, t0.AddSeconds(1)));
            Throttle.Reset(key);
            Assert.IsTrue(Throttle.ShouldRun(key, interval, t0.AddSeconds(1)));
        }
    }
}

