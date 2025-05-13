using System;
using System.Threading;
using System.Threading.Tasks;

namespace LaunchDarkly.Sdk.Server.SharedTests
{
    internal static class AsyncUtils
    {
        private static readonly TaskFactory _taskFactory = new TaskFactory(CancellationToken.None,
            TaskCreationOptions.None, TaskContinuationOptions.None, TaskScheduler.Default);

        // This procedure for blocking on a Task without using Task.Wait is derived from the MIT-licensed ASP.NET
        // code here: https://github.com/aspnet/AspNetIdentity/blob/master/src/Microsoft.AspNet.Identity.Core/AsyncHelper.cs
        // In general, mixing sync and async code is not recommended, and if done in other ways can result in
        // deadlocks. See: https://stackoverflow.com/questions/9343594/how-to-call-asynchronous-method-from-synchronous-method-in-c
        // Task.Wait would only be safe if we could guarantee that every intermediate Task within the async
        // code had been modified with ConfigureAwait(false), but that is very error-prone and we can't depend
        // on feature store implementors doing so.

        internal static void WaitSafely(Func<Task> taskFn)
        {
            _taskFactory.StartNew(taskFn)
                .Unwrap()
                .GetAwaiter()
                .GetResult();
            // Note, GetResult does not throw AggregateException so we don't need to post-process exceptions
        }

        internal static bool WaitSafely(Func<Task> taskFn, TimeSpan timeout)
        {
            try
            {
                return _taskFactory.StartNew(taskFn)
                    .Unwrap()
                    .Wait(timeout);
            }
            catch (AggregateException e)
            {
                throw UnwrapAggregateException(e);
            }
        }

        internal static T WaitSafely<T>(Func<Task<T>> taskFn)
        {
            return _taskFactory.StartNew(taskFn)
                .Unwrap()
                .GetAwaiter()
                .GetResult();
        }

        private static Exception UnwrapAggregateException(AggregateException e)
        {
            if (e.InnerExceptions.Count == 1)
            {
                return e.InnerExceptions[0];
            }
            return e;
        }
    }
}
