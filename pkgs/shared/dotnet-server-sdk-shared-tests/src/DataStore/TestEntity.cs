using static LaunchDarkly.Sdk.Server.Subsystems.DataStoreTypes;

namespace LaunchDarkly.Sdk.Server.SharedTests.DataStore
{
    /// <summary>
    /// A simple class that stands in for the SDK's data model types such as FeatureFlag.
    /// </summary>
    /// <remarks>
    /// Data stores are tested against this type, instead of the real data model types, to make sure
    /// they are using the generic mechanisms provided by the SDK and not anything specific to the
    /// currently defined data model types, since the SDK may add to or change those in the future.
    /// </remarks>
    public class TestEntity
    {
        public static readonly DataKind Kind = new DataKind(
            "test",
            Serialize,
            Deserialize
            );
        public static readonly DataKind OtherKind = new DataKind(
            "other",
            Serialize,
            Deserialize
            );

        public string Key { get; }
        public int Version { get; }
        public string Value { get; }

        public bool Deleted => Value is null;

        public TestEntity() { }

        public TestEntity(string key, int version, string value)
        {
            Key = key;
            Version = version;
            Value = value;
        }

        public TestEntity WithVersion(int newVersion) =>
            new TestEntity(Key, newVersion, Value);

        public TestEntity WithValue(string newValue) =>
            new TestEntity(Key, Version, newValue);

        public TestEntity NextVersion() =>
            WithVersion(Version + 1);

        internal SerializedItemDescriptor SerializedItemDescriptor =>
            Deleted ?
                new SerializedItemDescriptor(Version, true, Serialize(ItemDescriptor.Deleted(Version))) :
                new SerializedItemDescriptor(Version, false, Serialize(new ItemDescriptor(Version, this)));

        public override bool Equals(object obj) =>
            (obj is TestEntity o) &&
                Key == o.Key && Version == o.Version && Value == o.Value;

        public override int GetHashCode() =>
            new { Key, Version, Value }.GetHashCode();

        public static string Serialize(ItemDescriptor item)
        {
            if (item.Item is null)
            {
                return "$DELETED:" + item.Version;
            }
            var e = item.Item as TestEntity;
            return e.Key + ":" + e.Version + ":" + e.Value;
        }

        public static ItemDescriptor Deserialize(string serialized)
        {
            var parts = serialized.Split(':');
            var key = parts[0];
            var version = int.Parse(parts[1]);
            if (key == "$DELETED")
            {
                return ItemDescriptor.Deleted(version);
            }
            return new ItemDescriptor(version, new TestEntity(key, version, parts[2]));
        }
    }
}
