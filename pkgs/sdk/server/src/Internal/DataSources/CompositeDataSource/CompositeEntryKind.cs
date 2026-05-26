namespace LaunchDarkly.Sdk.Server.Internal.DataSources
{
    /// <summary>
    /// Categorizes an entry in a composite source's list. Appliers use this to express
    /// "block every FDv2 entry" via <see cref="ICompositeSourceActionable.BlockAll"/>
    /// without having to count positions or know which phase they were attached at.
    /// </summary>
    internal enum CompositeEntryKind
    {
        /// <summary>
        /// Default kind. Entries that participate in the FDv2 protocol -- initializers and
        /// FDv2 synchronizers in the outer composite, and any entry that is not the
        /// FDv1 fallback synchronizer.
        /// </summary>
        FDv2,

        /// <summary>
        /// The FDv1 fallback synchronizer entry. Used by the FDv1 fallback applier to
        /// distinguish the fallback target from the FDv2 entries it should block.
        /// </summary>
        FDv1Fallback
    }
}
