using System;
using System.Security.Cryptography;
using System.Text;

namespace TRWhisper.Core.Config
{
    /// <summary>
    /// Hassas verileri (API anahtarları vb.) Windows DPAPI (Data Protection API) ile şifreler.
    /// Şifreleme geçerli Windows kullanıcı hesabına (CurrentUser) ve uygulamaya özel entropy değerine bağlıdır.
    /// </summary>
    public static class SecretProtection
    {
        public const string Prefix = "enc:";
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("TRWhisper_DPAPI_Entropy_v2");

        /// <summary>
        /// Verilen düz metni DPAPI ile şifreleyip "enc:<base64>" olarak döner.
        /// Zaten şifreliyse veya boşsa dokunmaz.
        /// </summary>
        public static string Protect(string? plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return string.Empty;
            if (plainText.StartsWith(Prefix, StringComparison.Ordinal)) return plainText;

            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(plainText);
                byte[] encrypted = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
                return Prefix + Convert.ToBase64String(encrypted);
            }
            catch (Exception ex)
            {
                // Güvenlik: DPAPI başarısız olursa düz metin kesinlikle diske yazılmamalıdır
                Console.WriteLine($"[SecretProtection] DPAPI şifreleme başarısız: {ex.Message}");
                throw new CryptographicException("Hassas veri DPAPI ile şifrelenemedi. Güvenlik gerekçesiyle düz metin olarak saklanamaz.", ex);
            }
        }

        /// <summary>
        /// "enc:<base64>" biçimindeki şifreli metni çözer.
        /// Önce v2 entropy ile dener, geriye dönük uyumluluk için null entropy ile de dener.
        /// Ön ek yoksa (eski düz metin) doğrudan metni döner.
        /// </summary>
        public static string Unprotect(string? cipherOrPlainText)
        {
            if (string.IsNullOrEmpty(cipherOrPlainText)) return string.Empty;
            if (!cipherOrPlainText.StartsWith(Prefix, StringComparison.Ordinal)) return cipherOrPlainText;

            try
            {
                string base64 = cipherOrPlainText[Prefix.Length..];
                byte[] bytes = Convert.FromBase64String(base64);

                try
                {
                    // 1. Yeni format (Entropy ile)
                    byte[] decrypted = ProtectedData.Unprotect(bytes, Entropy, DataProtectionScope.CurrentUser);
                    return Encoding.UTF8.GetString(decrypted);
                }
                catch
                {
                    // 2. Geriye dönük uyumluluk: Eski format (Entropy = null)
                    byte[] decryptedLegacy = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
                    return Encoding.UTF8.GetString(decryptedLegacy);
                }
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
