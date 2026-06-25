using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

using static LaunchDarkly.Sdk.Json.LdJsonConverters;

namespace LaunchDarkly.Sdk.Internal.Events
{
    internal sealed class EventContextFormatter
    {
        private readonly bool _allAttributesPrivate;
        private readonly IEnumerable<AttributeRef> _globalPrivateAttributes;

        public EventContextFormatter(EventsConfiguration config)
        {
            _allAttributesPrivate = config.AllAttributesPrivate;
            _globalPrivateAttributes = config.PrivateAttributes ?? Enumerable.Empty<AttributeRef>();
        }

        public void Write(in Context c, Utf8JsonWriter w, bool redactAnonymous = false)
        {
            if (c.Multiple)
            {
                w.WriteStartObject();
                w.WriteString("kind", "multi");
                foreach (var mc in c.MultiKindContexts)
                {
                    w.WritePropertyName(mc.Kind.Value);
                    WriteSingle(mc, w, false, redactAnonymous);
                }
                w.WriteEndObject();
            }
            else
            {
                WriteSingle(c, w, true, redactAnonymous);
            }
        }

        private void WriteSingle(in Context c, Utf8JsonWriter w, bool includeKind, bool redactAnonymous)
        {
            w.WriteStartObject();

            if (includeKind)
            {
                w.WriteString("kind", c.Kind.Value);
            }
            w.WriteString("key", c.Key);
            JsonConverterHelpers.WriteBooleanIfTrue(w, "anonymous", c.Anonymous);

            var redactAll = _allAttributesPrivate || (redactAnonymous && c.Anonymous);

            List<string> redactedList = null;
            var privateRefs = _globalPrivateAttributes.Concat(c.PrivateAttributes);
            foreach (var attr in c.OptionalAttributeNames)
            {
                if (redactAll)
                {
                    AddRedacted(ref redactedList, attr); // the entire attribute is redacted
                    continue;
                }
                WriteOrRedact(attr, c, w, privateRefs, ref redactedList);
            }

            if (!(redactedList is null))
            {
                w.WriteStartObject("_meta");
                w.WriteStartArray("redactedAttributes");
                foreach (var attr in redactedList)
                {
                    w.WriteStringValue(attr);
                }
                w.WriteEndArray();
                w.WriteEndObject();
            }

            w.WriteEndObject();
        }

        // This method implements the context-aware attribute redaction logic, in which an attribute
        // can be either written as-is, fully redacted, or (for a JSON object) partially redacted.
        // In the latter two cases, this method returns the redacted attribute reference string;
        // otherwise it returns null.
        private void WriteOrRedact(
            string attrName,
            in Context c,
            Utf8JsonWriter obj,
            IEnumerable<AttributeRef> privateRefs,
            ref List<string> redactedList
            )
        {
            // First check if the whole attribute is redacted by name.
            foreach (var a in privateRefs)
            {
                if (a.Depth == 1 && a.GetComponent(0) == attrName)
                {
                    AddRedacted(ref redactedList, attrName); // the entire attribute is redacted
                    return;
                }
            }

            var value = c.GetValue(attrName);
            if (value.Type != LdValueType.Object)
            {
                obj.WritePropertyName(attrName);
                LdValueConverter.WriteJsonValue(value, obj);
                return;
            }

            // The value is a JSON object, and the attribute may need to be partially redacted.
            WriteRedactedValue(value, obj, privateRefs, 0, attrName, ref redactedList);
        }

        private void WriteRedactedValue(
            in LdValue value,
            Utf8JsonWriter obj,
            IEnumerable<AttributeRef> allPrivate,
            int depth,
            string pathComponent,
            ref List<string> redactedList)
        {
            IEnumerable<AttributeRef> filteredPrivate = allPrivate.Where(a =>
                a.GetComponent(depth) == pathComponent);

            var haveSubpaths = false;
            foreach (var a in filteredPrivate)
            {
                if (a.Depth <= depth + 1)
                {
                    // exact match for this subpath or a parent - the whole value is redacted
                    AddRedacted(ref redactedList, a.ToString());
                    return;
                }
                haveSubpaths = true;
            }

            if (!haveSubpaths || value.Type != LdValueType.Object)
            {
                obj.WritePropertyName(pathComponent);
                LdValueConverter.WriteJsonValue(value, obj);
                return;
            }

            obj.WriteStartObject(pathComponent);
            foreach (var kv in value.Dictionary)
            {
                WriteRedactedValue(kv.Value, obj, filteredPrivate, depth + 1, kv.Key, ref redactedList);
            }
            obj.WriteEndObject();
        }

        private void AddRedacted(ref List<string> redactedList, string attrName)
        {
            if (redactedList is null)
            {
                redactedList = new List<string>();
            }
            redactedList.Add(attrName);
        }
    }
}
