using System.Collections.Generic;
using System.ComponentModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using FFXIManager.Models;

namespace FFXIManager.Tests.Models
{
    [TestClass]
    public class OTPConfigurationTests
    {
        [TestMethod]
        public void IsConfigured_EnabledAndHasSecret_ReturnsTrue()
        {
            // Arrange
            var config = new OTPConfiguration
            {
                IsEnabled = true,
                HasStoredSecret = true
            };

            // Act & Assert
            Assert.IsTrue(config.IsConfigured);
        }

        [TestMethod]
        public void IsConfigured_EnabledButNoSecret_ReturnsFalse()
        {
            // Arrange
            var config = new OTPConfiguration
            {
                IsEnabled = true,
                HasStoredSecret = false
            };

            // Act & Assert
            Assert.IsFalse(config.IsConfigured);
        }

        [TestMethod]
        public void IsConfigured_DisabledWithSecret_ReturnsFalse()
        {
            // Arrange
            var config = new OTPConfiguration
            {
                IsEnabled = false,
                HasStoredSecret = true
            };

            // Act & Assert
            Assert.IsFalse(config.IsConfigured);
        }

        [TestMethod]
        public void ProviderName_DefaultsToSquareEnix()
        {
            // Arrange & Act
            var config = new OTPConfiguration();

            // Assert
            Assert.AreEqual("Square Enix", config.ProviderName);
        }

        [TestMethod]
        public void ProviderName_SetToNull_DefaultsToSquareEnix()
        {
            // Arrange
            var config = new OTPConfiguration();

            // Act
            config.ProviderName = null!;

            // Assert
            Assert.AreEqual("Square Enix", config.ProviderName);
        }

        [TestMethod]
        public void PropertyChanged_IsConfigured_TriggeredWhenDependenciesChange()
        {
            // Arrange
            var config = new OTPConfiguration();
            var propertyChangedEvents = new List<string>();
            config.PropertyChanged += (sender, e) =>
            {
                if (e.PropertyName != null)
                    propertyChangedEvents.Add(e.PropertyName);
            };

            // Act
            config.IsEnabled = true;

            // Assert
            Assert.IsTrue(propertyChangedEvents.Contains(nameof(OTPConfiguration.IsConfigured)));
        }

        [TestMethod]
        public void PropertyChanged_IsConfigured_TriggeredWhenHasStoredSecretChanges()
        {
            // Arrange
            var config = new OTPConfiguration();
            var propertyChangedEvents = new List<string>();
            config.PropertyChanged += (sender, e) =>
            {
                if (e.PropertyName != null)
                    propertyChangedEvents.Add(e.PropertyName);
            };

            // Act
            config.HasStoredSecret = true;

            // Assert
            Assert.IsTrue(propertyChangedEvents.Contains(nameof(OTPConfiguration.IsConfigured)));
        }
    }
}