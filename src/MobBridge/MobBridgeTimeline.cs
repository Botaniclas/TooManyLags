#if TML_MOB_BRIDGE || TML_DEBUG_SYSTEM
using System;

namespace TooManyLags
{
    // Half-speed presentation clock. An active finite recording can spend its
    // remaining time more slowly when delivery falls behind; it never rewinds
    // or treats estimated destination progress as an observed takeover state.
    internal sealed class MobBridgeTimeline
    {
        internal const float FakeRate = .5f;
        private readonly double started;
        private readonly float latency;
        private float expectedFirst;
        private float expectedMatch, deliveryDelay;
        private float includedDelay;
        private float receiverFrame;
        private double realStarted = Double.PositiveInfinity;
        private double lastNow;
        private float fakeTime, playbackRate, scheduledRate, initialGap;
        private float rateResponse;
        private float recordingEnd = Single.PositiveInfinity, bufferRate = Single.PositiveInfinity, bufferWait;
        private float inputEnd = Single.PositiveInfinity, inputWait;
        private bool bufferObserved;
        private float bufferStep;
        private float receivedFrontier;
        private bool bufferScheduled;
        private bool fallbackStart;
        private readonly bool frameDriven;
        private bool frameStarted;

        internal MobBridgeTimeline(double now, float latencySeconds, float initialRealRate = 1f, bool frameDriven = false)
        {
            if (!Finite(now)) throw new ArgumentException("Invalid bridge start.");
            if (!Finite(latencySeconds)) throw new ArgumentException("Invalid bridge latency.");
            this.frameDriven = frameDriven;
            started = now;
            lastNow = now;
            latency = Math.Max(.001f, latencySeconds);
            expectedFirst = latency;
            playbackRate = scheduledRate = Rate(initialRealRate);
        }

        internal float Latency { get { return latency; } }
        internal void ExpectFirst(float seconds)
        { if (Finite(seconds) && seconds >= latency) expectedFirst = seconds; }
        internal void ExpectMatch(float seconds, float included = 0f)
        { if (Finite(seconds) && seconds >= expectedFirst && Finite(included) && included >= 0f) { expectedMatch = seconds; includedDelay = included; } }
        internal void ExpectFrames(float seconds)
        { if (Finite(seconds) && seconds > 0f) receiverFrame = seconds; }
        internal float ObserveDelivery(float elapsed, float sample)
        {
            if (!Finite(elapsed) || !Finite(sample) || elapsed < 0f || sample < 0f) return deliveryDelay;
            double expected = latency + (double)sample;
            if (receiverFrame > 0f) expected = Math.Ceiling(expected / receiverFrame - .000001d) * receiverFrame;
            // The planned receipt already includes main-loop processing.
            // Add only lateness beyond it, not that same frame wait twice.
            deliveryDelay = (float)Math.Max(0d, elapsed - expected);
            return deliveryDelay;
        }
        internal float PlaybackRate { get { return playbackRate; } }
        internal bool BufferLimited { get { return bufferRate < scheduledRate; } }
        internal bool RealStarted { get { return Finite(realStarted); } }

        // Wall-time offset of the destination's simulation zero. Before the
        // first accepted clock, the measured route latency is the prediction.
        internal float RealStartOffset(double now)
        {
            if (RealStarted) return Math.Max(0f, (float)(realStarted - started));
            if (Finite(now) && now >= started + latency + .25d) return latency + .25f;
            return latency;
        }

        internal void AdvanceFrame(double now, float nativeFrameDelta)
        {
            if (!frameDriven || !Finite(now) || !Finite(nativeFrameDelta) || now < lastNow) return;
            // Unity already bounds its simulation delta after local stalls.
            // Callback wall-time spacing also contains changes in LateUpdate
            // phase; min(frameDelta, callbackGap) repeatedly drops those changes
            // and makes a steady frame clock alternate between fast and slow.
            float dt = Math.Max(0f, nativeFrameDelta);
            if (!frameStarted) dt = Math.Min(dt, (float)(now - started));
            frameStarted = true;
            lastNow = now;
            Integrate(dt);
        }

