namespace StalkerSaveEditor.Core.Hotkeys;

/// <summary>
/// Keeps "the keys are registered" equal to "the game has the focus". The state changes only after the native call
/// reported success: when another program took a key while the game was in the background, the registration is
/// retried every few checks instead of being remembered as active.
/// </summary>
internal sealed class FocusBoundRegistration(Func<bool, bool> trySet, bool active, int checksBetweenRetries = 10)
{
    private int _cooldown;

    public bool Active { get; private set; } = active;

    public void Sync(bool wanted)
    {
        if (wanted == Active)
        {
            _cooldown = 0;
            return;
        }

        if (_cooldown > 0)
        {
            _cooldown--;
            return;
        }

        if (trySet(wanted)) Active = wanted;
        else _cooldown = checksBetweenRetries;
    }
}
