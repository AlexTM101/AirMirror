// GPL-3.0. Bounded recovery of our native child, never an idle-video watchdog.
using System;
using System.Diagnostics;

namespace AirMirror {
    internal sealed class RecoveryPolicy {
        internal int Attempts { get; private set; }
        internal bool Scheduled { get; private set; }
        private long due;
        internal bool Schedule(long now) {
            if (Scheduled) return true;
            if (Attempts >= 3) return false;
            due = now + (1L << Attempts) * Stopwatch.Frequency;
            Attempts++; Scheduled = true; return true;
        }
        internal bool Take(long now) {
            if (!Scheduled || now < due) return false;
            Scheduled = false; return true;
        }
        internal void Reset() { Attempts = 0; Scheduled = false; due = 0; }
        internal static bool Verify() {
            var policy = new RecoveryPolicy();
            long now = 100 * Stopwatch.Frequency;
            for (int attempt=0; attempt<3; attempt++) {
                if (!policy.Schedule(now) || policy.Attempts != attempt+1) return false;
                long expected = now+(1L<<attempt)*Stopwatch.Frequency;
                if (policy.Take(expected-1) || !policy.Take(expected) || policy.Take(expected)) return false;
                now=expected;
            }
            if (policy.Schedule(now)) return false;
            policy.Reset();
            return policy.Attempts == 0 && !policy.Scheduled && policy.Schedule(now);
        }
    }
}