        internal float FakeTime(double now)
        {
            if (frameDriven || !Finite(now) || now < lastNow) return fakeTime;
            float dt = (float)Math.Max(0d, now - lastNow);
            lastNow = now;
            Integrate(dt);
            return fakeTime;
        }

        private void Integrate(float dt)
        {
            if (dt <= 0f) return;
            float entryRate = playbackRate;
            float difference = playbackRate - scheduledRate;
            float advance;
            if (rateResponse <= 0f || difference == 0f)
                advance = dt * playbackRate;
            else
            {
                // Integrate the continuous rate analytically. Multiplying frame
                // dt by the new endpoint rate would still introduce frame-size
                // dependent jumps when an owner-rate estimate arrives.
                double decay = Math.Exp(-(double)dt / rateResponse);
                advance = (float)(scheduledRate * (double)dt + difference * rateResponse * (1d - decay));
                playbackRate = (float)(scheduledRate + difference * decay);
            }
            double remaining = Math.Max(0d, (double)recordingEnd - fakeTime);
            // Observed packet age is sampled at this frame's END, while
            // fakeTime is still at its START. A packet due in 1ms cannot fund
            // an entire 16ms frame. Conversely, an exactly sufficient healthy
            // buffer needs no braking. Unknown/outage budgets keep their
            // existing continuous, frame-subdivision-independent envelope.
            bool budgetNeeded = bufferObserved ? advance + (double)entryRate * bufferWait > remaining * 1.000001d + 1e-12d :
                entryRate >= remaining / bufferWait;
            // A real state already covering this frame needs no reserve for a
            // future packet. Advance continuously, then join that actual state.
            if (bufferObserved && receivedFrontier + .000001f >= fakeTime + advance) budgetNeeded = false;
            if (bufferWait > 0f && remaining > 0d && budgetNeeded)
            {
                // Critically damp remaining time, preserving entry velocity.
                // x(t)=(x0+b*t)*exp(-omega*t), b=omega*x0-v0 >= 0.
                // omega >= 2/wait makes entry acceleration nonpositive when
                // v0 >= x0/wait; omega >= v0/x0 prevents endpoint overshoot.
                // This changes playback time only, never recorded poses.
                double wait = bufferWait + (bufferObserved ? dt : 0f);
                double omega = Math.Max(2d / wait, entryRate / remaining);
                double b = Math.Max(0d, omega * remaining - entryRate);
                double decay = Math.Exp(-omega * dt);
                float budgetAdvance = (float)Math.Max(0d, remaining - (remaining + b * dt) * decay);
                float budgetVelocity = (float)((entryRate + omega * b * dt) * decay);
                if (bufferScheduled && wait >= dt && 2d * remaining >= entryRate * wait)
                {
                    // A known physical notification deadline can be reached
                    // with nonnegative constant deceleration. The exponential
                    // outage envelope needlessly approaches a standstill here.
                    double acceleration = Math.Min(0d, 2d * (remaining - entryRate * wait) / (wait * wait));
                    budgetAdvance = (float)Math.Max(0d, entryRate * dt + .5d * acceleration * dt * dt);
                    budgetVelocity = (float)Math.Max(0d, entryRate + acceleration * dt);
                }
                if (budgetAdvance < advance)
                {
                    advance = budgetAdvance;
                    playbackRate = budgetVelocity;
                }
            }
            // A late warning can leave insufficient braking distance. Hold the
            // actual endpoint; never run an invisible clock beyond the buffer
            // and never replay a backlog when delivery resumes.
            if (inputWait > 0f && inputEnd > fakeTime)
            {
                double available = (double)inputEnd - fakeTime;
                if (entryRate >= available / inputWait)
                {
                    double omega = Math.Max(2d / inputWait, entryRate / available);
                    double b = Math.Max(0d, omega * available - entryRate), decay = Math.Exp(-omega * dt);
                    float budget = (float)Math.Max(0d, available - (available + b * dt) * decay);
                    if (budget < advance)
                    { advance = budget; playbackRate = (float)((entryRate + omega * b * dt) * decay); }
                }
            }
            float limit = Math.Min(recordingEnd, inputEnd);
            if (fakeTime + advance >= limit)
            {
                // Reaching the last sample is not itself a stop. A received
                // successor may take over this very frame and needs the moving
                // endpoint tangent. Only an actual exhausted-buffer wait has
                // zero outgoing velocity.
                bool movingEndpoint = fakeTime < limit;
                fakeTime = limit;
                if (!movingEndpoint || !bufferObserved || receivedFrontier + .000001f < limit) playbackRate = 0f;
            }
            else fakeTime += advance;
        }

