using PromptLibrary.Core.Team;

namespace PromptLibrary.App.ViewModels;

/// <summary>One row of the team check: the issue's message and a kind icon (a Segoe Fluent glyph).</summary>
public sealed record TeamCheckRow(HealthIssue Issue)
{
    public string Message => Issue.Message;

    public string Icon => Issue.Kind switch
    {
        HealthIssueKind.DuplicateTitle or HealthIssueKind.DuplicateText => "", // Copy
        HealthIssueKind.BrokenField => "",                                    // Error badge
        HealthIssueKind.EmptyCategory => "",                                  // Folder
        HealthIssueKind.MixedChoices => "",                                   // List
        HealthIssueKind.NoDescription => "",                                  // Description
        _ => "",                                                              // Warning
    };
}

/// <summary>The "Check the team library" dialog: one row per problem; choosing a row asks to go to it.</summary>
public sealed class TeamCheckViewModel(IReadOnlyList<HealthIssue> issues)
{
    public IReadOnlyList<TeamCheckRow> Rows { get; } = issues.Select(i => new TeamCheckRow(i)).ToList();

    public bool IsEmpty => Rows.Count == 0;

    public bool HasRows => Rows.Count > 0;

    public string EmptyText => "Everything looks good.";

    /// <summary>The issue the user chose to go to; null when the dialog was just closed.</summary>
    public HealthIssue? Chosen { get; private set; }

    public void Choose(TeamCheckRow row) => Chosen = row.Issue;
}
