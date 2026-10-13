using System;
using TooManyLags;

internal static class CameraPredictionTests
{
    private static int checks;
    private static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    private static int Count(int bits) { int count = 0; for (; bits != 0; bits &= bits - 1) count++; return count; }
    public static int Main()
    {
        try
        {
            var prediction = new CameraPrediction();
            Check(prediction.Sample(0f, 0f, 30f, .1f) == 0, "first measurement cannot predict");
            Check(prediction.Sample(20f, .05f, 30f, .1f) == 32, "right turn adds east ahead of north/northeast");
            Check(prediction.Sample(19.9f, .075f, 30f, .1f) == 32, "small reversal retains predictive fringe briefly");
            Check(prediction.Sample(19.9f, .16f, 30f, .1f) == 8, "stationary prediction expires");
            for (int i = 1; i <= 1000; i++) Check(prediction.Sample(19.9f, .16f + i * .05f, 30f, .1f) == 8, "standing still generates no coverage changes");
            prediction.Reset(); prediction.Sample(0f, 0f, 30f, .1f);
            Check(prediction.Sample(-20f, .05f, 30f, .1f) == 55, "left turn adds west across north wrap");
            prediction.Reset(); prediction.Sample(359f, 0f, 30f, .1f);
            Check(prediction.Sample(4f, .05f, 30f, .1f) == 24, "positive wrap predicts clockwise, not a full counter-turn");
            prediction.Reset(); prediction.Sample(1f, 0f, 30f, .1f);
            Check(prediction.Sample(356f, .05f, 30f, .1f) == 40, "negative wrap predicts counterclockwise");
            Check(prediction.Sample(20f, .1f, 30f, 0f) == 8, "zero disables prediction immediately");
            Check(prediction.Sample(20f, .15f, 30f, .1f) == 8, "re-enable needs fresh measurements");
            Check(prediction.Sample(40f, 2f, 30f, .1f) == 1, "stale sample gap cannot predict");
            Check(prediction.Sample(80f, 2f, 30f, .1f) == 9, "duplicate timestamp cannot predict");
            Check(prediction.Sample(120f, 1f, 30f, .1f) == 10, "backwards clock resets history");
            prediction.Reset(); prediction.Sample(0f, 0f, 30f, .1f);
            Check(prediction.Sample(180f, .05f, 30f, .1f) == 4, "ambiguous exact half-turn uses actual coverage");
            prediction.Reset(); prediction.Sample(0f, 0f, 30f, .1f);
            Check(prediction.Sample(Single.NaN, .05f, 30f, .1f) == -1, "invalid camera resets history");
            Check(prediction.Sample(90f, .1f, 30f, .1f) == 2, "camera recovery starts fresh");
            Check(prediction.Sample(0f, 0f, .2f, 30f, .1f) == -1, "vertical camera has no horizontal prediction");
            foreach (float overlap in new[] { 0f, 10f, 30f, 45f })
            foreach (float speed in new[] { -900f, -240f, -15f, 0f, 15f, 240f, 900f })
            for (float yaw = -720f; yaw <= 720f; yaw += 2.5f)
            {
                prediction.Reset(); prediction.Sample(yaw - speed * .05f, 0f, overlap, .1f);
                int result = prediction.Sample(yaw, .05f, overlap, .1f);
                int actual = CameraPrediction.Bits(AuthorityGrid.HeadingCoverage(yaw, overlap));
                int bits = CameraPrediction.Bits(result);
                Check((bits & actual) == actual, "actual coverage is never removed");
                Check(result >= 0 && result < AuthorityGrid.CoverageCount && Count(bits) <= 3, "at most three adjacent directions");
                Check(Count(bits & ~actual) <= 1, "at most one additional direction");
                if (speed == 0f) Check(bits == actual, "no predictive coverage without rotation");
            }
            for (int actual = 0; actual < 16; actual++) for (int extra = 0; extra < 8; extra++)
            {
                int joined = CameraPrediction.Join(actual, extra);
                Check((CameraPrediction.Bits(joined) & CameraPrediction.Bits(actual)) == CameraPrediction.Bits(actual), "join preserves baseline");
                Check(Count(CameraPrediction.Bits(joined) & ~CameraPrediction.Bits(actual)) <= 1, "join never adds a second new direction");
            }
            Console.WriteLine("PASS: " + checks + " production camera prediction assertions: direction/speed, wrap, bounded coverage, idle expiry, zero/off, reversal hold and lifecycle resets."); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
