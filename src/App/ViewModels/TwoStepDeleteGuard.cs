using System;
using System.Threading.Tasks;

namespace KnowledgeWeakness.App.ViewModels;

/// <summary>
/// Two-click delete confirmation state machine shared by the management pages
/// (same UX contract as the papers page delete): the first click arms the
/// guard and switches the button label to a warning; a second click within
/// <see cref="TimeoutMs"/> confirms. Switching the selected item or letting
/// the timeout lapse cancels.
/// </summary>
public sealed class TwoStepDeleteGuard
{
    public const int TimeoutMs = 5000;

    private readonly Action<bool> _onArmedChanged;
    private readonly Action? _onAutoCancelled;
    private readonly TimeSpan _timeout;
    private int _token;
    private bool _armed;

    public TwoStepDeleteGuard(Action<bool> onArmedChanged, Action? onAutoCancelled = null, TimeSpan? timeout = null)
    {
        _onArmedChanged = onArmedChanged;
        _onAutoCancelled = onAutoCancelled;
        _timeout = timeout ?? TimeSpan.FromMilliseconds(TimeoutMs);
    }

    public bool IsArmed => _armed;

    /// <summary>
    /// First call arms and returns <c>false</c> (caller shows the confirmation
    /// hint); the second call consumes the arm and returns <c>true</c>.
    /// </summary>
    public bool RequestConfirmed()
    {
        if (!_armed)
        {
            _armed = true;
            _onArmedChanged(true);
            var token = ++_token;
            _ = AutoCancelAsync(token);
            return false;
        }

        _token++;
        Disarm();
        return true;
    }

    /// <summary>Cancel any pending confirmation (e.g. selection changed).</summary>
    public void Cancel()
    {
        _token++;
        Disarm();
    }

    private async Task AutoCancelAsync(int token)
    {
        await Task.Delay(_timeout);
        if (token != _token || !_armed) return;
        Disarm();
        _onAutoCancelled?.Invoke();
    }

    private void Disarm()
    {
        if (!_armed) return;
        _armed = false;
        _onArmedChanged(false);
    }
}
