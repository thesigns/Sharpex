namespace Sharpex.Tests;

public class EvalTests
{
    private static int money;

    [Sharpex("pay")]
    private static bool Pay(int amount)
    {
        if (amount > money)
            return false;

        money -= amount;
        return true;
    }

    [Fact]
    public void Pay_returns_true_when_enough_money()
    {
        // arrange
        money = 20;

        // act
        var result = Sharpex.Eval("#pay 10");

        // assert
        Assert.True(result);
        Assert.Equal(10, money);
    }
}