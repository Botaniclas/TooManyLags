using System;
using System.Collections.Generic;
using System.Diagnostics;
using New = TooManyLags;
using Old = AuthorityBaseline;

internal static class FieldCacheBenchmark
{
    private sealed class NewNode : New.AuthorityNode
    {
        internal long Owner; internal int Writes;
        internal override long CurrentOwner { get { return Owner; } }
        internal override New.ApplyResult Apply(long target)
        { if (target != 0) { if (Owner != target) Writes++; Owner = target; ManagedOwner = target; Managed = true; } return New.ApplyResult.Done; }
    }
    private sealed class OldNode : Old.AuthorityNode
    {
        internal long Owner; internal int Writes;
        internal override long CurrentOwner { get { return Owner; } }
        internal override Old.ApplyResult Apply(long target)
        { if (target != 0) { if (Owner != target) Writes++; Owner = target; ManagedOwner = target; Managed = true; } return Old.ApplyResult.Done; }
    }
    private struct Result { internal double Milliseconds; internal int Attempts, Writes, PeakFields, TimerVisits; }
    private static New.GridPeer NP(int uid, int x, float yaw) { return new New.GridPeer(uid, new New.Cell(x, 0), yaw); }
    private static Old.GridPeer OP(int uid, int x, float yaw) { return new Old.GridPeer(uid, new Old.Cell(x, 0), yaw); }
    private static int X(int frame, int uid, string mode)
    { return (mode == "shared" ? 0 : uid * 100) + (mode=="rapid" ? frame*8 : mode == "unique" || mode == "cold" ? frame : mode == "revisit" || mode == "shared" ? (frame % 12 < 6 ? frame % 12 : 11 - frame % 12) : 0); }
    private static float Yaw(int frame, string mode) { return mode == "rotation" || mode == "fastturn" ? (frame % 2) * 180f : 0; }
    private static float Now(int frame,string mode){return frame*(mode=="rapid"?.1f:4f);}
    private static Result RunNew(int count, int objects, int frames, string mode,bool forward=false)
    {
        var world = new New.AuthorityWorld(99, 2, New.GridCoveragePattern.Rounded7x7, true, true,0,3,forward,.2f);
        var nodes = new List<NewNode>();
        for (int uid = 1; uid <= (mode == "shared" ? 1 : count); uid++) for (int x = -3; x <= 8; x++) for (int z = -3; z <= 3; z++)
            for (int n = 0; n < objects; n++) { var node = new NewNode { Cell = new New.Cell((mode == "shared" ? 0 : uid * 100) + x, z) }; nodes.Add(node); world.Add(node, 0); }
        var input = new List<New.GridPeer>(count);
        int warmup = mode == "cold" ? 0 : 100;
        for (int frame = 0; frame < warmup; frame++) { input.Clear(); for (int uid = 1; uid <= count; uid++) input.Add(NP(uid, X(frame, uid, mode), Yaw(frame, mode))); world.Tick(input, Now(frame,mode)); if (mode == "rotation") world.Tick(input, Now(frame,mode) + 3); }
        int attempts = world.Attempts, writes = 0; foreach (var node in nodes) writes += node.Writes;
        var timer = Stopwatch.StartNew();
        int peak=0,visits=world.Grid.TimerVisits;
        for (int frame = warmup; frame < frames + warmup; frame++) { input.Clear(); for (int uid = 1; uid <= count; uid++) input.Add(NP(uid, X(frame, uid, mode), Yaw(frame, mode))); world.Tick(input, Now(frame,mode)); if (mode == "rotation") world.Tick(input, Now(frame,mode) + 3); peak=Math.Max(peak,world.Grid.CachedCells); }
        timer.Stop(); int finalWrites = 0; foreach (var node in nodes) finalWrites += node.Writes;
        return new Result { Milliseconds = timer.Elapsed.TotalMilliseconds, Attempts = world.Attempts - attempts, Writes = finalWrites - writes, PeakFields=peak, TimerVisits=world.Grid.TimerVisits-visits };
    }
    private static Result RunOld(int count, int objects, int frames, string mode)
    {
        var world = new Old.AuthorityWorld(99, 2, Old.GridCoveragePattern.Rounded7x7, true, true);
        var nodes = new List<OldNode>();
        for (int uid = 1; uid <= (mode == "shared" ? 1 : count); uid++) for (int x = -3; x <= 8; x++) for (int z = -3; z <= 3; z++)
            for (int n = 0; n < objects; n++) { var node = new OldNode { Cell = new Old.Cell((mode == "shared" ? 0 : uid * 100) + x, z) }; nodes.Add(node); world.Add(node, 0); }
        var input = new List<Old.GridPeer>(count);
        int warmup = mode == "cold" ? 0 : 100;
        for (int frame = 0; frame < warmup; frame++) { input.Clear(); for (int uid = 1; uid <= count; uid++) input.Add(OP(uid, X(frame, uid, mode), Yaw(frame, mode))); world.Tick(input, Now(frame,mode)); if (mode == "rotation") world.Tick(input, Now(frame,mode) + 3); }
        int attempts = world.Attempts, writes = 0; foreach (var node in nodes) writes += node.Writes;
        var timer = Stopwatch.StartNew();
        int peak=0,visits=world.Grid.TimerVisits;
        for (int frame = warmup; frame < frames + warmup; frame++) { input.Clear(); for (int uid = 1; uid <= count; uid++) input.Add(OP(uid, X(frame, uid, mode), Yaw(frame, mode))); world.Tick(input, Now(frame,mode)); if (mode == "rotation") world.Tick(input, Now(frame,mode) + 3); peak=Math.Max(peak,world.Grid.CachedCells); }
        timer.Stop(); int finalWrites = 0; foreach (var node in nodes) finalWrites += node.Writes;
        return new Result { Milliseconds = timer.Elapsed.TotalMilliseconds, Attempts = world.Attempts - attempts, Writes = finalWrites - writes, PeakFields=peak, TimerVisits=world.Grid.TimerVisits-visits };
    }
    private static void Scenario(string mode, int players, int objects, int frames,bool forward=false)
    {
        var oldTimes = new double[5]; var newTimes = new double[5]; Result a = default(Result), b = default(Result);
        for (int trial = 0; trial < 5; trial++)
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            if ((trial & 1) == 0) { a = RunOld(players, objects, frames, mode); b = RunNew(players, objects, frames, mode,forward); }
            else { b = RunNew(players, objects, frames, mode,forward); a = RunOld(players, objects, frames, mode); }
            oldTimes[trial] = a.Milliseconds; newTimes[trial] = b.Milliseconds;
        }
        Array.Sort(oldTimes); Array.Sort(newTimes);
        if (a.Writes != b.Writes) throw new Exception("Benchmark changes actual ownership writes: " + mode);
        Console.WriteLine("mode=" + mode + "; forward_new="+forward+"; peers=" + players + "; objects/cell=" + objects + "; ticks=" + (mode == "rotation" ? frames * 2 : frames) +
            "; median_old_ms=" + oldTimes[2].ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
            "; median_new_ms=" + newTimes[2].ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
            "; old_jobs=" + a.Attempts + "; new_jobs=" + b.Attempts + "; actual_writes_old=new=" + a.Writes+
            "; peak_fields_old="+a.PeakFields+"; peak_fields_new="+b.PeakFields+"; timer_visits_old="+a.TimerVisits+"; timer_visits_new="+b.TimerVisits);
    }
    public static void Main()
    {
        Console.WriteLine("Synthetic pure-grid/scheduler benchmark versus supplied baseline; five alternating trials, median, minimal no-op object cost; no Unity/network timing.");
        Scenario("standing", 12, 10, 20000);
        Scenario("unique", 12, 0, 5000);
        Scenario("revisit", 5, 10, 5000);
        Scenario("revisit", 5, 100, 2000);
        Scenario("rotation", 5, 10, 5000);
        Scenario("fastturn", 5, 10, 5000);
        Scenario("shared", 12, 10, 5000);
        Scenario("cold", 5, 10, 12);
        Scenario("rapid",12,0,2000);
        Scenario("standing",12,10,20000,true);
        Scenario("revisit",5,10,5000,true);
        Scenario("rapid",12,0,2000,true);
    }
}
