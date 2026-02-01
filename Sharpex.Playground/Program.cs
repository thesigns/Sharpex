namespace Sharpex.Playground;

internal static class Program
{
    private static int money = 15;
    
    private static void Main(string[] args)
    {
        Sharpex.Eval("#test");
        Sharpex.Eval("#pay 10");
        Sharpex.Eval("#pay 10");
    }
    
    
    [Sharpex("test")]
    private static bool Test()
    {
        Console.WriteLine("Hello Sharpex!");
        return true;
    }
    
    [Sharpex("pay")]
    private static bool Pay(int amount)
    {
        if (amount > money)
        {
            Console.WriteLine("You don't have enough money.");
            return false;
        }
        money -= amount;
        Console.WriteLine("You paid " + amount + " USD.");
        return true;
    }
    
    
}