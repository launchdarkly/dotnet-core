using System;
using System.Threading.Tasks;
using LaunchDarkly.Sdk.Client.Interfaces;

namespace LaunchDarkly.Sdk.Client
{
    /// <summary>
    /// Convenience methods that extend the <see cref="ILdClient"/> interface.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These allow you to do the following:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    ///     Treat a string-valued flag as if it referenced values of an <c>enum</c> type.
    /// </description></item>
    /// <item><description>
    ///     Call <see cref="ILdClient"/> methods with the <see cref="User"/> type instead of
    ///     <see cref="Context"/>. The SDK's preferred type for identifying an evaluation context,
    ///     is <see cref="Context"/>; older versions of the SDK used only the simpler <see cref="User"/>
    ///     model. These extension methods provide backward compatibility with application code that
    ///     used the <see cref="User"/> type. Each of them simply converts the User to a Context with
    ///     <see cref="Context.FromUser(User)"/> and calls the equivalent ILdClient method.
    ///     For instance, <c>client.Identify(user)</c> is exactly equivalent to
    ///     <c>client.Identify(Context.FromUser(user))</c>.
    /// </description></item>
    /// </list>
    /// <para>
    /// These are implemented outside of <see cref="ILdClient"/> and <see cref="LdClient"/> because they do not
    /// rely on any implementation details of <see cref="LdClient"/>; they are decorators that would work equally
    /// well with a stub or test implementation of the interface.
    /// </para>
    /// </remarks>
    public static class ILdClientExtensions
    {
        /// <summary>
        /// Equivalent to <see cref="ILdClient.StringVariation(string, string)"/>, but converts the
        /// flag's string value to an enum value.
        /// </summary>
        /// <remarks>
        /// <para>
        /// If the flag has a value that is not one of the allowed enum value names, or is not a string,
        /// <c>defaultValue</c> is returned.
        /// </para>
        /// </remarks>
        /// <typeparam name="T">the enum type</typeparam>
        /// <param name="client">the client instance</param>
        /// <param name="key">the unique feature key for the feature flag</param>
        /// <param name="defaultValue">the default value of the flag (as an enum value)</param>
        /// <returns>the variation for the given user, or <c>defaultValue</c> if the flag cannot
        /// be evaluated or does not have a valid enum value</returns>
        public static T EnumVariation<T>(this ILdClient client, string key, T defaultValue) where T : struct, Enum
        {
            var stringVal = client.StringVariation(key, defaultValue.ToString());
            if (stringVal != null)
            {
                if (Enum.TryParse<T>(stringVal, out var enumValue))
                {
                    return enumValue;
                }
            }
            return defaultValue;
        }

        /// <summary>
        /// Equivalent to <see cref="ILdClient.StringVariationDetail(string, string)"/>, but converts the
        /// flag's string value to an enum value.
        /// </summary>
        /// <remarks>
        /// <para>
        /// If the flag has a value that is not one of the allowed enum value names, or is not a string,
        /// <c>defaultValue</c> is returned.
        /// </para>
        /// </remarks>
        /// <typeparam name="T">the enum type</typeparam>
        /// <param name="client">the client instance</param>
        /// <param name="key">the unique feature key for the feature flag</param>
        /// <param name="defaultValue">the default value of the flag (as an enum value)</param>
        /// <returns>an <see cref="EvaluationDetail{T}"/> object</returns>
        public static EvaluationDetail<T> EnumVariationDetail<T>(this ILdClient client,
            string key, T defaultValue) where T : struct, Enum
        {
            var stringDetail = client.StringVariationDetail(key, defaultValue.ToString());
            if (!stringDetail.IsDefaultValue && stringDetail.Value != null)
            {
                if (Enum.TryParse<T>(stringDetail.Value, out var enumValue))
                {
                    return new EvaluationDetail<T>(enumValue, stringDetail.VariationIndex, stringDetail.Reason);
                }
                return new EvaluationDetail<T>(defaultValue, stringDetail.VariationIndex, EvaluationReason.ErrorReason(EvaluationErrorKind.WrongType));
            }
            return new EvaluationDetail<T>(defaultValue, stringDetail.VariationIndex, stringDetail.Reason);
        }

        /// <summary>
        /// Changes the current user, requests flags for that user from LaunchDarkly if we are online,
        /// and generates an analytics event to tell LaunchDarkly about the user.
        /// </summary>
        /// <remarks>
        /// This is equivalent to <see cref="ILdClient.Identify(Context, TimeSpan)"/>, but using the
        /// <see cref="User"/> type instead of <see cref="Context"/>.
        /// </remarks>
        /// <param name="client">the client instance</param>
        /// <param name="user">the user; should not be null (a null reference will cause an error
        /// to be logged and no event will be sent</param>
        /// <param name="maxWaitTime">the maximum time to wait for the new flag values</param>
        /// <returns>true if new flag values were obtained</returns>
        [Obsolete("User has been superseded by Context.  See ILdClient.Identify(Context, TimeSpan)")]
        public static bool Identify(this ILdClient client, User user, TimeSpan maxWaitTime) =>
            client.Identify(Context.FromUser(user), maxWaitTime);


        /// <summary>
        /// Changes the current user, requests flags for that user from LaunchDarkly if we are online,
        /// and generates an analytics event to tell LaunchDarkly about the user.
        /// </summary>
        /// <remarks>
        /// This is equivalent to <see cref="ILdClient.IdentifyAsync(Context)"/>, but using the
        /// <see cref="User"/> type instead of <see cref="Context"/>.
        /// </remarks>
        /// <param name="client">the client instance</param>
        /// <param name="user">the user; should not be null (a null reference will cause an error
        /// to be logged and no event will be sent</param>
        /// <returns>a task that yields true if new flag values were obtained</returns>
        [Obsolete("User has been superseded by Context.  See ILdClient.Identify(Context, TimeSpan)")]
        public static Task<bool> IdentifyAsync(this ILdClient client, User user) =>
            client.IdentifyAsync(Context.FromUser(user));
    }
}
