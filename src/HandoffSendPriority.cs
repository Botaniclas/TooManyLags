using System;
using System.Collections.Generic;

namespace TooManyLags
{
    // Reorder only recognised slots in the already-forced native prefix.
    // Unrelated forced updates and the normal send list keep their positions.
    internal sealed class HandoffSendPriority<T>
    {
        private readonly List<int> positions = new List<int>();
        private readonly List<T>[] lanes = { new List<T>(), new List<T>(), new List<T>(), new List<T>() };
        private bool busy;
        internal void Apply(List<T> items, int inserted, Func<T, int> priority)
        {
            if (busy || inserted < 2 || inserted > items.Count) return;
            busy = true;
            try
            {
                int previous = -1; bool ordered = true;
                for (int i = 0; i < inserted; i++)
                {
                    int tier = priority(items[i]);
                    if (tier < 0 || tier > 3) continue;
                    if (tier < previous) ordered = false;
                    previous = tier; positions.Add(i); lanes[tier].Add(items[i]);
                }
                if (ordered) return;
                int index = 0;
                foreach (List<T> lane in lanes) foreach (T item in lane) items[positions[index++]] = item;
            }
            finally
            {
                positions.Clear(); foreach (List<T> lane in lanes) lane.Clear(); busy = false;
            }
        }
    }
}
