using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using FFXIManager.Services;
using FFXIManager.Models;
using Microsoft.Extensions.Logging;
using Moq;

namespace FFXIManager.Tests.Services
{
    [TestClass]
    public class OTPServiceTests
    {
        private Mock<IWindowsCredentialsService> _mockCredentialsService = null!;
        private Mock<ILoggingService> _mockLoggingService = null!;
        private OTPService _otpService = null!;

        [TestInitialize]
        public void Setup()
        {
            _mockCredentialsService = new Mock<IWindowsCredentialsService>();
            _mockLoggingService = new Mock<ILoggingService>();
            _otpService = new OTPService(_mockCredentialsService.Object, _mockLoggingService.Object);
        }

        [TestMethod]
        public void ValidateAndNormalizeAuthenticationKey_ValidKey_ReturnsNormalized()
        {
            // Arrange
            var validKey = "OBQV O3CU GA4V A6SN PJGW Q33B I5DF EVKW";
            var expectedNormalized = "OBQVO3CUGA4VA6SNPJGWQ33BI5DFEVKW";

            // Act
            var result = _otpService.ValidateAndNormalizeAuthenticationKey(validKey);

            // Assert
            Assert.AreEqual(expectedNormalized, result);
        }

        [TestMethod]
        public void ValidateAndNormalizeAuthenticationKey_InvalidLength_ReturnsNull()
        {
            // Arrange
            var invalidKey = "OBQV O3CU GA4V A6SN PJGW Q33B I5DF"; // Too short

            // Act
            var result = _otpService.ValidateAndNormalizeAuthenticationKey(invalidKey);

            // Assert
            Assert.IsNull(result);
        }

        [TestMethod]
        public void ValidateAndNormalizeAuthenticationKey_InvalidCharacters_ReturnsNull()
        {
            // Arrange
            var invalidKey = "OBQV O3CU GA4V A6SN PJGW Q33B I5DF EVKX"; // X is invalid in Base32

            // Act
            var result = _otpService.ValidateAndNormalizeAuthenticationKey(invalidKey);

            // Assert
            Assert.IsNull(result);
        }

        [TestMethod]
        public void ValidateAndNormalizeAuthenticationKey_EmptyString_ReturnsNull()
        {
            // Arrange
            var emptyKey = "";

            // Act
            var result = _otpService.ValidateAndNormalizeAuthenticationKey(emptyKey);

            // Assert
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GenerateOTPCredentialTarget_CreatesValidTarget()
        {
            // Arrange
            var profilePath = @"C:\test\profile.xml";
            var accountId = Guid.NewGuid();

            // Act
            var result = _otpService.GenerateOTPCredentialTarget(profilePath, accountId);

            // Assert
            Assert.IsNotNull(result);
            StringAssert.StartsWith(result, "FFXIManager.OTP.");
            StringAssert.Contains(result, accountId.ToString("N"));
        }

        [TestMethod]
        public async Task StoreOTPSecretAsync_ValidKey_CallsCredentialsService()
        {
            // Arrange
            var profilePath = @"C:\test\profile.xml";
            var accountId = Guid.NewGuid();
            var validKey = "OBQV O3CU GA4V A6SN PJGW Q33B I5DF EVKW";
            var expectedNormalized = "OBQVO3CUGA4VA6SNPJGWQ33BI5DFEVKW";

            _mockCredentialsService
                .Setup(x => x.StorePasswordAsync(It.IsAny<string>(), It.IsAny<string>(), expectedNormalized))
                .ReturnsAsync(true);

            // Act
            var result = await _otpService.StoreOTPSecretAsync(profilePath, accountId, validKey);

            // Assert
            Assert.IsTrue(result);
            _mockCredentialsService.Verify(
                x => x.StorePasswordAsync(
                    It.Is<string>(target => target.StartsWith("FFXIManager.OTP.")),
                    "SquareEnixOTP",
                    expectedNormalized),
                Times.Once);
        }

        [TestMethod]
        public async Task StoreOTPSecretAsync_InvalidKey_ReturnsFalse()
        {
            // Arrange
            var profilePath = @"C:\test\profile.xml";
            var accountId = Guid.NewGuid();
            var invalidKey = "INVALID KEY";

            // Act
            var result = await _otpService.StoreOTPSecretAsync(profilePath, accountId, invalidKey);

            // Assert
            Assert.IsFalse(result);
            _mockCredentialsService.Verify(
                x => x.StorePasswordAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
                Times.Never);
        }

        [TestMethod]
        public async Task HasOTPSecretAsync_ExistingSecret_ReturnsTrue()
        {
            // Arrange
            var profilePath = @"C:\test\profile.xml";
            var accountId = Guid.NewGuid();

            _mockCredentialsService
                .Setup(x => x.CredentialExistsAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(true);

            // Act
            var result = await _otpService.HasOTPSecretAsync(profilePath, accountId);

            // Assert
            Assert.IsTrue(result);
        }

        [TestMethod]
        public async Task DeleteOTPSecretAsync_CallsCredentialsService()
        {
            // Arrange
            var profilePath = @"C:\test\profile.xml";
            var accountId = Guid.NewGuid();

            _mockCredentialsService
                .Setup(x => x.DeletePasswordAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(true);

            // Act
            var result = await _otpService.DeleteOTPSecretAsync(profilePath, accountId);

            // Assert
            Assert.IsTrue(result);
            _mockCredentialsService.Verify(
                x => x.DeletePasswordAsync(
                    It.Is<string>(target => target.StartsWith("FFXIManager.OTP.")),
                    "SquareEnixOTP"),
                Times.Once);
        }

        [TestMethod]
        public async Task GenerateOTPCodeAsync_WithValidSecret_ReturnsCode()
        {
            // Arrange
            var profilePath = @"C:\test\profile.xml";
            var accountId = Guid.NewGuid();
            var validSecret = "OBQVO3CUGA4VA6SNPJGWQ33BI5DFEVKW";

            _mockCredentialsService
                .Setup(x => x.RetrievePasswordAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(validSecret);

            // Act
            var result = await _otpService.GenerateOTPCodeAsync(profilePath, accountId);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(6, result.Length);
            Assert.IsTrue(int.TryParse(result, out _)); // Should be all digits
        }

        [TestMethod]
        public async Task GenerateOTPCodeAsync_NoSecret_ReturnsNull()
        {
            // Arrange
            var profilePath = @"C:\test\profile.xml";
            var accountId = Guid.NewGuid();

            _mockCredentialsService
                .Setup(x => x.RetrievePasswordAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync((string?)null);

            // Act
            var result = await _otpService.GenerateOTPCodeAsync(profilePath, accountId);

            // Assert
            Assert.IsNull(result);
        }
    }
}