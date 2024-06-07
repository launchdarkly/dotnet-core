using System;
using System.Security.Cryptography;
using System.Text;

namespace LaunchDarkly.Sdk.Client.Internal
{
    internal static class Base64
    {
        private static readonly SHA256 _hasher = SHA256.Create();

        public static string UrlSafeEncode(this string plainText) =>
            UrlSafeBase64String(Encoding.UTF8.GetBytes(plainText));

        public static string UrlSafeSha256Hash(string input) =>
            UrlSafeBase64String(
                _hasher.ComputeHash(Encoding.UTF8.GetBytes(input))
                );

        public static string UrlSafeBase64String(byte[] input) =>
                Convert.ToBase64String(input).Replace('+', '-').Replace('/', '_');
    }
}
