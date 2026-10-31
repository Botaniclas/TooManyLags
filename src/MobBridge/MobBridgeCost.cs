#if TML_MOB_BRIDGE || TML_DEBUG_SYSTEM
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace TooManyLags
{
    // Ownership-event diagnostics only. Reporting is deferred until setup has
    // returned, so file/network logging does not contaminate phase timings.
    internal sealed class MobBridgeCost
    {
#if TML_BRIDGE_DIAGNOSTICS
        private static readonly Queue<string> reports = new Queue<string>();
        private readonly StringBuilder line;
        private readonly long started;
        private long last;
        private bool finished;
        internal static long Now { get { return Stopwatch.GetTimestamp(); } }
        internal static double Milliseconds(long ticks) { return ticks * (1000d / Stopwatch.Frequency); }
#endif
        internal MobBridgeCost(ZDOID id, string operation)
        {
#if TML_BRIDGE_DIAGNOSTICS
            started = last = Now;
            line = new StringBuilder("bridge cost; mob=").Append(id).Append("; operation=").Append(operation);
#endif
        }
        [Conditional("TML_BRIDGE_DIAGNOSTICS")]
        internal void Mark(string phase)
        {
#if TML_BRIDGE_DIAGNOSTICS
            long now = Now;
            line.Append("; ").Append(phase).Append("Ms=").Append(Milliseconds(now - last).ToString("F3", CultureInfo.InvariantCulture));
            last = now;
#endif
        }
        [Conditional("TML_BRIDGE_DIAGNOSTICS")]
        internal void Count(string name, int value)
        {
#if TML_BRIDGE_DIAGNOSTICS
            line.Append("; ").Append(name).Append('=').Append(value);
#endif
        }
        [Conditional("TML_BRIDGE_DIAGNOSTICS")]
        internal void Finish()
        {
#if TML_BRIDGE_DIAGNOSTICS
            if (finished) return; finished = true;
            line.Append("; totalMs=").Append(Milliseconds(Now - started).ToString("F3", CultureInfo.InvariantCulture));
            Record(line.ToString());
#endif
        }
        [Conditional("TML_BRIDGE_DIAGNOSTICS")]
        internal static void Record(string line)
        {
#if TML_BRIDGE_DIAGNOSTICS
            if (reports.Count < 128) reports.Enqueue(line);
#endif
        }
        [Conditional("TML_BRIDGE_DIAGNOSTICS")]
        internal static void Flush()
        {
#if TML_BRIDGE_DIAGNOSTICS
            while (reports.Count != 0) MobBridgeProtocol.Report(reports.Dequeue());
#endif
        }
    }
}
#endif
