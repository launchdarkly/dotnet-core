using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using LaunchDarkly.Logging;
using LaunchDarkly.Sdk.Server.Internal.Model;
using LaunchDarkly.Sdk.Server.Subsystems;
using LaunchDarkly.TestHelpers;
using System.Threading.Tasks;

using static LaunchDarkly.Sdk.Server.Subsystems.DataStoreTypes;

namespace LaunchDarkly.Sdk.Server
{
    public class TestUtils
    {
        public static readonly Logger NullLogger = Logs.None.Logger("");

#pragma warning disable 1998
        public static async Task CompletedTask() { } // Task.CompletedTask isn't supported in .NET Framework 4.5.x
#pragma warning restore 1998

        public static string TestFilePath(string name) => "./TestFiles/" + name;

        internal static ItemDescriptor DescriptorOf(FeatureFlag item) => new ItemDescriptor(item.Version, item);

        internal static ItemDescriptor DescriptorOf(Segment item) => new ItemDescriptor(item.Version, item);

        internal static bool UpsertFlag(IDataStore store, FeatureFlag item) =>
            store.Upsert(DataModel.Features, item.Key, DescriptorOf(item));

        internal static bool UpsertSegment(IDataStore store, Segment item) =>
            store.Upsert(DataModel.Segments, item.Key, DescriptorOf(item));

        internal static string MakeFlagsData(params FeatureFlag[] flags)
        {
            var flagsBuilder = LdValue.BuildObject();
            foreach (var flag in flags)
            {
                flagsBuilder.Add(flag.Key, LdValue.Parse(DataModel.Features.Serialize(DescriptorOf(flag))));
            }
            return LdValue.BuildObject()
                    .Add("flags", flagsBuilder.Build())
                    .Add("segments", LdValue.BuildObject().Build())
                    .Build().ToJsonString();
        }

        // Ensures that a data set is sorted by namespace and then by key
        internal static FullDataSet<ItemDescriptor> NormalizeDataSet(FullDataSet<ItemDescriptor> data) =>
            new FullDataSet<ItemDescriptor>(
                data.Data.OrderBy(kindAndItems => kindAndItems.Key.Name)
                    .Select(kindAndItems => new KeyValuePair<DataKind, KeyedItems<ItemDescriptor>>(
                        kindAndItems.Key,
                        new KeyedItems<ItemDescriptor>(
                            kindAndItems.Value.Items.OrderBy(keyAndItem => keyAndItem.Key)
                            )
                        )
                    )
                );

        internal static JsonTestValue DataSetAsJson(FullDataSet<ItemDescriptor> data)
        {
            var ob0 = LdValue.BuildObject();
            foreach (var kv0 in data.Data)
            {
                var ob1 = LdValue.BuildObject();
                foreach (var kv1 in kv0.Value.Items)
                {
                    ob1.Add(kv1.Key, LdValue.Parse(kv0.Key.Serialize(kv1.Value)));
                }
                ob0.Add(kv0.Key.Name, ob1.Build());
            }
            return JsonTestValue.JsonOf(ob0.Build().ToJsonString());
        }
    }

    public class TempDirectory : IDisposable
    {
        public string Path { get; }

        public static TempDirectory Create() => new TempDirectory();

        private TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ld-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string PathOf(string name) => System.IO.Path.Combine(Path, name);

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, true);
            }
            catch { }
        }
    }

#if NET6_0_OR_GREATER
    /// <summary>
    /// Creates symbolic links for tests. Creating a link needs a privilege on Windows that the test
    /// process may not have, so the Try methods report failure instead of throwing, and a test that
    /// needs links returns early when they cannot be created.
    /// </summary>
    public static class SymbolicLinks
    {
        public static bool TryCreateFileLink(string link, string target)
        {
            try
            {
                File.CreateSymbolicLink(link, target);
                return true;
            }
            catch (Exception e) when (e is UnauthorizedAccessException || e is IOException || e is PlatformNotSupportedException)
            {
                return false;
            }
        }

        public static bool TryCreateDirectoryLink(string link, string target)
        {
            try
            {
                Directory.CreateSymbolicLink(link, target);
                return true;
            }
            catch (Exception e) when (e is UnauthorizedAccessException || e is IOException || e is PlatformNotSupportedException)
            {
                return false;
            }
        }

        /// <summary>
        /// True when <see cref="Replace"/> is available. The atomic replacement of one directory link
        /// by another is a Unix operation.
        /// </summary>
        public static bool CanReplace => !OperatingSystem.IsWindows();

        /// <summary>
        /// Replaces the link at <paramref name="to"/> with the link at <paramref name="from"/> in one
        /// step, as a ConfigMap update replaces its data directory link.
        /// </summary>
        public static void Replace(string from, string to)
        {
            if (rename(from, to) != 0)
            {
                throw new IOException("rename failed with error " + Marshal.GetLastWin32Error());
            }
        }

        [DllImport("libc", SetLastError = true)]
        private static extern int rename(string oldPath, string newPath);
    }
#endif

    public class TempFile : IDisposable
    {
        public string Path { get; }

        public static TempFile Create() => new TempFile();

        public static string MakePathOfNonexistentFile()
        {
            var path = System.IO.Path.GetTempFileName();
            File.Delete(path);
            return path;
        }

        private TempFile()
        {
            this.Path = System.IO.Path.GetTempFileName();
        }

        public void SetContent(string text) =>
            File.WriteAllText(Path, text);

        public void SetContentFromPath(string path) =>
            SetContent(File.ReadAllText(path));

        public void Delete() => File.Delete(Path);

        public void Dispose()
        {
            try
            {
                Delete();
            }
            catch { }
        }
    }
}
