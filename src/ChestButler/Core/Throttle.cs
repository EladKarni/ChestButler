using UnityEngine;

namespace ChestButler.Core
{
    /// <summary>Self-throttling (v2 plan §16.6 — owner decision, W1 builds this).
    ///
    /// The config knobs only help a player who knows they exist, finds the file and guesses a value.
    /// This measures what the mod actually costs and backs off on its own, which is what turns the
    /// per-FRAME budget into a real per-SECOND one (§16.3: 4 moves/frame is 240 RPC/s at 60 fps but
    /// 2,304 RPC/s at 144 fps, from one client).
    ///
    /// **We measure our own work, never the framerate.** Frame time is confounded by every other mod
    /// and by the GPU, so it cannot be attributed to us. A Stopwatch around our two hot paths — the
    /// SorterBehaviour tick and Organizer's plan/run — measures exactly what we are responsible for.
    ///
    /// **Only RATES are throttled.** Radius, groups, routing order and every other result-affecting
    /// input are untouched: speed may legitimately differ per client, outcomes may not, because two
    /// clients computing different answers for the same base is a correctness bug (§16.3).
    ///
    /// No per-frame hook is needed — the window rolls lazily inside Record and the accessors, both of
    /// which are only called when we are about to do or have just done work. That keeps Plugin.cs
    /// untouched by this feature.</summary>
    internal static class Throttle
    {
        /// <summary>Target cost: ~1 ms per frame at 60 fps ≈ 6% of the frame.</summary>
        private const float TargetMsPerSecond = 60f;

        /// <summary>Back off above ~1.5 ms/frame, recover below ~0.75 ms/frame. The gap between the
        /// two thresholds IS the hysteresis: with a single threshold a base sitting exactly at the
        /// line oscillates between fast and slow every window.</summary>
        private const float BackOffAbove = 90f;
        private const float RecoverBelow = 45f;

        private const float WindowSeconds = 1f;
        private const float AdjustEvery = 4f;      // never re-tune more than once every few seconds
        private const float MinScale = 0.15f;      // a big base must never silently stop sorting
        private const float BackOffFactor = 0.7f;
        private const float RecoverFactor = 1.25f;

        private static float _windowStart;
        private static float _msInWindow;
        private static float _avgMsPerSecond;
        private static float _lastAdjust;

        /// <summary>1.0 = run at exactly the configured rate. Config is the CEILING, not the tuning
        /// dial — this only ever slows us down relative to it.</summary>
        private static float _scale = 1f;

        internal static float Scale => _scale;
        internal static float AverageMsPerSecond => _avgMsPerSecond;

        /// <summary>Report milliseconds we just spent on our own work.</summary>
        internal static void Record(float milliseconds)
        {
            if (milliseconds > 0f) _msInWindow += milliseconds;
            Roll();
        }

        private static void Roll()
        {
            float now = Time.realtimeSinceStartup;
            if (_windowStart <= 0f) { _windowStart = now; _lastAdjust = now; return; }

            float elapsed = now - _windowStart;
            if (elapsed < WindowSeconds) return;

            float rate = _msInWindow / elapsed;                       // ms of our work per second
            // Exponential moving average: one expensive plan should not pin the throttle down, and
            // one quiet second should not release it.
            _avgMsPerSecond = _avgMsPerSecond <= 0f ? rate : (_avgMsPerSecond * 0.7f + rate * 0.3f);
            _msInWindow = 0f;
            _windowStart = now;

            if (now - _lastAdjust < AdjustEvery) return;

            float before = _scale;
            if (_avgMsPerSecond > BackOffAbove)
                _scale = Mathf.Max(MinScale, _scale * BackOffFactor);
            else if (_avgMsPerSecond < RecoverBelow)
                _scale = Mathf.Min(1f, _scale * RecoverFactor);

            if (!Mathf.Approximately(before, _scale))
            {
                _lastAdjust = now;
                // Say so. A mod that quietly throttles itself into a crawl looks broken, and
                // "why isn't my sorter sorting?" has to be answerable from the log (§16.6).
                Plugin.Log.LogInfo("[throttle] ChestButler is using " + _avgMsPerSecond.ToString("0.0") +
                    " ms/s (target " + TargetMsPerSecond.ToString("0") + "); rate scale " +
                    before.ToString("0.00") + " -> " + _scale.ToString("0.00") +
                    (_scale < 1f ? " (backing off)" : " (recovering)"));
            }
            else
            {
                _lastAdjust = now;
            }
        }

        /// <summary>Moves per second for the Organize run, scaled down when we are over budget.</summary>
        internal static int MovesPerSecond(int configured)
        {
            Roll();
            return Mathf.Max(1, Mathf.RoundToInt(configured * _scale));
        }

        /// <summary>Seconds between sorter ticks — stretched when we are over budget, never shortened
        /// below the configured value.</summary>
        internal static float TransferInterval(float configured)
        {
            Roll();
            float v = configured / Mathf.Max(MinScale, _scale);
            return Mathf.Clamp(v, configured, configured * 8f);
        }

        /// <summary>How long a "no home for this item" answer is trusted. Lengthening it under load is
        /// the cheapest way to cut the sorter tick's cost, because a homeless item is the steady state
        /// of a sorter chest (§16.3).</summary>
        internal static float MissCooldown(float configured)
        {
            Roll();
            float v = configured / Mathf.Max(MinScale, _scale);
            return Mathf.Clamp(v, configured, configured * 6f);
        }

        /// <summary>Scoped timer: <c>using (Throttle.Measure()) { ... }</c>. Records on dispose.</summary>
        internal static Timer Measure() => new Timer(System.Diagnostics.Stopwatch.StartNew());

        internal struct Timer : System.IDisposable
        {
            private readonly System.Diagnostics.Stopwatch _sw;
            internal Timer(System.Diagnostics.Stopwatch sw) { _sw = sw; }

            /// <summary>Elapsed so far, for callers that also want to log the number.</summary>
            internal float Milliseconds => _sw != null ? (float)_sw.Elapsed.TotalMilliseconds : 0f;

            public void Dispose()
            {
                if (_sw == null) return;
                _sw.Stop();
                Record((float)_sw.Elapsed.TotalMilliseconds);
            }
        }
    }
}
