using PromptLibrary.Core.Models;
using PromptLibrary.Core.Team;

namespace PromptLibrary.Core.Tests.Team;

public class TeamHealthCheckTests
{
    private static Category Cat(string name) => new() { Id = Guid.NewGuid(), Name = name };

    private static Prompt P(Category category, string title, string text, string description = "A description") => new()
    {
        Id = Guid.NewGuid(),
        Title = title,
        Description = description,
        CategoryId = category.Id,
        Text = text,
    };

    private static Library Lib(IEnumerable<Category> categories, IEnumerable<Prompt> prompts) =>
        new() { Categories = categories.ToList(), Prompts = prompts.ToList() };

    [Fact]
    public void CleanLibrary_NoIssues()
    {
        var sales = Cat("Sales");
        var support = Cat("Support");
        var library = Lib([sales, support],
        [
            P(sales, "Discovery call", "Write {task} for {reader}."),
            P(sales, "Follow-up email", "Draft an email about {topic}."),
            P(support, "Refund reply", "Reply to {customer} about a refund."),
        ]);

        Assert.Empty(TeamHealthCheck.Run(library));
    }

    [Fact]
    public void DuplicateTitle_SameCategory_CaseAndSpaceInsensitive_OneIssuePerExtra()
    {
        var sales = Cat("Sales");
        var first = P(sales, "Follow-up email", "Text one {a}.");
        var copy1 = P(sales, "  follow-UP email ", "Text two {a}.");
        var copy2 = P(sales, "Follow-up Email", "Text three {a}.");
        var library = Lib([sales], [first, copy1, copy2]);

        var issues = TeamHealthCheck.Run(library).Where(i => i.Kind == HealthIssueKind.DuplicateTitle).ToList();

        Assert.Equal(2, issues.Count);
        Assert.Equal([copy1.Id, copy2.Id], issues.Select(i => i.PromptId).ToArray());
        Assert.All(issues, i => Assert.Equal(sales.Id, i.CategoryId));
        Assert.Equal(
        [
            "\"  follow-UP email \" appears more than once in Sales.",
            "\"Follow-up Email\" appears more than once in Sales.",
        ], issues.Select(i => i.Message).ToArray());
    }

    [Fact]
    public void DuplicateTitle_DifferentCategories_NoIssue()
    {
        var sales = Cat("Sales");
        var support = Cat("Support");
        var library = Lib([sales, support],
            [P(sales, "Summary", "Text one {a}."), P(support, "Summary", "Text two {a}.")]);

        Assert.DoesNotContain(TeamHealthCheck.Run(library), i => i.Kind == HealthIssueKind.DuplicateTitle);
    }

    [Fact]
    public void DuplicateText_WhitespaceDifferences_OneIssue()
    {
        var sales = Cat("Sales");
        var support = Cat("Support");
        var original = P(sales, "Thank you note", "Thank the customer for  their\n order.");
        var copy = P(support, "Thanks", "Thank the customer for their order.");
        var library = Lib([sales, support], [original, copy]);

        var issue = Assert.Single(TeamHealthCheck.Run(library), i => i.Kind == HealthIssueKind.DuplicateText);
        Assert.Equal(copy.Id, issue.PromptId);
        Assert.Null(issue.CategoryId);
        Assert.Equal("\"Thanks\" has the same text as \"Thank you note\".", issue.Message);
    }

    [Fact]
    public void BrokenField_UnclosedOrEmpty_Flagged_EscapedAndClosedNot()
    {
        var sales = Cat("Sales");
        var library = Lib([sales],
        [
            P(sales, "Empty braces", "Do {} now."),
            P(sales, "Unclosed", "Write about {topic and stop here."),
            P(sales, "Closed on next line", "Write about {topic\nand more}."),
            P(sales, "Escaped", "Literal {{topic and {{ more."),
            P(sales, "Closed", "Write about {topic}."),
        ]);

        var issues = TeamHealthCheck.Run(library).Where(i => i.Kind == HealthIssueKind.BrokenField).ToList();

        Assert.Equal(
        [
            "\"Closed on next line\" has a field that isn't closed.",
            "\"Empty braces\" has a field that isn't closed.",
            "\"Unclosed\" has a field that isn't closed.",
        ], issues.Select(i => i.Message).ToArray());
    }

