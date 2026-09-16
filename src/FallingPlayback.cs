namespace Gravity;

// Presentation time advances only while the player can see the map.
internal sealed class FallingPlayback(bool completed)
{
    private double _visibleSeconds;
    private double _elapsed;
    public bool Completed { get; private set; } = completed;
    public bool Started => _elapsed > 0;
    public int Frame => Completed ? FallingLayout.Steps
        : Math.Min(FallingLayout.Steps, (int)(_elapsed / FallingLayout.StepSeconds));

    public void Process(double delta, bool visible)
    {
        if (!visible)
        {
            _visibleSeconds = 0;
            return;
        }
        if (Completed) return;
        var seconds = Math.Clamp(delta, 0, 0.1);
        // Let the map finish appearing, and ignore brief opens between selection pages.
        _visibleSeconds += seconds;
        if (_visibleSeconds < 0.35) return;
        _elapsed += seconds * 2;
        Completed = Frame == FallingLayout.Steps;
    }

    public void Close(bool visible)
    {
        if (Started && visible) Completed = true;
        _visibleSeconds = 0;
    }
}
