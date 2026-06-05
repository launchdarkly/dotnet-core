namespace LaunchDarkly.Sdk.Server.Ai.Config;

public static partial class LdAiConfigTypes
{
    /// <summary>
    /// Represents the role of the prompt message.
    /// </summary>
    public enum Role
    {
        /// <summary>
        /// User role.
        /// </summary>
        User,

        /// <summary>
        /// System role.
        /// </summary>
        System,

        /// <summary>
        /// Assistant role.
        /// </summary>
        Assistant
    }
}
