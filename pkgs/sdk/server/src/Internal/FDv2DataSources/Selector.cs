namespace LaunchDarkly.Sdk.Server.Internal.FDv2DataSources
{
    /// <summary>
    /// A selector can either be empty or it can contain state and a version.
    /// </summary>
    internal struct Selector
    {
        public bool IsEmpty { get; }
        public int Version { get; }
        public string State { get; }

        private Selector(int version, string state, bool isEmpty)
        {
            Version = version;
            State = state;
            IsEmpty = isEmpty;
        }

        public static Selector Empty { get; } = new Selector(0, null, true);

        public static Selector Make(int version, string state) => new Selector(version, state, false);
    }
}