        internal void ManageInputBuffer(float end, float age, float interval, bool closed)
        {
            if (!Finite(end) || end < fakeTime || !Finite(age) || age < 0f || !Finite(interval) || interval <= 0f) return;
            inputEnd = end;
            inputWait = closed ? 0f : age < interval ? interval - age : interval;
            // Only observers use this input budget. A source that closes early
            // seals its actual endpoint, replacing the descriptor's estimate.
            if (closed) recordingEnd = Math.Min(recordingEnd, end);
        }

        internal void ManageBuffer(float end, float destination, float sampleAge, float ownerRate,
            float sampleInterval, bool observed, float physicalStep = 0f, float frameDelta = 0f)
        {
            if (!Finite(end) || end <= 0f || !Finite(destination) || destination < 0f ||
                !Finite(sampleAge) || sampleAge < 0f || !Finite(ownerRate) || ownerRate <= 0f ||
                !Finite(sampleInterval) || sampleInterval <= 0f) return;
            // End is fixed by the captured latency/rate, not by arriving packets.
            if (end < fakeTime || Finite(recordingEnd) && end != recordingEnd) return;
            recordingEnd = end;
            receivedFrontier = observed ? destination : 0f;
            bufferStep = Finite(physicalStep) && physicalStep > 0f ? physicalStep : 0f;
            float elapsed = (float)Math.Max(0d, lastNow - started);
            bufferScheduled = bufferStep > 0f && (observed || elapsed < expectedFirst);
            float remaining = Math.Max(0f, recordingEnd - fakeTime);
            // Predict only the next ordinary packet interval, and only to plan
            // presentation speed. A stale packet cannot promise endless progress.
            // A partial fake endpoint needs the next *completed* native step
            // on the destination. Plan for that actual sample, not a fractional
            // physics pose which the new owner will never publish.
            float target = bufferStep > 0f ? (float)(Math.Ceiling((double)end / bufferStep - .000001d) * bufferStep) : end;
            float expected = observed ? Math.Min(target, destination + ownerRate * Math.Min(sampleAge, sampleInterval)) : 0f;
            float wait = Math.Max(0f, (target - expected) / ownerRate);
            if (!observed && bufferStep > 0f)
                wait += Math.Max(0f, latency - elapsed);
            if (observed && destination < end)
            {
                // Even a fast owner cannot deliver between actual clock sends.
                // Once the expected packet is missing, reserve another ordinary
                // interval rather than treating its predicted frontier as real.
                float nextPacket = sampleAge < sampleInterval ? sampleInterval - sampleAge : sampleInterval;
                wait = Math.Max(wait, nextPacket);
            }
            // Capacity includes reserve; it is not a deadline for displaying
            // every reserved sample. Spend the nominal window at half speed
            // while its predicted matching deadline is still ahead. Late
            // delivery moves that deadline, never the already shown horizon.
            float frameEnd = elapsed + (Finite(frameDelta) ? Math.Max(0f, frameDelta) : 0f);
            float planned = expectedMatch + Math.Max(0f, deliveryDelay - includedDelay) - frameEnd;
            if (expectedMatch > 0f && planned > 0f)
            {
                // A received match transitions from the last displayed sample
                // and advances the real blend through its arrival frame. Do
                // not budget another full fake frame there: that double-spends
                // time and brakes a sufficient low-latency reserve too early.
                wait = Math.Max(0f, planned - (frameDriven && Finite(frameDelta) ? Math.Max(0f, frameDelta) : 0f));
                bufferScheduled = bufferStep > 0f;
            }
            bufferRate = wait > 0f ? remaining / wait : Single.PositiveInfinity;
            bufferWait = wait;
            bufferObserved = observed;
            // Returning packets restore the owner-rate schedule smoothly too.
            if (rateResponse <= 0f) rateResponse = sampleInterval;
        }

