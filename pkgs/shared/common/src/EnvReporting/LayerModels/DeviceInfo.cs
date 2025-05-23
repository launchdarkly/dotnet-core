namespace LaunchDarkly.Sdk.EnvReporting.LayerModels 
{
    /// <summary>
    /// An object that encapsulates application metadata.
    /// </summary>
    public readonly struct DeviceInfo
    {
        /// <summary>
        /// The device's model.
        /// </summary>
        public string Model { get; }

        /// <summary>
        /// The device's manufacturer.
        /// </summary>
        public string Manufacturer { get;  }
        
       
        /// <summary>
        /// Constructs a new DeviceInfo instance.
        /// </summary>
        /// <param name="manufacturer">the manufacturer.</param>
        /// <param name="model">the model.</param>
        public DeviceInfo(string manufacturer, string model)
        {
            Model = model;
            Manufacturer = manufacturer;
        }
    }
}
