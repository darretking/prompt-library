using PromptLibrary.Core.Models;
using PromptLibrary.Core.Seed;

namespace PromptLibrary.Core.Team;

/// <summary>
/// The business example prompts offered when a team library is started: 24 prompts in 6 categories, 4 each, written
/// for a generic company (no real brands, no legal or financial advice). Each prompt follows the pattern of the
/// personal starter set: a role, the details as {placeholders}, the task and the reason behind it, a set output
/// format, and permission to ask questions when details are missing. HR, finance and policy prompts carry a
/// "check with your ... team" note.
/// </summary>
public static class TeamSeedLibrary
{
    public static Library Create(DateTimeOffset now)
    {
        Category C(string name) => new() { Id = Guid.NewGuid(), Name = name, Emoji = SeedDecor.Emoji(name) };

        var sales = C("Sales");
        var support = C("Customer support");
        var marketing = C("Marketing");
        var meetings = C("Meetings & management");
        var hr = C("HR & people");
        var ops = C("Operations & finance");

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
            Categories = [sales, support, marketing, meetings, hr, ops],
            Prompts =
            [
                // Sales
                P("Discovery call questions", "Questions that uncover real needs", sales,
                    """"
                    You're an experienced sales consultant at {company}, which sells {product or service} to {type of customer}. I have a first discovery call with {prospect name and role} at {prospect company} on {date}.

                    Write the questions I should ask so I understand their situation before I pitch anything. Group them under: current situation, goals and what success looks like, problems and what they've tried, who else is involved in the decision, budget and timing.

                    Give me 12 questions in that order, each with a short note on why it matters. Mark the 3 I must not skip. If you need more detail about the prospect or our offer first, ask me up to 3 questions.
                    """"),
                P("Follow-up email after a meeting", "A clear recap and next step", sales,
                    """"
                    You're a sales professional who writes short, friendly, useful emails. I just met {contact name, role} at {their company} about {topic}.

                    What we covered: {key points discussed}
                    What they said matters most: {their priorities or concerns}
                    What we agreed to do next: {next steps, owners and dates}

                    Write a follow-up email I can send today. Thank them in one line, recap in 3 bullets what we heard, state the next step and who does it, and end with one easy question to move forward. Keep it under 150 words in a {tone, e.g. warm and professional} voice, and give me 3 subject line options. If a detail is missing, ask me before you write.
                    """"),
                P("Handle a sales objection", "Respond calmly and keep the door open", sales,
                    """"
                    You're a sales coach. A prospect has raised this objection: "{the objection, in their words}".

                    Context: we sell {product or service} to {type of customer}. Their situation: {what you know about them}. Where we are in the sale: {stage}.

                    Help me respond well. First, say what is probably behind the objection. Then give me 3 responses I could say out loud, each taking a different approach (understand first, reframe the value, offer proof). Finish with the one follow-up question I should ask. Be honest: don't suggest pressure tactics or claims we can't back up. If you need to know more about the situation, ask me first.
                    """"),
                P("Proposal summary for decision makers", "One page the boss will actually read", sales,
                    """"
                    You're a sales writer who turns long proposals into clear one-page summaries. The reader is {decision maker's role}, who has little time and cares about {what they care about, e.g. cost, risk, speed}.

                    Here is the full proposal or my notes:
                    {paste the proposal or notes}

                    Write a summary with these headings: the problem we are solving, our recommended solution, what they get, price and what it includes, timeline, and what we need from them to start. Use plain language, no jargon, and numbers wherever I have given them. Keep it to about 250 words. List anything you had to assume so I can check it.
                    """"),

                // Customer support
                P("Reply to an upset customer", "Empathetic, specific, and constructive", support,
                    """"
                    You're a senior customer support agent at {company} known for calm, kind and clear replies.

                    The customer's message:
                    {paste the message}

                    What actually happened: {the facts from our side}
                    What we can offer: {options we are allowed to offer}

                    Write a reply that acknowledges how they feel in plain words (no scripted phrases), says what happened without blaming them or hiding our part, explains what we will do and by when, and ends with how to reach me. Keep it under 150 words in a {tone, e.g. warm and professional} voice. If the facts or the options are unclear, ask me before you write.
                    """"),
                P("Troubleshooting steps for a customer", "Simple numbered steps to fix a problem", support,
                    """"
                    You're a technical support specialist who explains things to non-experts. The customer uses {product or service} and has this problem: {description of the problem}.

                    What we know: {device, version, what they already tried}

                    Write step-by-step instructions to fix it. Number every step, one action per step, say what they should see after each one, and mention what to do if it doesn't work and where to go next. Start with the most likely and simplest fixes. Avoid jargon. End with the details they should send us if it still isn't solved. If you need more information to narrow it down, ask me up to 3 questions.
                    """"),
                P("Refund or escalation response", "Explain the decision and what happens next", support,
                    """"
                    You're a customer support lead at {company}. A customer has asked for {refund, replacement, or escalation} because {their reason}.

                    Our policy says: {paste the relevant policy}
                    My decision: {approve, partly approve, decline, or escalate}, because {reason}

                    Write the reply. Start with the decision, explain the reason in plain language, say exactly what happens next and when (amounts, dates, who will contact them), and offer a real alternative if the answer is no. Be fair and respectful, and don't promise anything outside the policy. If a decision needs approval, check with your manager or the team that owns the policy before sending. Ask me if any detail is missing.
                    """"),
                P("FAQ answer from our policy", "Turn policy text into a clear answer", support,
                    """"
                    You're a support writer who turns policies into answers customers understand.

                    The question customers ask: {question}
                    Our policy text:
                    {paste the policy}

                    Write an FAQ answer of no more than 100 words. Answer the question in the first sentence, then give any conditions, deadlines or exceptions as a short list. Use "you" and everyday words. Add a one-line "Still need help?" pointing to {contact route}. Don't add anything the policy doesn't say; if the policy is unclear or has gaps, list them for me to settle first.
                    """"),

                // Marketing
                P("Campaign brief", "A one-page brief the team can work from", marketing,
                    """"
                    You're a marketing manager at {company}. Write a campaign brief for {campaign name or goal}.

                    What I know: product or offer {what}, audience {who}, budget {amount}, dates {start to end}, channels available {channels}.

                    Use these headings: objective (one measurable goal), target audience and what they care about, key message, proof points, channels and what goes where, timeline with milestones, budget split, how we will measure success, and risks. Keep each section short and specific. Tell me which assumptions you made, and ask me up to 3 questions if something important is missing.
                    """"),
                P("Product launch announcement", "Announce a launch to customers", marketing,
                    """"
                    You're a product marketer. We are launching {product or feature} on {date}. It helps {audience} to {main benefit}.

                    Key details: {what's new, price or availability, how to get it}
                    Tone: {tone, e.g. confident and friendly}

                    Write the announcement in three versions: an email (subject line plus about 150 words), a short social post (under 60 words), and a 3-sentence internal note for our team. Lead with what the customer gains, not our effort; include one clear call to action in each. Don't use hype words or claims we can't prove. Ask me if you need more detail.
                    """"),
                P("Case study draft", "From interview notes to a customer story", marketing,
                    """"
                    You're a B2B content writer. Turn my notes into a customer case study about {customer} and how they use {product or service}.

                    My notes:
                    {paste notes, quotes and numbers}

                    Structure: a headline with the result, a 2-sentence summary, the challenge, why they chose us, what we did together, the results (use only the numbers I gave), and a closing quote. Write it in about 400 words, in a clear and honest style. Mark any claim that needs the customer's approval, and list questions I should ask them to make the story stronger.
                    """"),
                P("Rewrite in our brand voice", "Keep the meaning, match the voice", marketing,
                    """"
                    You're an editor who protects a brand's voice. Our voice is: {describe the voice, e.g. friendly, plain-spoken, a little witty, never pushy}. Words we like: {examples}. Words we avoid: {examples}.

                    Rewrite the text below so it sounds like us. Keep the meaning and all the facts, make it shorter where you can, and use active sentences.

                    {paste the text}

                    Give me the rewrite, then a short list of the main changes you made and why. If you need an example of our writing to match the voice, ask me for it.
                    """"),

                // Meetings & management
                P("Meeting agenda", "A focused agenda with timings", meetings,
                    """"
                    You're an experienced chief of staff. Plan an agenda for a {length} meeting called "{meeting title}" with {attendees and roles}.

                    The purpose is {why we are meeting}, and by the end we must have decided or agreed {desired outcomes}.

                    Give me a table with: time slot, topic, who leads it, and what we need from the group (inform, discuss or decide). Put decisions early, leave 5 minutes at the end for next steps, and list what people should read or prepare beforehand. Tell me if the agenda is too full for the time. If you need to know more, ask me first.
                    """"),
                P("Minutes and action items", "Turn rough notes into clear minutes", meetings,
                    """"
                    You're a meeting secretary. Turn my notes from {meeting title} on {date} into minutes.

                    Attendees: {names}
                    My notes:
                    {paste notes or transcript}

                    Format: purpose in one line, decisions made (a list), discussion highlights (max 5 bullets), and an action table with columns: action, owner, due date. If an owner or date wasn't stated, write "to confirm" instead of guessing. Finish with open questions to settle at the next meeting. Keep it under one page.
                    """"),
                P("One-to-one prep", "Make a catch-up useful for both sides", meetings,
                    """"
                    You're a management coach. I'm preparing a one-to-one with {team member, role}, who I manage. We last spoke on {date} about {topics}.

                    What's going on: {recent work, wins, concerns, anything they've said}
                    What I want from this conversation: {goal}

                    Give me: a suggested agenda for 30 minutes, 8 open questions (mixing how they are, their work, blockers, growth, and feedback for me), one piece of specific positive feedback and one piece of constructive feedback I could give, and how to word it. Keep a supportive and direct tone. Ask me if you need more context.
                    """"),
                P("Project status update", "A short update stakeholders will read", meetings,
                    """"
                    You're a project manager writing for senior stakeholders who have two minutes. Write the status update for {project name} for {period}.

                    My notes: progress {what got done}, upcoming {what's next}, issues {problems}, risks {risks}, budget and schedule {on track or not, and by how much}.

                    Format: an overall status (green, amber or red) with one sentence of reason; what we achieved; what's next; the top 3 risks or issues, each with its owner and what we are doing; and any decisions or help we need from the readers. Be direct and don't hide bad news. Keep it under 200 words. Ask me for missing facts rather than guessing.
                    """"),

                // HR & people
                P("Job description", "A clear, inclusive role description", hr,
                    """"
                    You're an HR specialist writing a job description for a {job title} at {company}, in the {team} team, reporting to {manager's role}. The role is {location and working pattern}.

                    What the person will do: {main responsibilities}
                    Must-have skills and experience: {list}
                    Nice to have: {list}

                    Write the description with: a short summary of the role and why it matters, key responsibilities (6 to 8 bullets), what we're looking for, what we offer ({benefits, pay range if known}), and how to apply. Use plain, inclusive language and avoid unnecessary requirements. This is a draft: check with your HR team before acting or publishing, especially on pay, titles and legal wording. Ask me if details are missing.
                    """"),
                P("Interview questions and scoring guide", "Fair, consistent interviews", hr,
                    """"
                    You're an HR consultant who designs structured interviews. I'm hiring a {job title}. The most important skills for this role are {skills and qualities}.

                    Write 8 interview questions: a mix of experience-based, situational, and role-specific. For each: what a strong answer includes, signs of a weak answer, and a 1 to 5 scoring scale. Add a short opening and closing script, and a list of topics that must not be asked.

                    Keep every question job-related and the same for all candidates. This is a starting point: check with your HR team before acting on how you assess and record candidates. Ask me if you need more about the role.
                    """"),
                P("New starter onboarding plan", "The first 30, 60 and 90 days", hr,
                    """"
                    You're a people operations manager. {New starter name}, a {job title}, joins {team} on {start date}, with {manager} as their manager.

                    Create an onboarding plan for the first 90 days. Include: before day one (equipment, accounts, welcome message), the first week day by day, goals for days 30, 60 and 90, people they should meet and why, training to complete, and check-in dates with their manager. Format it as a table by week, with an owner for each item.

                    Keep it realistic and welcoming. Policies and required training vary, so check with your HR team before acting on anything about contracts, probation or compliance. Ask me for what's specific to our company if you need it.
                    """"),
                P("Policy summary for staff", "Explain a policy in plain words", hr,
                    """"
                    You're an internal communications writer. Turn this policy into a clear summary for all staff at {company}.

                    The policy text:
                    {paste the policy}

                    Write: a one-sentence "what this means for you", who it applies to, the key rules as a short list, what to do and who to contact with questions, and the date it takes effect. Use everyday words and no legal jargon, in about 200 words. Don't add rules the policy doesn't contain, and list any points that are unclear. The summary doesn't replace the policy: check with your HR or legal team before acting or sending it out.
                    """"),

                // Operations & finance
                P("Process write-up (SOP)", "A step-by-step procedure anyone can follow", ops,
                    """"
                    You're an operations analyst. Write a standard operating procedure for {process name}, used by {team or role}.

                    How it works today (rough notes): {describe the steps}
                    Tools involved: {systems, forms}
                    Common mistakes: {what goes wrong}

                    Use these sections: purpose, who it applies to, what's needed before starting, numbered steps (one action each, with who does it), checks that confirm each stage is done, what to do when something goes wrong, and who owns the document. Write it so a new starter could follow it. Where steps involve approvals, safety or regulations, check with your compliance or operations lead before acting. Ask me about any gaps.
                    """"),
                P("Compare suppliers", "A fair side-by-side comparison", ops,
                    """"
                    You're a procurement analyst. We need {product or service} and are comparing these suppliers: {supplier A}, {supplier B}, {supplier C}.

                    What I know about each (prices, terms, delivery, support, references):
                    {paste details}

                    What matters most to us, in order: {criteria, e.g. cost, reliability, support}.

                    Build a table comparing the suppliers on those criteria, then give a weighted score, a recommendation with your reasoning, the main risks of each option, and 5 questions to ask before we decide. Say clearly where information is missing. This is a decision aid, not advice: check with your finance and legal teams before acting on contracts or spending.
                    """"),
                P("Explain a budget variance", "What changed and what to do about it", ops,
                    """"
                    You're a finance business partner explaining numbers to non-finance managers. For {department or project}, the budget for {period} was {budget amount} and the actual was {actual amount}.

                    What I know about the causes: {drivers, e.g. price increases, extra hires, timing}

                    Write an explanation in plain English: the size of the variance in money and percent, the main reasons ranked by impact, whether each is one-off or will continue, the effect on the full-year forecast, and 2 or 3 options for what we could do. Keep it under 250 words with one small table. Use only the figures I give you, and list any assumptions. Check with your finance team before acting or reporting these figures.
                    """"),
                P("Risk register entry", "Describe a risk and how to manage it", ops,
                    """"
                    You're a risk and operations specialist. Help me write a risk register entry for this risk: {describe the risk} in {team, project or process}.

                    Give me: a clear risk statement (cause, event, effect), likelihood and impact on a 1 to 5 scale with a short reason for each, the overall rating, early warning signs, preventive actions, a response plan if it happens, a suggested owner, and a review date. Keep every field to one or two sentences.

                    Ratings are judgement calls: check with your risk or compliance team before acting on them. Ask me up to 3 questions if the risk is not clear enough.
                    """"),
            ],
        };
    }
}
