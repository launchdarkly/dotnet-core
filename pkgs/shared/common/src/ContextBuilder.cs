using System.Collections.Immutable;

namespace LaunchDarkly.Sdk
{
    /// <summary>
    /// A mutable object that uses the builder pattern to specify properties for a <see cref="Context"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Use this type if you need to construct a Context that has only a single kind. To define a
    /// multi-kind Context, use <see cref="Context.NewMulti(Context[])"/> or <see cref="Context.MultiBuilder"/>.
    /// </para>
    /// <para>
    /// Obtain an instance of ContextBuilder by calling <see cref="Context.Builder(string)"/>. Then,
    /// call setter methods such as <see cref="Kind(string)"/>, <see cref="Name(string)"/>, or
    /// <see cref="Set(string, string)"/> to specify any additional attributes. Then, call <see cref="Build"/>
    /// to create the Context. ContextBuilder setters return a reference to the same builder, so calls can be
    /// chained:
    /// </para>
    /// <code>
    ///     var context = Context.Builder("user-key").
    ///         Name("my-name").
    ///         Set("country", "us").
    ///         Build();
    /// </code>
    /// <para>
    /// A ContextBuilder should not be accessed by multiple threads at once. Once you have called
    /// <see cref="Build"/>, the resulting Context is immutable and is safe to use from multiple threads.
    /// Instances created with <see cref="Build"/> are not affected by subsequent actions taken on the builder.
    /// </para>
    /// </remarks>
    public sealed class ContextBuilder
    {
        private ContextKind _kind = ContextKind.Default;
        private string _key;
        private string _name;
        private bool _anonymous;
        private ImmutableDictionary<string, LdValue>.Builder _attributes;
        private ImmutableList<AttributeRef>.Builder _privateAttributes;
        private bool _allowEmptyKey;

        internal ContextBuilder() {}

        /// <summary>
        /// Creates a <see cref="Context"/> from the current Builder properties.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The Context is immutable and will not be affected by any subsequent actions on the ContextBuilder.
        /// </para>
        /// <para>
        /// It is possible to specify invalid attributes for a ContextBuilder, such as an empty key. Instead
        /// of throwing an exception, the ContextBuilder always returns a Context and you can check
        /// <see cref="Context.Error"/> to see if it has an error. See <see cref="Context.Error"/> for more
        /// information about invalid Context conditions. If you pass an invalid Context to an SDK method, the
        /// SDK will detect this and will generally log a description of the error.
        /// </para>
        /// </remarks>
        /// <returns>a new <see cref="Context"/></returns>
        public Context Build()
        {
            return new Context(
                _kind,
                _key,
                _name,
                _anonymous,
                _attributes?.ToImmutableDictionary(),
                _privateAttributes?.ToImmutableList(),
                _allowEmptyKey
                );
        }

        /// <summary>
        /// Sets the Context's kind attribute.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Every Context has a kind. Setting it to an empty string or null is equivalent to
        /// <see cref="ContextKind.Default"/> ("user"). This value is case-sensitive. For validation
        /// rules, see <see cref="ContextKind"/>.
        /// </para>
        /// <para>
        /// If the value is invalid at the time <see cref="Build"/> is called, you will receive an invalid
        /// Context whose <see cref="Context.Error"/> will describe the problem.
        /// </para>
        /// </remarks>
        /// <param name="kind">the context kind</param>
        /// <returns>the builder</returns>
        /// <seealso cref="Kind(string)"/>
        public ContextBuilder Kind(ContextKind kind)
        {
            _kind = kind;
            return this;
        }

        /// <summary>
        /// Sets the Context's kind attribute. This is a shortcut for calling
        /// <c>Kind(ContextKind.Of(kindString))</c>, since the method name already prevents
        /// ambiguity about the intended type.
        /// </summary>
        /// <param name="kindString">the context kind</param>
        /// <returns>the builder</returns>
        /// <seealso cref="Kind(ContextKind)"/>
        public ContextBuilder Kind(string kindString) => Kind(ContextKind.Of(kindString));

