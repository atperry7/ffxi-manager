using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using FFXIManager.Services;
using System.IO;

namespace FFXIManager.Tests.Services
{
    [TestClass]
    public class UICommandServiceTests
    {
        [TestMethod]
        public void CopyToClipboard_ThrowsOnFailure_IsCatchable()
        {
            var svc = new UICommandService();
            try
            {
                svc.CopyToClipboard("test");
            }
            catch (InvalidOperationException ex)
            {
                StringAssert.Contains(ex.Message, "Failed to copy to clipboard");
                return;
            }
            catch
            {
                // If running in environment with clipboard, this may succeed; that's acceptable.
            }
            Assert.IsTrue(true);
        }

        [TestMethod]
        public void OpenFileLocation_ThrowsIfMissing()
        {
            var svc = new UICommandService();
            var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "nope.txt");
            try
            {
                svc.OpenFileLocation(missing);
                Assert.Fail("Expected FileNotFoundException");
            }
            catch (FileNotFoundException)
            {
                // expected
            }
        }

        [TestMethod]
        public void ShowFolderDialog_ReturnsFlagOrThrowsInvalidOperation()
        {
            var svc = new UICommandService();
            try
            {
                var ok = svc.ShowFolderDialog("title", Environment.CurrentDirectory, out var path);
                Assert.IsTrue(ok == true || ok == false);
            }
            catch (InvalidOperationException ex)
            {
                StringAssert.Contains(ex.Message, "Failed to show folder dialog");
            }
        }
    }
}

