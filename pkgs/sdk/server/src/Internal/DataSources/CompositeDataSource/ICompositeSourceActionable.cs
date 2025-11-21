namespace LaunchDarkly.Sdk.Server.Internal.DataSources
{
    /// <summary>
    /// Defines operations that can be performed by <see cref="CompositeSourceAction"/>s
    /// on a composite data source.
    /// </summary>
    internal interface ICompositeSourceActionable
    {
        void StartCurrent();

        void DisposeCurrent();

        void GoToNext();

        void GoToFirst();

        void BlacklistCurrent();
    }
}


