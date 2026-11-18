using System;

namespace ARVisualizer
{
    /// <summary>A countdown independent of frame rate. The caller supplies a monotonic clock.</summary>
    public sealed class CountdownTimer
    {
        public const int MaximumSeconds = 99 * 60 + 59;
        public int PresetSeconds { get; private set; } = 60;
        public bool IsRunning { get; private set; }
        public bool Finished { get; private set; }
        double remaining = 60;
        double deadline;

        public int Seconds(double now)
        {
            Tick(now);
            return (int)Math.Ceiling(IsRunning ? Math.Max(0, deadline - now) : remaining);
        }

        public void Tick(double now)
        {
            if (!IsRunning || now < deadline) return;
            remaining = 0;
            IsRunning = false;
            Finished = true;
        }

        public void Toggle(double now)
        {
            Tick(now);
            if (IsRunning)
            {
                remaining = Math.Max(0, deadline - now);
                IsRunning = false;
            }
            else if (remaining > 0)
            {
                deadline = now + remaining;
                IsRunning = true;
                Finished = false;
            }
        }

        // Editing pauses the countdown and makes the displayed value the new reset value.
        public void Adjust(int delta, double now)
        {
            PresetSeconds = Math.Clamp(Seconds(now) + delta, 0, MaximumSeconds);
            remaining = PresetSeconds;
            IsRunning = Finished = false;
        }

        public void Reset()
        {
            remaining = PresetSeconds;
            IsRunning = Finished = false;
        }
    }
}