    [Fact]
    public void EmptyCategory_ReportedWithCategoryIdOnly()
    {
        var sales = Cat("Sales");
        var empty = Cat("Legal");
        var library = Lib([sales, empty], [P(sales, "Discovery call", "Write {task}.")]);

        var issue = Assert.Single(TeamHealthCheck.Run(library), i => i.Kind == HealthIssueKind.EmptyCategory);
        Assert.Null(issue.PromptId);
        Assert.Equal(empty.Id, issue.CategoryId);
        Assert.Equal("Legal has no prompts.", issue.Message);
    }

    [Fact]
    public void MixedChoices_SharedOptionsButDifferentLists_OneIssue()
    {
        var sales = Cat("Sales");
        var support = Cat("Support");
        var first = P(sales, "Priority A", "Rate it {low / high}.");
        var second = P(support, "Priority B", "Rate it {low / medium / high}.");
        var library = Lib([sales, support], [first, second]);

        var issue = Assert.Single(TeamHealthCheck.Run(library), i => i.Kind == HealthIssueKind.MixedChoices);
        Assert.Equal(first.Id, issue.PromptId);
        Assert.Equal("The choices {low / high} and {low / medium / high} are almost the same. Use one list everywhere.", issue.Message);
    }

    [Fact]
    public void MixedChoices_EqualSetsOrOneSharedOption_NoIssue()
    {
        var sales = Cat("Sales");
        var library = Lib([sales],
        [
            P(sales, "Same set", "Pick {Yes / No}."),
            P(sales, "Same set reordered", "Pick {no / yes}."),
            P(sales, "One shared", "Pick {yes / maybe}."),
        ]);

        Assert.DoesNotContain(TeamHealthCheck.Run(library), i => i.Kind == HealthIssueKind.MixedChoices);
    }

    [Fact]
    public void NoDescription_BlankDescriptionFlagged()
    {
        var sales = Cat("Sales");
        var blank = P(sales, "Undocumented", "Write {task}.", description: "   ");
        var library = Lib([sales], [blank]);

        var issue = Assert.Single(TeamHealthCheck.Run(library), i => i.Kind == HealthIssueKind.NoDescription);
        Assert.Equal(blank.Id, issue.PromptId);
        Assert.Equal("\"Undocumented\" has no description.", issue.Message);
    }

    [Fact]
    public void LongTitle_OverEightyFlagged_EightyNot()
    {
        var sales = Cat("Sales");
        var exactly80 = new string('a', 80);
        var long81 = new string('b', 81);
        var library = Lib([sales], [P(sales, exactly80, "Text one {a}."), P(sales, long81, "Text two {a}.")]);

        var issue = Assert.Single(TeamHealthCheck.Run(library), i => i.Kind == HealthIssueKind.LongTitle);
        Assert.Equal($"\"{long81}\" has a long title (over 80 characters).", issue.Message);
    }

    [Fact]
    public void Ordering_KindOrder_ThenCategoryThenTitle()
    {
        var bSales = Cat("B Sales");
        var aSupport = Cat("A Support");
        var empty = Cat("Z Empty");
        var longTitle = new string('x', 81);
        var library = Lib([bSales, aSupport, empty],
        [
            P(bSales, longTitle, "Text one {a}.", description: ""),
            P(bSales, "Zeta", "Text two {a}.", description: ""),
            P(bSales, "Alpha", "Text three {a}.", description: ""),
            P(bSales, "Dup", "Text four {a}."),
            P(bSales, "dup", "Text five {a}."),
            P(aSupport, "Dup", "Text six {a}."),
            P(aSupport, "Dup", "Text seven {a}."),
            P(aSupport, "Broken", "Broken {x"),
        ]);

        var issues = TeamHealthCheck.Run(library);

        Assert.Equal(
        [
            HealthIssueKind.DuplicateTitle, HealthIssueKind.DuplicateTitle,
            HealthIssueKind.BrokenField,
            HealthIssueKind.EmptyCategory,
            HealthIssueKind.NoDescription, HealthIssueKind.NoDescription, HealthIssueKind.NoDescription,
            HealthIssueKind.LongTitle,
        ], issues.Select(i => i.Kind).ToArray());

        Assert.Equal(
        [
            "\"Dup\" appears more than once in A Support.",
            "\"dup\" appears more than once in B Sales.",
        ], issues.Where(i => i.Kind == HealthIssueKind.DuplicateTitle).Select(i => i.Message).ToArray());

        Assert.Equal(
        [
            "\"Alpha\" has no description.",
            $"\"{longTitle}\" has no description.",
            "\"Zeta\" has no description.",
        ], issues.Where(i => i.Kind == HealthIssueKind.NoDescription).Select(i => i.Message).ToArray());
    }
}
