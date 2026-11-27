using Wideska.Infrastructure;

internal static partial class Program
{
    private static void TestScrollMotion()
    {
        var motion = new ScrollMotion();
        motion.Reset(600);
        motion.Retarget(672);
        motion.Advance(.04);
        double position = motion.Position, velocity = motion.Velocity;
        motion.Retarget(744);
        Check(motion.Position == position && motion.Velocity == velocity,
            "Repeated wheel input must preserve both position and velocity without restarting easing");
        double previous = motion.Position;
        foreach (double interval in new[] { .007, .016, .032, .009, .021, .08, .15, .2 })
        {
            motion.Advance(interval);
            Check(motion.Position >= previous && motion.Position <= motion.Target,
                "Uneven frame timing must remain monotonic without overshoot");
            previous = motion.Position;
        }
        Check(motion.IsSettled, "Scroll motion must settle and release rendering work");
        var lowRate = new ScrollMotion();
        var highRate = new ScrollMotion();
        lowRate.Reset(0); highRate.Reset(0);
        lowRate.Retarget(300); highRate.Retarget(300);
        for (int i = 0; i < 12; i++) lowRate.Advance(1d / 30);
        for (int i = 0; i < 48; i++) highRate.Advance(1d / 120);
        Check(Math.Abs(lowRate.Position - highRate.Position) < .00001 && Math.Abs(lowRate.Velocity - highRate.Velocity) < .00001,
            "Motion must travel identically at 30 and 120 frames per second");
    }
}
