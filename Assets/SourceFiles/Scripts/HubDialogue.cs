using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// F98: every line the hospital's staff and patients say through the dialogue panel. Static data like PatientFiles / PatientVoice.Lines:
/// a conversation is a short list of nodes (speaker + text, an optional pair of choices, an optional sound cue). Keys are
/// "who.situation" (agnes.offer, bruno.later3, ...); HubMission picks the key from the mission stage and the floor. Texts may use
/// {n} (things found so far) and {total}. A line stays under 100 characters and a conversation under 8 lines - the panel is not a book.
/// No state here: the file is pure data.
/// </summary>
public static class HubDialogue
{
    /// <summary>A pick shown at a choice node: its label, and the lines that follow it before the conversation goes on (flavour only).</summary>
    public sealed class Choice
    {
        public readonly string label;
        public readonly Node[] reply;
        public Choice(string label, params Node[] reply) { this.label = label; this.reply = reply; }
    }

    public sealed class Node
    {
        public readonly string speaker;
        public readonly string text;
        public readonly Choice[] choices;
        /// <summary>Sound cue played as the line starts: "whisper" (GameAudio whisper slot, low) or null.</summary>
        public readonly string fx;
        public Node(string speaker, string text, string fx = null, Choice[] choices = null) { this.speaker = speaker; this.text = text; this.fx = fx; this.choices = choices; }
        public bool HasChoices => choices != null && choices.Length > 0;
    }

    public const string Agnes = "SISTER AGNES";
    public const string Bruno = "BRUNO";
    public const string Josef = "JOSEF";
    public const string Marta = "MARTA";
    public const string Weiss = "DR. WEISS";
    public const string Clerk = "THE CLERK";

    public static Color ColourOf(string speaker)
    {
        switch (speaker)
        {
            case Agnes: return new Color(0.95f, 0.88f, 0.72f);
            case Bruno: return new Color(0.62f, 0.72f, 0.82f);
            case Josef: return new Color(0.9f, 0.7f, 0.38f);
            case Marta: return new Color(0.92f, 0.7f, 0.76f);
            case Weiss: return new Color(0.5f, 0.82f, 0.8f);
            case Clerk: return new Color(0.66f, 0.7f, 0.62f);
            default: return new Color(0.85f, 0.85f, 0.85f);
        }
    }

    private static Node A(string t, string fx = null) => new Node(Agnes, t, fx);
    private static Node B(string t, string fx = null) => new Node(Bruno, t, fx);
    private static Node J(string t, string fx = null) => new Node(Josef, t, fx);
    private static Node M(string t, string fx = null) => new Node(Marta, t, fx);
    private static Node W(string t, string fx = null) => new Node(Weiss, t, fx);
    private static Node C(string t, string fx = null) => new Node(Clerk, t, fx);

