/// <summary>
/// The title screen's credits: plain data, never translated (asset names, authors and licences stay as their authors wrote them; only the
/// section headings go through <c>L10n</c>, by <see cref="Section.HeadingKey"/>). Source: <c>plannings/licence-audit.md</c>, "Credits screen".
/// To add or fix a credit, edit the arrays below; <c>MainMenu</c> builds the panel from them.
/// </summary>
public static class CreditsList
{
    /// <summary>
    /// TODO(user): replace with your name or studio. Shown under the title at the top of the credits panel.
    /// </summary>
    public const string DeveloperName = "KEEP IT LIT team";

    /// <summary>One heading and its lines. The heading is an <c>ui.credits.*</c> localization key; the lines are shown verbatim.</summary>
    public readonly struct Section
    {
        public readonly string HeadingKey;
        public readonly string[] Lines;

        public Section(string headingKey, params string[] lines)
        {
            HeadingKey = headingKey;
            Lines = lines;
        }
    }

    public static readonly Section[] Sections =
    {
        new Section("ui.credits.unity",
            // The Unity Companion License notice (licence-audit.md, question 1): the template's assets ship with it. The full text is in
            // Notices/THIRD-PARTY-NOTICES.txt, which WindowsBuild copies next to the exe.
            "Robot character, Starter Assets third-person controller and tutorial assets",
            "from \"Get Started With Unity\" (com.unity.template.get-started)",
            "© 2025 Unity Technologies, used under the Unity Companion License",
            "unity.com/legal/licenses/unity-companion-license",
            "Made with Unity"),

        new Section("ui.credits.music",
            "Gil Guzman - PSX Horror Music Pack Vol 1 - Exploration",
            "Adam Simoes - Free Survival Horror Game Music",
            "FictiumSoundDesign - Horror Music Pack - Starter Kit"),

        new Section("ui.credits.sound",
            "Anthon - Horror Elements, Scary Sound FX",
            "Olivier Girardot - Free Sound Effects Pack",
            "SoundBits - Free Sound FX Collection"),

        new Section("ui.credits.art",
            "BK - Alchemist House",
            "Abandoned Asylum environment pack",
            "Rokay3D - Morgue Room PBR",
            "SR Studios Kerala - Deadbody 3D Lite",
            "NorSat Entertainment - Blood splatter decal package",
            "TrueBlue Studios - Sockets and Fuse Box Pack",
            "3Digitalis - Simple Low Poly Keys",
            "Green Moss Games - Ultimate Material Library (Poly Haven scans, CC0)",
            "Game Seed Assets - Fog Particles",
            "Poly Etereo - Maze Modular Puzzle Kit (Imphenzia PixPal palette, CC0)",
            "ambientCG - surface textures (CC0)"),

        new Section("ui.credits.characters",
            "Mixamo (Adobe) - characters and animations",
            "\"Thermal Katana\" by Brave_Iruka (CC BY)",
            "Pistol \"cx33\" by DJMaesen (CC BY)",
            "\"Magnum Elite Spider\" by Nick G. Gizelis (CC BY)",
            "Cyborg with Thermal Katana (Fab)"),

        new Section("ui.credits.fonts",
            "Barlow Condensed, Cinzel, IM Fell English, Saira Stencil One, Reenie Beanie, Inter, Liberation Sans (SIL Open Font License 1.1)",
            "Rock Salt (Apache License 2.0)"),
    };
}
