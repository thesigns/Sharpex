namespace Sharpex.Playground;

internal static class Program
{
    private static int gold = 50;
    private static int health = 80;
    private static int luck = 75;
    private static bool hasSword;
    private static bool hasShield;
    private static bool hasPotion;
    private static string playerName = "Adventurer";

    private class PlaygroundVarProvider : ISharpexVar
    {
        private readonly Dictionary<string, object?> _vars = new();
        public object? GetValue(string name) => _vars.TryGetValue(name, out var val) ? val : null;
        public void SetValue(string name, object? value) => _vars[name] = value;
    }

    private static async Task Main()
    {
        var vars = new PlaygroundVarProvider();
        vars.SetValue("isNight", true);
        vars.SetValue("gold", gold);
        Sharpex.VarProvider = vars;

        // 1. Basic calls & quoted strings
        Run("#banner \"=== The Dragon's Rest Tavern ===\"");
        Run("#say \"You push open the heavy oak door and step inside.\"");
        Run("#say \"The warm glow of a fireplace greets you.\"");
        Run("#set-name \"Kael\"");
        Run("#status");

        // 2. AND (short-circuit)
        Section("AND (short-circuit)");
        Run("#say \"Barkeep: 'A fine sword, just 10 gold.'\"");
        Run("#pay 10 #give sword");
        Run("#say \"Barkeep: 'A dragon-scale shield, only 200 gold.'\"");
        Run("#pay 200 #give shield");

        // 3. OR (short-circuit)
        Section("OR (short-circuit)");
        Run("#say \"Two potions on the shelf: Grand Elixir (100g) or Small Tonic (5g).\"");
        Run("#pay 100 | #pay 5");
        Run("#give potion");

        // 4. NOT (~)
        Section("NOT (~)");
        Run("#say \"A shady figure offers a gamble...\"");
        Run("~fail #say \"Not failing is succeeding!\"");
        Run("~lucky 90 #say \"You're not THAT lucky, but that's OK here.\"");

        // 5. Conditionals (? :)
        Section("Conditionals (? :)");
        Run("#has-item sword ? #say \"You brandish your sword!\" : #say \"No weapon... you gulp nervously.\"");
        Run("#has-item shield ? #say \"Your shield gleams with power!\"");

        // 6. Groups (;)
        Section("Groups (;)");
        Run("#say \"Barkeep: 'One for the road?'\" ; #pay 2 ; #heal 10");
        Run("#status");

        // 7. Timed encounter (EvalAsync + delays)
        Section("Timed encounter (delays)");
        await RunAsync(
            "#say \"The ground shakes...\" [1.5] #say \"A DRAGON bursts through the wall!\" [1] #has-item sword ? #say \"You slash the dragon! It flees!\" : #say \"You dive behind a table!\"");

        // 8. Variables (#if $)
        Section("Variables (#if $)");
        Run("#if $isNight ? #say \"The moon casts long shadows...\" : #say \"The sun is shining.\"");
        Run("#if $gold #say \"You still have some gold.\"");
        Run("#if $gold > 20 ? #say \"Plenty of gold!\" : #say \"Running low on gold...\"");

        // 9. Finale
        Section("Finale");
        Run("#say \"The dust settles. The tavern is in ruins, but you survived.\"");
        Run("#status");
        Run("#banner \"=== THE END ===\"");
    }

    // --- helpers ---

    private static void Run(string expression)
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"  > {expression}");
        Console.ResetColor();

        var result = Sharpex.Eval(expression);

        Console.ForegroundColor = result ? ConsoleColor.Green : ConsoleColor.Red;
        Console.WriteLine($"  [{(result ? "OK" : "FAIL")}]");
        Console.ResetColor();
        Console.WriteLine();
    }

    private static async Task RunAsync(string expression)
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"  > {expression}");
        Console.ResetColor();

        var result = await Sharpex.EvalAsync(expression, async seconds =>
        {
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.Write($"  ... waiting {seconds}s ...");
            Console.ResetColor();
            await Task.Delay((int)(seconds * 1000));
            Console.WriteLine();
        });

        Console.ForegroundColor = result ? ConsoleColor.Green : ConsoleColor.Red;
        Console.WriteLine($"  [{(result ? "OK" : "FAIL")}]");
        Console.ResetColor();
        Console.WriteLine();
    }

    private static void Section(string title)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.WriteLine(new string('-', 50));
        Console.WriteLine($"  {title}");
        Console.WriteLine(new string('-', 50));
        Console.ResetColor();
    }

    // --- sharpex functions ---

    [Sharpex("banner")]
    private static bool Banner(string text)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(text);
        Console.ResetColor();
        Console.WriteLine();
        return true;
    }

    [Sharpex("say")]
    private static bool Say(string text)
    {
        Console.WriteLine($"  {text}");
        return true;
    }

    [Sharpex("status")]
    private static bool Status()
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"  [{playerName}] Gold: {gold} | Health: {health} | Luck: {luck}%");
        Console.Write("  Items:");
        if (hasSword) Console.Write(" [Sword]");
        if (hasShield) Console.Write(" [Shield]");
        if (hasPotion) Console.Write(" [Potion]");
        if (!hasSword && !hasShield && !hasPotion) Console.Write(" (none)");
        Console.WriteLine();
        Console.ResetColor();
        return true;
    }

    [Sharpex("pay")]
    private static bool Pay(int amount)
    {
        if (amount > gold)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  Not enough gold! Need {amount}, have {gold}.");
            Console.ResetColor();
            return false;
        }

        gold -= amount;
        Console.WriteLine($"  Paid {amount} gold. (Remaining: {gold})");
        return true;
    }

    [Sharpex("heal")]
    private static bool Heal(int amount)
    {
        health = Math.Min(100, health + amount);
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  Healed {amount} HP! (Health: {health})");
        Console.ResetColor();
        return true;
    }

    [Sharpex("damage")]
    private static bool Damage(int amount)
    {
        health = Math.Max(0, health - amount);
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"  Took {amount} damage! (Health: {health})");
        Console.ResetColor();
        return health > 0;
    }

    [Sharpex("has-item")]
    private static bool HasItem(string name) => name switch
    {
        "sword" => hasSword,
        "shield" => hasShield,
        "potion" => hasPotion,
        _ => false
    };

    [Sharpex("give")]
    private static bool Give(string name)
    {
        switch (name)
        {
            case "sword": hasSword = true; break;
            case "shield": hasShield = true; break;
            case "potion": hasPotion = true; break;
            default: return false;
        }

        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine($"  Acquired: {name}!");
        Console.ResetColor();
        return true;
    }

    [Sharpex("lucky")]
    private static bool Lucky(int threshold) => luck >= threshold;

    [Sharpex("set-name")]
    private static bool SetName(string name)
    {
        playerName = name;
        Console.WriteLine($"  Player name set to: {name}");
        return true;
    }

    [Sharpex("ok")]
    private static bool Ok() => true;

    [Sharpex("fail")]
    private static bool Fail() => false;
}
