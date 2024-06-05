using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Linq;
using LaunchDarkly.Sdk.Internal;

using static LaunchDarkly.Sdk.Internal.JsonConverterHelpers;

namespace LaunchDarkly.Sdk.Client.Internal.DataStores
{
    /// <summary>
    /// Used internally to track which contexts have flag data in the persistent store.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This exists because we can't assume that the persistent store mechanism has an "enumerate
    /// all the keys that exist under such-and-such prefix" capability, so we need a table of
    /// contents at a fixed location. The only information being tracked here is, for each flag
    /// data set that exists in storage, 1. a context identifier (hashed fully-qualified key, as
    /// defined by FlagDataManager.ContextIdFor) and 2. the millisecond timestamp when it was
    /// last accessed, to support the LRU eviction behavior of FlagDataManager.
    /// </para>
    /// <para>
    /// To minimize overhead, this is stored as JSON in a very simple format: a JSON array where
    /// each element is a nested JSON array in the form ["contextId", millisecondTimestamp].
    /// </para>
    /// </remarks>
    internal class ContextIndex
    {
        internal ImmutableList<IndexEntry> Data { get; }

        internal struct IndexEntry
        {
            public string ContextId { get; set; }
            public UnixMillisecondTime Timestamp { get; set; }
        }

        internal ContextIndex(ImmutableList<IndexEntry> data = null)
        {
            Data = data ?? ImmutableList<IndexEntry>.Empty;
        }

        public ContextIndex UpdateTimestamp(string contextId, UnixMillisecondTime timestamp)
        {
            var builder = ImmutableList.CreateBuilder<IndexEntry>();
            builder.AddRange(Data.Where(e => e.ContextId != contextId));
            builder.Add(new IndexEntry { ContextId = contextId, Timestamp = timestamp });
            return new ContextIndex(builder.ToImmutable());
        }

        public ContextIndex Prune(int maxContextsToRetain, out IEnumerable<string> removedUserIds)
        {
            if (Data.Count <= maxContextsToRetain)
            {
                removedUserIds = ImmutableList<string>.Empty;
                return this;
            }
            // The data will normally already be in ascending timestamp order, in which case this Sort
            // won't do anything, but this is just in case unsorted data somehow got persisted.
            var sorted = Data.Sort((e1, e2) => e1.Timestamp.CompareTo(e2.Timestamp));
            var numDrop = Data.Count - maxContextsToRetain;
            removedUserIds = ImmutableList.CreateRange(sorted.Take(numDrop).Select(e => e.ContextId));
            return new ContextIndex(ImmutableList.CreateRange(sorted.Skip(numDrop)));
        }

        /// <summary>
        /// Returns a JSON representation of the context index.
        /// </summary>
        /// <returns>the JSON representation</returns>
        public string Serialize()
        {
            return JsonUtils.WriteJsonAsString(w =>
            {
                w.WriteStartArray();
                {
                    foreach (var e in Data)
                    {
                        w.WriteStartArray();
                        w.WriteStringValue(e.ContextId);
                        w.WriteNumberValue(e.Timestamp.Value);
                        w.WriteEndArray();
                    }
                }
                w.WriteEndArray();
            });
        }

        /// <summary>
        /// Parses the context index from a JSON representation. If the JSON string is null or
        /// empty, it returns an empty index.
        /// </summary>
        /// <param name="json">the JSON representation</param>
        /// <returns>the parsed data</returns>
        /// <exception cref="FormatException">if the JSON is malformed</exception>
        public static ContextIndex Deserialize(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return new ContextIndex();
            }
            var builder = ImmutableList.CreateBuilder<IndexEntry>();
            try
            {
                var r = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
                for (var a0 = RequireArray(ref r); a0.Next(ref r);)
                {
                    var a1 = RequireArray(ref r);
                    if (a1.Next(ref r))
                    {
                        var contextId = r.GetString();
                        if (a1.Next(ref r))
                        {
                            var timeMillis = r.GetInt64();
                            builder.Add(new IndexEntry { ContextId = contextId, Timestamp = UnixMillisecondTime.OfMillis(timeMillis) });
                            while (a1.Next(ref r)) { } // discard any extra elements
                        }
                    }
                }
            }
            catch (Exception e)
            {
                throw new FormatException("invalid stored context index", e);
            }
            return new ContextIndex(builder.ToImmutable());
        }
    }
}