    private static readonly Dictionary<string, Node[]> Table = new Dictionary<string, Node[]>
    {
        // ------------------------------------------------------------------ Sister Agnes - the ward keys (floor 1)
        { "agnes.offer", new[]
        {
            A("There you are. Late again, and no coat. It's February, dear."),
            A("Ward A is locked, and the keys are on my ring, and my ring is in the kitchen."),
            A("I left it on its hook when the lights went. I don't go where the lights go."),
            A("Be a love. West end, past the dining hall. A big brass ring. You'll hear it."),
            A("And mind the floor. They haven't mopped since the... well. Since."),
        } },
        { "agnes.remind", new[]
        {
            A("The kitchen, dear. West end. A brass ring on a hook."),
            A("I'd fetch it myself, but you know how I am about the dark."),
        } },
        { "agnes.return", new[]
        {
            A("That's the one. Heavier than I remember."),
            A("Ward A is the long door on the east side. Day room first, then the wing."),
            A("Matron says there will be fewer of you each night. I tell her, nonsense. Nonsense, Matron."),
            A("Take the key. Bring it back before the morning count."),
        } },
        { "agnes.after", new[]
        {
            A("The morning count is at six. You'll be back by six?"),
            A("It's always six. It's been six for such a long time."),
        } },
        { "agnes.later2", new[]
        {
            A("Back so soon? Did you wipe your feet?"),
            A("Mind the lift. It sticks. It sticks on purpose, I think."),
        } },
        { "agnes.later3", new[]
        {
            A("I counted the beds twice tonight. One of them keeps being one more."),
            A("Don't tell Matron. She's very tired."),
        } },
        { "agnes.later4", new[]
        {
            A("Don't mention the lamps to Dr. Weiss. He takes it so personally."),
            A("Oh, and if you hear someone calling my name, it isn't me. I'm right here."),
        } },

        // ------------------------------------------------------------------ Bruno - the tray (floor 2)
        { "bruno.pre", new[]
        {
            B("Salt on the sill. That's what my mother said. Nobody listens."),
            B("Don't whistle down here. Just don't."),
        } },
        { "bruno.offer", new[]
        {
            B("Boiler room, aye. I've got the key. It's on me somewhere."),
            B("I'm not going down there myself. Not for money. Not for anything."),
            new Node(Bruno, "Do me a turn first. Josef's supper. Tray's on the pass. He bites when he's hungry.", null, new[]
            {
                new Choice("He bites?", B("Figure of speech. Mostly.")),
                new Choice("Why don't you take it?", B("He knows things. About me. About everyone. I'd sooner not be looked at.")),
            }),
            B("Bedroom, top of the stairs. Take the tray up, then come and see me."),
        } },
        { "bruno.remind", new[]
        {
            B("Tray's on the pass, by the stove. Don't let it go cold. He minds."),
        } },
        { "bruno.carry", new[]
        {
            B("Go on, then. Don't spill it. Don't look at him too long."),
        } },
        { "bruno.after", new[]
        {
            B("He gave you the key? Huh."),
            B("Then I never had it. Funny. I could've sworn it was on my belt."),
            B("Boiler room's down in the basement. Lift's the only way. Don't ask me what it's like."),
        } },
        { "bruno.later3", new[]
        {
            B("Heard the pipes again. Three knocks, then two. I put my hands over my ears."),
            B("Don't tell Tomas I said so."),
        } },
        { "bruno.later4", new[]
        {
            B("The salt's gone off the sill. All of it. Who takes salt?"),
            B("I'm staying in the kitchen. The kitchen has a door."),
        } },

        // ------------------------------------------------------------------ Josef - recipient of the tray (floor 2)
        { "josef.pre", new[]
        {
            J("You walk like the new one. They always walk like they were told where the stairs are."),
            J("Sit. No, don't sit. That's Elias's bed, and he's particular."),
        } },
        { "josef.hello", new[]
        {
            J("No tray. Then you're not here for me."),
            J("Come back with supper. Boiled something, grey, late. I'll know it by the smell."),
        } },
        { "josef.remind", new[]
        {
            J("The tray, boy. The kitchen. Bruno knows where the food is. Bruno always knows."),
        } },
        { "josef.deliver", new[]
        {
            J("Cold. Of course it's cold. Thank you."),
            J("Nobody's brought me anything since the doctor stopped coming. The one who stopped being a doctor."),
            J("You'll want the Boiler room. Bruno says he has the key."),
            J("Bruno lies. Poor man, he believes every word of it."),
            J("Under the pillow. My pillow, you impertinent thing. There."),
            J("Brass, with a tag. I took it off his belt last October. The October."),
            J("Mind the lift. It will stop for you. They always do."),
        } },
        { "josef.after", new[]
        {
            J("Go on. I'll finish my supper. Cold is how I like it now."),
            J("You'll be back. They all come back. It's only a question of how many of you."),
        } },
        { "josef.later3", new[]
        {
            J("Elias is up. Standing with his face to the wall, since the lights went."),
            J("Don't speak to him. He thinks it's rude to be answered."),
        } },
        { "josef.later4", new[]
        {
            J("They took Elias down this morning. Drawer five. He'd have wanted a window."),
            J("Someone should have told him the drawers have no windows. I'd have done it."),
        } },

        // ------------------------------------------------------------------ Marta - the music box (floor 3)
        { "marta.pre", new[]
        {
            M("Do you want to see my drawing? It's the house. It has more windows than the real one."),
            M("Shh. The chalk is sleeping."),
        } },
        { "marta.offer", new[]
        {
            M("I had a music box. It was blue on the inside, like water."),
            M("I took it down to the long cold hall to show the pipes. They liked it."),
            M("Then the lights went away one at a time, and I came upstairs. I forgot it."),
            M("Will you fetch it? I'm not allowed down there. The pipes get jealous."),
            M("It's on the floor, I think. By the wall. Tell it I'm not cross."),
        } },
        { "marta.remind", new[]
        {
            M("The long cold hall. By the wall. Tell it I'm not cross."),
        } },
        { "marta.return", new[]
        {
            M("You found it! It's still warm. It's always warm."),
            M("Here. This is for you. I took it from the cold man's coat. He didn't mind."),
            M("It opens the Morgue. Don't be scared of the Morgue. Everyone is very quiet there."),
            M("Thank you. Now you can go and be quiet with them.", "whisper"),
        } },
        { "marta.after", new[]
        {
            M("It's singing in the other room. Can you hear it?"),
            M("No? Oh. Good. Don't wind it. It winds itself."),
        } },
        { "marta.later4", new[]
        {
            M("I can't hear it any more. I think it went very far away."),
            M("Don't go and look. Please. It was only a box."),
        } },

        // ------------------------------------------------------------------ Dr. Weiss - rounds (floor 4)
        { "weiss.pre", new[]
        {
            W("Sorry! Sorry. Rounds. I'm late for rounds. Are you on my list?"),
            W("No. No, you're not on my list. Good. That's good."),
        } },
        { "weiss.offer", new[]
        {
            W("Thank God. Hands. I need hands. Are you steady? You look steady."),
            W("Room 4. The injection. Three ampoules, clear glass, blue cap. I counted them. I count every shift."),
            W("I put them down somewhere on this floor. The surgery side, the offices. I don't know."),
            W("They roll. They're small. They'll be wherever the light isn't."),
            W("Bring all three. I won't give him a short dose. I won't."),
        } },
        { "weiss.remind", new[]
        {
            W("{n} of three. Blue caps, clear glass. Room 4 is waiting."),
            W("He's been waiting such a long time."),
        } },
        { "weiss.return", new[]
        {
            W("Three. All three. You're a marvel. I could kiss you. I won't. Protocol."),
            W("The Surgery key. Here. Take it, take it, I'll need both hands."),
            W("Room 4. Room four..."),
            W("...it's been empty for years, hasn't it?"),
            W("Who was I giving these to?"),
        } },
        { "weiss.after", new[]
        {
            W("Rounds. I should do my rounds."),
            W("There's always a light on in Room 4. Isn't there? Isn't there always?"),
        } },

        // ------------------------------------------------------------------ The Clerk - ambient only
        { "clerk.a", new[]
        {
            C("Name?"),
            C("No. Don't tell me. I have it here somewhere."),
            C("The file's with the Director. The file is always with the Director."),
            C("Name?"),
        } },
        { "clerk.b", new[]
        {
            C("Page forty-one. Page forty-one. Page forty-two."),
            C("I have typed this one before."),
            C("Don't read over my shoulder. It isn't finished."),
        } },
        { "clerk.c", new[]
        {
            C("You're in here, you know. Page seventeen. Under 'Present'."),
            C("It's very good of you to come. It saves me looking."),
        } },
        { "clerk.finale", new[]
        {
            C("It's finished."),
            C("It was always going to be finished. I only had to type it out."),
        } },
    };

    /// <summary>The conversation for `key`, or null when there is none.</summary>
    public static Node[] Get(string key)
    {
        return key != null && Table.TryGetValue(key, out Node[] nodes) ? nodes : null;
    }

    public static bool Has(string key) => key != null && Table.ContainsKey(key);

    /// <summary>Longest line in the table, for the editor audit (the panel is built for about 100 characters).</summary>
    public static int LongestLine()
    {
        int longest = 0;
        foreach (Node[] nodes in Table.Values) longest = Mathf.Max(longest, Longest(nodes));
        return longest;
    }

    private static int Longest(Node[] nodes)
    {
        int longest = 0;
        if (nodes == null) return 0;
        foreach (Node n in nodes)
        {
            longest = Mathf.Max(longest, n.text.Length);
            if (n.HasChoices) foreach (Choice c in n.choices) longest = Mathf.Max(longest, Longest(c.reply));
        }
        return longest;
    }
}
