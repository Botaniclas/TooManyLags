namespace TooManyLags.GridHandoffs
{
    // One outstanding query per owner/state snapshot. Retries retain its
    // token so replies slower than the retry interval can still complete.
    internal struct ReadinessProbe
    {
        internal long Token;
        internal float Next;
        internal bool Ready;
        internal bool Query(float now, ref long sequence)
        {
            if (Ready || now < Next) return false;
            if (Token == 0) Token = ++sequence;
            Next = now + .5f;
            return true;
        }
        internal bool Response(long token, bool available, float now)
        {
            if (Token == 0 || Token != token) return false;
            Ready = available; Next = now + .5f;
            if (!available) Token = 0;
            return true;
        }
        internal bool Lost(long token)
        {
            if (Token == 0 || Token != token) return false;
            Invalidate(); return true;
        }
        internal void Invalidate() { Ready = false; Token = 0; Next = 0; }
    }
}