        /// <summary>
        /// Sets the Context's key attribute.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Every Context has a key, which is always a string. It cannot be an empty string, but there are no
        /// other restrictions on its value.
        /// </para>
        /// <para>
        /// The key attribute can be referenced by flag rules, flag target lists, and segments.
        /// </para>
        /// </remarks>
        /// <param name="key">the context key</param>
        /// <returns>the builder</returns>
        public ContextBuilder Key(string key)
        {
            _key = key;
            return this;
        }

        internal ContextBuilder AllowEmptyKey(bool value)
        {
            _allowEmptyKey = value;
            return this;
        }

        /// <summary>
        /// Sets the Context's name attribute.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This attribute is optional. It has the following special rules:
        /// </para>
        /// <list type="bullet">
        /// <item><description>Unlike most other attributes, it is always a string if it is specified.
        /// </description></item>
        /// <item><description>The LaunchDarkly dashboard treats this attribute as the preferred display name
        /// for contexts.</description></item>
        /// </list>
        /// </remarks>
        /// <param name="name">the name attribute (null to unset the attribute)</param>
        /// <returns>the builder</returns>
        /// <seealso cref="Context.Name"/>
        public ContextBuilder Name(string name)
        {
            _name = name;
            return this;
        }

        /// <summary>
        /// Sets whether the Context is only intended for flag evaluations and should not be indexed by
        /// LaunchDarkly.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The default value is false. False means that this Context represents an entity such as a user that
        /// you want to be able to see on the LaunchDarkly dashboard.
        /// </para>
        /// <para>
        /// Setting Anonymous to true excludes this Context from the database that is used by the dashboard. It does
        /// not exclude it from analytics event data, so it is not the same as making attributes private; all
        /// non-private attributes will still be included in events and data export. There is no limitation on what
        /// other attributes may be included (so, for instance, Anonymous does not mean there is no <see cref="Name(string)"/>),
        /// and the Context will still have whatever <see cref="Key(string)"/> you have given it.
        /// </para>
        /// <para>
        /// This value is also addressable in evaluations as the attribute name "anonymous". It is always treated as
        /// a boolean true or false in evaluations.
        /// </para>
        /// </remarks>
        /// <param name="anonymous">true if the Context should be excluded from the LaunchDarkly database</param>
        /// <returns>the builder</returns>
        /// <seealso cref="Context.Anonymous"/>
        public ContextBuilder Anonymous(bool anonymous)
        {
            _anonymous = anonymous;
            return this;
        }

        /// <summary>
        /// Sets the value of any attribute for the Context.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This includes only attributes that are addressable in evaluations-- not metadata such as
        /// <see cref="Private(string[])"/>. If <paramref name="attributeName"/> is "private", you will
        /// be setting an attribute with that name which you can use in evaluations or to record data
        /// for your own purposes, but it will be unrelated to <see cref="Private(string[])"/>.
        /// </para>
        /// <para>
        /// This method uses the <see cref="LdValue"/> type to represent a value of any JSON type: null,
        /// boolean, number, string, array, or object. For all attribute names that do not have special
        /// meaning to LaunchDarkly, you may use any of those types. Values of different JSON types are
        /// always treated as different values: for instance, null, false, and the empty string "" are
        /// not the the same, and the number 1 is not the same as the string "1".
        /// </para>
        /// <para>
        /// The following attribute names have special restrictions on their value types, and any value
        /// of an unsupported type will be ignored (leaving the attribute unchanged):
        /// </para>
        /// <list type="bullet">
        /// <item><description>"kind", "key": Must be a string. See <see cref="Kind(string)"/> and
        /// <see cref="Key(string)"/>.</description></item>
        /// <item><description>"name": Must be a string or null. See <see cref="Name(string)"/>.
        /// </description></item>
        /// <item><description>"anonymous": Must be a boolean. See <see cref="Anonymous(bool)"/>.
        /// </description></item>
        /// </list>
        /// <para>
        /// The attribute name "_meta" is not allowed, because it has special meaning in the JSON
        /// schema for contexts; any attempt to set an attribute with this name has no effect. Also, any
        /// attempt to set an attribute with an empty or null name has no effect.
        /// </para>
        /// <para>
        /// Values that are JSON arrays or objects have special behavior when referenced in flag/segment
        /// rules.
        /// </para>
        /// <para>
        /// A value of <see cref="LdValue.Null"/> is equivalent to removing any current non-default value
        /// of the attribute. Null is not a valid attribute value in the LaunchDarkly model; any expressions
        /// in feature flags that reference an attribute with a null value will behave as if the
        /// attribute did not exist.
        /// </para>
        /// </remarks>
        /// <param name="attributeName">the attribute name to set</param>
        /// <param name="value">the value to set</param>
        /// <returns>the builder</returns>
        /// <seealso cref="TrySet(string, LdValue)"/>
        /// <seealso cref="Set(string, bool)"/>
        /// <seealso cref="Set(string, int)"/>
        /// <seealso cref="Set(string, double)"/>
        /// <seealso cref="Set(string, long)"/>
        /// <seealso cref="Set(string, string)"/>
        /// <seealso cref="Remove(string)"/>
        /// <seealso cref="Context.GetValue(string)"/>
        public ContextBuilder Set(string attributeName, LdValue value)
        {
            TrySet(attributeName, value);
            return this;
        }

