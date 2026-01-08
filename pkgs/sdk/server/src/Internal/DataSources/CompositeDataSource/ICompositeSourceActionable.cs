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
        /// Blacklists the current source. This prevents the current source from being used again. 
        /// Note that blacklisting does not tear down the current data source, it just prevents it from being used again.
        /// </summary>
        void BlacklistCurrent();
    }
}


