using System;

namespace LaunchDarkly.Sdk.Internal
{
    /// <summary>
    /// Extension methods for URIs.
    /// </summary>
    public static class UriExtensions
    {
        /// <summary>
        /// Returns a new URI with the specified path appended to the original path, adding a "/"
        /// separator if the original path did not end in one.
        /// </summary>
        /// <remarks>
        /// This behaves differently from <c>new Uri(baseUri, path)</c>, which instead follows
        /// relative URI rules: that is, <c>new Uri("/hostname/basepath", "relativepath")</c>
        /// would return <c>/hostname/relativepath</c> because the original URI did not end in
        /// a slash. We should always use <c>AddPath</c> in any context where the caller has
        /// specified a base URL that might or might not have a path prefix already.
        /// </remarks>
        /// <param name="baseUri">the original URI</param>
        /// <param name="path">the path to append</param>
        /// <returns>a new URI</returns>
        public static Uri AddPath(this Uri baseUri, string path)
        {
            var ub = new UriBuilder(baseUri);
            ub.Path = ub.Path.TrimEnd('/') + "/" + path.TrimStart('/');
            return ub.Uri;
        }

        /// <summary>
        /// Returns a new URI with the specified query string appended, adding a "?" first if the
        /// original URI had no query or a "&" if it had one.
        /// </summary>
        /// <param name="baseUri">the original URI</param>
        /// <param name="query">the query string to add (not including "?")</param>
        /// <returns>a new URI</returns>
        public static Uri AddQuery(this Uri baseUri, string query)
        {
            var ub = new UriBuilder(baseUri);
            ub.Query = string.IsNullOrEmpty(ub.Query) ? query :
                ub.Query.TrimStart('?') + "&" + query;
            return ub.Uri;
        }
    }
}
