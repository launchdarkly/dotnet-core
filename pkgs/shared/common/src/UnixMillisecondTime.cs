using System;
using System.Text.Json.Serialization;
using LaunchDarkly.Sdk.Json;

namespace LaunchDarkly.Sdk
{
    /// <summary>
    /// An instant measured in milliseconds since the Unix epoch.
    /// </summary>
    /// <remarks>
    /// <para>
    /// LaunchDarkly services internally use this method of representing a date/timestamp as an
    /// integer. For instance, it is used for the creation time property of an analytics event.
    /// You do not need to refer to this type during normal usage of LaunchDarkly SDKs, but it
    /// is public and supported for convenience.
    /// </para>
    /// <para>
    /// When converting to or from JSON, it is encoded as an integer.
    /// </para>
    /// </remarks>
    [JsonConverter(typeof(LdJsonConverters.UnixMillisecondTimeConverter))]
    public struct UnixMillisecondTime : IEquatable<UnixMillisecondTime>, IComparable<UnixMillisecondTime>,
        IJsonSerializable
    {
        /// <summary>
        /// The instant that defines the beginning of Unix time.
        /// </summary>
        public static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// The millisecond time value.
        /// </summary>
        public long Value { get; }

        /// <summary>
        /// Converts this value to a <c>DateTime</c>.
        /// </summary>
        public DateTime AsDateTime => Epoch.AddMilliseconds(Value);

        private UnixMillisecondTime(long value)
        {
            Value = value;
        }

        /// <summary>
        /// Gets the current date/time as a <c>UnixMillisecondTime</c>.
        /// </summary>
        public static UnixMillisecondTime Now => FromDateTime(DateTime.UtcNow);

        /// <summary>
        /// Creates a <c>UnixMillisecondTime</c> value.
        /// </summary>
        /// <param name="millis">the millisecond time value</param>
        /// <returns>a <c>UnixMillisecondTime</c></returns>
        public static UnixMillisecondTime OfMillis(long millis) =>
            new UnixMillisecondTime(millis);

        /// <summary>
        /// Converts a <c>DateTime</c> to <c>UnixMillisecondTime</c>.
        /// </summary>
        /// <param name="dateTime">a <c>DateTime</c></param>
        /// <returns>a <c>UnixMillisecondTime</c></returns>
        public static UnixMillisecondTime FromDateTime(DateTime dateTime) =>
            new UnixMillisecondTime(
                (long)(dateTime - Epoch).TotalMilliseconds
                );

        /// <summary>
        /// Computes a new time based on a offset in milliseconds from this one.
        /// </summary>
        /// <param name="millis">a positive or negative number of milliseconds</param>
        /// <returns>a new <c>UnixMillisecondTime</c></returns>
        public UnixMillisecondTime PlusMillis(long millis) =>
            new UnixMillisecondTime(Value + millis);

#pragma warning disable CS1591  // don't need XML comments for these standard methods
        public bool Equals(UnixMillisecondTime other) => Value == other.Value;

        public int CompareTo(UnixMillisecondTime other) => Value.CompareTo(other.Value);

        public override bool Equals(object other) => other is UnixMillisecondTime &&
            Equals((UnixMillisecondTime)other);

        public override int GetHashCode() => Value.GetHashCode();

        public static bool operator ==(UnixMillisecondTime a, UnixMillisecondTime b) =>
            a.Value == b.Value;

        public static bool operator !=(UnixMillisecondTime a, UnixMillisecondTime b) =>
            a.Value != b.Value;

        public static bool operator <(UnixMillisecondTime a, UnixMillisecondTime b) =>
            a.Value < b.Value;

        public static bool operator <=(UnixMillisecondTime a, UnixMillisecondTime b) =>
            a.Value <= b.Value;

        public static bool operator >(UnixMillisecondTime a, UnixMillisecondTime b) =>
            a.Value > b.Value;

        public static bool operator >=(UnixMillisecondTime a, UnixMillisecondTime b) =>
            a.Value >= b.Value;
#pragma warning restore CS1591
    }
}
