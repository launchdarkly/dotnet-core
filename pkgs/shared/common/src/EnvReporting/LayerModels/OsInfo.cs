namespace LaunchDarkly.Sdk.EnvReporting.LayerModels 
{
    /// <summary>
    /// An object that encapsulates application metadata.
    /// </summary>
    public readonly struct OsInfo
    {
        /// <summary>
        /// The operating system's family.
        /// </summary>
        public string Family { get; }
        
        /// <summary>
        /// The operating system's name.
        /// </summary>
        public string Name { get; }
        
        /// <summary>
        /// The operating system's version.
        /// </summary>
        public string Version { get; }
        
        
        /// <summary>
        /// Constructs a new OsInfo instance.
        /// </summary>
        /// <param name="family">the family.</param>
        /// <param name="name">the name.</param>
        /// <param name="version">the version.</param>
        public OsInfo(string family, string name, string version)
        {
            Family = family;
            Name = name;
            Version = version;
        }
    }
}
