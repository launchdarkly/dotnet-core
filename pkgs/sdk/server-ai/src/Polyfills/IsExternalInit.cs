// Required for C# 9+ init-only properties when targeting net462 or netstandard2.0.
#if !NET5_0_OR_GREATER
namespace System.Runtime.CompilerServices
{
    using System.ComponentModel;

    // Polyfill for the IsExternalInit marker class, which the C# compiler requires
    // for 'init' accessors and positional record properties but which is only defined
    // in .NET 5+ runtimes.
    [EditorBrowsable(EditorBrowsableState.Never)]
    internal static class IsExternalInit { }
}
#endif
