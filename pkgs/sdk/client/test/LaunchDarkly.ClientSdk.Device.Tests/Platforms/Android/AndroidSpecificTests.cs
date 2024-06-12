using System.Threading;
using LaunchDarkly.Sdk.Client.Integrations;
using LaunchDarkly.TestHelpers;
using Android.OS;
using Xunit;

namespace LaunchDarkly.Sdk.Client.Android.Tests
{
    public class AndroidSpecificTests : BaseTest
    {
        [Fact]
        public void EventHandlerIsCalledOnUIThread()
        {
            var td = TestData.DataSource();
            var config = BasicConfig().DataSource(td).Build();

            var captureMainThread = new EventSink<Thread>();
            new Handler(Looper.MainLooper).Post(() => captureMainThread.Enqueue(Thread.CurrentThread));
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
