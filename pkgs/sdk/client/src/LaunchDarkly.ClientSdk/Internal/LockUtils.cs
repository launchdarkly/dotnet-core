using System;
using System.Threading;

namespace LaunchDarkly.Sdk.Client.Internal
{
    internal static class LockUtils
    {
        public static T WithReadLock<T>(ReaderWriterLockSlim rwLock, Func<T> fn)
        {
            rwLock.EnterReadLock();
            try
            {
                return fn();
            }
            finally
            {
                rwLock.ExitReadLock();
            }
        }

        public static T WithWriteLock<T>(ReaderWriterLockSlim rwLock, Func<T> fn)
        {
            rwLock.EnterWriteLock();
            try
            {
                return fn();
            }
            finally
            {
                rwLock.ExitWriteLock();
            }
        }

        public static void WithWriteLock(ReaderWriterLockSlim rwLock, Action a)
        {
            rwLock.EnterWriteLock();
            try
            {
                a();
            }
            finally
            {
                rwLock.ExitWriteLock();
            }
        }
    }
}
