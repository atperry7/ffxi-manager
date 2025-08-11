using System;
using FFXIManager.Infrastructure;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FFXIManager.Tests.Infrastructure
{
    [TestClass]
    public class UiDispatcherTests
    {
        private class TestDispatcher : IUiDispatcher
        {
            public bool CheckAccess() => true;
            public void BeginInvoke(Action action) => action();
            public void Invoke(Action action) => action();
            public System.Threading.Tasks.Task InvokeAsync(Action action) { action(); return System.Threading.Tasks.Task.CompletedTask; }
            public System.Threading.Tasks.Task<T> InvokeAsync<T>(Func<T> func) => System.Threading.Tasks.Task.FromResult(func());
        }

        [TestMethod]
        public void Invoke_ExecutesAction()
        {
            var dispatcher = new TestDispatcher();
            int value = 0;
            dispatcher.Invoke(() => value = 42);
            Assert.AreEqual(42, value);
        }

        [TestMethod]
        public void BeginInvoke_ExecutesAction()
        {
            var dispatcher = new TestDispatcher();
            int value = 0;
            dispatcher.BeginInvoke(() => value = 7);
            Assert.AreEqual(7, value);
        }

        [TestMethod]
        public void InvokeAsync_ExecutesAction()
        {
            var dispatcher = new TestDispatcher();
            int value = 0;
            dispatcher.InvokeAsync(() => value = 5).Wait();
            Assert.AreEqual(5, value);
        }

        [TestMethod]
        public void InvokeAsync_Generic_ExecutesFuncAndReturns()
        {
            var dispatcher = new TestDispatcher();
            var result = dispatcher.InvokeAsync(() => 123).Result;
            Assert.AreEqual(123, result);
        }
    }
}

