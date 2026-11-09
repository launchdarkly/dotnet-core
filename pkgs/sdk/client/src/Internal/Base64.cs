using System;
using System.Security.Cryptography;
using System.Text;

namespace LaunchDarkly.Sdk.Client.Internal
{
    internal static class Base64
    {
        public static string UrlSafeEncode(this string plainText) =>
            UrlSafeBase64String(Encoding.UTF8.GetBytes(plainText));

        // Same as UrlSafeEncode but strips the '=' padding, so the result contains only
        // base64url characters (A-Z, a-z, 0-9, -, _).
        public static string UrlSafeEncodeNoPadding(this string plainText) =>
            UrlSafeEncode(plainText).TrimEnd('=');

        public static string UrlSafeSha256Hash(string input)
        {
            using (var sha = SHA256.Create())
            {
                return UrlSafeBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(input)));
            }
        }

        public static string UrlSafeBase64String(byte[] input) =>
                Convert.ToBase64String(input).Replace('+', '-').Replace('/', '_');
    }
}
