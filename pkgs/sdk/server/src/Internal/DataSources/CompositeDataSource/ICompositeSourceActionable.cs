using System;
using System.Threading.Tasks;

namespace LaunchDarkly.Sdk.Server.Internal.DataSources
{
    /// <summary>
    /// Defines operations that can be performed by <see cref="IDataSourceObserver"/>s
    /// on a composite data source.
    /// </summary>
    internal interface ICompositeSourceActionable
    {
        /// <summary>
        /// Starts the current data source.
        /// </summary>
        Task<bool> StartCurrent();

        /// <summary>
        /// Disposes of the current data source. You must call GoToNext or GoToFirst after this to change to a new factory.
        /// </summary>
        void DisposeCurrent();

        /// <summary>
        /// Switches to the next source in the list. You must still call StartCurrent after this to actually start the new source.
        /// </summary>
        void GoToNext();

        /// <summary>
        /// Switches to the first source in the list. You must still call StartCurrent after this to actually start the new source.
        /// </summary>
        void GoToFirst();

        /// <summary>
        /// Returns whether the composite source is currently at the first source in the list.
        /// </summary>
        bool IsAtFirst();

        /// <summary>
        /// Blocks the the current source's factory. This prevents the current source's factory from being used again.
        /// Note that this does not tear down the current data source, it just prevents its factory from being used again.
        /// </summary>
        void BlockCurrent();

        /// <summary>
        /// Enqueues an arbitrary action to run on the composite's serialized action queue.
        /// </summary>
        /// <remarks>
        /// The action runs at queue-processing time, after any actions enqueued earlier on the
        /// same queue. Observers can use this to defer advancement decisions until after the
        /// current synchronous propagation chain has completed -- for example, an applier that
        /// wants to check a latch that is set by a sibling applier in the same propagation.
        /// </remarks>
        /// <param name="action">the action to enqueue</param>
        void EnqueueAction(Action action);
    }
}


