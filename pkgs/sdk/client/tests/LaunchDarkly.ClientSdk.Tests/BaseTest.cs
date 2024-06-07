using System;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Client.Integrations;
using LaunchDarkly.Sdk.Client.Subsystems;
using LaunchDarkly.Sdk.Internal;
using Xunit;
using Xunit.Abstractions;

namespace LaunchDarkly.Sdk.Client
{
    [Collection("serialize all tests")]
    public class BaseTest : IDisposable
    {
        protected const string BasicMobileKey = "mobile-key";
        protected static readonly Context BasicUser = Context.New("user-key");

        protected readonly LoggingConfigurationBuilder testLogging;
        protected readonly Logger testLogger;
        protected readonly LogCapture logCapture = Logs.Capture();
        protected readonly TaskExecutor BasicTaskExecutor;

        public BaseTest() : this(capture => capture) { }

        public BaseTest(ITestOutputHelper testOutput) :
            this(capture => Logs.ToMultiple(TestLogging.TestOutputAdapter(testOutput), capture))
        { }

        protected BaseTest(Func<ILogAdapter, ILogAdapter> adapterFn)
        {
            var adapter = adapterFn(logCapture);
            testLogger = adapter.Level(LogLevel.Debug).Logger("");
            testLogging = Components.Logging(adapter).Level(LogLevel.Debug);
            BasicTaskExecutor = new TaskExecutor("test-sender", testLogger);
        }

        public void Dispose()
        {
            TestUtil.ClearClient();
        }

        // Returns a ConfigurationBuilder with no external data source, events disabled, and logging redirected
        // to the test output. Using this as a base configuration for tests, and then overriding properties as
        // needed, protects against accidental interaction with external services and also makes it easier to
        // see which properties are important in a test.
        protected ConfigurationBuilder BasicConfig() =>
            Configuration.Builder(BasicMobileKey, ConfigurationBuilder.AutoEnvAttributes.Disabled)
                .BackgroundModeManager(new MockBackgroundModeManager())
                .ConnectivityStateManager(new MockConnectivityStateManager(true))
                .DataSource(new MockDataSource().AsSingletonFactory<IDataSource>())
                .Events(Components.NoEvents)
                .Logging(testLogging)
                .Persistence(
                    Components.Persistence().Storage(new MockPersistentDataStore().AsSingletonFactory<IPersistentDataStore>())
                );
    }
}