        /// <summary>
        /// Same as <see cref="Set(string, LdValue)"/>, but returns a boolean indicating whether the
        /// attribute was successfully set.
        /// </summary>
        /// <param name="attributeName">the attribute name to set</param>
        /// <param name="value">the value to set</param>
        /// <returns>true if successful; false if the name was invalid or the value was not an allowed
        /// type for that attribute</returns>
        /// <seealso cref="Set(string, LdValue)"/>
        public bool TrySet(string attributeName, LdValue value)
        {
            if (attributeName is null || attributeName == "")
            {
                return false;
            }
            switch (attributeName)
            {
                case "kind":
                    if (!value.IsString)
                    {
                        return false;
                    }
                    Kind(value.AsString);
                    return true;

                case "key":
                    if (!value.IsString)
                    {
                        return false;
                    }
                    Key(value.AsString);
                    return true;

                case "name":
                    if (!value.IsString && !value.IsNull)
                    {
                        return false;
                    }
                    Name(value.AsString);
                    return true;

                case "anonymous":
                    if (value.Type != LdValueType.Bool)
                    {
                        return false;
                    }
                    Anonymous(value.AsBool);
                    return true;

                case "_meta":
                    return false;

                default:
                    if (value.IsNull)
                    {
                        _attributes?.Remove(attributeName);
                    }
                    else
                    {
                        if (_attributes is null)
                        {
                            _attributes = ImmutableDictionary.CreateBuilder<string, LdValue>();
                        }
                        _attributes.Remove(attributeName);
                        _attributes.Add(attributeName, value);
                    }
                    return true;
            }
        }

        /// <summary>
        /// Same as <see cref="Set(string, LdValue)"/> for a boolean value.
        /// </summary>
        /// <param name="attributeName">the attribute name to set</param>
        /// <param name="value">the value to set</param>
        /// <returns>the builder</returns>
        /// <seealso cref="Set(string, LdValue)"/>
        public ContextBuilder Set(string attributeName, bool value) => Set(attributeName, LdValue.Of(value));

        /// <summary>
        /// Same as <see cref="Set(string, LdValue)"/> for an integer numeric value.
        /// </summary>
        /// <param name="attributeName">the attribute name to set</param>
        /// <param name="value">the value to set</param>
        /// <returns>the builder</returns>
        /// <seealso cref="Set(string, LdValue)"/>
        public ContextBuilder Set(string attributeName, int value) => Set(attributeName, LdValue.Of(value));

        /// <summary>
        /// Same as <see cref="Set(string, LdValue)"/> for a double-precision numeric value.
        /// </summary>
        /// <remarks>
        /// Numeric values in custom attributes have some precision limitations, the same as for
        /// numeric values in flag variations. For more details, see our documentation on
        /// <see href="https://docs.launchdarkly.com/sdk/concepts/flag-types">flag value types</see>.
        /// </remarks>
        /// <param name="attributeName">the attribute name to set</param>
        /// <param name="value">the value to set</param>
        /// <returns>the builder</returns>
        /// <seealso cref="Set(string, LdValue)"/>
        public ContextBuilder Set(string attributeName, double value) => Set(attributeName, LdValue.Of(value));

