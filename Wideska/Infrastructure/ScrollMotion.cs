namespace Wideska.Infrastructure;

internal sealed class ScrollMotion
{
    private const double Response = 32;
    public double Position { get; private set; }
    public double Velocity { get; private set; }
    public double Target { get; private set; }
    public bool IsSettled => Math.Abs(Target - Position) < .08 && Math.Abs(Velocity) < 2;

    public void Reset(double position) { Position = Target = position; Velocity = 0; }
    public void Retarget(double target) => Target = target;

    public double Advance(double seconds)
    {
        seconds = Math.Max(0, seconds);
        // Exact critically damped response: velocity stays continuous when wheel input changes the target.
        double displacement = Position - Target;
        double coefficient = Velocity + Response * displacement;
        double decay = Math.Exp(-Response * seconds);
        Position = Target + (displacement + coefficient * seconds) * decay;
        Velocity = (Velocity - Response * coefficient * seconds) * decay;
        return Position;
    }
}
