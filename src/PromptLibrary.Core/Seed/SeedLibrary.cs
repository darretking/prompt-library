using PromptLibrary.Core.Models;

namespace PromptLibrary.Core.Seed;

/// <summary>
/// The example prompts a brand-new library starts with: a varied, everyday set (13 categories, 3 prompts each).
/// Each prompt follows the same pattern: a role, the user's details as {placeholders}, a clear task with the
/// reason behind it, a set output format, and permission to ask questions when details are missing.
/// </summary>
public static class SeedLibrary
{
    /// <summary>
    /// Adds the example prompts to <paramref name="target"/>, skipping any whose title is already there (compared
    /// ignoring case). Categories are matched by name, or created when missing. Returns how many prompts were added.
    /// </summary>
    public static int AddMissing(Library target, DateTimeOffset now)
    {
        var seed = Create(now);
        var titles = new HashSet<string>(target.Prompts.Select(p => p.Title.Trim()), StringComparer.OrdinalIgnoreCase);
        var added = 0;
        foreach (var category in seed.Categories)
        {
            var fresh = seed.Prompts.Where(p => p.CategoryId == category.Id && !titles.Contains(p.Title.Trim())).ToList();
            if (fresh.Count == 0) continue;

            var home = target.Categories.FirstOrDefault(c => string.Equals(c.Name, category.Name, StringComparison.OrdinalIgnoreCase));
            if (home is null)
            {
                target.Categories.Add(category);
                home = category;
            }
            foreach (var prompt in fresh)
            {
                prompt.CategoryId = home.Id;
                target.Prompts.Add(prompt);
                added++;
            }
        }
        return added;
    }

