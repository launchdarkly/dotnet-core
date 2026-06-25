
namespace LaunchDarkly.Sdk.Internal
{
    /// <summary>
    /// Helper type for building implementations of <c>object.GetHashCode()</c>.
    /// </summary>
    /// <example>
    /// <code>
    ///     var hashValue = HashCodeBuilder.New()
    ///         .With(someValue1)
    ///         .With(someValue2)
    ///         .Value;
    /// </code>
    /// </example>
    public struct HashCodeBuilder
    {
        /// <summary>
        /// The result value.
        /// </summary>
        public int Value { get; }

        private HashCodeBuilder(int value)
        {
            Value = value;
        }

        /// <summary>
        /// Creates a new builder.
        /// </summary>
        /// <returns></returns>
        public static HashCodeBuilder New() => new HashCodeBuilder(0);

        /// <summary>
        /// Returns an updated builder with the hash value for the given object added in.
        /// </summary>
        /// <param name="o">any object; may be null</param>
        /// <returns>an updated builder</returns>
        public HashCodeBuilder With(object o) =>
            new HashCodeBuilder(Value * 17 + (o == null ? 0 : o.GetHashCode()));
    }
}
