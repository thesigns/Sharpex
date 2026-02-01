# Sharpex

A lightweight behavior scripting DSL for C#. Define boolean functions in your code, then compose them into expressions with AND, OR, NOT, conditionals, sequencing, and time delays — all from a single string.

Works with .NET and Unity.

## Defining functions

Mark any static method returning `bool` with the `[Sharpex]` attribute. Functions are discovered automatically at startup via reflection.

```csharp
[Sharpex("pay")]
static bool Pay(int amount)
{
    if (amount > money) return false;
    money -= amount;
    return true;
}

[Sharpex("has-item")]
static bool HasItem(string itemId)
{
    return inventory.Contains(itemId);
}

[Sharpex("log")]
static bool Log(string message)
{
    Console.WriteLine(message);
    return true;
}
```

Parameters are automatically converted from strings using `Convert.ChangeType`. Any number and combination of parameter types is supported.

## Syntax

### Calls

```
#pay 10           call pay with argument 10
#log "hello world" call log with a quoted string argument
~pay 10           NOT pay — calls pay, negates the result
```

`#` starts a function call. `~` starts a negated call (`NOT`). Arguments follow the function name, separated by spaces. Strings containing spaces or special characters (`# " | > ? :`) must be quoted. Use `""` inside quotes for a literal `"`.

### AND (implicit)

```
#pay 10 #pay 5
```

Multiple calls in sequence are AND-ed together with short-circuit evaluation. If `#pay 10` returns `false`, `#pay 5` is skipped.

### OR (`|`)

```
#pay 10 | #pay 5
```

OR with short-circuit. If `#pay 10` returns `true`, `#pay 5` is skipped.

AND has higher precedence than OR:

```
#pay 10 #log "paid" | #log "not enough"
→ (#pay 10 AND #log "paid") OR #log "not enough"
```

### Conditionals (`?` `:`)

```
#has-item sword ? #log "You have a sword"
#has-item sword ? #log "Armed" : #log "Unarmed"
```

If the condition is true, the `?` branch executes. If false and a `:` branch exists, that executes instead. Without `:`, a false condition returns `false`. Each branch supports full AND/OR expressions. No nesting — max one `?` and one `:` per group.

### Groups (`>`)

```
#pay 10 > #log "paid"
```

Groups execute sequentially. The result is the result of the **last group**. All groups execute regardless of previous results.

### Time delays (`[n]`)

```
#log "now" [2] #log "2 seconds later" [3] #log "5 seconds later"
```

`[n]` splits the expression into time-delayed super groups. Delays accumulate: the third group above executes at 2 + 3 = 5 seconds. `[0]` is not allowed — omit the delay token for immediate execution.

### Operator precedence (highest to lowest)

| Operator | Syntax | Description |
|----------|--------|-------------|
| Call | `#name` / `~name` | Function call / negated call |
| AND | (space) | Short-circuit AND |
| OR | `\|` | Short-circuit OR |
| Conditional | `? :` | If-then-else |
| Group | `>` | Sequential group |
| Delay | `[n]` | Time-delayed super group |

### Full example

```
#has-item key ? #open-door #log "Door opened" : #log "Need a key"
> #pay 5 | #log "Not enough gold"
[2] #log "2 seconds later..."
```

## API

### `Sharpex.Eval(string source)`

Evaluates an expression synchronously. Throws `InvalidOperationException` if the expression contains time delays.

```csharp
bool result = Sharpex.Eval("#pay 10 #log \"paid\"");
```

### `Sharpex.EvalAsync(string source, Func<float, Task> delay)`

Evaluates an expression asynchronously, awaiting the provided delay function between time-delayed super groups.

```csharp
// .NET
bool result = await Sharpex.EvalAsync(
    "#log \"now\" [2] #log \"later\"",
    seconds => Task.Delay(TimeSpan.FromSeconds(seconds)));

// Unity
bool result = await Sharpex.EvalAsync(
    "#log \"now\" [2] #log \"later\"",
    async seconds => await Awaitable.WaitForSecondsAsync(seconds));
```

## License

Public domain ([Unlicense](https://unlicense.org/)).
