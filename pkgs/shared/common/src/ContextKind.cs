using System;

namespace LaunchDarkly.Sdk
{
    /// <summary>
    /// A string value provided by the application to describe what kind of entity a
    /// <see cref="Context"/> represents.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The type is a simple wrapper for a string. Using a type that is not just <c>string</c>
    /// makes it clearer where a context kind is expected or returned in the SDK API, so it
    /// cannot be confused with other important strings such as <see cref="Context.Key"/>. To
    /// convert a literal string to this type, you can use the shortcut <see cref="Of"/>.
    /// </para>
    /// <para>
    /// The meaning of the context kind is completely up to the application. Validation rules are
    /// as follows:
    /// </para>
    /// <list type="bullet">
    /// <item><description>It may only contain letters, numbers, and the characters ".", "_", and "-".
    /// </description></item>
    /// <item><description>It cannot equal the literal string "kind".</description></item>
    /// <item><description>For a single-kind context, it cannot equal "multi".</description></item>
    /// </list>
    /// <para>
    /// If no kind is specified, the default is "user" (the constant <see cref="Default"/>).
    /// However, an uninitialized struct (<c>new ContextKind()</c> is invalid and has a string
    /// value of "".
    /// </para>
    /// <para>
    /// For a multi-kind Context (see <see cref="Context.NewMulti(Context[])"/>), the kind
    /// of the top-level Context is always "multi" (the constant <see cref="Multi"/>);
    /// there is a specific Kind for each of the Contexts contained within it.
    /// </para>
    /// <para>
    /// To learn more, read <see href="https://docs.launchdarkly.com/home/contexts">the
    /// documentation</see>.
    /// </para>
    /// </remarks>
    public readonly struct ContextKind : IEquatable<ContextKind>
    {
        private const string userKind = "user";

        /// <summary>
        /// A constant for the default kind of "user".
        /// </summary>
        public static readonly ContextKind Default = new ContextKind(userKind);

        /// <summary>
        /// A constant for the kind that all multi-kind Contexts have.
        /// </summary>
        public static readonly ContextKind Multi = new ContextKind("multi");

        private readonly string _value;

        /// <summary>
        /// The string value of the context kind. This is never null.
        /// </summary>
        public string Value => _value ?? "";

        /// <summary>
        /// Constructor from a string value.
        /// </summary>
        /// <remarks>
        /// A value of null or "" will be changed to <see cref="Default"/>.
        /// </remarks>
        /// <param name="stringValue">the string value</param>
        public ContextKind(string stringValue)
        {
            _value = string.IsNullOrEmpty(stringValue) ? userKind : stringValue;
        }

        /// <summary>
        /// Shortcut for calling the constructor.
        /// </summary>
        /// <param name="stringValue">the string value</param>
        /// <returns>a <see cref="ContextKind"/> wrapping this value</returns>
        public static ContextKind Of(string stringValue) =>
            new ContextKind(stringValue);

        /// <summary>
        /// True if this is equal to <see cref="Default"/> ("user").
        /// </summary>
        public bool IsDefault => Value == userKind;

        internal string Validate()
        {
            switch (Value)
            {
                case "kind":
                    return Errors.ContextKindCannotBeKind;
                case "multi":
                    return Errors.ContextKindMultiForSingle;
                default:
                    foreach (var ch in Value)
                    {
                        if ((ch < 'a' || ch > 'z') && (ch < 'A' || ch > 'Z') && (ch < '0' || ch > '9') &&
                            ch != '.' && ch != '_' && ch != '-')
                        {
                            return Errors.ContextKindInvalidChars;
                        }
                    }
                    return null;
            }
        }

        /// <inheritdoc/>
        public override string ToString() => Value;

        /// <inheritdoc/>
        public override int GetHashCode() => Value.GetHashCode();

        /// <inheritdoc/>
        public override bool Equals(object obj) =>
            obj is ContextKind other && Value == other.Value;

        /// <inheritdoc/>
        public bool Equals(ContextKind other) =>
            Value == other.Value;

        /// <inheritdoc/>
        public static bool operator ==(ContextKind a, ContextKind b) => a.Value == b.Value;

        /// <inheritdoc/>
        public static bool operator !=(ContextKind a, ContextKind b) => a.Value != b.Value;
    }
}
