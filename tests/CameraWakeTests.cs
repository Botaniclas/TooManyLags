using System;
using System.Collections.Generic;
using TooManyLags;
#if PLAYER_SIMULATION
using Timing = TooManyLags.PlayerSimulation.OwnershipCadence;
#else
using Timing = TooManyLags.OwnershipCadence;
#endif

internal static class HashFixture { internal static int GetStableHashCode(this string value) { return value.GetHashCode(); } }
public struct ZDOID { public long Value; }
public struct EulerFixture { public float y; }
public struct RotationFixture { public EulerFixture eulerAngles; }
public sealed class ZDO
{
    public ZDOID m_uid;
    public long Owner;
    public int Coverage = -1, OwnerReads, IntReads;
    public bool Valid = true;
    public bool IsValid() { return Valid; }
    public long GetOwner() { OwnerReads++; return Owner; }
    public int GetInt(int key, int fallback) { IntReads++; return Coverage; }
    public RotationFixture GetRotation() { return new RotationFixture(); }
}
public sealed class ZDOMan
{
    public static ZDOMan instance = new ZDOMan();
    public readonly Dictionary<ZDOID, ZDO> Objects = new Dictionary<ZDOID, ZDO>();
    public ZDO GetZDO(ZDOID id) { ZDO data; return Objects.TryGetValue(id, out data) ? data : null; }
}
public sealed class WakeController { public void Poll(float now) { } }
namespace UnityEngine { public static class Time { public static float unscaledTime; } }
namespace TooManyLags { internal static class Plugin { internal const string Id = "botanic.toomanylags.fgnaddon"; internal static WakeController Controller; } }
namespace TooManyLags.PlayerSimulation { internal static class Plugin { internal static WakeController Controller; } }

internal static class CameraWakeTests
{
    private static int checks;
    private static void Check(bool value, string label) { checks++; if (!value) throw new Exception(label); }
    public static int Main()
    {
        try
        {
            var wake = new CameraWake(); var timer = new Timing(1);
            var player = new ZDO { m_uid = new ZDOID { Value = 1 }, Owner = 7, Coverage = 0 };
            ZDOMan.instance.Objects.Add(player.m_uid, player);
            Check(CameraHeading.Read(player, 7, wake) == 0 && wake.Count == 1, "only currently sampled player is tracked");
            Check(timer.Due(0) && !timer.Due(.01f), "normal cadence holds");
            for (int i = 0; i < 1000; i++) Check(!wake.Changed(player), "unchanged receipt never wakes another pass");
            for (int i = 0; i < 10000; i++)
            {
                var unrelated = new ZDO { m_uid = new ZDOID { Value = i + 100 }, Owner = 7, Coverage = 8 };
                Check(!wake.Changed(unrelated) && unrelated.OwnerReads == 0 && unrelated.IntReads == 0, "unrelated objects stop at one ID lookup, without field reads or cached records");
            }
            Check(wake.Count == 1, "unrelated traffic cannot grow cache");
            player.Coverage = 8;
            Check(wake.Changed(player) && !wake.Changed(player), "changed receipt accepted once");
            timer.Request(); timer.Request();
            Check(timer.Due(.01f) && !timer.Due(.01f) && !timer.Due(.011f), "coalesced receipt wakes next poll without changing configured interval");
            Check(timer.Interval == 1f && timer.Due(1.01f), "normal interval resumes after requested poll");
            player.Owner = 9; player.Coverage = 1;
            Check(!wake.Changed(player), "foreign player ownership cannot request a pass");
            player.Owner = 7; player.Valid = false;
            Check(!wake.Changed(player), "invalid player cannot request a pass"); player.Valid = true;
            var impostor = new ZDO { m_uid = player.m_uid, Owner = 7, Coverage = 1 };
            Check(!wake.Changed(impostor), "same ID from stale object cannot request a pass");
            player.Coverage = 16;
            Check(wake.Changed(player) && !wake.Changed(player), "invalid advisory triggers fallback once");
            player.Coverage = Int32.MaxValue;
            Check(!wake.Changed(player), "equivalent invalid advisories do not create repeated work");
            player.Coverage = 0;
            Check(wake.Changed(player), "restored camera advisory requests one pass");
            wake.Clear(); Check(wake.Count == 0 && !wake.Changed(player), "departure/world reset releases records");
            for (int i = 0; i < 12; i++) wake.Track(new ZDOID { Value = i + 1 }, i + 1, -1);
            Check(wake.Count == 12, "cache contains only twelve sampled peers");
            wake.Clear(); wake.Track(player.m_uid, 7, 0);
            Check(wake.Count == 1, "next sampling replaces offline-peer records");
            Console.WriteLine("PASS: " + checks + " camera receipt, event cadence, origin, unrelated-traffic cost and bounded lifecycle assertions."); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
