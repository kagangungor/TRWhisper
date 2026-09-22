using System;
using System.Security.Cryptography;
using System.Text;

namespace TRWhisper.Core.Config
{
    /// <summary>
    /// Hassas verileri (API anahtarları vb.) Windows DPAPI (Data Protection API) ile şifreler.
    /// Şifreleme geçerli Windows kullanıcı hesabına (CurrentUser) bağlıdır.
    /// </summary>
    public static class SecretProtection
    {
        public const string Prefix = "enc:";

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
                byte[] encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
                return Prefix + Convert.ToBase64String(encrypted);
            }
            catch
            {
                // DPAPI başarısız olursa düz metni koru (veri kaybını önleme)
                return plainText;
            }
        }

        /// <summary>
        /// "enc:<base64>" biçimindeki şifreli metni çözer. Ön ek yoksa (eski düz metin) doğrudan metni döner.
        /// </summary>
        public static string Unprotect(string? cipherOrPlainText)
        {
            if (string.IsNullOrEmpty(cipherOrPlainText)) return string.Empty;
            if (!cipherOrPlainText.StartsWith(Prefix, StringComparison.Ordinal)) return cipherOrPlainText;

            try
            {
                string base64 = cipherOrPlainText[Prefix.Length..];
                byte[] bytes = Convert.FromBase64String(base64);
                byte[] decrypted = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(decrypted);
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
