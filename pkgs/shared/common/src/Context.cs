using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using LaunchDarkly.Sdk.Json;

namespace LaunchDarkly.Sdk
{
    /// <summary>
    /// A collection of attributes that can be referenced in flag evaluations and analytics events.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Context is the newer replacement for the previous, less flexible <see cref="User"/> type.
    /// The current SDK still supports User, but Context is now the preferred model and may
    /// entirely replace User in the future.
    /// </para>
    /// <para>
    /// To create a Context of a single kind, such as a user, you may use <see cref="New(string)"/>
    /// or <see cref="New(ContextKind, string)"/> when only the key matters; or, to specify other
    /// attributes, use <see cref="Builder(string)"/>.
    /// </para>
    /// <para>
    /// To create a Context with multiple kinds, use <see cref="NewMulti(Context[])"/> or
    /// <see cref="MultiBuilder"/>.
    /// </para>
    /// <para>
    /// An uninitialized Context struct is not valid for use in any SDK operations. Also, a Context can
    /// be in an error state if it was built with invalid attributes. See <see cref="Error"/>.
    /// </para>
    /// <para>
    /// A Context can be converted to or from JSON using a standard schema; see
    /// <see cref="LdJsonConverters.ContextConverter"/>.
    /// </para>
    /// <para>
    /// To learn more about contexts, read <see href="https://docs.launchdarkly.com/home/contexts">the
    /// documentation</see>.
    /// </para>
    /// </remarks>
    [JsonConverter(typeof(LdJsonConverters.ContextConverter))]
    public readonly struct Context : IEquatable<Context>, IJsonSerializable
    {
        private readonly string _error;
        internal readonly ImmutableList<Context> _multiContexts;
        internal readonly ImmutableDictionary<string, LdValue> _attributes;
        internal readonly ImmutableList<AttributeRef> _privateAttributes;

        /// <summary>
        /// True if this is a Context that was created with a constructor or builder (regardless of
        /// whether its properties are valid), or false if it is an empty uninitialized struct.
        /// </summary>
        /// <seealso cref="Valid"/>
        /// <seealso cref="Error"/>
        public bool Defined { get; }

        /// <summary>
        /// True for a valid Context, false for an invalid Context.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A valid Context is one that can be used in SDK operations. An invalid Context is one that is
        /// missing necessary attributes or has invalid attributes, indicating an incorrect usage of the
        /// SDK API. The only ways for a Context to be invalid are:
        /// </para>
        /// <list type="bullet">
        /// <item><description>
        ///     It has a disallowed value for the Kind property. See <see cref="ContextBuilder.Kind(string)"/>.
        /// </description></item>
        /// <item><description>
        ///     It is a single-kind Context whose Key is empty.
        /// </description></item>
        /// <item><description>
        ///     It is a multi-kind Context that does not have any kinds. See <see cref="MultiBuilder"/>.
        /// </description></item>
        /// <item><description>
        ///     It is a multi-kind Context where the same kind appears more than once.
        /// </description></item>
        /// <item><description>
        ///     It is a multi-kind Context where at least one of the nested Contexts had an error.
        /// </description></item>
        /// <item><description>
        ///     It was created with <see cref="FromUser(User)"/> from a null User reference, or from a
        ///     User that had a null key.
        /// </description></item>
        /// <item><description>
        ///     It is an uninitialized struct (<c>new Context()</c>).
        /// </description></item>
        /// </list>
        /// <para>
        /// Since in normal usage it is easy for applications to be sure they are using context kinds
        /// correctly, and because throwing an exception is undesirable in application code that uses
        /// LaunchDarkly, and because some states such as the empty value are impossible to prevent in
        /// .NET, the SDK stores the error state in the Context itself and checks for such errors
        /// at the time the Context is used, such as in a flag evaluation. At that point, if the Context is
        /// invalid, the operation will fail in some well-defined way as described in the documentation for
        /// that method, and the SDK will generally log a warning as well. But in any situation where you
        /// are not sure if you have a valid Context, you can check the <see cref="Error"/> property.
        /// </para>
        /// </remarks>
        /// <seealso cref="Error"/>
        /// <seealso cref="Defined"/>
        public bool Valid => Error is null;

        /// <summary>
        /// Null for a valid Context, or an error message for an invalid Context.
        /// </summary>
        /// <remarks>
        /// If this is null, then <see cref="Valid"/> is true. If it is non-null, then <see cref="Valid"/> is false.
        /// </remarks>
        /// <seealso cref="Valid"/>
        /// <seealso cref="Defined"/>
        public string Error => Defined ? _error : Errors.ContextUninitialized;

        /// <summary>
        /// The Context's kind attribute.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Every valid Context has a non-empty kind. For multi-kind contexts, this value is
        /// <c>"multi"</c> and the kinds within the Context can be inspected with <see cref="MultiKindContexts"/>
        /// or <see cref="TryGetContextByKind(ContextKind, out Context)"/>.
        /// </para>
        /// </remarks>
        public ContextKind Kind { get; }

        /// <summary>
        /// The Context's key attribute.
        /// </summary>
        /// <remarks>
        /// <para>
        /// For a single-kind context, this value is set by a Context factory method
        /// (<see cref="New(string)"/>, <see cref="New(ContextKind, string)"/>), or
        /// by <see cref="Builder(string)"/> or <see cref="ContextBuilder.Key(string)"/>.
        /// </para>
        /// <para>
        /// For a multi-kind context, there is no single value and <see cref="Key"/> return an
        /// empty string. Use <see cref="MultiKindContexts"/> or <see cref="TryGetContextByKind(ContextKind, out Context)"/>
        /// to inspect a Context for a particular kind, then get the <see cref="Key"/> from it.
        /// </para>
        /// <para>
        /// This value is never null.
        /// </para>
        /// </remarks>
        /// <seealso cref="ContextBuilder.Key(string)"/>
        public string Key { get; }

        /// <summary>
        /// The Context's optional name attribute.
        /// </summary>
        /// <remarks>
        /// <para>
        /// For a single-kind context, this value is set by <see cref="ContextBuilder.Name(string)"/>.
        /// It is null if no value was set.
        /// </para>
        /// <para>
        /// For a multi-kind context, there is no single value and <see cref="Name"/> returns
        /// null. Use <see cref="MultiKindContexts"/> or <see cref="TryGetContextByKind(ContextKind, out Context)"/>
        /// to inspect a Context for a particular kind, then get the <see cref="Name"/> from it.
        /// </para>
        /// </remarks>
        /// <seealso cref="ContextBuilder.Name(string)"/>
        public string Name { get; }

        /// <summary>
        /// True if this Context is only intended for flag evaluations and will not be indexed by
        /// LaunchDarkly.
        /// </summary>
        /// <remarks>
        /// <para>
        /// For a single-kind context, this value is set by <see cref="ContextBuilder.Anonymous(bool)"/>.
        /// It is false if no value was set.
        /// </para>
        /// <para>
        /// Setting Anonymous to true excludes this Context from the database that is used by the dashboard. It does
        /// not exclude it from analytics event data, so it is not the same as making attributes private; all
        /// non-private attributes will still be included in events and data export. There is no limitation on what
        /// other attributes may be included (so, for instance, Anonymous does not mean there is no <see cref="Name"/>),
        /// and the Context will still have whatever <see cref="Key"/> you have given it.
        /// </para>
        /// <para>
        /// For a multi-kind context, there is no single value and <see cref="Anonymous"/> returns
        /// false. Use <see cref="MultiKindContexts"/> or <see cref="TryGetContextByKind(ContextKind, out Context)"/>
        /// to inspect a Context for a particular kind, then get the <see cref="Anonymous"/> value from it.
        /// </para>
        /// </remarks>
        /// <seealso cref="ContextBuilder.Anonymous(bool)"/>
        public bool Anonymous { get; }

        /// <summary>
        /// A string that describes the entire Context based on Kind and Key values.
        /// </summary>
        /// <remarks>
        /// This value is used whenever LaunchDarkly needs a string identifier based on all of the Kind and
        /// Key values in the context; the SDK may use this for caching previously seen contexts, for instance.
        /// </remarks>
        public string FullyQualifiedKey { get; }

        /// <summary>
        /// True for a multi-kind Context, or false for a single-kind Context.
        /// </summary>
        /// <remarks>
        /// <para>
        /// If this value is true, then <see cref="Kind"/> is guaranteed to be "multi", and you can inspect the
        /// individual Contexts for each kind with <see cref="MultiKindContexts"/> or
        /// <see cref="TryGetContextByKind(ContextKind, out Context)"/>.
        /// </para>
        /// <para>
        /// If this value is false, then <see cref="Kind"/> is guaranteed to have a value that is not "multi"/
        /// </para>
        /// </remarks>
        public bool Multiple => !(_multiContexts is null);

        /// <summary>
        /// Enumerates the names of all regular optional attributes defined on this Context.
        /// </summary>
        /// <remarks>
        /// These do not include attributes that always have a value (<see cref="Kind"/>, <see cref="Key"/>,
        /// <see cref="Anonymous"/>), or metadata that is not an attribute addressable in targeting rules
        /// (<see cref="PrivateAttributes"/>). They include any attributes with application-defined names
        /// that have a value, and also "name" if <see cref="Name"/> has a value.
        /// </remarks>
        public IEnumerable<string> OptionalAttributeNames
        {
            get
            {
                if (!(Name is null))
                {
                    yield return "name";
                }
                if (!(_attributes is null))
                {
                    foreach (var entry in _attributes)
                    {
                        yield return entry.Key;
                    }
                }
            }
        }

        /// <summary>
        /// The list of all attribute references marked as private for this specific Context.
        /// </summary>
        /// <remarks>
        /// This includes all attribute names/paths that were specified with
        /// <see cref="ContextBuilder.Private(string[])"/> or <see cref="ContextBuilder.Private(AttributeRef[])"/>.
        /// If there are none, it is an empty list (never null).
        /// </remarks>
        public ImmutableList<AttributeRef> PrivateAttributes => _privateAttributes ??
            ImmutableList.Create<AttributeRef>();

        /// <summary>
        /// Returns all of the individual contexts Contained in a multi-kind Context.
        /// </summary>
        /// <remarks>
        /// <para>
        /// If this is a multi-kind Context, then it returns the individual contexts that were passed to
        /// <see cref="NewMulti(Context[])"/> or <see cref="ContextMultiBuilder.Add(Context)"/>. The
        /// ordering is not guaranteed to be the same.
        /// </para>
        /// <para>
        /// If this is a single-kind Context, then it returns an empty list.
        /// </para>
        /// </remarks>
        /// <seealso cref="TryGetContextByKind(ContextKind, out Context)"/>
        public ImmutableList<Context> MultiKindContexts =>
            _multiContexts ?? ImmutableList.Create<Context>();

        /// <summary>
        /// Creates a single-kind Context with a Kind of <see cref="ContextKind.Default"/> and the specified key.
        /// </summary>
        /// <remarks>
        /// To specify additional properties, use <see cref="Builder(string)"/>. To create a
        /// multi-kind Context, use <see cref="NewMulti(Context[])"/> or <see cref="MultiBuilder"/>.
        /// To create a single-kind Context of a different kind than "user", use
        /// <see cref="New(ContextKind, string)"/>.
        /// </remarks>
        /// <param name="key">the context key</param>
        /// <returns>a Context</returns>
        /// <seealso cref="New(ContextKind, string)"/>
        /// <seealso cref="Builder(string)"/>
        public static Context New(string key) =>
            new Context(
                ContextKind.Default,
                key,
                null,
                false,
                null,
                null,
                false
                );

        /// <summary>
        /// Creates a single-kind Context with only the Kind and Key properties specified.
        /// </summary>
        /// <remarks>
        /// To specify additional properties, use <see cref="Builder(string)"/>. To create a
        /// multi-kind Context, use <see cref="NewMulti(Context[])"/> or <see cref="MultiBuilder"/>.
        /// </remarks>
        /// <seealso cref="New(string)"/>
        /// <seealso cref="Builder(string)"/>
        /// <param name="kind">the context kind</param>
        /// <param name="key">the context key</param>
        /// <returns>a Context</returns>
        public static Context New(ContextKind kind, string key) =>
            new Context(
                kind,
                key,
                null,
                false,
                null,
                null,
                false
                );

        /// <summary>
        /// Creates a multi-kind Context out of the specified single-kind Contexts.
        /// </summary>
        /// <remarks>
        /// <para>
        /// To create a single-kind Context, use <see cref="New(string)"/>,
        /// <see cref="New(ContextKind, string)"/>, or <see cref="Builder(string)"/>.
        /// </para>
        /// <para>
        /// For the returned Context to be valid, the contexts list must not be empty, and all of its
        /// elements must be valid Contexts. Otherwise, the returned Context will be invalid as
        /// reported by <see cref="Error"/>.
        /// </para>
        /// <para>
        /// If only one context parameter is given, <see cref="NewMulti(Context[])"/> returns a single-kind
        /// context (that is, just that same context) rather than a multi-kind context.
        /// </para>
        /// <para>
        /// If a nested context is multi-kind, this is exactly equivalent to adding each of the
        /// individual kinds from it separately. For instance, in the following example, "multi1" and
        /// "multi2" end up being exactly the same:
        /// </para>
        /// <code>
        ///     var c1 = Context.New(ContextKind.Of("kind1"), "key1");
        ///     var c2 = Context.New(ContextKind.Of("kind2"), "key2");
        ///     var c3 = Context.New(ContextKind.Of("kind3"), "key3");
        ///
        ///     var multi1 = Context.NewMulti(c1, c2, c3);
        ///
        ///     var c1plus2 = Context.NewMulti(c1, c2);
        ///     var multi2 = Context.NewMulti(c1plus2, c3);
        /// </code>
        /// </remarks>
        /// <param name="contexts">a list of contexts</param>
        /// <returns>a multi-kind Context</returns>
        /// <seealso cref="MultiBuilder"/>
        public static Context NewMulti(params Context[] contexts)
        {
            if (contexts is null || contexts.Length == 0)
            {
                return new Context(Errors.ContextKindMultiWithNoKinds);
            }
            if (contexts.Length == 1)
            {
                return contexts[0];
            }
            if (contexts.Any(c => c.Multiple))
            {
                var b = MultiBuilder();
                foreach (var c in contexts)
                {
                    b.Add(c);
                }
                return b.Build();
            }
            else
            {
                return new Context(ImmutableList.Create(contexts));
            }
        }

        /// <summary>
        /// Converts a User to an equivalent <see cref="Context"/> instance.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This method is used by the SDK whenever an application passes a <see cref="User"/> instance
        /// to methods such as <c>Identify</c>. The SDK operates internally on the <see cref="Context"/>
        /// model, which is more flexible than the older User model: a User can always be converted to a
        /// Context, but not vice versa. The <see cref="Context.Kind"/> of the resulting Context is
        /// <see cref="ContextKind.Default"/> ("user").
        /// </para>
        /// <para>
        /// Because there is some overhead to this conversion, it is more efficient for applications to
        /// construct a Context and pass that to the SDK, rather than a User. This is also recommended
        /// because the User type may be removed in a future version of the SDK.
        /// </para>
        /// <para>
        /// If the <paramref name="user"/> parameter is null, or if the user has a null key, the method
        /// returns a Context in an invalid state (see <see cref="Valid"/>).
        /// </para>
        /// </remarks>
        /// <param name="user">a User object</param>
        /// <returns>a Context with the same attributes as the User</returns>
        public static Context FromUser(User user)
        {
            if (user is null)
            {
                return new Context(Errors.ContextFromNullUser);
            }

            ImmutableDictionary<string, LdValue>.Builder attrs = null;
            foreach (var a in UserAttribute.OptionalStringAttrs)
            {
                if (a == UserAttribute.Name)
                {
                    continue;
                }
                var value = a.BuiltInGetter(user);
                if (!value.IsNull)
                {
                    if (attrs is null)
                    {
                        attrs = ImmutableDictionary.CreateBuilder<string, LdValue>();
                    }
                    attrs.Add(a.AttributeName, value);
                }
            }
            foreach (var kv in user.Custom)
            {
                if (kv.Key != "" && !kv.Value.IsNull)
                {
                    if (attrs is null)
                    {
                        attrs = ImmutableDictionary.CreateBuilder<string, LdValue>();
                    }
                    attrs.Add(kv.Key, kv.Value);
                }
            }
            ImmutableList<AttributeRef> privateAttrs;
            if (user.PrivateAttributeNames.Count == 0)
            {
                privateAttrs = null;
            }
            else
            {
                ImmutableList<AttributeRef>.Builder privateAttrsBuilder =
                    ImmutableList.CreateBuilder<AttributeRef>();
                foreach (string pa in user.PrivateAttributeNames)
                {
                    privateAttrsBuilder.Add(AttributeRef.FromLiteral(pa));
                }
                privateAttrs = privateAttrsBuilder.ToImmutableList();
            }
            return new Context(
                ContextKind.Default,
                user.Key,
                user.Name,
                user.Anonymous,
                attrs?.ToImmutableDictionary(),
                privateAttrs,
                true // allow empty key for backward compatibility with user model
                );
        }

        /// <summary>
        /// Creates a ContextBuilder for building a Context, initializing its <see cref="ContextBuilder.Key(string)"/>
        /// and setting <see cref="ContextBuilder.Kind(string)"/> to <see cref="ContextKind.Default"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// You may use <see cref="ContextBuilder"/> methods to set additional attributes and/or change the
        /// <see cref="ContextBuilder.Kind(string)"/> before calling <see cref="ContextBuilder.Build"/>.
        /// If you do not change any values, the defaults for the Context are that its <see cref="Kind"/> is
        /// <see cref="ContextKind.Default"/> ("user"), its <see cref="Key"/> is set to whatever value you passed for
        /// <paramref name="key"/>, its <see cref="Anonymous"/> attribute is false, and it has no values for any
        /// other attributes.
        /// </para>
        /// <para>
        /// This method is for building a Context that has only a single Kind. To define a multi-kind
        /// Context, use <see cref="NewMulti(Context[])"/> or <see cref="MultiBuilder"/>.
        /// </para>
        /// <para>
        /// If <paramref name="key"/> is an empty string, there is no default. A Context must have a
        /// non-empty key, so if you call <see cref="ContextBuilder.Build"/> in this state without using
        /// <see cref="ContextBuilder.Key(string)"/> to set the key, you will get an invalid Context.
        /// </para>
        /// </remarks>
        /// <param name="key">the context key</param>
        /// <returns>a builder</returns>
        /// <seealso cref="New(string)"/>
        /// <seealso cref="Builder(ContextKind, string)"/>
        /// <seealso cref="MultiBuilder"/>
        /// <seealso cref="BuilderFromContext(Context)"/>
        public static ContextBuilder Builder(string key) =>
            new ContextBuilder().Key(key);

        /// <summary>
        /// Equivalent to <see cref="Builder(string)"/>, but sets the initial value of
        /// <see cref="ContextBuilder.Kind(ContextKind)"/> as well as the key.
        /// </summary>
        /// <param name="kind">the context kind</param>
        /// <param name="key">the context key</param>
        /// <returns>a builder</returns>
        /// <seealso cref="New(ContextKind, string)"/>
        /// <seealso cref="Builder(string)"/>
        /// <seealso cref="MultiBuilder"/>
        /// <seealso cref="BuilderFromContext(Context)"/>
        public static ContextBuilder Builder(ContextKind kind, string key) =>
            new ContextBuilder().Kind(kind).Key(key);

        /// <summary>
        /// Creates a ContextBuilder whose properties are the same as an existing single-kind Context.
        /// You may then change the ContextBuilder's state in any way and call <see cref="ContextBuilder.Build"/>
        /// to create a new independent Context.
        /// </summary>
        /// <param name="context">the context to copy from</param>
        /// <returns>a builder</returns>
        /// <seealso cref="Builder(string)"/>
        public static ContextBuilder BuilderFromContext(Context context) =>
            new ContextBuilder().CopyFrom(context);

        /// <summary>
        /// Creates a ContextMultiBuilder for building a Context.
        /// </summary>
        /// <remarks>
        /// This method is for building a Context athat has multiple Kind values, each with its own
        /// nested Context. To define a single-kind context, use <see cref="Builder(string)"/> instead.
        /// </remarks>
        /// <returns>a builder</returns>
        public static ContextMultiBuilder MultiBuilder() =>
            new ContextMultiBuilder();

        internal Context(
            ContextKind kind,
            string key,
            string name,
            bool anonymous,
            ImmutableDictionary<string, LdValue> attributes,
            ImmutableList<AttributeRef> privateAttributes,
            bool allowEmptyKey
            )
        {
            Defined = true;

            var error = kind.Validate();
            if (error is null)
            {
                if (key is null || (key == "" && !allowEmptyKey))
                {
                    error = Errors.ContextNoKey;
                }
            }

            _error = error;
            if (error is null)
            {
                Kind = kind;
                Key = key;
                Name = name;
                Anonymous = anonymous;
                _attributes = attributes;
                _privateAttributes = privateAttributes;
                FullyQualifiedKey = Kind.IsDefault ? key :
                    (Kind + ":" + EscapeKeyForFullyQualifiedKey(key));
            }
            else
            {
                Kind = new ContextKind();
                Key = "";
                Name = null;
                Anonymous = false;
                _attributes = null;
                _privateAttributes = null;
                FullyQualifiedKey = "";
            }
            _multiContexts = null;
        }

        internal Context(ImmutableList<Context> contexts)
        {
            // Before calling this constructor, we have already verified that contexts is non-null
            // and has more than one element.
            Defined = true;

            List<string> errors = null;
            var duplicates = false;
            for (int i = 0; i < contexts.Count; i++)
            {
                var c = contexts[i];
                if (c.Error != null)
                {
                    if (errors is null)
                    {
                        errors = new List<string>();
                    }
                    errors.Add($"({c.Kind}) {c.Error}");
                }
                else
                {
                    for (int j = 0; j < i; j++)
                    {
                        if (c.Kind == contexts[j].Kind)
                        {
                            duplicates = true;
                            break;
                        }
                    }
                }
            }
            if (duplicates)
            {
                if (errors is null)
                {
                    errors = new List<string>();
                }
                errors.Add(Errors.ContextKindMultiDuplicates);
            }

            _error = (errors is null || errors.Count == 0) ? null :
                string.Join(", ", errors);

            if (_error is null)
            {
                Kind = ContextKind.Multi;
                _multiContexts = contexts.OrderBy(c => c.Kind.Value).ToImmutableList();
                var buildKey = new StringBuilder();
                foreach (var c in _multiContexts)
                {
                    if (buildKey.Length != 0)
                    {
                        buildKey.Append(':');
                    }
                    buildKey.Append(c.Kind).Append(':').Append(EscapeKeyForFullyQualifiedKey(c.Key));
                }
                FullyQualifiedKey = buildKey.ToString();
            }
            else
            {
                Kind = new ContextKind();
                _multiContexts = null;
                FullyQualifiedKey = "";
            }

            Key = "";
            Name = null;
            Anonymous = false;
            _attributes = null;
            _privateAttributes = null;
        }

        internal Context(string error)
        {
            Defined = true;
            _error = error;
            _multiContexts = null;
            Kind = new ContextKind();
            Key = "";
            Name = null;
            Anonymous = false;
            _attributes = null;
            _privateAttributes = null;
            FullyQualifiedKey = "";
        }

        /// <summary>
        /// Looks up the value of any attribute of the Context by name. This includes only attributes
        /// that are addressable in evaluations-- not metadata such as <see cref="PrivateAttributes"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// For a single-kind context, the attribute name can be any custom attribute that was set by methods
        /// like <see cref="ContextBuilder.Set(string, bool)"/>. It can also be one of the built-in ones like
        /// "kind", "key", or "name"; in such cases, it is equivalent to <see cref="Kind"/>,
        /// <see cref="Key"/>, or <see cref="Name"/>, except that the value is returned using the general-purpose
        /// <see cref="LdValue"/> type.
        /// </para>
        /// <para>
        /// For a multi-kind context, the only supported attribute name is "kind". Use
        /// <see cref="MultiKindContexts"/> or <see cref="TryGetContextByKind(ContextKind, out Context)"/> to inspect
        /// a Context for a particular kind and then get its attributes.
        /// </para>
        /// <para>
        /// This method does not support complex expressions for getting individual values out of JSON objects
        /// or arrays, such as "/address/street". Use <see cref="GetValue(in AttributeRef)"/> with an
        /// <see cref="AttributeRef"/> for that purpose.
        /// </para>
        /// <para>
        /// If the value is found, the return value is the attribute value, using the type <see cref="LdValue"/>
        /// to represent a value of any JSON type.
        /// </para>
        /// <para>
        /// If there is no such attribute, the return value is <see cref="LdValue.Null"/>. An attribute that
        /// actually exists cannot have a null value.
        /// </para>
        /// </remarks>
        /// <param name="attributeName">the desired attribute name</param>
        /// <returns>the value or <see cref="LdValue.Null"/></returns>
        /// <seealso cref="GetValue(in AttributeRef)"/>
        public LdValue GetValue(string attributeName) =>
            GetValue(AttributeRef.FromLiteral(attributeName));

        /// <summary>
        /// Looks up the value of any attribute of the Context, or a value contained within an
        /// attribute, based on an <see cref="AttributeRef"/>. This includes only attributes that
        /// are addressable in evaluations-- not metadata such as <see cref="PrivateAttributes"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This implements the same behavior that the SDK uses to resolve attribute references during a
        /// flag evaluation. In a single-kind context, the <see cref="AttributeRef"/> can represent a
        /// simple attribute name-- either a built-in one like "name" or "key", or a custom attribute
        /// that was set by methods like <see cref="ContextBuilder.Set(string, string)"/>-- or, it can be a
        /// a slash-delimited path using a JSON-Pointer-like syntax. See <see cref="AttributeRef"/>
        /// for more details.
        /// </para>
        /// <para>
        /// For a multi-kind context, the only supported attribute name is "kind". Use
        /// <see cref="MultiKindContexts"/> or <see cref="TryGetContextByKind(ContextKind, out Context)"/> to inspect
        /// a Context for a particular kind and then get its attributes.
        /// </para>
        /// <para>
        /// If the value is found, the return value is the attribute value, using the type
        /// <see cref="LdValue"/> to represent a value of any JSON type).
        /// </para>
        /// <para>
        /// If there is no such attribute, or if the <see cref="AttributeRef"/> is invalid, the return
        /// value is <see cref="LdValue.Null"/>. An attribute that actually exists cannot have a null
        /// value.
        /// </para>
        /// </remarks>
        /// <param name="attributeRef">an attribute reference</param>
        /// <returns>the value or <see cref="LdValue.Null"/></returns>
        /// <seealso cref="GetValue(string)"/>
        public LdValue GetValue(in AttributeRef attributeRef)
        {
            if (!attributeRef.Valid)
            {
                return LdValue.Null;
            }

            var name = attributeRef.GetComponent(0);
            if (name is null)
            {
                return LdValue.Null;
            }

            if (Multiple)
            {
                if (attributeRef.Depth == 1 && name == "kind")
                {
                    return LdValue.Of(Kind.Value);
                }
                return LdValue.Null; // multi-kind context has no other addressable attributes
            }

            // Look up attribute in single-kind context
            var value = GetTopLevelAddressableAttributeSingleKind(name);
            if (value.IsNull)
            {
                return value;
            }
            for (int i = 1; i < attributeRef.Depth; i++)
            {
                var component = attributeRef.GetComponent(i);
                if (component is null)
                {
                    return LdValue.Null;
                }
                var dict = value.Dictionary; // returns an empty dictionary if value is not an object
                if (!dict.TryGetValue(component, out value))
                {
                    value = LdValue.Null;
                }
                if (value.IsNull)
                {
                    break;
                }
            }
            return value;
        }

        /// <summary>
        /// Gets the single-kind context, if any, whose <see cref="Kind"/> matches the specified
        /// value exactly.
        /// </summary>
        /// <remarks>
        /// <para>
        /// If the method is called on a single-kind context, then the specified kind must match the
        /// <see cref="Kind"/> of that context. If the method is called on a multi-kind context, then
        /// the kind can match any of the individual contexts within.
        /// </para>
        /// </remarks>
        /// <param name="kind">the desired context kind</param>
        /// <param name="context">receives the context that was found, if successful</param>
        /// <returns>true if found, false if not found</returns>
        public bool TryGetContextByKind(ContextKind kind, out Context context)
        {
            if (Multiple)
            {
                foreach (var c in _multiContexts)
                {
                    if (c.Kind == kind)
                    {
                        context = c;
                        return true;
                    }
                }
                context = new Context();
                return false;
            }
            if (Kind == kind)
            {
                context = this;
                return true;
            }
            context = new Context();
            return false;
        }

        /// <inheritdoc/>
        public override bool Equals(object other) =>
            other is Context c && Equals(c);

        /// <inheritdoc/>
        public bool Equals(Context other)
        {
            if (!Defined || !other.Defined)
            {
                return Defined == other.Defined;
            }

            if (Kind != other.Kind)
            {
                return false;
            }

            if (Multiple)
            {
                if (other._multiContexts is null || _multiContexts.Count != other._multiContexts.Count)
                {
                    return false;
                }
                foreach (var mc1 in _multiContexts)
                {
                    if (!other.TryGetContextByKind(mc1.Kind, out var mc2) || !mc1.Equals(mc2))
                    {
                        return false;
                    }
                }
                return true;
            }

            if (Key != other.Key || Name != other.Name || Anonymous != other.Anonymous)
            {
                return false;
            }
            if (_attributes?.Count != other._attributes?.Count ||
                (!(_attributes is null) && !_attributes.All(kv => other._attributes.TryGetValue(kv.Key, out var v) && kv.Value.Equals(v))))
            {
                return false;
            }
            if (_privateAttributes?.Count != other._privateAttributes?.Count ||
                (!(_privateAttributes is null) && !_privateAttributes.All(a => other._privateAttributes.Contains(a))))
            {
                return false;
            }

            return true;
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            var hashBuilder = new HashCodeBuilder();
            if (Multiple)
            {
                foreach (var c in _multiContexts)
                {
                    hashBuilder = hashBuilder.With(c);
                }
            }
            else
            {
                hashBuilder = hashBuilder.With(Kind)
                    .With(Key)
                    .With(Name)
                    .With(Anonymous);
                if (!(_attributes is null))
                {
                    foreach (var attr in _attributes.Keys.OrderBy(a => a))
                    {
                        hashBuilder = hashBuilder.With(attr).With(_attributes[attr]);
                    }
                }
                if (!(_privateAttributes is null))
                {
                    foreach (var p in _privateAttributes.OrderBy(p => p.ToString()))
                    {
                        hashBuilder = hashBuilder.With(p);
                    }
                }
            }
            return hashBuilder.Value;
        }

        /// <summary>
        /// Returns a string representation of the Context.
        /// </summary>
        /// <remarks>
        /// For a valid Context, this is currently defined as being the same as the JSON representation,
        /// since that is the simplest way to represent all of the Context properties. However, application
        /// code should not rely on <see cref="ToString"/> always being the same as the JSON representation.
        /// If you specifically want the latter, use <see cref="LdJsonSerialization.SerializeObject{T}(T)"/>.
        /// For an invalid Context, ToString() returns a description of why it is invalid.
        /// </remarks>
        /// <returns>a string representation</returns>
        public override string ToString()
        {
            if (!Defined)
            {
                return "(uninitialized Context)";
            }
            if (!(Error is null))
            {
                return "(invalid Context: " + Error + ")";
            }
#if NET7_0_OR_GREATER
            return System.Text.Json.JsonSerializer.Serialize(this, LdJsonSerializerContext.Default.Context);
#else
            return LdJsonSerialization.SerializeObject(this);
#endif
        }

        private LdValue GetTopLevelAddressableAttributeSingleKind(string name)
        {
            switch (name)
            {
                case "kind":
                    return LdValue.Of(Kind.Value);
                case "key":
                    return LdValue.Of(Key);
                case "name":
                    return LdValue.Of(Name);
                case "anonymous":
                    return LdValue.Of(Anonymous);
                default:
                    if (_attributes is null)
                    {
                        return LdValue.Null;
                    }
                    return _attributes.TryGetValue(name, out var value) ? value : LdValue.Null;
            }
        }

        // When building a FullyQualifiedKey, ':' and '%' are percent-escaped; we do not use a full
        // URL-encoding function because implementations of this are inconsistent across platforms.
        private static string EscapeKeyForFullyQualifiedKey(string key) =>
            key.Replace("%", "%25").Replace(":", "%3A");
    }
}
