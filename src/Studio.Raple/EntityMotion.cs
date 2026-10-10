namespace Studio.Raple;

// Presentation endpoints from one completed enemy tick. Sampling never moves the runtime entity.
public sealed record EntityMotion(float FromX, float FromY, float ToX, float ToY, long TickMs, int DurationMs)
{
    public double VelocityX => (ToX - (double)FromX) / DurationMs;
    public double VelocityY => (ToY - (double)FromY) / DurationMs;
    public (double X, double Y) At(double ms)
    {
        // GameObject.onTickPos/update blends the position at the update toward its new target.
        // Clamp the segment so pauses, catch-up frames and stops never extrapolate beyond it.
        var fraction = Math.Clamp((ms - TickMs) / DurationMs, 0, 1);
        return (FromX + (ToX - (double)FromX) * fraction, FromY + (ToY - (double)FromY) * fraction);
    }
}
