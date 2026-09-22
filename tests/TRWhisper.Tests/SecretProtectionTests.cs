using TRWhisper.Core.Config;
using Xunit;

namespace TRWhisper.Tests
{
    public class SecretProtectionTests
    {
        [Fact]
        public void Protect_Then_Unprotect_RoundtripsSuccessfully()
        {
            const string original = "AIzaSyTest_SecretKey_123456789";

            string encrypted = SecretProtection.Protect(original);
            Assert.StartsWith(SecretProtection.Prefix, encrypted);
            Assert.NotEqual(original, encrypted);

            string decrypted = SecretProtection.Unprotect(encrypted);
            Assert.Equal(original, decrypted);
        }

        [Fact]
        public void Protect_EmptyOrNull_ReturnsEmptyString()
        {
            Assert.Equal(string.Empty, SecretProtection.Protect(string.Empty));
            Assert.Equal(string.Empty, SecretProtection.Protect(null));
            Assert.Equal(string.Empty, SecretProtection.Unprotect(string.Empty));
            Assert.Equal(string.Empty, SecretProtection.Unprotect(null));
        }

        [Fact]
        public void Protect_AlreadyProtected_DoesNotDoubleEncrypt()
        {
            const string original = "my-secret-token";
            string encryptedOnce = SecretProtection.Protect(original);
            string encryptedTwice = SecretProtection.Protect(encryptedOnce);

            Assert.Equal(encryptedOnce, encryptedTwice);
            Assert.Equal(original, SecretProtection.Unprotect(encryptedTwice));
        }

        [Fact]
        public void Unprotect_PlainTextWithoutPrefix_ReturnsOriginalStringDirectly()
        {
            const string legacyPlainText = "sk-ant-api03-legacy-plain-token";
            string result = SecretProtection.Unprotect(legacyPlainText);

            Assert.Equal(legacyPlainText, result);
        }
    }
}
