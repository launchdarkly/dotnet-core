using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Text;
using System.Text.Json;
using LaunchDarkly.Sdk.Internal;
using LaunchDarkly.Sdk.Json;

using static LaunchDarkly.Sdk.Client.DataModel;
using static LaunchDarkly.Sdk.Client.Subsystems.DataStoreTypes;
using static LaunchDarkly.Sdk.Internal.JsonConverterHelpers;

namespace LaunchDarkly.Sdk.Client.Internal
{
    // Methods for converting data to or from a serialized form.
    //
    // The JSON representation of a Context is defined along with Context in LaunchDarkly.CommonSdk.
    //
    // The serialized representation of a single FeatureFlag is simply a JSON object containing
    // its properties, as defined in FeatureFlag.
    //
    // For a whole set of FeatureFlags, the format used to store serialized data is the same as
    // the format used by the LaunchDarkly polling and streaming endpoints. It is a JSON object
    // where each key is a flag key and each value is the FeatureFlag representation. There is
    // no way to represent a deleted item placeholder in this format. The version for each flag
    // is simply the "version" property of the flag's JSON representation.
    //
    // All deserialization methods throw InvalidDataException for malformed data.

    internal static class DataModelSerialization
    {
        private const string ParseErrorMessage = "Data was not in a recognized format";

        internal static string SerializeContext(Context context) =>
            LdJsonSerialization.SerializeObject(context);

        internal static string SerializeFlag(FeatureFlag flag) =>
            LdJsonSerialization.SerializeObject(flag);

        internal static string SerializeAll(FullDataSet allData)
        {
            return JsonUtils.WriteJsonAsString(w =>
            {
                w.WriteStartObject();
                foreach (var item in allData.Items)
                {
                    if (item.Value.Item != null)
                    {
                        w.WritePropertyName(item.Key);
                        JsonSerializer.Serialize(w, item.Value.Item);
                    }
                }
                w.WriteEndObject();
            });
        }

        internal static FeatureFlag DeserializeFlag(string json)
        {
            try
            {
                return LdJsonSerialization.DeserializeObject<FeatureFlag>(json);
            }
            catch (Exception e)
            {
                throw new InvalidDataException(ParseErrorMessage, e);
            }
        }

        internal static FullDataSet DeserializeAll(string serializedData)
        {
            try
            {
                return DeserializeV1Schema(serializedData);
            }
            catch (InvalidDataException)
            {
                throw;
            }
            catch (Exception e)
            {
                throw new InvalidDataException(ParseErrorMessage, e);
            }
            throw new InvalidDataException(ParseErrorMessage);
        }

        // Currently there is only one serialization schema, but it is possible that future
        // SDK versions will require a richer model. In that case we will need to design the
        // serialized format to be distinguishable from previous formats and allow reading
        // of older formats, while only writing the new format.

        internal static FullDataSet DeserializeV1Schema(string serializedData)
        {
            var builder = ImmutableList.CreateBuilder<KeyValuePair<string, ItemDescriptor>>();
            var r = new Utf8JsonReader(Encoding.UTF8.GetBytes(serializedData));
            r.Read();

            try
            {
                for (var obj = RequireObject(ref r); obj.Next(ref r);)
                {
                    var name = obj.Name;
                    var flag = FeatureFlagJsonConverter.ReadJsonValue(ref r);
                    builder.Add(new KeyValuePair<string, ItemDescriptor>(name, flag.ToItemDescriptor()));
                }
            }
            catch (Exception e)
            {
                throw new InvalidDataException(ParseErrorMessage, e);
            }
            return new FullDataSet(builder.ToImmutable());
        }
    }
}
