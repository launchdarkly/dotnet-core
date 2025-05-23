using System;
using System.Collections.Immutable;
using System.Linq;
using LaunchDarkly.Sdk.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;

namespace LaunchDarkly.Sdk
{
    /// <summary>
    /// Attributes of a user for whom you are evaluating feature flags.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="User"/> contains any user-specific properties that may be used in feature flag
    /// configurations to produce different flag variations for different users. You may define
    /// these properties however you wish.
    /// </para>
    /// <para>
    /// User supports only a subset of the behaviors that are available with the newer
    /// <see cref="Context"/> type. A User is equivalent to an individual Context that has a
    /// <see cref="Context.Kind"/> of <see cref="ContextKind.Default"/> ("user"); it also has
    /// more constraints on attribute values than a Context does (for instance, built-in attributes
    /// such as <see cref="User.Email"/> can only have string values). Older LaunchDarkly SDKs only
    /// had the User model, and the User type has been retained for backward compatibility, but it
    /// may be removed in a future SDK version; also, the SDK will always convert a User to a
    /// Context internally, which has some overhead. Therefore, developers are recommended to
    /// migrate toward using Context.
    /// </para>
    /// <para>
    /// The only mandatory property of User is the <see cref="Key"/>, which must uniquely identify
    /// each user. For authenticated users, this may be a username or e-mail address. For anonymous
    /// users, this could be an IP address or session ID.
    /// </para>
    /// <para>
    /// Besides the mandatory key, <see cref="User"/> supports two kinds of optional attributes:
    /// built-in attributes (e.g. <see cref="Name"/> and <see cref="Country"/>) and custom
    /// attributes. The built-in attributes have specific allowed value types; also, two of them
    /// (<see cref="Name"/> and <see cref="Anonymous"/>) have special meanings in LaunchDarkly.
    /// Custom attributes have flexible value types, and can have any names that do not conflict
    /// with built-in attributes.
    /// </para>
    /// <para>
    /// Both built-in attributes and custom attributes can be referenced in targeting rules, and
    /// are included in analytics data.
    /// </para>
    /// <para>
    /// Instances of <c>User</c> are immutable once created. They can be created with the factory method
    /// <see cref="User.WithKey(string)"/>, or using a builder pattern with <see cref="User.Builder(string)"/>
    /// or <see cref="User.Builder(User)"/>.
    /// </para>
    /// <para>
    /// For converting this type to or from JSON, see <see cref="LaunchDarkly.Sdk.Json"/>.
    /// </para>
    /// </remarks>
    [JsonConverter(typeof(LdJsonConverters.UserConverter))]
    public class User : IEquatable<User>, IJsonSerializable
    {
        private readonly string _key;
        private readonly string _ip;
        private readonly string _country;
        private readonly string _firstName;
        private readonly string _lastName;
        private readonly string _name;
        private readonly string _avatar;
        private readonly string _email;
        private readonly bool _anonymous;
        internal readonly ImmutableDictionary<string, LdValue> _custom;
        internal readonly ImmutableHashSet<string> _privateAttributeNames;

        /// <summary>
        /// The unique key for the user.
        /// </summary>
        public string Key => _key;

        /// <summary>
        /// The IP address of the user.
        /// </summary>
        public string IPAddress => _ip;

        /// <summary>
        /// The country code for the user.
        /// </summary>
        public string Country => _country;

        /// <summary>
        /// The user's first name.
        /// </summary>
        public string FirstName => _firstName;

        /// <summary>
        /// The user's last name.
        /// </summary>
        public string LastName => _lastName;

        /// <summary>
        /// The user's full name.
        /// </summary>
        public string Name => _name;

        /// <summary>
        /// The user's avatar.
        /// </summary>
        public string Avatar => _avatar;

        /// <summary>
        /// The user's email address.
        /// </summary>
        public string Email => _email;

        /// <summary>
        /// Whether or not the user is anonymous.
        /// </summary>
        public bool Anonymous => _anonymous;

        /// <summary>
        /// Custom attributes for the user.
        /// </summary>
        public IImmutableDictionary<string, LdValue> Custom => _custom;

        /// <summary>
        /// Used internally to track which attributes are private.
        /// </summary>
        public IImmutableSet<string> PrivateAttributeNames => _privateAttributeNames;

        /// <summary>
        /// Creates an <see cref="IUserBuilder"/> for constructing a user object using a fluent syntax.
        /// </summary>
        /// <remarks>
        /// This is the only method for building a <see cref="User"/> if you are setting properties
        /// besides the <see cref="User.Key"/>. The <see cref="IUserBuilder"/> has methods for setting
        /// any number of properties, after which you call <see cref="IUserBuilder.Build"/> to get the
        /// resulting <see cref="User"/> instance.
        /// </remarks>
        /// <example>
        /// <code>
        ///     var user = User.Builder("my-key").Name("Bob").Email("test@example.com").Build();
        /// </code>
        /// </example>
        /// <param name="key">a <see langword="string"/> that uniquely identifies a user</param>
        /// <returns>a builder object</returns>
        public static IUserBuilder Builder(string key)
        {
            return new UserBuilder(key);
        }

