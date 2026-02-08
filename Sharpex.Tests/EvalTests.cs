namespace Sharpex.Tests;

public class EvalTests : IDisposable
{
    private static int money;

    private class TestVarProvider : ISharpexVar
    {
        public Dictionary<string, object?> Vars { get; } = new();
        public object? GetValue(string name) => Vars.TryGetValue(name, out var val) ? val : null;
        public void SetValue(string name, object? value) => Vars[name] = value;
    }

    public void Dispose() => Sharpex.VarProvider = null;

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

        // group 1: pay 10 (true) ; group 2: pay 5 (true)
        var result = Sharpex.Eval("#pay 10 ; #pay 5");

        Assert.True(result);
        Assert.Equal(5, money);
    }

    [Fact]
    public void Groups_first_false_second_true_returns_true()
    {
        money = 5;

        // group 1: pay 10 (false, not enough) ; group 2: pay 3 (true)
        var result = Sharpex.Eval("#pay 10 ; #pay 3");

        Assert.True(result);
        Assert.Equal(2, money);
    }

    [Fact]
    public void Groups_first_true_second_false_returns_false()
    {
        money = 5;

        // group 1: pay 3 (true) ; group 2: pay 10 (false)
        var result = Sharpex.Eval("#pay 3 ; #pay 10");

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

        // group 1: pay 5 (true) ; group 2: pay 100 ? pay 3 : pay 1
        // group 2 condition fails → else: pay 1
        var result = Sharpex.Eval("#pay 5 ; #pay 100 ? #pay 3 : #pay 1");

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

    [Fact]
    public void Not_negates_false_to_true()
    {
        money = 2;

        // pay 5 fails → negated → true
        var result = Sharpex.Eval("~pay 5");

        Assert.True(result);
        Assert.Equal(2, money);
    }

    [Fact]
    public void Not_negates_true_to_false()
    {
        money = 20;

        // pay 5 succeeds → negated → false
        var result = Sharpex.Eval("~pay 5");

        Assert.False(result);
        Assert.Equal(15, money);
    }

    [Fact]
    public void Not_with_and()
    {
        money = 20;

        // pay 5 (true) AND NOT pay 100 (fails → negated → true) → true
        var result = Sharpex.Eval("#pay 5 ~pay 100");

        Assert.True(result);
        Assert.Equal(15, money);
    }

    [Fact]
    public void Delay_zero_throws()
    {
        Assert.Throws<FormatException>(() => Sharpex.Eval("[0] #pay 5"));
    }

    [Fact]
    public void Sync_eval_throws_on_delay()
    {
        Assert.Throws<InvalidOperationException>(() => Sharpex.Eval("[2] #pay 5"));
    }

    [Fact]
    public async Task Async_eval_with_delay()
    {
        money = 20;

        var result = await Sharpex.EvalAsync(
            "[0.5] #pay 5",
            _ => Task.CompletedTask); // fake delay

        Assert.True(result);
        Assert.Equal(15, money);
    }

    [Fact]
    public async Task Async_eval_two_super_groups()
    {
        money = 20;

        // SG0: pay 5 (immediate) → SG1: pay 3 (after delay)
        var result = await Sharpex.EvalAsync(
            "#pay 5 [1] #pay 3",
            _ => Task.CompletedTask);

        Assert.True(result);
        Assert.Equal(12, money); // 20 - 5 - 3
    }

    [Fact]
    public async Task Async_eval_result_from_last_super_group()
    {
        money = 5;

        // SG0: pay 3 (true) → SG1: pay 10 (false)
        var result = await Sharpex.EvalAsync(
            "#pay 3 [1] #pay 10",
            _ => Task.CompletedTask);

        Assert.False(result);
        Assert.Equal(2, money);
    }

    [Fact]
    public async Task Async_eval_accumulates_delays()
    {
        money = 30;
        var delays = new List<float>();

        await Sharpex.EvalAsync(
            "#pay 5 [2] #pay 3 [3] #pay 1",
            d => { delays.Add(d); return Task.CompletedTask; });

        Assert.Equal([2f, 3f], delays);
        Assert.Equal(21, money); // 30 - 5 - 3 - 1
    }

    [Fact]
    public void Too_few_arguments_throws()
    {
        Assert.Throws<FormatException>(() => Sharpex.Eval("#pay"));
    }

    [Fact]
    public void Too_many_arguments_throws()
    {
        Assert.Throws<FormatException>(() => Sharpex.Eval("#pay 10 20"));
    }

    [Fact]
    public void Unknown_function_throws()
    {
        Assert.Throws<KeyNotFoundException>(() => Sharpex.Eval("#nonexistent"));
    }

    // --- #if (variable truthiness) ---

    [Fact]
    public void Is_bool_true_returns_true()
    {
        Sharpex.VarProvider = new TestVarProvider { Vars = { ["isOpen"] = true } };
        Assert.True(Sharpex.Eval("#is $isOpen"));
    }

    [Fact]
    public void Is_bool_false_returns_false()
    {
        Sharpex.VarProvider = new TestVarProvider { Vars = { ["isOpen"] = false } };
        Assert.False(Sharpex.Eval("#is $isOpen"));
    }

    [Fact]
    public void Is_numeric_zero_returns_false()
    {
        Sharpex.VarProvider = new TestVarProvider { Vars = { ["money"] = 0 } };
        Assert.False(Sharpex.Eval("#is $money"));
    }

    [Fact]
    public void Is_numeric_nonzero_returns_true()
    {
        Sharpex.VarProvider = new TestVarProvider { Vars = { ["money"] = 42 } };
        Assert.True(Sharpex.Eval("#is $money"));
    }

    [Fact]
    public void Is_string_empty_returns_false()
    {
        Sharpex.VarProvider = new TestVarProvider { Vars = { ["name"] = "" } };
        Assert.False(Sharpex.Eval("#is $name"));
    }

    [Fact]
    public void Is_string_nonempty_returns_true()
    {
        Sharpex.VarProvider = new TestVarProvider { Vars = { ["name"] = "John" } };
        Assert.True(Sharpex.Eval("#is $name"));
    }

    [Fact]
    public void Is_null_returns_false()
    {
        Sharpex.VarProvider = new TestVarProvider { Vars = { ["x"] = null } };
        Assert.False(Sharpex.Eval("#is $x"));
    }

    [Fact]
    public void Is_missing_var_returns_false()
    {
        Sharpex.VarProvider = new TestVarProvider();
        Assert.False(Sharpex.Eval("#is $unknown"));
    }

    [Fact]
    public void Is_negated_returns_opposite()
    {
        Sharpex.VarProvider = new TestVarProvider { Vars = { ["isOpen"] = true } };
        Assert.False(Sharpex.Eval("~is $isOpen"));
    }

    [Fact]
    public void Is_in_conditional()
    {
        money = 20;
        Sharpex.VarProvider = new TestVarProvider { Vars = { ["isOpen"] = true } };

        var result = Sharpex.Eval("#is $isOpen ? #pay 5 : #pay 1");

        Assert.True(result);
        Assert.Equal(15, money);
    }

    [Fact]
    public void Is_in_conditional_false_branch()
    {
        money = 20;
        Sharpex.VarProvider = new TestVarProvider { Vars = { ["isOpen"] = false } };

        var result = Sharpex.Eval("#is $isOpen ? #pay 5 : #pay 1");

        Assert.True(result);
        Assert.Equal(19, money);
    }

    [Fact]
    public void Is_and_function_combined()
    {
        money = 20;
        Sharpex.VarProvider = new TestVarProvider { Vars = { ["isOpen"] = true } };

        var result = Sharpex.Eval("#is $isOpen #pay 10");

        Assert.True(result);
        Assert.Equal(10, money);
    }

    [Fact]
    public void Is_false_short_circuits_and()
    {
        money = 20;
        Sharpex.VarProvider = new TestVarProvider { Vars = { ["isOpen"] = false } };

        var result = Sharpex.Eval("#is $isOpen #pay 10");

        Assert.False(result);
        Assert.Equal(20, money); // pay not executed
    }

    [Fact]
    public void Is_without_provider_throws()
    {
        Sharpex.VarProvider = null;
        Assert.Throws<InvalidOperationException>(() => Sharpex.Eval("#is $x"));
    }

    [Fact]
    public void Is_wrong_arg_count_throws()
    {
        Sharpex.VarProvider = new TestVarProvider();
        Assert.Throws<FormatException>(() => Sharpex.Eval("#is"));
    }

    [Fact]
    public void Is_non_var_arg_throws()
    {
        Sharpex.VarProvider = new TestVarProvider();
        Assert.Throws<FormatException>(() => Sharpex.Eval("#is hello"));
    }

    [Fact]
    public void Is_two_args_throws()
    {
        Sharpex.VarProvider = new TestVarProvider { Vars = { ["x"] = 1 } };
        Assert.Throws<FormatException>(() => Sharpex.Eval("#is $x >"));
    }

    // --- #if comparisons ---

    [Fact]
    public void Is_greater_than_true()
    {
        Sharpex.VarProvider = new TestVarProvider { Vars = { ["money"] = 42 } };
        Assert.True(Sharpex.Eval("#is $money > 10"));
    }

    [Fact]
    public void Is_greater_than_false()
    {
        Sharpex.VarProvider = new TestVarProvider { Vars = { ["money"] = 42 } };
        Assert.False(Sharpex.Eval("#is $money > 100"));
    }

    [Fact]
    public void Is_less_than()
    {
        Sharpex.VarProvider = new TestVarProvider { Vars = { ["money"] = 42 } };
        Assert.True(Sharpex.Eval("#is $money < 100"));
    }

    [Fact]
    public void Is_greater_or_equal_when_equal()
    {
        Sharpex.VarProvider = new TestVarProvider { Vars = { ["money"] = 42 } };
        Assert.True(Sharpex.Eval("#is $money >= 42"));
    }

    [Fact]
    public void Is_less_or_equal_when_equal()
    {
        Sharpex.VarProvider = new TestVarProvider { Vars = { ["money"] = 42 } };
        Assert.True(Sharpex.Eval("#is $money <= 42"));
    }

    [Fact]
    public void Is_equals_numeric()
    {
        Sharpex.VarProvider = new TestVarProvider { Vars = { ["money"] = 42 } };
        Assert.True(Sharpex.Eval("#is $money == 42"));
    }

    [Fact]
    public void Is_equals_numeric_false()
    {
        Sharpex.VarProvider = new TestVarProvider { Vars = { ["money"] = 42 } };
        Assert.False(Sharpex.Eval("#is $money == 99"));
    }

    [Fact]
    public void Is_equals_string()
    {
        Sharpex.VarProvider = new TestVarProvider { Vars = { ["name"] = "John" } };
        Assert.True(Sharpex.Eval("#is $name == \"John\""));
    }

    [Fact]
    public void Is_equals_string_false()
    {
        Sharpex.VarProvider = new TestVarProvider { Vars = { ["name"] = "John" } };
        Assert.False(Sharpex.Eval("#is $name == \"Jane\""));
    }

    [Fact]
    public void Is_equals_bool()
    {
        Sharpex.VarProvider = new TestVarProvider { Vars = { ["isOpen"] = true } };
        Assert.True(Sharpex.Eval("#is $isOpen == true"));
    }

    [Fact]
    public void Is_compare_with_var()
    {
        Sharpex.VarProvider = new TestVarProvider { Vars = { ["money"] = 42, ["price"] = 10 } };
        Assert.True(Sharpex.Eval("#is $money > $price"));
    }

    [Fact]
    public void Is_compare_with_var_equal()
    {
        Sharpex.VarProvider = new TestVarProvider { Vars = { ["a"] = 5, ["b"] = 5 } };
        Assert.True(Sharpex.Eval("#is $a == $b"));
    }

    [Fact]
    public void Is_compare_null_throws()
    {
        Sharpex.VarProvider = new TestVarProvider { Vars = { ["x"] = null } };
        Assert.Throws<FormatException>(() => Sharpex.Eval("#is $x > 10"));
    }

    [Fact]
    public void Is_compare_string_ordering_throws()
    {
        Sharpex.VarProvider = new TestVarProvider { Vars = { ["name"] = "Alice" } };
        Assert.Throws<FormatException>(() => Sharpex.Eval("#is $name > \"Bob\""));
    }

    [Fact]
    public void Is_compare_type_mismatch_throws()
    {
        Sharpex.VarProvider = new TestVarProvider { Vars = { ["money"] = 42 } };
        Assert.Throws<FormatException>(() => Sharpex.Eval("#is $money >= \"test\""));
    }

    [Fact]
    public void Is_negated_comparison()
    {
        Sharpex.VarProvider = new TestVarProvider { Vars = { ["money"] = 42 } };
        Assert.True(Sharpex.Eval("~is $money > 100"));
    }

    [Fact]
    public void Is_comparison_in_conditional()
    {
        money = 20;
        Sharpex.VarProvider = new TestVarProvider { Vars = { ["threshold"] = 10 } };

        var result = Sharpex.Eval("#is $threshold >= 10 ? #pay 5 : #pay 1");

        Assert.True(result);
        Assert.Equal(15, money);
    }

    [Fact]
    public void Is_unknown_operator_throws()
    {
        Sharpex.VarProvider = new TestVarProvider { Vars = { ["money"] = 42 } };
        Assert.Throws<FormatException>(() => Sharpex.Eval("#is $money != 10"));
    }

    [Fact]
    public void Is_float_comparison()
    {
        Sharpex.VarProvider = new TestVarProvider { Vars = { ["temperature"] = 36.6 } };
        Assert.True(Sharpex.Eval("#is $temperature > 36"));
    }
}