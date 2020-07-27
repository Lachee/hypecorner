using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace HypeCorner.Utility
{
    /// <summary>
    /// Cyclic List will never end and cycle back on itself.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    class CyclicList<T> : IEnumerable<T>
    {
        private T[] _list;
        private int _index;

        /// <summary>
        /// Size of the cycle
        /// </summary>
        public int Size { get; }

        /// <summary>
        /// Number of items currently in the list. No greater than <see cref="Size"/>
        /// </summary>
        public int Count { get; private set; }

        /// <summary>
        /// Item at a specific offset
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        public T this[int index]
        {
            get => Get(index);
            set { Set(index, value); }
        }

        /// <summary>
        /// Creates a new list
        /// </summary>
        /// <param name="size"></param>
        public CyclicList(int size)
        {
            Size = size;
            Clear();
        }

        /// <summary>
        /// Gets a value at the offset, where 0 is the last item in the list, and Size-1 is the current.
        /// </summary>
        /// <param name="offset"></param>
        /// <returns></returns>
        public T Get(int offset)
        {
            int index = ((_index - Count) + offset) % _list.Length;
            if (index < 0) index = _list.Length + index;
            if (index < 0 || index >= _list.Length)
                throw new ArgumentOutOfRangeException("index");

            return _list[index];
        }


        /// <summary>
        /// Sets a value at the offset, where 0 is the last item in the list, and Size-1 is the current.
        /// </summary>
        /// <param name="offset"></param>
        /// <returns></returns>
        public void Set(int offset, T value)
        {
            int index = ((_index - Count) + offset) % _list.Length;
            if (index < 0) index = _list.Length + index;
            if (index < 0 || index >= _list.Length) 
                throw new ArgumentOutOfRangeException("index");

            _list[index] = value;
        }

        /// <summary>
        /// Adds a value to the list, incrementing the cycle.
        /// </summary>
        /// <param name="value"></param>
        public void Add(T value)
        {
            _list[_index] = value;
            _index = (_index + 1) % _list.Length;
            if (Count < Size) Count++;
        }

        /// <summary>
        /// Clears the list and resets its count to 0
        /// </summary>
        public void Clear()
        {
            _list = new T[Size];
            _index = 0;
            Count = 0;
        }

        public IEnumerator<T> GetEnumerator()
        {
            for (int i = 0; i < Count; i++)
                yield return Get(i);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            for (int i = 0; i < Count; i++)
                yield return Get(i);
        }
    }
}