        /// <summary>
        /// Same as <see cref="Set(string, LdValue)"/> for a long integer numeric value.
        /// </summary>
        /// <remarks>
        /// Numeric values in custom attributes have some precision limitations, the same as for
        /// numeric values in flag variations. For more details, see our documentation on
        /// <see href="https://docs.launchdarkly.com/sdk/concepts/flag-types">flag value types</see>.
        /// </remarks>
        /// <param name="attributeName">the attribute name to set</param>
        /// <param name="value">the value to set</param>
        /// <returns>the builder</returns>
        /// <seealso cref="Set(string, LdValue)"/>
        public ContextBuilder Set(string attributeName, long value) => Set(attributeName, LdValue.Of(value));

        /// <summary>
        /// Same as <see cref="Set(string, LdValue)"/> for a string value.
        /// </summary>
        /// <param name="attributeName">the attribute name to set</param>
        /// <param name="value">the value to set</param>
        /// <returns>the builder</returns>
        /// <seealso cref="Set(string, LdValue)"/>
        public ContextBuilder Set(string attributeName, string value) => Set(attributeName, LdValue.Of(value));

        /// <summary>
        /// Unsets a previously set attribute value. Has no effect if no such value was set.
        /// </summary>
        /// <param name="attributeName">the attribute name to unset</param>
        /// <returns>the builder</returns>
        /// <seealso cref="Set(string, LdValue)"/>
        public ContextBuilder Remove(string attributeName)
        {
            _attributes?.Remove(attributeName);
            return this;
        }

        /// <summary>
        /// Designates any number of Context attributes, or properties within them, as private: that is,
        /// their values will not be sent to LaunchDarkly.
        /// </summary>
        /// <remarks>
        /// TKTK: conceptual information about private attributes might be in online docs
        /// </remarks>
        /// <param name="attributeRefs">attribute references to mark as private</param>
        /// <returns>the builder</returns>
        /// <seealso cref="Private(AttributeRef[])"/>
        /// <seealso cref="Context.PrivateAttributes"/>
        public ContextBuilder Private(params string[] attributeRefs)
        {
            if (!(attributeRefs is null) && attributeRefs.Length != 0)
            {
                if (_privateAttributes is null)
                {
                    _privateAttributes = ImmutableList.CreateBuilder<AttributeRef>();
                }
                foreach (var a in attributeRefs)
                {
                    _privateAttributes.Add(AttributeRef.FromPath(a));
                }
            }
            return this;
        }

        /// <summary>
        /// Equivalent to <see cref="Private(string[])"/>, but uses the <see cref="AttributeRef"/> type.
        /// </summary>
        /// <remarks>
        /// Application code is unlikely to need to use the <see cref="AttributeRef"/> type directly; however,
        /// in cases where you are constructing Contexts constructed repeatedly with the same set of private
        /// attributes, if you are also using complex private attribute path references such as "/address/street",
        /// converting this to an AttributeRef once and reusing it in many Private calls is slightly more
        /// efficient than passing a string (since it does not need to parse the path repeatedly).
        /// </remarks>
        /// <param name="attributeRefs">attribute references to mark as private</param>
        /// <returns>the builder</returns>
        /// <seealso cref="Private(string[])"/>
        /// <seealso cref="Context.PrivateAttributes"/>
        public ContextBuilder Private(params AttributeRef[] attributeRefs)
        {
            if (!(attributeRefs is null) && attributeRefs.Length != 0)
            {
                if (_privateAttributes is null)
                {
                    _privateAttributes = ImmutableList.CreateBuilder<AttributeRef>();
                }
                _privateAttributes.AddRange(attributeRefs);
            }
            return this;
        }

        internal ContextBuilder CopyFrom(Context c)
        {
            _kind = c.Kind;
            _key = c.Key;
            _name = c.Name;
            _anonymous = c.Anonymous;
            if (c._attributes is null)
            {
                _attributes = null;
            }
            else
            {
                _attributes = ImmutableDictionary.CreateBuilder<string, LdValue>();
                _attributes.AddRange(c._attributes);
            }
            if (c._privateAttributes is null)
            {
                _privateAttributes = null;
            }
            else
            {
                _privateAttributes = ImmutableList.CreateBuilder<AttributeRef>();
                _privateAttributes.AddRange(c._privateAttributes);
            }
            return this;
        }
    }
}
