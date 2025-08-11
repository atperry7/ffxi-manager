using System;
using System.Collections.Generic;
using FFXIManager.Utilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FFXIManager.Tests.Utilities
{
    [TestClass]
    public class ProcessFiltersTests
    {
        [TestMethod]
        public void WildcardMatch_BasicCases()
        {
            Assert.IsTrue(ProcessFilters.WildcardMatch("pol", "pol"));
            Assert.IsTrue(ProcessFilters.WildcardMatch("PlayOnlineViewer", "Play*"));
            Assert.IsTrue(ProcessFilters.WildcardMatch("ffxi", "*"));
            Assert.IsFalse(ProcessFilters.WildcardMatch("ffxi", "pol*"));
            Assert.IsFalse(ProcessFilters.WildcardMatch("ffxi", null));
        }

        [TestMethod]
        public void ExtractProcessName_HandlesPathAndExtension()
        {
            Assert.AreEqual("pol", ProcessFilters.ExtractProcessName("C:/Games/PlayOnline/pol.exe"));
            Assert.AreEqual("ffxi", ProcessFilters.ExtractProcessName("ffxi.exe"));
            Assert.AreEqual("", ProcessFilters.ExtractProcessName(null));
        }

        [TestMethod]
        public void MatchesNamePatterns_IncludeExclude()
        {
            var includes = new[] { "pol", "ffxi*" };
            var excludes = new[] { "ffxi-boot" };
            Assert.IsTrue(ProcessFilters.MatchesNamePatterns("pol.exe", includes, excludes));
            Assert.IsTrue(ProcessFilters.MatchesNamePatterns("ffxi-main", includes, excludes));
            Assert.IsFalse(ProcessFilters.MatchesNamePatterns("ffxi-boot", includes, excludes));
            Assert.IsFalse(ProcessFilters.MatchesNamePatterns("unknown", includes, excludes));
        }

        [TestMethod]
        public void MatchesProcessName_UsesNormalization()
        {
            var names = new[] { "pol", "ffxi" };
            Assert.IsTrue(ProcessFilters.MatchesProcessName("pol.exe", names));
            Assert.IsTrue(ProcessFilters.MatchesProcessName("FFXI", names));
            Assert.IsFalse(ProcessFilters.MatchesProcessName("other", names));
        }

        [TestMethod]
        public void IsAcceptableWindowTitle_DefaultIgnoredPrefixes()
        {
            Assert.IsFalse(ProcessFilters.IsAcceptableWindowTitle("Default IME window"));
            Assert.IsFalse(ProcessFilters.IsAcceptableWindowTitle("MSCTFIME UI something"));
            Assert.IsFalse(ProcessFilters.IsAcceptableWindowTitle("Program Manager"));
            Assert.IsTrue(ProcessFilters.IsAcceptableWindowTitle("Final Fantasy XI"));
        }
    }
}

