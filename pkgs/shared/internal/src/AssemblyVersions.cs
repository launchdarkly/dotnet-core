using System;
using System.Reflection;

namespace LaunchDarkly.Sdk.Internal
{
    /// <summary>
    /// Helper methods for inspecting the version of an SDK assembly.
    /// </summary>
    /// <remarks>
    /// The .NET SDK and Xamarin SDK can discover their own current versions dynamically
    /// using these methods, by passing in any type that is defined in the SDK assembly.
    /// </remarks>
    public static class AssemblyVersions
    {
        /// <summary>
        /// Returns the version string for the assembly that provides the specified type.
        /// </summary>
        /// <param name="t">a type defined in the assembly you're interested in</param>
        /// <returns>the version string for that assembly</returns>
        public static string GetAssemblyVersionStringForType(Type t) =>
            ((AssemblyInformationalVersionAttribute)
                t.GetTypeInfo().Assembly.GetCustomAttribute(typeof(AssemblyInformationalVersionAttribute))
            ).InformationalVersion;
    
        /// <summary>
        /// Same as <see cref="GetAssemblyVersionStringForType(Type)"/>, but returns a
        /// <see cref="Version"/> object rather than a string.
        /// </summary>
        /// <param name="t">a type defined in the assembly you're interested in</param>
        /// <returns>the version for that assembly</returns>
        public static Version GetAssemblyVersionForType(Type t) =>
            t.GetTypeInfo().Assembly.GetName().Version;
    }
}
