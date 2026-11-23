using System;
using System.Security.Cryptography;
using System.Text;

namespace ScanDrop.Native.Core
{
    /// <summary>
    /// Encrypts sensitive configuration values (e.g. Cloudflare Named Tunnel tokens)
    /// using Windows DPAPI bound to the current Windows user account.
    /// Never stores secrets in plaintext on disk.
    /// </summary>
    public static class SecretProtector
    {
        private const string Prefix = "dpapi:";
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("ScanDrop.LocalFirst.v2.DPAPI");

        public static string Protect(string plaintext)
        {
            if (string.IsNullOrEmpty(plaintext)) return "";
            if (plaintext.StartsWith(Prefix, StringComparison.Ordinal)) return plaintext;
            try
            {
                byte[] plainBytes = Encoding.UTF8.GetBytes(plaintext);
                byte[] cipher = ProtectedData.Protect(plainBytes, Entropy, DataProtectionScope.CurrentUser);
                return Prefix + Convert.ToBase64String(cipher);
            }
            catch
            {
                return "";
            }
        }

        public static string Unprotect(string stored)
        {
            if (string.IsNullOrEmpty(stored)) return "";
            if (!stored.StartsWith(Prefix, StringComparison.Ordinal))
            {
                // Legacy plaintext value read once (callers should re-save protected)
                return stored;
            }
            try
            {
                byte[] cipher = Convert.FromBase64String(stored.Substring(Prefix.Length));
                byte[] plainBytes = ProtectedData.Unprotect(cipher, Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plainBytes);
            }
            catch
            {
                return "";
            }
        }

        public static string ComputeSha256Hex(string input)
        {
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(input ?? ""));
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        public static string GenerateSecureToken(int byteCount)
        {
            byte[] buf = new byte[byteCount];
            using (var rng = new RNGCryptoServiceProvider())
            {
                rng.GetBytes(buf);
            }
            return Convert.ToBase64String(buf).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        }

        public static string GenerateReceiptCode()
        {
            // 6-character unambiguous alphanumeric receipt code (PRD Sec 4 Step 5)
            const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            byte[] buf = new byte[6];
            using (var rng = new RNGCryptoServiceProvider())
            {
                rng.GetBytes(buf);
            }
            char[] chars = new char[6];
            for (int i = 0; i < 6; i++)
            {
                chars[i] = alphabet[buf[i] % alphabet.Length];
            }
            return new string(chars);
        }
    }
}
