using System;

namespace LaunchDarkly.Sdk.EnvReporting
{
    /// <summary>
    /// Builder class for making the configuration based <see cref="Layer"/> for use in the
    /// <see cref="EnvironmentReporterBuilder"/>.
    /// </summary>
    public class ConfigLayerBuilder
    {

        private ApplicationInfo _info;

        /// <param name="info">the application info that will be used by this layer when built.</param>
        public ConfigLayerBuilder SetAppInfo(ApplicationInfo info)
        {
            _info = info;
            return this;
        }

        /// <summary>
        /// Builds the <see cref="Layer"/>
        /// </summary>
        /// <returns>the layer</returns>
        public Layer Build()
        {
            return Validate(_info) ? new Layer(_info, null, null, null) : new Layer();
        }
        
        private static bool Validate(ApplicationInfo info)
        {
            return info.ApplicationId != null;
        }
    }
}
