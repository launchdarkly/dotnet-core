using LaunchDarkly.TestHelpers.HttpTest;

using static LaunchDarkly.Sdk.Client.Subsystems.DataStoreTypes;

namespace LaunchDarkly.Sdk.Client
{
    public static class MockResponses
    {
        public static Handler Error401Response => Handlers.Status(401);

        public static Handler Error503Response => Handlers.Status(503);

        public static Handler EventsAcceptedResponse => Handlers.Status(202);

        public static Handler PollingResponse(FullDataSet? data = null) =>
            Handlers.BodyJson((data ?? DataSetBuilder.Empty).ToJsonString());

        public static Handler StreamWithEmptyData => StreamWithInitialData(null);

        public static Handler StreamWithInitialData(FullDataSet? data = null) =>
            Handlers.SSE.Start()
                .Then(PutEvent(data))
                .Then(Handlers.SSE.LeaveOpen());

        public static Handler StreamWithEmptyInitialDataAndThen(params Handler[] handlers)
        {
            var ret = Handlers.SSE.Start().Then(PutEvent());
            foreach (var h in handlers)
            {
                if (h != null)
                {
                    ret = ret.Then(h);
                }
            }
            return ret.Then(Handlers.SSE.LeaveOpen());
        }

        public static Handler StreamThatStaysOpenWithNoEvents =>
            Handlers.SSE.Start().Then(Handlers.SSE.LeaveOpen());

        public static Handler AllowOnlyStreamRequests(Handler streamHandler)
        {
            var ret = Handlers.Router(out var router);
            router.AddRegex("^/meval/.*", streamHandler);
            router.AddRegex(".*", Handlers.Status(500));
            return ret;
        }

        public static Handler PutEvent(FullDataSet? data = null) =>
            Handlers.SSE.Event(
                "put",
                (data ?? DataSetBuilder.Empty).ToJsonString()
                );

        public static Handler PatchEvent(string data) =>
            Handlers.SSE.Event("patch", data);

        public static Handler DeleteEvent(string key, int version) =>
            Handlers.SSE.Event("delete", @"{""key"":""" + key + @""",""version"":" + version + "}");

        public static Handler PingEvent => Handlers.SSE.Event("ping", "");
    }
}
