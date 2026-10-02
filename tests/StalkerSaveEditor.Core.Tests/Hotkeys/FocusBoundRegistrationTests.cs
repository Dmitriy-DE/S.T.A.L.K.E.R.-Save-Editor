using Xunit;
using StalkerSaveEditor.Core.Hotkeys;

namespace StalkerSaveEditor.Core.Tests.Hotkeys;

public sealed class FocusBoundRegistrationTests
{
    [Fact]
    public void A_refused_registration_is_not_remembered_as_active_and_is_retried()
    {
        var calls = new List<bool>();
        var taken = true;
        var registration = new FocusBoundRegistration(wanted =>
        {
            calls.Add(wanted);
            return !wanted || !taken;
        }, active: true, checksBetweenRetries: 2);

        registration.Sync(false);
        Assert.False(registration.Active);

        // The game is back, but another program holds the key.
        registration.Sync(true);
        Assert.False(registration.Active);
        registration.Sync(true);
        registration.Sync(true);
        Assert.Equal([false, true], calls);

        // After the pause it is tried again; once the key is free the state follows.
        registration.Sync(true);
        Assert.Equal([false, true, true], calls);
        Assert.False(registration.Active);
        taken = false;
        registration.Sync(true);
        registration.Sync(true);
        registration.Sync(true);
        Assert.True(registration.Active);
        Assert.Equal([false, true, true, true], calls);
    }

    [Fact]
    public void Nothing_is_called_while_the_state_already_matches()
    {
        var calls = 0;
        var registration = new FocusBoundRegistration(_ => { calls++; return true; }, active: true);

        registration.Sync(true);
        registration.Sync(true);

        Assert.Equal(0, calls);
        registration.Sync(false);
        registration.Sync(false);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Losing_the_focus_during_the_retry_pause_cancels_the_retry()
    {
        var calls = new List<bool>();
        var registration = new FocusBoundRegistration(wanted => { calls.Add(wanted); return !wanted; }, active: false, checksBetweenRetries: 5);

        registration.Sync(true);
        registration.Sync(false);
        registration.Sync(true);

        Assert.Equal([true, true], calls);
    }
}
