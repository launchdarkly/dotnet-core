using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace LaunchDarkly.Sdk.Internal.Helpers
{
    // This struct simply represents a list of T as a list of U, without doing any
    // copying, using a conversion function.
    internal struct LdValueListConverter<T, U> : IReadOnlyList<U>
    {
        private readonly IList<T> _source;
        private readonly Func<T, U> _converter;

        internal LdValueListConverter(IList<T> source, Func<T, U> converter)
        {
            _source = source;
            _converter = converter;
        }

        public U this[int index]
        {
            get
            {
                if (_source is null || index < 0 || index >= _source.Count)
                {
                    throw new IndexOutOfRangeException();
                }
                return _converter(_source[index]);
            }
        }

        public int Count => _source is null ? 0 : _source.Count;

        public IEnumerator<U> GetEnumerator() =>
            _source is null ? Enumerable.Empty<U>().GetEnumerator() :
                _source.Select<T, U>(_converter).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public override string ToString()
        {
            return "[" + string.Join(",", this) + "]";
        }
    }

    // This struct simply represents a dictionary of <string, T> as a dictionary of
    // <string, U>, without doing any copying, using a conversion function.
    internal struct LdValueDictionaryConverter<T, U> : IReadOnlyDictionary<string, U>
    {
        private readonly IDictionary<string, T> _source;
        private readonly Func<T, U> _converter;

        internal LdValueDictionaryConverter(IDictionary<string, T> source, Func<T, U> converter)
        {
            _source = source;
            _converter = converter;
        }

        public U this[string key]
        {
            get
            {
                // Note that JObject[key] does *not* throw a KeyNotFoundException, but we should
                if (_source is null || !_source.TryGetValue(key, out var v))
                {
                    throw new KeyNotFoundException();
                }
                return _converter(v);
            }
        }

        public IEnumerable<string> Keys =>
            _source is null ? Enumerable.Empty<string>() : _source.Keys;

        public IEnumerable<U> Values =>
            _source is null ? Enumerable.Empty<U>() :
                _source.Values.Select(_converter);

        public int Count => _source is null ? 0 : _source.Count;

        public bool ContainsKey(string key) =>
            !(_source is null) && _source.TryGetValue(key, out var ignore);

        public IEnumerator<KeyValuePair<string, U>> GetEnumerator()
        {
            if (_source is null)
            {
                return Enumerable.Empty<KeyValuePair<string, U>>().GetEnumerator();
            }
            var conv = _converter; // lambda can't use instance field
            return _source.Select<KeyValuePair<string, T>, KeyValuePair<string, U>>(
                p => new KeyValuePair<string, U>(p.Key, conv(p.Value))
                ).GetEnumerator();
        }

        public bool TryGetValue(string key, out U value)
        {
            if (!(_source is null) && _source.TryGetValue(key, out var v))
            {
                value = _converter(v);
                return true;
            }
            value = default(U);
            return false;
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public override string ToString()
        {
            return "{" +
                string.Join(",", this.Select(kv => "\"" + kv.Key + "\":" + kv.Value)) +
                "}";
        }
    }
}
