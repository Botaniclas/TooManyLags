using System;
using System.Collections.Generic;
using TooManyLags.GridHandoffs;

internal static class ReadinessProbeTests
{
    private static int checks;
    private static void Check(string name, bool value) { checks++; if (!value) throw new Exception(name); }
    private struct Reply { internal long Token; internal float At; }
    public static void Main()
    {
        try
        {
            // Actual production probe, with responses crossing several retries.
            foreach (float delay in new[] { .05f, .6f, 1.2f, 3f, 10f })
            {
                ReadinessProbe probe = new ReadinessProbe(); long sequence = 0;
                var replies = new Queue<Reply>(); int sent = 0, processed = 0;
                for (int step = 0; step <= 220 && !probe.Ready; step++)
                {
                    float now = step * .1f;
                    if (probe.Query(now, ref sequence)) { sent++; replies.Enqueue(new Reply { Token = probe.Token, At = now + delay }); }
                    while (replies.Count != 0 && replies.Peek().At <= now)
                    { var reply = replies.Dequeue(); if (probe.Response(reply.Token, true, now)) processed++; }
                }
                Check("delayed ready reply is accepted at " + delay + "s", probe.Ready && processed > 0);
                Check("pending retries reuse one request identity", sequence == 1 && sent > 0);
                Check("ready recipient needs no idle probing", !probe.Query(100, ref sequence));
            }
            ReadinessProbe item = new ReadinessProbe(); long next = 0;
            Check("first readiness request starts immediately", item.Query(0, ref next)); long first = item.Token;
            Check("false readiness response stays blocked", item.Response(first, false, .2f) && !item.Ready);
            Check("unavailable recipient retains original retry delay", !item.Query(.6f, ref next));
            Check("later retry gets fresh identity after a completed response", item.Query(.8f, ref next) && item.Token != first);
            Check("late previous reply cannot unlock new query", !item.Response(first, true, 1f) && !item.Ready);
            long beforeFloor = item.Token; item.Invalidate(); item.Query(1f, ref next);
            Check("changed minimum state rejects old available reply", !item.Response(beforeFloor, true, 1.1f) && !item.Ready);
            long current = item.Token;
            Check("unchanged floor accepts current reply", item.Response(current, true, 1.2f) && item.Ready);
            Check("foreign unload token does not retire current readiness", !item.Lost(beforeFloor) && item.Ready);
            Check("actual unload retires ready token", item.Lost(current) && !item.Ready && item.Token == 0);
            Check("duplicate reply after unload cannot restore readiness", !item.Response(current, true, 1.3f) && !item.Ready);
            item.Query(1.4f, ref next); Check("reload requests independent readiness", item.Token != current);
            item = new ReadinessProbe(); Check("new owner snapshot rejects previous transport reply", !item.Response(current, true, 2f));
            Check("unsolicited zero token cannot unlock anything", !item.Response(0, true, 2f));
            Console.WriteLine("PASS: " + checks + " production readiness-probe delay, retry, state-floor, unload and stale-reply checks.");
        }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
    }
}