        internal float RealTime(double now)
        {
            if (!RealStarted || !Finite(now) || now < realStarted) return 0f;
            return (float)(now - realStarted);
        }

        // First real state observed at T has fake progress P. Real advances at
        // 1x while fake continues at 0.5x. Equality is reached after 2P:
        // P + 0.5*t = t. For the nominal L arrival, P = L/2 and completion is
        // at 2L.
        internal float RealWeight(double now)
        {
            if (!RealStarted || !Finite(now) || now <= realStarted) return 0f;
            float fake = FakeTime(now), real = RealTime(now);
            if (initialGap <= .000001f || fake <= real) return 1f;
            float t = (float)Math.Max(0d, Math.Min(1d, 1d - (fake - real) / initialGap));
            return t * t * (3f - 2f * t);
        }

        internal bool Complete(double now)
        {
            return RealStarted && Finite(now) && RealTime(now) + .000001f >= FakeTime(now);
        }

        // Authoritative completion for a live bridge. Wall time is only a
        // fallback; the destination's reported simulation frontier avoids an
        // early takeover when its AI runs slower than real time.
        internal bool CompleteAtDestination(double now, float destinationSimulation)
        {
            return RealStarted && Finite(now) && Finite(destinationSimulation) &&
                destinationSimulation + .000001f >= FakeTime(now);
        }

        internal void MatchHorizon(double now, float destinationSimulation, float sampleAge)
        {
            if (!Finite(now) || !Finite(destinationSimulation) || !Finite(sampleAge)) return;
            // Keep continuous playback until an observed destination sample
            // reaches the fake frontier. A near-match hold introduces a stop
            // before takeover and repeats when sample spacing varies. Do not
            // snap a currently transitioning rate to its scheduled endpoint.
        }

        // The first accepted destination state is the real simulation's zero.
        // Late/duplicate observations never move that epoch.
        internal void ObserveReal(double now, bool fallback = false)
        {
            if (!Finite(now)) return;
            if (!RealStarted || fallbackStart && !fallback && now >= realStarted)
            {
                realStarted = Math.Max(started, now);
                initialGap = Math.Max(.000001f, FakeTime(realStarted));
                fallbackStart = fallback;
            }
        }

        // CPU slowdown scales the wall-time scheduler, not simulation dt. The
        // rate is monotone per update and can never rewind consumed fake time.
        internal void ObserveRealRate(float ratio, float sampleInterval)
        {
            if (!Finite(ratio) || !Finite(sampleInterval) || sampleInterval <= 0f) return;
            scheduledRate = Rate(ratio);
            // Response follows measured destination-local sample spacing.
            // This is a continuous filter, not a new simulation tick raster.
            rateResponse = sampleInterval;
        }

        private static float Rate(float ratio)
        {
            if (!Finite(ratio)) ratio = 1f;
            return Math.Max(.05f, Math.Min(.75f, FakeRate * Math.Max(.1f, Math.Min(1.5f, ratio))));
        }

        internal static bool Finite(double value)
        {
            return !Double.IsNaN(value) && !Double.IsInfinity(value);
        }

        internal static bool Finite(float value)
        {
            return !Single.IsNaN(value) && !Single.IsInfinity(value);
        }
    }
}
#endif
