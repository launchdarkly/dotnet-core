using System.Threading.Tasks;

namespace LaunchDarkly.Sdk.Server.Internal.DataSources
{
    /// <summary>
    /// Defines operations that can be performed by <see cref="IDataSourceObserver"/>s
    /// on a composite data source.
    /// </summary>
    internal interface ICompositeSourceActionable
    {
        Task<bool> StartCurrent();

        void DisposeCurrent();

        void GoToNext();

        void GoToFirst();

        bool IsAtFirst();

        void BlacklistCurrent();
    }
}


