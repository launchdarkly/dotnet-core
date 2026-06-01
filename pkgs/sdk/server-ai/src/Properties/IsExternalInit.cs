// Polyfill for System.Runtime.CompilerServices.IsExternalInit, which is required by the C# 9+
// `init` accessor but is not provided by netstandard2.0 / net462. Declaring this internal type
// allows the SDK to use `init`-only properties across all target frameworks.

namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit
    {
    }
}
