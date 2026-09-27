namespace Gravity;

// Waiting for a good reveal opportunity still displays the usable, settled map.
internal sealed class FallingPlayback
{
    private enum State { Settled, Waiting, Playing }
    private State _state;
    private bool _opened;
    private double _elapsed;
    public bool Completed => _state == State.Settled;
    public bool Playing => _state == State.Playing;
    public int Frame => !Playing ? FallingLayout.Steps
        : (int)Math.Clamp((_elapsed - 0.35) * 2 / FallingLayout.StepSeconds, 0, FallingLayout.Steps);

    public void Open(bool reveal)
    {
        if (_opened) return;
        _opened = true;
        if (reveal) _state = State.Waiting;
    }

    public void Process(double delta, bool canBegin)
    {
        if (Completed) return;
        if (_state == State.Waiting)
        {
            if (!canBegin) return;
            _state = State.Playing;
        }
        // Visibility matters only when starting. Once begun, the reveal never pauses.
        _elapsed += Math.Max(delta, 0);
        if (Frame == FallingLayout.Steps) Finish();
    }

    public void Finish()
    {
        _state = State.Settled;
        _opened = true;
    }
}
