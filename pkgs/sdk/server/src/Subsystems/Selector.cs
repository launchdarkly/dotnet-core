namespace LaunchDarkly.Sdk.Server.Subsystems
{
    /// <summary>
    /// A selector can either be empty or it can contain state and a version.
    /// <para>
    /// This struct is not stable, and not subject to any backwards compatibility guarantees or semantic versioning.
    /// It is in early access. If you want access to this feature please join the EAP. https://launchdarkly.com/docs/sdk/features/data-saving-mode
    /// </para>
    /// </summary>
    public struct Selector
    {
        /// <summary>
        /// If true, then this selector is empty. An empty selector cannot be used as a basis for a data source.
        /// </summary>
        public bool IsEmpty { get; }
        
        /// <summary>
        /// The version of the data associated with this selector.
        /// </summary>
        public int Version { get; }
        
        /// <summary>
        /// The state associated with the payload.
        /// </summary>
        public string State { get; }

        private Selector(int version, string state, bool isEmpty)
        {
            Version = version;
            State = state;
            IsEmpty = isEmpty;
        }

        internal static Selector Empty { get; } = new Selector(0, null, true);

        internal static Selector Make(int version, string state) => new Selector(version, state, false);
    }
}
