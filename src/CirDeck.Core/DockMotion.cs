namespace CirDeck.Core;

public static class DockMotion
{
    /// <summary>
    /// Critically damped step. The caller owns velocity and feeds the result back next frame.
    /// </summary>
    public static double Follow(double current, double target, ref double velocity, double dt, double omega = 18)
    {
        if (dt <= 0 || omega <= 0)
        {
            return current;
        }

        var displacement = current - target;
        var acceleration = -omega * omega * displacement - 2 * omega * velocity;
        velocity += acceleration * dt;
        return current + velocity * dt;
    }
}
