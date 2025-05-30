using System;
using System.Collections.Generic;
using System.Linq;
using LaunchDarkly.Sdk.EnvReporting.LayerModels;

namespace LaunchDarkly.Sdk.EnvReporting
{
    

    /// <summary>
    /// Represents one layer of sourcing environment properties.  A layer may know how to source any
    /// number of properties (even possibly 0 properties in edge cases), in which case it may return null for individual
    /// properties.
    /// </summary>
    public readonly struct Layer
    {
        /// <summary>
        /// The application info.
        /// </summary>
        public ApplicationInfo? ApplicationInfo { get; }

        /// <summary>
        /// The operating system info.
        /// </summary>
        public OsInfo? OsInfo { get;  }

        /// <summary>
        /// The device info.
        /// </summary>
        public DeviceInfo? DeviceInfo { get; }

        /// <summary>
        /// The application locale in the format languagecode2-country/regioncode2.
        /// </summary>
        public string Locale { get; }

        /// <summary>
        /// Constructs a new layer with optional property values.
        /// </summary>
        /// <param name="appInfo">the optional ApplicationInfo.</param>
        /// <param name="osInfo">the optional OsInfo.</param>
        /// <param name="deviceInfo">the optional DeviceInfo.</param>
        /// <param name="locale">the optional application locale.</param>
        public Layer(ApplicationInfo? appInfo, OsInfo? osInfo, DeviceInfo? deviceInfo, string locale)
        {
            ApplicationInfo = appInfo;
            OsInfo = osInfo;
            DeviceInfo = deviceInfo;
            Locale = locale;
        }
    }

    internal class PrioritizedReporter : IEnvironmentReporter
    {
        public OsInfo? OsInfo { get; internal set; }
        public DeviceInfo? DeviceInfo { get; internal set; }
        public ApplicationInfo? ApplicationInfo { get; internal set; }
        public string Locale { get; internal set; }
    }


    /// <summary>
    /// EnvironmentReporterBuilder constructs an IEnvironmentReporter that is capable
    /// of returning properties associated with the runtime environment of the SDK.
    /// </summary>
    public sealed class EnvironmentReporterBuilder
    {
        private Layer _configLayer;
        private Layer _platformLayer;
        private Layer _sdkLayer;

        private const string Unknown = "unknown";

        /// <summary>
        /// Sets the properties that come from the user-provided SDK configuration.
        /// Properties in this layer will always override properties from the platform layer.
        /// </summary>
        /// <param name="config">the Layer.</param>
        /// <returns>the EnvironmentReporterBuilder.</returns>
        public EnvironmentReporterBuilder SetConfigLayer(Layer config)
        {
            _configLayer = config;
            return this;
        }

        /// <summary>
        /// Sets the properties that come from the platform-specific runtime information.
        /// Properties in this layer will always override properties from the default layer (provisioned
        /// by this build.)
        /// </summary>
        /// <param name="platform">the Layer.</param>
        /// <returns>the EnvironmentReporterBuilder.</returns>
        public EnvironmentReporterBuilder SetPlatformLayer(Layer platform)
        {
            _platformLayer = platform;
            return this;
        }

        /// <summary>
        /// Sets the properties that come from the SDK that is using this <see cref="EnvironmentReporterBuilder"/>.
        /// Properties in this layer will always override properties from the default layer.
        /// </summary>
        /// <param name="sdkLayer">the Layer.</param>
        public EnvironmentReporterBuilder SetSdkLayer(Layer sdkLayer)
        {
            _sdkLayer = sdkLayer;
            return this;
        }

        /// <summary>
        /// Builds an IEnvironmentReporter, which can be used to obtain information about
        /// the runtime environment of the SDK.
        /// </summary>
        /// <returns></returns>
        public IEnvironmentReporter Build()
        {
            var layers = new List<Layer> { _configLayer, _platformLayer, _sdkLayer };

            return new PrioritizedReporter()
            {
                ApplicationInfo =
                    layers.Select(layer => layer.ApplicationInfo)
                        .FirstOrDefault(prop => prop != null),
                OsInfo =
                    layers.Select(layer => layer.OsInfo)
                        .FirstOrDefault(prop => prop != null),
                DeviceInfo = 
                    layers.Select(layer => layer.DeviceInfo)
                        .FirstOrDefault(prop => prop != null),
                Locale =
                    layers.Select(layer => layer.Locale)
                        .FirstOrDefault(prop => prop != null)
            };
        }
    }
}
