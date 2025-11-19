namespace LaunchDarkly.Sdk.Server.Internal.FDv2DataSources
{
    /// <summary>
    /// A selector can either be empty or it can contain state and a version.
    /// </summary>
    internal struct FDv2Selector
    {
        public bool IsEmpty { get; }
        public int Version { get; }
        public string State { get; }

        private FDv2Selector(int version, string state, bool isEmpty)
        {
            Version = version;
            State = state;
            IsEmpty = isEmpty;
        }

        public static FDv2Selector Empty { get; } = new FDv2Selector(0, null, true);

        public static FDv2Selector Make(int version, string state) => new FDv2Selector(version, state, false);
    }
}
