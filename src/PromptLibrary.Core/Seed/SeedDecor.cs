namespace PromptLibrary.Core.Seed;

/// <summary>Default emoji for the seed categories and tags for the seed prompts (personal and team).</summary>
internal static class SeedDecor
{
    private static readonly Dictionary<string, string> CategoryEmoji = new()
    {
        ["Marketing"] = "\U0001F4E2",           // loudspeaker (the megaphone was dim on dark tiles)
        ["Website design"] = "\U0001F3A8",      // palette
        ["Productivity"] = "✅",            // check mark
        ["Kids & family"] = "\U0001F9F8",       // teddy bear
        ["Recipes & cooking"] = "\U0001F373",   // cooking
        ["Business"] = "\U0001F4BC",            // briefcase
        ["Writing & email"] = "✍️",   // writing hand (the white envelope faded into light tiles)
        ["Learning"] = "\U0001F4DA",            // books (the black cap faded into dark tiles)
        ["Health & fitness"] = "\U0001F4AA",    // flexed biceps
        ["Travel"] = "✈️",            // aeroplane
        ["Money"] = "\U0001F4B0",               // money bag
        ["Career"] = "\U0001F454",              // necktie
        ["Coding"] = "\U0001F4BB",              // laptop
        ["Sales"] = "\U0001F91D",               // handshake
        ["Customer support"] = "\U0001F6DF",    // ring buoy (the white speech bubble faded into light tiles)
        ["Meetings & management"] = "\U0001F4C5", // calendar
        ["HR & people"] = "\U0001F465",         // people
        ["Operations & finance"] = "\U0001F4CA", // bar chart
    };

    private static readonly Dictionary<string, string[]> PromptTags = new()
    {
        ["Social media content calendar"] = ["social", "planning", "content"],
        ["Ad copy in five angles"] = ["ads", "copywriting"],
        ["Ideal customer profile"] = ["audience", "research"],
        ["Plan a landing page"] = ["website", "layout", "copy"],
        ["Review my homepage"] = ["website", "review"],
        ["Colours and fonts"] = ["branding", "design"],
        ["Plan my week"] = ["planning", "time"],
        ["Break down a big task"] = ["planning", "tasks"],
        ["Turn notes into actions"] = ["notes", "tasks"],
        ["Party games for kids"] = ["kids", "party"],
        ["Rainy-day activities"] = ["kids", "activities"],
        ["Personalised bedtime story"] = ["kids", "story"],
        ["Cook with what I have"] = ["cooking", "ingredients"],
        ["Weekly meal plan"] = ["meals", "planning", "shopping"],
        ["Adapt a recipe"] = ["cooking", "diet"],
        ["Test a business idea"] = ["strategy", "ideas"],
        ["SWOT analysis"] = ["strategy", "analysis"],
        ["Set my prices"] = ["pricing", "strategy"],
        ["Reply to an email"] = ["email", "reply"],
        ["Make this sound better"] = ["editing", "tone"],
        ["Summarise this"] = ["summary", "reading"],
        ["Explain it simply"] = ["explain", "study"],
        ["Make me a study plan"] = ["study", "planning"],
        ["Quiz me"] = ["quiz", "practice"],
        ["Beginner workout plan"] = ["workout", "fitness"],
        ["Build a healthy habit"] = ["habits", "wellbeing"],
        ["Desk stretch routine"] = ["stretch", "desk"],
        ["Plan a trip itinerary"] = ["trip", "planning"],
        ["Packing list"] = ["packing", "trip"],
        ["Local tips and phrases"] = ["language", "culture"],
        ["Make a monthly budget"] = ["budget", "saving"],
        ["Savings goal plan"] = ["saving", "goals"],
        ["Help me decide on a purchase"] = ["shopping", "compare"],
        ["Tailor my CV to a job"] = ["cv", "jobs"],
        ["Interview practice"] = ["interview", "practice"],
        ["Ask for a raise"] = ["salary", "negotiation"],
        ["Explain this code"] = ["code", "explain"],
        ["Fix this error"] = ["debug", "code"],
        ["Review my code"] = ["code", "review"],

        // Team seed
        ["Discovery call questions"] = ["sales", "calls"],
        ["Follow-up email after a meeting"] = ["email", "follow-up"],
        ["Handle a sales objection"] = ["sales", "objections"],
        ["Proposal summary for decision makers"] = ["proposal", "summary"],
        ["Reply to an upset customer"] = ["support", "email"],
        ["Troubleshooting steps for a customer"] = ["support", "howto"],
        ["Refund or escalation response"] = ["support", "refund"],
        ["FAQ answer from our policy"] = ["faq", "policy"],
        ["Campaign brief"] = ["campaign", "brief"],
        ["Product launch announcement"] = ["launch", "announcement"],
        ["Case study draft"] = ["case-study", "customers"],
        ["Rewrite in our brand voice"] = ["brand", "editing"],
        ["Meeting agenda"] = ["meeting", "agenda"],
        ["Minutes and action items"] = ["meeting", "notes"],
        ["One-to-one prep"] = ["meeting", "management"],
        ["Project status update"] = ["project", "update"],
        ["Job description"] = ["hiring", "jobs"],
        ["Interview questions and scoring guide"] = ["hiring", "interview"],
        ["New starter onboarding plan"] = ["onboarding", "hiring"],
        ["Policy summary for staff"] = ["policy", "summary"],
        ["Process write-up (SOP)"] = ["process", "sop"],
        ["Compare suppliers"] = ["suppliers", "compare"],
        ["Explain a budget variance"] = ["finance", "budget"],
        ["Risk register entry"] = ["risk", "planning"],
    };

    public static string? Emoji(string categoryName) => CategoryEmoji.GetValueOrDefault(categoryName);

    public static List<string> Tags(string promptTitle) => [.. PromptTags.GetValueOrDefault(promptTitle) ?? []];
}