        /// <summary>
        /// Creates an <see cref="IUserBuilder"/> for constructing a user object, with its initial
        /// properties copied from an existeing user.
        /// </summary>
        /// <remarks>
        /// This is the same as calling <c>User.Builder(fromUser.Key)</c> and then calling the
        /// <see cref="IUserBuilder"/> methods to set each of the individual properties from their current
        /// values in <c>fromUser</c>. Modifying the builder does not affect the original <see cref="User"/>.
        /// </remarks>
        /// <example>
        /// <code>
        ///     var user1 = User.Builder("my-key").FirstName("Joe").LastName("Schmoe").Build();
        ///     var user2 = User.Builder(user1).FirstName("Jane").Build();
        ///     // this is equvalent to: user2 = User.Builder("my-key").FirstName("Jane").LastName("Schmoe").Build();
        /// </code>
        /// </example>
        /// <param name="fromUser">the user to copy</param>
        /// <returns>a builder object</returns>
        public static IUserBuilder Builder(User fromUser)
        {
            return new UserBuilder(fromUser);
        }

        private User(string key)
        {
            _key = key;
            _custom = ImmutableDictionary.Create<string, LdValue>();
            _privateAttributeNames = ImmutableHashSet.Create<string>();
        }

        /// <summary>
        /// Creates a user by specifying all properties.
        /// </summary>
        public User(string key, string secondary, string ip, string country, string firstName,
                    string lastName, string name, string avatar, string email, bool? anonymous,
                    ImmutableDictionary<string, LdValue> custom, ImmutableHashSet<string> privateAttributeNames)
        {
            _key = key;
            // _secondary = secondary;
            // secondary no longer exists; retained in constructor to minimize breakage if applications
            // were calling the User constructor directly
            _ip = ip;
            _country = country;
            _firstName = firstName;
            _lastName = lastName;
            _name = name;
            _avatar = avatar;
            _email = email;
            _anonymous = anonymous ?? false;
            // anonymous is now just a simple bool; kept bool? type in constructor for same reason as above
            _custom = custom ?? ImmutableDictionary.Create<string, LdValue>();
            _privateAttributeNames = privateAttributeNames ?? ImmutableHashSet.Create<string>();
        }

        /// <summary>
        /// Creates a user with the given key.
        /// </summary>
        /// <param name="key">a <see langword="string"/> that uniquely identifies a user</param>
        /// <returns>a <see cref="User"/> instance</returns>
        public static User WithKey(string key)
        {
            return new User(key);
        }

        /// <summary>
        /// Gets the value of a user attribute, if present.
        /// </summary>
        /// <remarks>
        /// This can be either a built-in attribute or a custom one. It returns the value using the
        /// <see cref="LdValue"/> type, which can have any type that is supported in JSON. If the
        /// attribute does not exist, it returns <see cref="LdValue.Null"/>.
        /// </remarks>
        /// <param name="attribute">the attribute to get</param>
        /// <returns>the attribute value or <see cref="LdValue.Null"/></returns>
        public LdValue GetAttribute(UserAttribute attribute)
        {
            if (attribute.BuiltIn)
            {
                return attribute.BuiltInGetter(this);
            }
            if (_custom != null && _custom.TryGetValue(attribute.AttributeName, out var value))
            {
                return value;
            }
            return LdValue.Null;
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            if (obj is User u)
            {
                return ((IEquatable<User>)this).Equals(u);
            }
            return false;
        }

        /// <inheritdoc/>
        public bool Equals(User u)
        {
            if (u == null)
            {
                return false;
            }
            if (ReferenceEquals(this, u))
            {
                return true;
            }
            return Object.Equals(Key, u.Key) &&
                Object.Equals(IPAddress, u.IPAddress) &&
                Object.Equals(Country, u.Country) &&
                Object.Equals(FirstName, u.FirstName) &&
                Object.Equals(LastName, u.LastName) &&
                Object.Equals(Name, u.Name) &&
                Object.Equals(Avatar, u.Avatar) &&
                Object.Equals(Email, u.Email) &&
                Anonymous == u.Anonymous &&
                Custom.Count == u.Custom.Count &&
                Custom.Keys.All(k => u.Custom.ContainsKey(k) && Object.Equals(Custom[k], u.Custom[k])) &&
                PrivateAttributeNames.SetEquals(u.PrivateAttributeNames);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            var hashBuilder = new HashCodeBuilder()
                .With(Key)
                .With(IPAddress)
                .With(Country)
                .With(FirstName)
                .With(LastName)
                .With(Name)
                .With(Avatar)
                .With(Email)
                .With(Anonymous);
            foreach (var c in Custom)
            {
                hashBuilder = hashBuilder.With(c.Key).With(c.Value);
            }
            foreach (var p in PrivateAttributeNames)
            {
                hashBuilder = hashBuilder.With(p);
            }
            return hashBuilder.Value;
        }
    }
}
