using System.Threading;
using LaunchDarkly.Sdk.Client.Integrations;
using LaunchDarkly.TestHelpers;
using Foundation;
using Xunit;

namespace LaunchDarkly.Sdk.Client.iOS.Tests
{
    public class IOsSpecificTests : BaseTest
    {
        [Fact]
        public void EventHandlerIsCalledOnUIThread()
        {
            var td = TestData.DataSource();
            var config = BasicConfig().DataSource(td).Build();

            var captureMainThread = new EventSink<Thread>();
            NSRunLoop.Main.BeginInvokeOnMainThread(() => captureMainThread.Enqueue(Thread.CurrentThread));
            var mainThread = captureMainThread.ExpectValue();

            using (var client = TestUtil.CreateClient(config, BasicUser))
            {
                var receivedOnThread = new EventSink<Thread>();
                client.FlagTracker.FlagValueChanged += (sender, args) =>
                    receivedOnThread.Enqueue(Thread.CurrentThread);

                td.Update(td.Flag("flagkey").Variation(true));

                var t = receivedOnThread.ExpectValue();
                Assert.Equal(mainThread, t);
            }
        }
    }
}
