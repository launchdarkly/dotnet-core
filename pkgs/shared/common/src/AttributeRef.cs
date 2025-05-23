using System;
using System.Text;
using System.Text.Json.Serialization;
using LaunchDarkly.Sdk.Json;

namespace LaunchDarkly.Sdk
{
    /// <summary>
    /// An attribute name or path expression identifying a value within a <see cref="Context"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This type is mainly intended to be used internally by LaunchDarkly SDK and service code, where
    /// efficiency is a major concern so it's desirable to do any parsing or preprocessing just once.
    /// Applications are unlikely to need to use the AttributeRef type directly.
    /// </para>
    /// <para>
    /// It can be used to retrieve a value with <see cref="Context.GetValue(in AttributeRef)"/>, or to
    /// identify an attribute or nested value that should be considered private with Builder.Private()
    /// (the SDK configuration can also have a list of private attribute references).
    /// </para>
    /// <para>
    /// Parsing and validation are done at the time that <see cref="FromPath(string)"/> or
    /// <see cref="FromLiteral(string)"/> is called. If an AttributeRef instance was created from an
    /// invalid string, or if it is an uninitialized struct (<c>new AttributeRef()</c>), it is
    /// considered invalid and its <see cref="Error"/> property will return a non-null error.
    /// </para>
    /// <para>
    /// The string representation of an attribute reference in LaunchDarkly JSON data uses the following
    /// syntax:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// If the first character is not a slash, the string is interpreted literally as an attribute name.
    /// An attribute name can contain any characters, but must not be empty.
    /// </description></item>
    /// <item><description>
    /// If the first character is a slash, the string is interpreted as a slash-delimited path where the
    /// first path component is an attribute name, and each subsequent path component is the name of a
    /// property in a JSON object. Any instances of the characters "/" or "~" in a path component are
    /// escaped as "~1" or "~0" respectively. This syntax deliberately resembles JSON Pointer, but no
    /// JSON Pointer behaviors other than those mentioned here are supported.
    /// </description></item>
    /// </list>
    /// <para>
    /// For example, suppose there is a context whose JSON representation looks like this:
    /// </para>
    /// <code>
    ///     {
    ///       "kind": "user",
    ///       "key": "value1",
    ///       "address": {
    ///         "street": {
    ///           "line1": "value2",
    ///           "line2": "value3"
    ///         },
    ///         "city": "value4"
    ///       },
    ///       "good/bad": "value5"
    ///     }
    /// </code>
    /// <list type="bullet">
    /// <item><description>
    /// The attribute references "key" and "/key" would both point to "value1".
    /// </description></item>
    /// <item><description>
    /// The attribute reference "/address/street/line1" would point to "value2".
    /// </description></item>
    /// <item><description>
    /// The attribute references "good/bad" and "/good~1bad" would both point to "value5".
    /// </description></item>
    /// </list>
    /// </remarks>
    [JsonConverter(typeof(LdJsonConverters.AttributeRefConverter))]
    public readonly struct AttributeRef : IEquatable<AttributeRef>, IJsonSerializable
    {
        
        private readonly string _error;
        private readonly string _rawPath;
        private readonly string _singlePathComponent;
        private readonly string[] _components;

        /// <summary>
        /// True if the AttributeRef has a value, meaning that it is not an uninitialized struct
        /// (<c>new AttributeRef()</c>). That does not guarantee that the value is valid; use
        /// <see cref="Valid"/> or <see cref="Error"/> to test that.
        /// </summary>
        /// <seealso cref="Valid"/>
        /// <seealso cref="Error"/>
        public bool Defined => !(_rawPath is null) || !(_error is null);

        /// <summary>
        /// True for a valid AttributeRef, false for an invalid AttributeRef.
        /// </summary>
        /// <remarks>
        /// <para>
        /// An AttributeRef can only be invalid for the following reasons:
        /// </para>
        /// <list type="number">
        /// <item><description>The input string was empty, or consisted only of "/".</description></item>
        /// <item><description>A slash-delimited string had a double slash causing one component
        /// to be empty, such as "/a//b".</description></item>
        /// <item><description>A slash-delimited string contained a "~" character that was not followed
        /// by "0" or "1".</description></item>
        /// </list>
        /// <para>
        /// Otherwise, the AttributeRef is valid, but that does not guarantee that such an attribute exists
        /// in any given <see cref="Context"/>. For instance, <c>AttributeRef.FromLiteral("name")</c> is a
        /// valid Ref, but a specific Context might or might not have a name.
        /// </para>
        /// <para>
        /// See comments on the <see cref="AttributeRef"/> type for more details of the attribute reference
        /// syntax.
        /// </para>
        /// </remarks>
        /// <seealso cref="Defined"/>
        /// <seealso cref="Error"/>
        public bool Valid => Error is null;

        /// <summary>
        /// Null for a valid AttributeRef, or a non-null error message for an invalid AttributeRef.
        /// </summary>
        /// <remarks>
        /// If this is null, then <see cref="Valid"/> is true. If it is non-null, then <see cref="Valid"/> is false.
        /// </remarks>
        /// <seealso cref="Valid"/>
        /// <seealso cref="Defined"/>
        public string Error
        {
            get
            {
                if (_error is null && _rawPath is null)
                {
                    return Errors.AttrEmpty;
                }
                return _error;
            }
        }

        /// <summary>
        /// The number of path components in the AttributeRef.
        /// </summary>
        /// <remarks>
        /// <para>
        /// For a simple attribute reference such as "name" with no leading slash, this returns 1.
        /// </para>
        /// <para>
        /// For an attribute reference with a leading slash, it is the number of slash-delimited path
        /// components after the initial slash. For instance, <c>AttributeRef.FromPath("/a/b").Depth</c>
        /// returns 2.
        /// </para>
        /// <para>
        /// For an invalid attribute reference, it returns zero.
        /// </para>
        /// </remarks>
        /// <seealso cref="GetComponent(int)"/>
        public int Depth
        {
            get
            {
                if (!(_error is null) || (_singlePathComponent is null && _components is null))
                {
                    return 0;
                }
                if (_components is null)
                {
                    return 1;
                }
                return _components.Length;
            }
        }

        /// <summary>
        /// Creates an AttributeRef from a string. For the supported syntax and examples, see comments on the
        /// <see cref="AttributeRef"/> type.
        /// </summary>
        /// <remarks>
        /// This method always returns an AttributeRef that preserves the original string, even if validation
        /// fails, so that calling <see cref="ToString"/> (or serializing the AttributeRef to JSON) will
        /// produce the original string. If validation fails, <see cref="Error"/> will return a non-null
        /// error and any SDK method that takes this AttributeRef as a parameter will consider it invalid.
        /// </remarks>
        /// <param name="refPath">an attribute name or path</param>
        /// <returns>an AttributeRef</returns>
        /// <seealso cref="FromLiteral(string)"/>
        public static AttributeRef FromPath(string refPath)
        {
            if (refPath is null || refPath == "" || refPath == "/")
            {
                return new AttributeRef(Errors.AttrEmpty, refPath);
            }
            if (refPath[0] != '/')
            {
                // When there is no leading slash, this is a simple attribute reference with no character escaping.
                return new AttributeRef(refPath, refPath, null);
            }
            if (refPath.IndexOf('/', 1) < 0)
            {
                // There's only one segment, so this is still a simple attribute reference. However, we still may
                // need to unescape special characters.
                var unescaped = UnescapePath(refPath.Substring(1));
                if (unescaped is null)
                {
                    return new AttributeRef(Errors.AttrInvalidEscape, refPath);
                }
                return new AttributeRef(refPath, unescaped, null);
            }
            var parsed = refPath.Substring(1).Split('/');
            for (var i = 0; i < parsed.Length; i++)
            {
                var p = parsed[i];
                if (p == "")
                {
                    return new AttributeRef(Errors.AttrExtraSlash, refPath);
                }
                var unescaped = UnescapePath(p);
                if (unescaped is null)
                {
                    return new AttributeRef(Errors.AttrInvalidEscape, refPath);
                }
                parsed[i] = unescaped;
            }
            return new AttributeRef(refPath, null, parsed);
        }

        /// <summary>
        /// Similar to <see cref="FromPath(string)"/>, except that it always interprets the string as a literal
        /// attribute name, never as a slash-delimited path expression. There is no escaping or unescaping,
        /// even if the name contains literal '/' or '~' characters. Since an attribute name can contain
        /// any characters, this method always returns a valid AttributeRef unless the name is empty.
        /// </summary>
        /// <remarks>
        /// For example: <c>AttributeRef.FromLiteral("name")</c> is exactly equivalent to
        /// <c>AttributeRef.FromPath("name")</c>. <c>AttributeRef.FromLiteral("a/b")</c> is exactly equivalent
        /// to <c>AttributeRef.FromPath("a/b")</c> (since the syntax used by <see cref="FromPath(string)"/>
        /// treats the whole string as a literal as long as it does not start with a slash), or to
        /// <c>AttributeRef.FromPath("/a~1b")</c>.
        /// </remarks>
        /// <param name="attributeName">an attribute name</param>
        /// <returns>an AttributeRef</returns>
        /// <seealso cref="FromPath(string)"/>
        public static AttributeRef FromLiteral(string attributeName)
        {
            if (attributeName is null || attributeName == "")
            {
                return new AttributeRef(Errors.AttrEmpty, attributeName);
            }
            if (attributeName[0] != '/')
            {
                // When there is no leading slash, this is a simple attribute reference with no character escaping.
                return new AttributeRef(attributeName, attributeName, null);
            }
            // If there is a leading slash, then the attribute name actually starts with a slash. To represent it
            // as an AttributeRef, it'll need to be escaped.
            var escapedPath = "/" + attributeName.Replace("~", "~0").Replace("/", "~1");
            return new AttributeRef(escapedPath, attributeName, null);
        }

        private AttributeRef(string error, string rawPath)
        {
            _error = error;
            _rawPath = rawPath;
            _singlePathComponent = null;
            _components = null;
        }

        private AttributeRef(string rawPath, string singlePathComponent, string[] components)
        {
            _error = null;
            _rawPath = rawPath;
            _singlePathComponent = singlePathComponent;
            _components = components;
        }

        /// <summary>
        /// Retrieves a single path component from the attribute reference.
        /// </summary>
        /// <remarks>
        /// <para>
        /// For a simple attribute reference such as "name" with no leading slash, if index is zero,
        /// TryGetComponent returns the attribute name.
        /// </para>
        /// <para>
        /// For an attribute reference with a leading slash, if index is non-negative and less than
        /// <see cref="Depth"/>, TryGetComponent returns the path component.
        /// </para>
        /// <para>
        /// It returns null if the index is out of range.
        /// </para>
        /// </remarks>
        /// <param name="index">the zero-based index of the desired path component</param>
        /// <returns>the path component or null</returns>
        public string GetComponent(int index)
        {
            if (!(_error is null))
            {
                return null;
            }
            if (index == 0 && _components is null)
            {
                return _singlePathComponent;
            }
            if (_components is null || index < 0 || index >= _components.Length)
            {
                return null;
            }
            return _components[index];
        }

        /// <inheritdoc/>
        public override bool Equals(object obj) =>
            obj is AttributeRef other && Equals(other);

        /// <inheritdoc/>
        public bool Equals(AttributeRef other) =>
            _rawPath == other._rawPath;

        /// <inheritdoc/>
        public override int GetHashCode() =>
            _rawPath is null ? 0 : _rawPath.GetHashCode();

        /// <summary>
        /// Returns the attribute reference as a string, in the same format used by
        /// <see cref="FromPath(string)"/>
        /// </summary>
        /// <remarks>
        /// If the AttributeRef was created with <see cref="FromPath(string)"/>, this value is
        /// identical to the original string. If it was created with <see cref="FromLiteral(string)"/>,
        /// the value may be different due to unescaping (for instance, an attribute whose name is
        /// "/a" would be represented as "~1a"). For an uninitialized struct
        /// (<c>new AttributeRef()</c>), it returns an empty string.
        /// </remarks>
        /// <returns>the attribute reference string (guaranteed non-null)</returns>
        public override string ToString()
        {
            return _rawPath ?? "";
        }

        private static string UnescapePath(string path)
        {
            // If there are no tildes then there's definitely nothing to do
            if (!path.Contains("~"))
            {
                return path;
            }
            var ret = new StringBuilder(100); // arbitrary initial capacity
            for (var i = 0; i < path.Length; i++)
            {
                var ch = path[i];
                if (ch != '~')
                {
                    ret.Append(ch);
                    continue;
                }
                i++;
                if (i >= path.Length)
                {
                    return null;
                }
                switch (path[i])
                {
                    case '0':
                        ret.Append('~');
                        break;
                    case '1':
                        ret.Append('/');
                        break;
                    default:
                        return null;
                }
            }
            return ret.ToString();
        }
    }
}