    public static Library Create(DateTimeOffset now)
    {
        Category C(string name) => new() { Id = Guid.NewGuid(), Name = name, Emoji = SeedDecor.Emoji(name) };

        var marketing = C("Marketing");
        var website = C("Website design");
        var productivity = C("Productivity");
        var kids = C("Kids & family");
        var recipes = C("Recipes & cooking");
        var business = C("Business");
        var writing = C("Writing & email");
        var learning = C("Learning");
        var health = C("Health & fitness");
        var travel = C("Travel");
        var money = C("Money");
        var career = C("Career");
        var coding = C("Coding");

        Prompt P(string title, string description, Category category, string text) => new()
        {
            Id = Guid.NewGuid(),
            Title = title,
            Description = description,
            CategoryId = category.Id,
            Text = text,
            Tags = SeedDecor.Tags(title),
            CreatedAt = now,
            UpdatedAt = now,
        };

        return new Library
        {
            Categories = [marketing, website, productivity, kids, recipes, business, writing, learning, health, travel, money, career, coding],
            Prompts =
            [
                // Marketing
                P("Social media content calendar", "Two weeks of posts, ready to schedule", marketing,
                    """"
                    You're a social media manager for {business or brand}, which sells {product or service} to {audience}.

                    Plan 2 weeks of posts for {platforms, e.g. Instagram and LinkedIn}. Mix the content: about 40% helpful tips, 30% behind-the-scenes or stories, 20% customer proof, 10% direct offers.

                    Give me a table with: day, platform, post idea, the caption (ready to post), a visual idea, and 3–5 hashtags. Keep captions in a {tone, e.g. warm and practical} voice.
                    """"),
                P("Ad copy in five angles", "Headlines and body text to A/B test", marketing,
                    """"
                    Write ad copy for {product or offer}. The audience is {who it's for} and the main thing they want is {their goal or pain point}.

                    Give me 5 versions, each from a different angle: the problem, the result, social proof, curiosity, and the offer. For each: a headline (max 40 characters), body text (max 125 characters) and a call to action.

                    Then tell me which 2 you'd test first and why.
                    """"),
                P("Ideal customer profile", "Who you're really selling to", marketing,
                    """"
                    Act as a marketing strategist. My business: {what you sell and to whom}.

                    Build a profile of my ideal customer: who they are, a typical day, what they're trying to achieve, what frustrates them, what they've already tried, the words they'd use to describe the problem, and what would make them trust a new brand.

                    Finish with 5 messages that would grab this person's attention. If you need more detail about my business first, ask me up to 3 questions.
                    """"),

                // Website design
                P("Plan a landing page", "Section-by-section layout and copy", website,
                    """"
                    You're a conversion-focused web designer. Plan a landing page for {product or service}. Visitors are {audience} and the one action I want them to take is {goal, e.g. book a call}.

                    Give me the page section by section, top to bottom. For each section: its purpose, the headline, the supporting copy, and what image or element goes there. Include a hero, the benefits, how it works, social proof, an FAQ and a final call to action.

                    Keep the copy short and specific, written for someone who's skimming on a phone.
                    """"),
                P("Review my homepage", "Clarity, trust and conversion check", website,
                    """"
                    Review my homepage as a UX and conversion expert. Here's the copy and a description of the layout:

                    {paste your homepage text, or describe it}

                    Answer: can a first-time visitor tell within 5 seconds what this is, who it's for and what to do next? Then list the 10 most important improvements, ranked by impact, each with a concrete rewrite or change. Point out anything that hurts trust.
                    """"),
                P("Colours and fonts", "A brand palette and type pairing", website,
                    """"
                    Suggest a visual style for the website of {business}, a {type of business} whose personality is {3 words, e.g. calm, premium, friendly}.

                    Give me: a colour palette of 5 colours with hex codes and where each one is used, a heading font and a body font from Google Fonts, and button and link styles. Check that text on the main background meets WCAG AA contrast, and tell me if any pair doesn't.

                    Explain in 2–3 sentences why this style fits the brand.
                    """"),

                // Productivity
                P("Plan my week", "Priorities, time blocks and what to drop", productivity,
                    """"
                    Act as my productivity coach. Here's everything on my plate this week:

                    {list tasks, deadlines, meetings and personal commitments}

                    My working hours are {hours} and my energy is best {e.g. in the morning}.

                    Sort the tasks by urgency and importance, then build a day-by-day plan with time blocks, putting deep work in my best hours. Flag anything that won't fit, and suggest what to delegate, postpone or drop. Keep at least 30 minutes of buffer each day.
                    """"),
                P("Break down a big task", "From overwhelming to first step", productivity,
                    """"
                    I keep putting off this task: {describe the task}. It's due {deadline}.

                    Break it into small steps that each take under 30 minutes, in the order I should do them. Mark the very first step as something I could start in the next 5 minutes. Add a rough time estimate per step and point out where I'm likely to get stuck, with a tip for each.
                    """"),
                P("Turn notes into actions", "Messy notes to a clear to-do list", productivity,
                    """"
                    Here are my rough notes from {a meeting, call or brainstorm}:

                    {paste notes}

                    Turn them into: 1) a 3-sentence summary, 2) the decisions made, 3) a to-do list with an owner and a due date for each item (write "unassigned" where it's unclear), and 4) open questions to follow up. Use only what's in the notes; don't invent details.
                    """"),

                // Kids & family
                P("Party games for kids", "Games for the right age and space", kids,
                    """"
                    Plan party games for {number} kids aged {ages}, {indoors or outdoors}, for about {duration}. The theme is {theme, or "none"}. 🎉

                    Suggest 6 games with: the name, what you need, how to play in 3–4 simple steps, and roughly how long it takes. Include a mix of active and calm games, at least one that needs no equipment, and one that works if the kids are shy. Keep everything safe and inclusive, with no elimination games that leave a child sitting out for long.
                    """"),
                P("Rainy-day activities", "Screen-free ideas with what's at home", kids,
                    """"
                    My kids are {ages} and we're stuck inside today. We have {materials at home, e.g. paper, tape, cardboard boxes, crayons}.

                    Give me 8 screen-free activities: 3 creative, 3 active, and 2 quiet ones for winding down. For each, list what's needed, the setup time, and how to make it a bit harder for the older child or easier for the younger one.
                    """"),
                P("Personalised bedtime story", "A calming story starring your child", kids,
                    """"
                    Write a bedtime story for {child's name}, age {age}, who loves {favourite things}. Make {child's name} the hero.

                    Keep it about {5} minutes when read aloud, gentle and calming, with a cosy ending that leads to sleep. Gently include this lesson: {e.g. being brave, sharing, trying new things}. Use simple words, a little repetition children can join in with, and no scary parts.
                    """"),

                // Recipes & cooking
                P("Cook with what I have", "Recipes from the ingredients in your kitchen", recipes,
                    """"
                    I have these ingredients: {list what's in your fridge and cupboard}. I also have basics like oil, salt, pepper and {other staples}.

                    Suggest 3 dishes I can make, ready in under {time} minutes, for {number} people. For each: the name, why it works with what I have, the ingredients with amounts, and numbered steps. Say if a dish needs one extra item I'd have to buy. Dietary needs: {none, vegetarian, gluten-free…}.
                    """"),
                P("Weekly meal plan", "Seven days of dinners plus a shopping list", recipes,
                    """"
                    Act as a practical meal planner. Plan {7} dinners for {number of people}. Dietary needs: {any}. Budget: {budget}. Weeknight cooking time: {max minutes}.

                    Reuse ingredients across meals to cut waste, include one batch-cook meal that gives leftovers, and keep the variety high. Give me a table for the week, then one shopping list grouped by supermarket section, with quantities.
                    """"),
                P("Adapt a recipe", "Change servings, diet or ingredients", recipes,
                    """"
                    Here's a recipe:

                    {paste recipe}

                    Adapt it to {what you need, e.g. serve 6, make it dairy-free, use an air fryer, cut the sugar}. Give me the full updated recipe with new amounts and steps, and a short list of what changed and how it affects the taste or texture. Mention any swap that's risky and a safer option.
                    """"),

                // Business
                P("Test a business idea", "Honest pros, risks and next steps", business,
                    """"
                    Act as an experienced startup advisor. Be honest, not just encouraging.

                    My idea: {describe the idea}. Target customers: {who}. How it makes money: {pricing or business model}.

                    Assess: the problem and how painful it is, who else solves it, what's different about mine, the biggest risks, and what I'd need to believe for this to work. Then suggest 3 cheap experiments I could run in the next 2 weeks to test it, with what result would mean "keep going".
                    """"),
                P("SWOT analysis", "Strengths, weaknesses, opportunities, threats", business,
                    """"
                    Do a SWOT analysis for {business or project} in {industry or market}. Here's what I know: {key facts, numbers, competitors}.

                    Put it in a 2×2 table with 4–6 specific points per box. Then give me the 3 most important actions that use a strength to grab an opportunity, or that protect against a threat. Mark anything that's an assumption rather than a fact from what I told you.
                    """"),
                P("Set my prices", "Pricing options with reasoning", business,
                    """"
                    Help me price {product or service}. My costs are {costs per unit or per hour}, competitors charge {competitor prices}, and my customers are {who they are and what they value}.

                    Suggest 3 pricing approaches (for example good-better-best tiers, value-based, or a subscription), with example prices for each, the pros and cons, and who each one suits. Recommend one and explain how I could test it before committing.
                    """"),

                // Writing & email
                P("Reply to an email", "A clear, polite reply in your voice", writing,
                    """"
                    Help me reply to this email:

                    {paste the email}

                    What I want to say: {your main points, e.g. agree to the meeting but move it to Thursday}. Tone: {friendly / formal / firm but polite}.

                    Write the reply in under {150} words, with a clear subject line if it's a new thread. Then give me a shorter alternative in 2–3 sentences.
                    """"),
                P("Make this sound better", "Rewrite for clarity and tone", writing,
                    """"
                    Rewrite the text below so it's clear, confident and easy to read, for {audience}. Keep my meaning and any facts exactly as they are.

                    {paste your text}

                    Give me the rewrite first, then a short list of the main changes you made and why. If anything in the original is unclear, ask me rather than guessing.
                    """"),
                P("Summarise this", "The key points from something long", writing,
                    """"
                    Summarise the text below for someone who has 2 minutes.

                    {paste text, article or transcript}

                    Give me: a one-sentence summary, the 5 key points as bullets, anything I need to act on, and any numbers or dates that matter. Stick to what the text says, and point out anything that seems to be missing or contradictory.
                    """"),

                // Learning
                P("Explain it simply", "Understand any topic step by step", learning,
                    """"
                    Explain {topic} to me as if I'm a curious beginner. I already know {what you know, or "nothing"}.

                    Start with a one-sentence explanation in plain words, then build up in 3–4 steps, using an everyday analogy. Point out the most common misunderstanding. Finish with 3 quick questions to check I've understood, and give the answers after a "Answers" heading so I can test myself first.
                    """"),
                P("Make me a study plan", "A realistic plan to learn something new", learning,
                    """"
                    I want to learn {skill or subject} so that I can {goal}. I can spend {hours per week} for {number of weeks} weeks. My current level: {beginner / some experience}.

                    Build a week-by-week plan with what to learn, how to practise it (not just read), and a small project or milestone each week. Suggest free resources where you can, and build in review sessions so I don't forget earlier material.
                    """"),
                P("Quiz me", "Active practice with feedback", learning,
                    """"
                    Quiz me on {topic or chapter}. Level: {beginner / intermediate / exam prep}.

                    Ask me one question at a time and wait for my answer. After each answer, tell me if I'm right, explain briefly, and adjust the difficulty: harder when I get it right, easier with a hint when I don't. After 10 questions, give me my score and the 3 areas I should review.
                    """"),

                // Health & fitness
                P("Beginner workout plan", "A safe plan for your goal and schedule", health,
                    """"
                    Act as a friendly personal trainer. Create a {number}-week workout plan for me. Goal: {e.g. get stronger, lose weight, run 5 km}. Fitness level: {beginner / returning}. Time: {days per week} days, {minutes} minutes each. Equipment: {none / dumbbells / gym}.

                    For each session list the exercises, sets, reps or time, and rest. Include a warm-up and cool-down, an easier version of every exercise, and how to progress week by week. Add a short note to check with a doctor first if I have health conditions or injuries: {any to mention}.
                    """"),
                P("Build a healthy habit", "Small steps that actually stick", health,
                    """"
                    I want to build this habit: {e.g. drink more water, sleep by 11, walk daily}. What's stopped me before: {your obstacles}.

                    Design a 30-day plan that starts very small and grows gradually. Include: the exact cue and routine, how to track it, what to do on a bad day so I don't give up, and one small reward at each milestone. Keep it realistic for a busy person.
                    """"),
                P("Desk stretch routine", "Ten minutes for neck, back and wrists", health,
                    """"
                    I sit at a desk for about {hours} hours a day and feel stiff in my {neck, shoulders, lower back, wrists}.

                    Give me a 10-minute stretch routine I can do at my desk or next to it, with no equipment. For each stretch: how to do it in 2–3 steps, how long to hold it, and what I should feel. Add 3 quick micro-breaks I can do every hour. Keep everything gentle, and tell me to stop if anything hurts.
                    """"),

                // Travel
                P("Plan a trip itinerary", "Day by day, at your pace and budget", travel,
                    """"
                    Plan a {number}-day trip to {destination} for {who's going}. We like {interests, e.g. food, history, hiking} and prefer a {relaxed / packed} pace. Budget: {budget}. We're staying in {area or "not booked yet"}.

                    Give me a day-by-day plan with morning, afternoon and evening, grouping places that are close together. Include one local food to try each day, rough costs, and how to get around. Mark which things need booking ahead, and suggest a rainy-day backup.
                    """"),
                P("Packing list", "Everything you need, nothing you don't", travel,
                    """"
                    Make a packing list for {number} days in {destination} in {month}. Activities: {what you'll do}. Luggage: {carry-on only / checked bag}. Travelling with: {e.g. kids, nobody}.

                    Group the list by category (clothes, toiletries, documents, tech, health, extras), with quantities. Point out anything specific to this destination and season, and the things people most often forget.
                    """"),
                P("Local tips and phrases", "Customs and words to know before you go", travel,
                    """"
                    I'm visiting {country or city} for the first time. Tell me what I should know: customs and etiquette, tipping, dress, how to stay safe, and common tourist mistakes.

                    Then give me 15 useful phrases in the local language, with how to pronounce each one and when to use it. Keep it practical.
                    """"),

                // Money
                P("Make a monthly budget", "See where your money goes", money,
                    """"
                    Help me make a monthly budget. My take-home income is {amount}. My fixed costs are {rent, bills, loans…}, and roughly, I spend {spending on food, transport, fun…}. My goal is {e.g. save 500 a month, pay off a card}.

                    Organise this into a simple budget table with categories, current amounts and suggested amounts. Show where I could realistically cut back, and give me a few easy habits to stay on track. Explain things in plain words; this is for general planning, not professional financial advice.
                    """"),
                P("Savings goal plan", "How to reach a target by a date", money,
                    """"
                    I want to save {amount} for {goal} by {date}. I currently have {amount saved} and can put aside about {monthly amount} a month.

                    Tell me if that's on track, and how much I'd need to save each month to get there. Give me 10 practical ways to save or earn a bit more, sorted by how easy they are. Lay out a month-by-month plan with milestones.
                    """"),
                P("Help me decide on a purchase", "Compare options side by side", money,
                    """"
                    I'm deciding between {option A} and {option B} (and {option C}, if any). What matters most to me: {e.g. price, quality, how long it lasts, running costs}. My budget: {budget}.

                    Compare them in a table covering upfront cost, ongoing costs, the main pros and cons, and the total cost over {number} years. Then give me a recommendation and the questions I should answer before I buy.
                    """"),

                // Career
                P("Tailor my CV to a job", "Match your CV to the job advert", career,
                    """"
                    Act as a recruiter in {industry}. Here's the job advert:

                    {paste job advert}

                    And here's my CV:

                    {paste CV}

                    Tell me how well I match (strong, partial, missing) for each key requirement. Then rewrite my summary and my 5 most relevant bullet points to fit this job, using the advert's keywords naturally and showing results with numbers where I have them. Don't invent experience I don't have.
                    """"),
                P("Interview practice", "A mock interview with feedback", career,
                    """"
                    Run a mock interview with me for the role of {job title} at {company or type of company}. My background: {short summary}.

                    Ask me one question at a time: a mix of behavioural, role-specific and one tricky question. Wait for my answer each time. After each one, give me quick feedback (what worked, what to improve) and a stronger example answer using the STAR method. After 6 questions, sum up my strengths and what to practise.
                    """"),
                P("Ask for a raise", "Prepare the case and the conversation", career,
                    """"
                    Help me prepare to ask for a raise. My role: {job title}, {time in role}. My key achievements this year: {list}. Market rate for my role: {if known}. What I'm asking for: {amount or percentage}.

                    Give me: the case in 3 strong points, a short opening script for the conversation, answers to the 4 most likely objections, and what to ask for instead if the answer is "not now".
                    """"),

                // Coding
                P("Explain this code", "Plain-English walkthrough of unfamiliar code", coding,
                    """"
                    Explain what this code does, step by step, as if I'm a developer who's new to this codebase:

                    ```
                    {paste code}
                    ```

                    Call out anything surprising, risky or non-obvious. End with a one-sentence summary.
                    """"),
                P("Fix this error", "", coding,
                    """"
                    I'm getting this error:

                    {paste error here}

                    Find the most likely root cause before suggesting a fix. Ask me for the relevant file if you need it. Don't guess.
                    """"),
                P("Review my code", "A thorough review with priorities", coding,
                    """"
                    Review this code like a strict but fair senior engineer. For each issue give: where it is, the problem, why it matters, and a suggested fix. Label each as "must fix", "should fix" or "nit". Check especially for: null handling, "off-by-one" errors, security, and anything that's 'clever' but hard to read.

                    {paste code or diff}
                    """"),
            ],
        };
    }
}
