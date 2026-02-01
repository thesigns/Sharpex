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
        money = 20;

        var result = Sharpex.Eval("#pay 10");

        Assert.True(result);
        Assert.Equal(10, money);
    }

    [Fact]
    public void And_both_true()
    {
        money = 20;

        var result = Sharpex.Eval("#pay 5 #pay 5");

        Assert.True(result);
        Assert.Equal(10, money);
    }

    [Fact]
    public void And_short_circuits_on_false()
    {
        money = 5;

        var result = Sharpex.Eval("#pay 10 #pay 3");

        Assert.False(result);
        Assert.Equal(5, money); // #pay 3 never executed
    }

    [Fact]
    public void Groups_return_last_group_result()
    {
        money = 20;

        // group 1: pay 10 (true) > group 2: pay 5 (true)
        var result = Sharpex.Eval("#pay 10 > #pay 5");

        Assert.True(result);
        Assert.Equal(5, money);
    }

    [Fact]
    public void Groups_first_false_second_true_returns_true()
    {
        money = 5;

        // group 1: pay 10 (false, not enough) > group 2: pay 3 (true)
        var result = Sharpex.Eval("#pay 10 > #pay 3");

        Assert.True(result);
        Assert.Equal(2, money);
    }

    [Fact]
    public void Groups_first_true_second_false_returns_false()
    {
        money = 5;

        // group 1: pay 3 (true) > group 2: pay 10 (false)
        var result = Sharpex.Eval("#pay 3 > #pay 10");

        Assert.False(result);
        Assert.Equal(2, money);
    }

    [Fact]
    public void Or_first_true_short_circuits()
    {
        money = 20;

        // pay 5 (true) | pay 3 (skipped)
        var result = Sharpex.Eval("#pay 5 | #pay 3");

        Assert.True(result);
        Assert.Equal(15, money); // only 5 deducted
    }

    [Fact]
    public void Or_first_false_tries_second()
    {
        money = 5;

        // pay 10 (false) | pay 3 (true)
        var result = Sharpex.Eval("#pay 10 | #pay 3");

        Assert.True(result);
        Assert.Equal(2, money);
    }

    [Fact]
    public void Or_both_false()
    {
        money = 1;

        var result = Sharpex.Eval("#pay 10 | #pay 5");

        Assert.False(result);
        Assert.Equal(1, money);
    }

    [Fact]
    public void And_has_higher_precedence_than_or()
    {
        money = 5;

        // (#pay 10 AND #pay 1) OR #pay 3
        // pay 10 fails → AND short-circuits → try OR → pay 3 succeeds
        var result = Sharpex.Eval("#pay 10 #pay 1 | #pay 3");

        Assert.True(result);
        Assert.Equal(2, money);
    }

    [Fact]
    public void Conditional_true_executes_then()
    {
        money = 20;

        // pay 5 (true) → then: pay 3
        var result = Sharpex.Eval("#pay 5 ? #pay 3");

        Assert.True(result);
        Assert.Equal(12, money); // 20 - 5 - 3
    }

    [Fact]
    public void Conditional_false_skips_then()
    {
        money = 2;

        // pay 5 (false) → then skipped → false
        var result = Sharpex.Eval("#pay 5 ? #pay 1");

        Assert.False(result);
        Assert.Equal(2, money); // nothing deducted
    }

    [Fact]
    public void Conditional_false_executes_else()
    {
        money = 2;

        // pay 5 (false) → else: pay 1
        var result = Sharpex.Eval("#pay 5 ? #pay 99 : #pay 1");

        Assert.True(result);
        Assert.Equal(1, money); // only else branch ran
    }

    [Fact]
    public void Conditional_true_skips_else()
    {
        money = 20;

        // pay 5 (true) → then: pay 3, else skipped
        var result = Sharpex.Eval("#pay 5 ? #pay 3 : #pay 99");

        Assert.True(result);
        Assert.Equal(12, money); // 20 - 5 - 3
    }

    [Fact]
    public void Conditional_with_groups()
    {
        money = 20;

        // group 1: pay 5 (true) > group 2: pay 100 ? pay 3 : pay 1
        // group 2 condition fails → else: pay 1
        var result = Sharpex.Eval("#pay 5 > #pay 100 ? #pay 3 : #pay 1");

        Assert.True(result);
        Assert.Equal(14, money); // 20 - 5 - 1
    }

    [Fact]
    public void Colon_without_question_throws()
    {
        Assert.Throws<FormatException>(() => Sharpex.Eval("#pay 5 : #pay 3"));
    }

    [Fact]
    public void Multiple_question_marks_throws()
    {
        Assert.Throws<FormatException>(() => Sharpex.Eval("#pay 5 ? #pay 3 ? #pay 1"));
    }
}