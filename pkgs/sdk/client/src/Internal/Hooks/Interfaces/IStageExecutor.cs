using System.Collections.Generic;
using System.Collections.Immutable;

namespace LaunchDarkly.Sdk.Client.Internal.Hooks.Interfaces
{
    using SeriesData = ImmutableDictionary<string, object>;

    /// <summary>
    /// Allows the Executor to arbitrarily wrap stage execution logic. For example, a benchmarking utility
    /// can take an arbitrary IStageExecutor and wrap the execution logic in a timer.
    /// </summary>
    /// <typeparam name="TContext">the context type</typeparam>
    internal interface IStageExecutor<in TContext>
    {
        /// <summary>
        /// Implementation should execute the same stage for all hooks with the given context and series data.
        /// </summary>
        /// <param name="context">the context</param>
        /// <param name="data">the pre-existing series data; if null, the implementation should create empty data as necessary</param>
        /// <returns>updated series data</returns>
        IEnumerable<SeriesData> Execute(TContext context, IEnumerable<SeriesData> data);
    }

    internal interface IStageExecutor<in TContext, in TExtra>
    {
        /// <summary>
        /// Implementation should execute the same stage for all hooks with the given context, series data, and extra data.
        /// </summary>
        /// <param name="context">the context</param>
        /// <param name="extra">the extra data</param>
        /// <param name="data">the pre-existing series data</param>
        /// <returns>updated series data</returns>
        IEnumerable<SeriesData> Execute(TContext context, TExtra extra, IEnumerable<SeriesData> data);
    }
}
