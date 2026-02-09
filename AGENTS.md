# Sharpex

Behavior scripting DSL for C#. A single-file library (`Sharpex/Sharpex.cs`) that evaluates boolean expressions composed of user-defined functions. Designed for .NET.

## Design intent

The DSL is designed for one-liner behavior scripts stored in game data spreadsheets (CSV, Excel, Google Sheets). Expressions live in columns like `OnOpen`, `OnUse`, `OnTalk` and are evaluated at runtime via `Eval`/`EvalAsync`. The syntax is intentionally compact to fit in a single cell.

## Unity compatibility

The code MUST compile in Unity (C# 9 / .NET Standard 2.1). Do not use:

- **Primary constructors** (C# 12) — use traditional constructors
- **Collection expressions** `[...]` (C# 12) — use `new List<T>()`, `new HashSet<T> { ... }`, `Array.Empty<T>()`
- **Records** — use sealed classes (records require `IsExternalInit` which is unreliable in Unity/IL2CPP)

The file includes `#nullable enable` at the top because Unity projects don't enable nullable globally.

## Project structure

```
Sharpex/Sharpex.cs          — entire library (tokenizer, parser, evaluator)
Sharpex/Sharpex.csproj      — .NET 10, nullable enabled
Sharpex.Tests/              — xUnit tests
  TokenizeTests.cs           — tokenizer tests
  EvalTests.cs               — end-to-end eval tests (AND, OR, NOT, groups, conditionals, delays)
Sharpex.Playground/         — scratch project for manual testing
```

## Build and test

```
dotnet build
dotnet test
```

All tests must pass before any change is considered complete.

## Architecture

Everything lives in a single static class `Sharpex` in `Sharpex/Sharpex.cs`. The pipeline is:

```
source string → Tokenize → Parse → Validate / Eval / EvalAsync
```

### Function registration

Static methods marked with `[Sharpex("name")]` are discovered via reflection at static constructor time. Each must return `bool`. Delegates are compiled from expression trees for fast invocation (no `MethodInfo.Invoke`). Registered in `Dictionary<string, (Func<object?[], bool> Fn, ParameterInfo[] Parameters)>`. Reserved names (`is`, `set`) cannot be used — throws `InvalidOperationException`.

### Variable provider (`ISharpexVar`)

`ISharpexVar` interface with `GetValue(string name)` and `SetValue(string name, object? value)`. Set via static property `Sharpex.VarProvider`. Used by built-in `#is` to read variable values. `$name` tokens are a naming convention for variable references in arguments — not a parser-level operator.

### Tokenizer (`Tokenize`)

Splits input into tokens by whitespace. Supports quoted strings (`"..."`) with `""` escape for literal quotes. Returns `List<string>`. Does not interpret `#`, `~`, `$`, `;`, `|`, `?`, `:`, `[n]` — those are just tokens.

### Parser (`Parse`, `ParseGroups`, `ParseOrExpr`)

Three-level parsing:

1. **`Parse`** — splits by `[n]` delay tokens into `List<ParsedSuperGroup>`. Each super group has a `float Delay` and `List<ParsedGroup>`.
2. **`ParseGroups`** — splits by `;` into groups, then by `?` / `:` into conditionals. Returns `List<ParsedGroup>` where each has Condition, optional Then, optional Else.
3. **`ParseOrExpr`** — splits by `|` into OR clauses, each containing AND-ed calls. Each call is `(string Name, List<string> Args, bool Negated)`.

Operator precedence (highest first): `#`/`~` call → AND (space) → OR (`|`) → conditional (`? :`) → group (`;`) → delay (`[n]`).

### Evaluator (`Eval`, `EvalAsync`, `EvalGroups`, `EvalOrExpr`, `ExecuteCall`)

- `Validate(string)` — checks syntax, function names, argument counts, and built-in format rules without executing. Does not require `VarProvider`. Throws `FormatException`/`KeyNotFoundException` on errors.
- `IsDelayed(string)` — returns true if expression contains `[n]` delays. Use to choose between `Eval` and `EvalAsync`.
- `Eval(string)` — synchronous. Throws `InvalidOperationException` if delays > 0.
- `EvalAsync(string, Func<float, Task>)` — async. Caller provides the delay implementation.
- AND: short-circuit on false.
- OR: short-circuit on true.
- Groups (`;`): all execute, result = last.
- Conditionals (`? :`): condition true → then branch; false → else branch (or false if no else).
- Delays (`[n]`): accumulated sequentially. `[0]` is disallowed (`FormatException`).
- NOT (`~`): negates the result of a single call.
- Built-in `#is`: 1 arg (`$var`) → truthiness via `IsTruthy`. 3 args (`$var op value`) → comparison. Operators `>`, `<`, `>=`, `<=` require numeric operands. `==` and `!=` work on all types. Right side can be literal or `$var`. Null left side → `FormatException`.
- Built-in `#set`: 3 args (`$var op value`). `=` assigns (converts to existing type or auto-detects via `ParseLiteral`). `+=`, `-=`, `*=`, `/=` compound on numeric. Always returns `true`.

Arguments starting with `$` are resolved via `VarProvider.GetValue` before conversion. Other arguments are converted via `Convert.ChangeType` with `CultureInfo.InvariantCulture` using the target method's `ParameterInfo`. Argument count is validated — mismatch throws `FormatException`.

## DSL syntax reference

```
#name args...         function call
#name $var            $var resolved via VarProvider
~name args...         negated call (NOT)
#a #b                 AND (short-circuit)
#a | #b               OR (short-circuit)
#cond ? #then         conditional (no else → false on fail)
#cond ? #then : #else conditional with else
#is $var              variable truthiness (built-in)
#is $var > 10         comparison (>, <, >=, <=, ==, !=)
#is $var == $other    compare two variables
#set $var = 10        assign variable (built-in)
#set $var += 5        compound assign (+=, -=, *=, /=)
#a ; #b               groups (result = last)
[n] #a                delay n seconds (n > 0, accumulates)
```

Strings with spaces or special chars (`# $ " | ; ? :`) must be quoted. `""` inside quotes = literal `"`.

## Key conventions

- The library is intentionally a single file. Do not split it into multiple files.
- `internal` methods (`Tokenize`, `Parse`, `ParseGroups`, `ParseOrExpr`) are exposed for testing via `InternalsVisibleTo("Sharpex.Tests")`.
- Parser uses `SplitTokens` helper and local `FlushCall`/`FlushOr` functions for state machine parsing.
- Tests use a `[Sharpex("pay")]` function with side effects (mutates `static int money`) to verify both return values and execution/short-circuit behavior.
- Test names follow the pattern `Feature_scenario_expected_result`.

## Common pitfalls

- The static constructor scans all loaded assemblies. Some assemblies (e.g. test runners) may throw `TypeLoadException` or `ReflectionTypeLoadException` — these are caught and skipped silently.
- `[0]` delay is a `FormatException`, not silently ignored.
- `;` inside a conditional (between `?` and `:`) is a parse error — conditionals must stay within a single group.
- `:` without a preceding `?` is a parse error.
- Multiple `?` in one group is a parse error.
- Wrong number of arguments throws `FormatException` with expected vs actual count.
- Unknown function name throws `KeyNotFoundException`.
- Duplicate `[Sharpex("name")]` across methods throws `InvalidOperationException` at startup.
- `[Sharpex("is")]` or `[Sharpex("set")]` throws `InvalidOperationException` — reserved built-in names.
- Using `#is`/`#set` without setting `VarProvider` throws `InvalidOperationException`.
- `#is` without a `$` prefix argument throws `FormatException`.
- `#set` compound operators on non-numeric or null variables throws `FormatException`.
