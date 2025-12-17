using System;
using System.Collections.Generic;

namespace LaunchDarkly.Sdk.Server.Internal.DataSources
{
    /// <summary>
    /// Helper class for <see cref="CompositeSource"/> to manage iterating on data sources
    /// and removing them on the fly.
    /// </summary>
    /// <typeparam name="T">the element type</typeparam>
    internal sealed class Factories<T>
    {
        private List<T> _list;
        private readonly bool _circular;
        private int _pos;

        /// <summary>
        /// Creates a new <see cref="Factories{T}"/>.
        /// </summary>
        /// <param name="circular">whether to loop off the end of the list back to the start</param>
        /// <param name="initialList">optional initial contents</param>
        public Factories(bool circular, IEnumerable<T> initialList = null)
        {
            _list = initialList == null ? new List<T>() : new List<T>(initialList);
            _circular = circular;
            _pos = 0;
        }

        /// <summary>
        /// Returns the current head element and then advances the internal position.
        /// If the list is empty or the position is past the end of the list, returns
        /// <c>default(T)</c>.
        /// </summary>
        /// <returns>the current head element, or <c>default(T)</c> if none</returns>
        public T Next()
        {
            if (_list.Count <= 0 || _pos >= _list.Count)
            {
                return default(T);
            }

            var result = _list[_pos];

            if (_circular)
            {
                _pos = (_pos + 1) % _list.Count;
            }
            else
            {
                _pos += 1;
            }

            return result;
        }

        /// <summary>
        /// Replaces all elements with the provided list and resets the position to the start.
        /// </summary>
        /// <param name="input">the new list contents</param>
        public void Replace(IEnumerable<T> input)
        {
            if (input is null) throw new ArgumentNullException(nameof(input));

            _list = new List<T>(input);
            _pos = 0;
        }

        /// <summary>
        /// Removes the provided element from the list. If the removed element was the head,
        /// head moves to the next element. The head may be undefined if the list is empty
        /// after removal.
        /// </summary>
        /// <param name="element">the element to remove</param>
        /// <returns><c>true</c> if the element was removed</returns>
        public bool Remove(T element)
        {
            var index = _list.IndexOf(element);
            if (index < 0)
            {
                return false;
            }

            _list.RemoveAt(index);
            if (_list.Count > 0)
            {
                // if removed item was before head, adjust head
                if (index < _pos)
                {
                    _pos -= 1;
                }

                if (_circular && _pos > _list.Count - 1)
                {
                    _pos = 0;
                }
            }
            else
            {
                _pos = 0;
            }

            return true;
        }

        /// <summary>
        /// Reset the head position to the start of the list.
        /// </summary>
        public void Reset()
        {
            _pos = 0;
        }

        /// <summary>
        /// Gets the current head position in the list, 0-indexed.
        /// </summary>
        public int Pos => _pos;

        /// <summary>
        /// Gets the current length of the list.
        /// </summary>
        public int Length => _list.Count;

        /// <summary>
        /// Clears the list and resets the head position.
        /// </summary>
        public void Clear()
        {
            _list.Clear();
            _pos = 0;
        }
    }
}
