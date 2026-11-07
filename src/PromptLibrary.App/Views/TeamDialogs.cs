using System.Windows;
using PromptLibrary.App.ViewModels;

namespace PromptLibrary.App.Views;

/// <summary>Opens the modal team dialogs for Settings and the setup guide, wired to the host's team service.</summary>
public static class TeamDialogs
{
    /// <summary>Join a team library. True when one was joined.</summary>
    public static bool Join(Window? owner, AppHost host)
    {
        if (host.Team is not { } service) return false;
        var viewModel = new TeamJoinViewModel(service.JoinAsync, ProviderOf);
        return Show(new TeamJoinDialog(viewModel), owner);
    }

    /// <summary>Start a team library. True when one was started (or the existing one joined).</summary>
    public static bool Start(Window? owner, AppHost host)
    {
        if (host.Team is not { } service) return false;
        var viewModel = new TeamStartViewModel(host.Library.Categories.ToList(), service.StartAsync, service.JoinAsync,
            ProviderOf);
        return Show(new TeamStartDialog(viewModel), owner);
    }

    /// <summary>
    /// Leave team library: first "Save changes?" for a team prompt being edited in the main window (Cancel stays; a
    /// team save still writing asks again once it has gone through), then asks to leave, then leaves. True when it did.
    /// </summary>
    public static bool Leave(Window? owner, AppHost host, TeamConnectionViewModel team)
    {
        if (!team.IsConnected) return false;
        if (!host.ConfirmLeaveTeam(() => Leave(owner is { IsVisible: true } ? owner : null, host, team))) return false;
        if (!ConfirmDialog.Ask(owner, team.LeaveQuestion, "Leave", "Stay")) return false;
        team.Leave();
        return true;
    }

    private static string? ProviderOf(string path) => TeamDialogText.ProviderOf(path, AppHost.DetectCloudFolders);

    private static bool Show(Window dialog, Window? owner)
    {
        if (owner is { IsVisible: true })
        {
            dialog.Owner = owner;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            dialog.ShowInTaskbar = true;
        }
        return dialog.ShowDialog() == true;
    }
}
