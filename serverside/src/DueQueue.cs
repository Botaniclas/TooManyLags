using System;
using System.Collections.Generic;

namespace TooManyLagsDedicatedAddon
{
    // Indexed heap: cancellation removes storage immediately, with no stale timer entries.
    internal sealed class DueQueue<T> where T : class
    {
        private struct Item { internal T Value; internal float At; }
        private readonly List<Item> heap = new List<Item>();
        private readonly Dictionary<T, int> indexes = new Dictionary<T, int>();
        internal int Count { get { return heap.Count; } }
        internal bool Contains(T value) { return indexes.ContainsKey(value); }
        internal void Add(T value, float at)
        {
            if (indexes.ContainsKey(value)) return;
            indexes.Add(value, heap.Count); heap.Add(new Item { Value = value, At = at }); Up(heap.Count - 1);
        }
        internal bool Remove(T value)
        {
            int index; if (!indexes.TryGetValue(value, out index)) return false;
            indexes.Remove(value); int last = heap.Count - 1;
            if (index == last) { heap.RemoveAt(last); return true; }
            heap[index] = heap[last]; heap.RemoveAt(last); indexes[heap[index].Value] = index;
            if (index > 0 && heap[index].At < heap[(index - 1) / 2].At) Up(index); else Down(index);
            return true;
        }
        internal bool Take(float now, out T value)
        {
            value = null; if (heap.Count == 0 || heap[0].At > now) return false;
            value = heap[0].Value; Remove(value); return true;
        }
        internal void Clear() { heap.Clear(); indexes.Clear(); }
        private void Swap(int a, int b)
        { Item item = heap[a]; heap[a] = heap[b]; heap[b] = item; indexes[heap[a].Value] = a; indexes[heap[b].Value] = b; }
        private void Up(int i)
        { while (i > 0) { int parent = (i - 1) / 2; if (heap[parent].At <= heap[i].At) return; Swap(i, parent); i = parent; } }
        private void Down(int i)
        {
            while (true)
            {
                int child = i * 2 + 1; if (child >= heap.Count) return;
                if (child + 1 < heap.Count && heap[child + 1].At < heap[child].At) child++;
                if (heap[i].At <= heap[child].At) return; Swap(i, child); i = child;
            }
        }
    }
}
