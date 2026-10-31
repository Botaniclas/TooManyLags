namespace TooManyLags
{
    // Exact non-visual semantics of BaseAI.UpdateTakeoffLanding. Character.Land
    // and TakeOff are replaced by a shadow flight flag so no Animator, effect or
    // gameplay body is touched.
    internal static class NativeFlightTransition
    {
        internal static void Advance(bool randomFly, float delta, bool busy, bool flying, float altitude,
            float maxLandAltitude, float airDuration, float groundDuration, float chanceToLand,
            float chanceToTakeoff, float roll, ref float timer, ref bool nextFlying)
        {
            nextFlying = flying;
            if (!randomFly) return;
            timer += delta;
            if (busy) return;
            if (flying)
            {
                if (timer <= airDuration || altitude >= maxLandAltitude) return;
                timer = 0f;
                if (roll > chanceToLand) return;
                nextFlying = false;
                return;
            }
            if (timer <= groundDuration) return;
            timer = 0f;
            if (roll > chanceToTakeoff) return;
            nextFlying = true;
        }
    }
}
