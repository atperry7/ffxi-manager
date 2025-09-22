using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using FFXIManager.Models;

namespace FFXIManager.Tests.Models
{
    [TestClass]
    public class PlayOnlineMemberAccountTests
    {
        [TestMethod]
        public void IsOTPEnabled_WithEnabledOTP_ReturnsTrue()
        {
            // Arrange
            var account = new PlayOnlineMemberAccount
            {
                OTPConfiguration = new OTPConfiguration { IsEnabled = true }
            };

            // Act & Assert
            Assert.IsTrue(account.IsOTPEnabled);
        }

        [TestMethod]
        public void IsOTPEnabled_WithDisabledOTP_ReturnsFalse()
        {
            // Arrange
            var account = new PlayOnlineMemberAccount
            {
                OTPConfiguration = new OTPConfiguration { IsEnabled = false }
            };

            // Act & Assert
            Assert.IsFalse(account.IsOTPEnabled);
        }

        [TestMethod]
        public void IsOTPEnabled_WithNullOTP_ReturnsFalse()
        {
            // Arrange
            var account = new PlayOnlineMemberAccount
            {
                OTPConfiguration = null
            };

            // Act & Assert
            Assert.IsFalse(account.IsOTPEnabled);
        }

        [TestMethod]
        public void OTPCodeDisplay_NotEnabled_ReturnsNA()
        {
            // Arrange
            var account = new PlayOnlineMemberAccount
            {
                OTPConfiguration = new OTPConfiguration { IsEnabled = false }
            };

            // Act & Assert
            Assert.AreEqual("N/A", account.OTPCodeDisplay);
        }

        [TestMethod]
        public void OTPCodeDisplay_EnabledButNoSecret_ReturnsNoKey()
        {
            // Arrange
            var account = new PlayOnlineMemberAccount
            {
                OTPConfiguration = new OTPConfiguration
                {
                    IsEnabled = true,
                    HasStoredSecret = false
                }
            };

            // Act & Assert
            Assert.AreEqual("No Key", account.OTPCodeDisplay);
        }

        [TestMethod]
        public void OTPCodeDisplay_EnabledWithSecretButNoCode_ReturnsDashes()
        {
            // Arrange
            var account = new PlayOnlineMemberAccount
            {
                OTPConfiguration = new OTPConfiguration
                {
                    IsEnabled = true,
                    HasStoredSecret = true
                },
                CurrentOTPCode = null
            };

            // Act & Assert
            Assert.AreEqual("------", account.OTPCodeDisplay);
        }

        [TestMethod]
        public void OTPCodeDisplay_VisibleWithCode_ReturnsCode()
        {
            // Arrange
            var account = new PlayOnlineMemberAccount
            {
                OTPConfiguration = new OTPConfiguration
                {
                    IsEnabled = true,
                    HasStoredSecret = true
                },
                CurrentOTPCode = "123456",
                IsOTPCodeVisible = true
            };

            // Act & Assert
            Assert.AreEqual("123456", account.OTPCodeDisplay);
        }

        [TestMethod]
        public void OTPCodeDisplay_HiddenWithCode_ReturnsMasked()
        {
            // Arrange
            var account = new PlayOnlineMemberAccount
            {
                OTPConfiguration = new OTPConfiguration
                {
                    IsEnabled = true,
                    HasStoredSecret = true
                },
                CurrentOTPCode = "123456",
                IsOTPCodeVisible = false
            };

            // Act & Assert
            Assert.AreEqual("••••••", account.OTPCodeDisplay);
        }

        [TestMethod]
        public void DisplayName_WithOTPAndPassword_ShowsBothSecured()
        {
            // Arrange
            var account = new PlayOnlineMemberAccount
            {
                AccountName = "Test Account",
                HasStoredPassword = true,
                OTPConfiguration = new OTPConfiguration
                {
                    IsEnabled = true,
                    HasStoredSecret = true
                }
            };

            // Act & Assert
            Assert.AreEqual("Test Account (OTP, Secured)", account.DisplayName);
        }

        [TestMethod]
        public void DisplayName_WithOTPOnly_ShowsOTP()
        {
            // Arrange
            var account = new PlayOnlineMemberAccount
            {
                AccountName = "Test Account",
                HasStoredPassword = false,
                OTPConfiguration = new OTPConfiguration
                {
                    IsEnabled = true,
                    HasStoredSecret = true
                }
            };

            // Act & Assert
            Assert.AreEqual("Test Account (OTP)", account.DisplayName);
        }

        [TestMethod]
        public void PropertyChanged_OTPCodeDisplay_TriggeredWhenVisibilityChanges()
        {
            // Arrange
            var account = new PlayOnlineMemberAccount
            {
                OTPConfiguration = new OTPConfiguration
                {
                    IsEnabled = true,
                    HasStoredSecret = true
                },
                CurrentOTPCode = "123456"
            };

            var propertyChangedEvents = new List<string>();
            account.PropertyChanged += (sender, e) =>
            {
                if (e.PropertyName != null)
                    propertyChangedEvents.Add(e.PropertyName);
            };

            // Act
            account.IsOTPCodeVisible = true;

            // Assert
            Assert.IsTrue(propertyChangedEvents.Contains(nameof(PlayOnlineMemberAccount.OTPCodeDisplay)));
        }

        [TestMethod]
        public void PropertyChanged_OTPCodeDisplay_TriggeredWhenCodeChanges()
        {
            // Arrange
            var account = new PlayOnlineMemberAccount
            {
                OTPConfiguration = new OTPConfiguration
                {
                    IsEnabled = true,
                    HasStoredSecret = true
                }
            };

            var propertyChangedEvents = new List<string>();
            account.PropertyChanged += (sender, e) =>
            {
                if (e.PropertyName != null)
                    propertyChangedEvents.Add(e.PropertyName);
            };

            // Act
            account.CurrentOTPCode = "123456";

            // Assert
            Assert.IsTrue(propertyChangedEvents.Contains(nameof(PlayOnlineMemberAccount.OTPCodeDisplay)));
        }
    }
}