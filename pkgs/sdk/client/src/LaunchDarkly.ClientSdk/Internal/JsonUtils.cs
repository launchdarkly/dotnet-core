using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace LaunchDarkly.Sdk.Internal
{
    internal static class JsonUtils
    {
        /// <summary>
        /// Shortcut for creating a Utf8JsonWriter, doing some action with it, and getting the output as a string.
        /// </summary>
        /// <param name="serializeAction">action to create some output</param>
        /// <returns>the output</returns>
        public static string WriteJsonAsString(Action<Utf8JsonWriter> serializeAction)
        {
            var stream = new MemoryStream();
            var w = new Utf8JsonWriter(stream);
            serializeAction(w);
            w.Flush();
            return Encoding.UTF8.GetString(stream.ToArray());
        }
    }
}
