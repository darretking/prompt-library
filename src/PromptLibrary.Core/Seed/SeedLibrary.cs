using PromptLibrary.Core.Models;

namespace PromptLibrary.Core.Seed;

/// <summary>The example prompts a brand-new library starts with.</summary>
public static class SeedLibrary
{
    public static Library Create(DateTimeOffset now)
    {
        var cCoding = new Category { Id = Guid.NewGuid(), Name = "Coding" };
        var cTesting = new Category { Id = Guid.NewGuid(), Name = "Testing" };
        var cUserInterface = new Category { Id = Guid.NewGuid(), Name = "User Interface" };
        var cMarketing = new Category { Id = Guid.NewGuid(), Name = "Marketing" };
        var cDocumentation = new Category { Id = Guid.NewGuid(), Name = "Documentation" };
        var cCodeReview = new Category { Id = Guid.NewGuid(), Name = "Code Review" };

        Prompt P(string title, string description, Category category, string text) => new()
        {
            Id = Guid.NewGuid(),
            Title = title,
            Description = description,
            CategoryId = category.Id,
            Text = text,
            CreatedAt = now,
            UpdatedAt = now,
        };

        return new Library
        {
            Categories = [cCoding, cTesting, cUserInterface, cMarketing, cDocumentation, cCodeReview],
            Prompts =
            [
                P("Explain this code", "Plain-English walkthrough of unfamiliar code", cCoding,
                    """"
                    Explain what the following code does, step by step, as if I'm a mid-level developer new to this codebase. Call out anything surprising, risky, or non-obvious. End with a one-sentence summary.
                    """"),
                P("Refactor for readability", "Clean up without changing behaviour", cCoding,
                    """"
                    Refactor the code below for readability. Rules:
                    - Do NOT change behaviour or public signatures
                    - Prefer small, well-named functions
                    - Remove dead code and duplicated logic
                    Show the refactored code, then a bullet list of what you changed and why.
                    """"),
                P("Fix this error", "", cCoding,
                    """"
                    I'm getting this error:

                    {paste error here}

                    Find the most likely root cause before suggesting a fix. Ask me for the relevant file if you need it. Don't guess.
                    """"),
                P("Write unit tests", "xUnit tests covering happy path + edge cases", cTesting,
                    """"
                    Write xUnit tests for the class below. Cover: the happy path, null/empty inputs, boundary values, and at least one failure case. Use the Arrange / Act / Assert pattern and descriptive test names like Method_Scenario_ExpectedResult.
                    """"),
                P("Find edge cases", "Brainstorm what could break", cTesting,
                    """"
                    List every edge case you can think of for this feature: "{feature description}". Group them by: input, timing/concurrency, permissions, and data size. Mark the 3 most likely to cause a real bug.
                    """"),
                P("Critique this screen", "Usability review of a screenshot", cUserInterface,
                    """"
                    Review the attached screenshot as a senior UX designer. Comment on: visual hierarchy, spacing, contrast/accessibility, and anything a first-time user would find confusing. Rank issues by impact — highest first.
                    """"),
                P("Write empty-state copy", "Friendly text for \"nothing here yet\" screens", cUserInterface,
                    """"
                    Write 3 options for empty-state text for a screen that shows "{what the list contains}". Each option: a short heading (max 6 words) + one helpful sentence + a button label. Tone: friendly, not cute. 🙂
                    """"),
                P("Product announcement post", "LinkedIn launch post", cMarketing,
                    """"
                    Write a LinkedIn post announcing "{product name}". Audience: developers and tech leads. Lead with the problem it solves, not the features. Max 150 words, no hashtag spam (2 max), end with a clear call to action.
                    """"),
                P("Landing page headline ideas", "10 headline + subheadline pairs", cMarketing,
                    """"
                    Give me 10 headline + subheadline pairs for a landing page for {product}. Mix styles: benefit-led, problem-led, curiosity, and "how it works". Keep headlines under 10 words.
                    """"),
                P("Write a README", "Project README from scratch", cDocumentation,
                    """""
                    Write a README.md for this project. Sections: what it is (2 sentences), screenshot placeholder, install, quick start, configuration, FAQ, license. Use code blocks like:

                    ```bash
                    dotnet run
                    ```

                    Keep it scannable — short paragraphs, lots of headings.
                    """""),
                P("Summarise changes for a release", "Turn commit list into release notes", cDocumentation,
                    """"
                    Turn the commit messages below into user-facing release notes. Group into "New", "Improved", "Fixed". Drop internal-only changes (refactors, CI tweaks). Write for end users, not developers.
                    """"),
                P("Review my pull request", "Thorough PR review with priorities", cCodeReview,
                    """"
                    Review this diff like a strict but fair senior engineer. For each issue give: file + line, the problem, why it matters, and a suggested fix. Label each as "must fix", "should fix", or "nit". Don't comment on formatting — we have a linter for that. Check especially for: null handling, "off-by-one" errors, and anything that's 'clever' but hard to read.
                    """"),
            ],
        };
    }
}
