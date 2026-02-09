using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Sharpex
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class SharpexAttribute : Attribute
    {
        public string Name { get; }
        public SharpexAttribute(string name) { Name = name; }
    }

    public interface ISharpexVar
    {
        object? GetValue(string name);
        void SetValue(string name, object? value);
    }

    public static class Sharpex
    {
        private static readonly Dictionary<string, (Func<object?[], bool> Fn, ParameterInfo[] Parameters)> Functions = new();
        private static readonly HashSet<string> ReservedNames = new HashSet<string> { "is", "set" };

        public static ISharpexVar? VarProvider { get; set; }

        static Sharpex()
        {
            foreach (var method in AppDomain.CurrentDomain
                         .GetAssemblies()
                         .SelectMany(a =>
                         {
                             try { return a.GetTypes(); }
                             catch (ReflectionTypeLoadException) { return Array.Empty<Type>(); }
                         })
                         .SelectMany(t => t.GetMethods(
                             BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)))
            {
                SharpexAttribute? attr;
                try { attr = method.GetCustomAttribute<SharpexAttribute>(); }
                catch (TypeLoadException) { continue; }

                if (attr == null)
                    continue;

                if (ReservedNames.Contains(attr.Name))
                    throw new InvalidOperationException($"'{attr.Name}' is a reserved built-in function");

                if (method.ReturnType != typeof(bool))
                    throw new InvalidOperationException($"{method.Name} must return bool");

                var parameters = method.GetParameters();
                var fn = BuildDelegate(method, parameters);
                if (Functions.ContainsKey(attr.Name))
                    throw new InvalidOperationException(
                        $"Duplicate Sharpex function '{attr.Name}' (method '{method.Name}')");
                Functions[attr.Name] = (fn, parameters);
            }
        }

        private static Func<object?[], bool> BuildDelegate(MethodInfo method, ParameterInfo[] parameters)
        {
            var argsParam = Expression.Parameter(typeof(object?[]), "args");

            var convertedArgs = new Expression[parameters.Length];
            for (var i = 0; i < parameters.Length; i++)
            {
                convertedArgs[i] = Expression.Convert(
                    Expression.ArrayIndex(argsParam, Expression.Constant(i)),
                    parameters[i].ParameterType);
            }

            var call = Expression.Call(method, convertedArgs);
            return Expression.Lambda<Func<object?[], bool>>(call, argsParam).Compile();
        }

        internal static List<string> Tokenize(ReadOnlySpan<char> input)
        {
            var tokens = new List<string>();
            var sb = new StringBuilder();
            var i = 0;

            while (i < input.Length)
            {
                // skip whitespace between tokens
                if (char.IsWhiteSpace(input[i]))
                {
                    i++;
                    continue;
                }

                sb.Clear();

                if (input[i] == '"')
                {
                    // quoted token
                    i++; // skip opening quote
                    while (true)
                    {
                        if (i >= input.Length)
                            throw new FormatException("Unterminated quoted string");

                        if (input[i] == '"')
                        {
                            if (i + 1 < input.Length && input[i + 1] == '"')
                            {
                                sb.Append('"');
                                i += 2;
                            }
                            else
                            {
                                i++; // skip closing quote
                                break;
                            }
                        }
                        else
                        {
                            sb.Append(input[i]);
                            i++;
                        }
                    }
                }
                else
                {
                    // unquoted token
                    while (i < input.Length && !char.IsWhiteSpace(input[i]))
                    {
                        sb.Append(input[i]);
                        i++;
                    }
                }

                tokens.Add(sb.ToString());
            }

            return tokens;
        }

        internal sealed class ParsedGroup
        {
            public List<List<(string Name, List<string> Args, bool Negated)>> Condition { get; }
            public List<List<(string Name, List<string> Args, bool Negated)>>? Then { get; }
            public List<List<(string Name, List<string> Args, bool Negated)>>? Else { get; }

            public ParsedGroup(
                List<List<(string Name, List<string> Args, bool Negated)>> condition,
                List<List<(string Name, List<string> Args, bool Negated)>>? then,
                List<List<(string Name, List<string> Args, bool Negated)>>? @else)
            {
                Condition = condition;
                Then = then;
                Else = @else;
            }
        }

        internal sealed class ParsedSuperGroup
        {
            public float Delay { get; }
            public List<ParsedGroup> Groups { get; }

            public ParsedSuperGroup(float delay, List<ParsedGroup> groups)
            {
                Delay = delay;
                Groups = groups;
            }
        }

        private static bool TryParseDelay(string token, out float delay)
        {
            delay = 0;
            if (token.Length < 3 || token[0] != '[' || token[^1] != ']')
                return false;
            return float.TryParse(token[1..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out delay);
        }

        internal static List<ParsedSuperGroup> Parse(List<string> tokens)
        {
            var superGroups = new List<ParsedSuperGroup>();
            var currentTokens = new List<string>();
            var currentDelay = 0f;

            foreach (var token in tokens)
            {
                if (TryParseDelay(token, out var delay))
                {
                    if (delay <= 0)
                        throw new FormatException("Delay must be greater than 0");

                    if (currentTokens.Count > 0)
                    {
                        superGroups.Add(new ParsedSuperGroup(currentDelay, ParseGroups(currentTokens)));
                        currentTokens = new List<string>();
                    }

                    currentDelay = delay;
                }
                else
                {
                    currentTokens.Add(token);
                }
            }

            if (currentTokens.Count > 0 || currentDelay > 0)
                superGroups.Add(new ParsedSuperGroup(currentDelay, ParseGroups(currentTokens)));

            return superGroups;
        }

        internal static List<ParsedGroup> ParseGroups(List<string> tokens)
        {
            var groups = new List<ParsedGroup>();

            // split tokens by ";"
            var groupTokenLists = SplitTokens(tokens, ";");

            foreach (var groupTokens in groupTokenLists)
            {
                var sections = SplitTokens(groupTokens, "?");
                if (sections.Count > 2)
                    throw new FormatException("Multiple '?' in a single group");

                if (sections.Count == 1)
                {
                    // no conditional — check for stray ":"
                    if (groupTokens.Contains(":"))
                        throw new FormatException("':' without '?' in group");
                    groups.Add(new ParsedGroup(ParseOrExpr(sections[0]), null, null));
                }
                else
                {
                    var conditionTokens = sections[0];
                    var bodyTokens = sections[1];

                    var branches = SplitTokens(bodyTokens, ":");
                    if (branches.Count > 2)
                        throw new FormatException("Multiple ':' in a single conditional");

                    var thenExpr = ParseOrExpr(branches[0]);
                    var elseExpr = branches.Count == 2 ? ParseOrExpr(branches[1]) : null;

                    groups.Add(new ParsedGroup(ParseOrExpr(conditionTokens), thenExpr, elseExpr));
                }
            }

            return groups;
        }

        private static List<List<string>> SplitTokens(List<string> tokens, string separator)
        {
            var result = new List<List<string>>();
            var current = new List<string>();
            foreach (var token in tokens)
            {
                if (token == separator)
                {
                    result.Add(current);
                    current = new List<string>();
                }
                else
                {
                    current.Add(token);
                }
            }

            if (current.Count > 0 || result.Count > 0)
                result.Add(current);

            return result;
        }

        internal static List<List<(string Name, List<string> Args, bool Negated)>> ParseOrExpr(List<string> tokens)
        {
            var orClauses = new List<List<(string Name, List<string> Args, bool Negated)>>();
            var currentAnd = new List<(string Name, List<string> Args, bool Negated)>();
            string? currentName = null;
            List<string>? currentArgs = null;

            var currentNegated = false;

            void FlushCall()
            {
                if (currentName != null)
                    currentAnd.Add((currentName, currentArgs!, currentNegated));
                currentName = null;
                currentArgs = null;
                currentNegated = false;
            }

            void FlushOr()
            {
                FlushCall();
                if (currentAnd.Count > 0)
                    orClauses.Add(currentAnd);
                currentAnd = new List<(string Name, List<string> Args, bool Negated)>();
            }

            foreach (var token in tokens)
            {
                if (token == "|")
                {
                    FlushOr();
                }
                else if (token.StartsWith('#') || token.StartsWith('~'))
                {
                    FlushCall();
                    currentNegated = token[0] == '~';
                    currentName = token[1..];
                    currentArgs = new List<string>();
                }
                else
                {
                    if (currentArgs == null)
                        throw new FormatException($"Unexpected token '{token}' outside of a sharpex call");
                    currentArgs.Add(token);
                }
            }

            FlushOr();
            return orClauses;
        }

        public static bool IsDelayed(string source) =>
            Parse(Tokenize(source)).Any(sg => sg.Delay > 0);

        private static readonly HashSet<string> IsOperators = new HashSet<string> { "==", "!=", ">", "<", ">=", "<=" };
        private static readonly HashSet<string> SetOperators = new HashSet<string> { "=", "+=", "-=", "*=", "/=" };

        public static void Validate(string source)
        {
            var superGroups = Parse(Tokenize(source));
            foreach (var sg in superGroups)
                foreach (var group in sg.Groups)
                {
                    ValidateCalls(group.Condition);
                    if (group.Then != null) ValidateCalls(group.Then);
                    if (group.Else != null) ValidateCalls(group.Else);
                }
        }

        private static void ValidateCalls(
            List<List<(string Name, List<string> Args, bool Negated)>> orExpr)
        {
            foreach (var andClause in orExpr)
                foreach (var (name, args, _) in andClause)
                    ValidateCall(name, args);
        }

        private static void ValidateCall(string name, List<string> args)
        {
            if (name == "is")
            {
                if (args.Count != 1 && args.Count != 3)
                    throw new FormatException(
                        $"Function 'is' expects 1 or 3 arguments, got {args.Count}");
                if (!args[0].StartsWith('$'))
                    throw new FormatException("Function 'is' expects a $variable argument");
                if (args.Count == 3 && !IsOperators.Contains(args[1]))
                    throw new FormatException($"Unknown operator '{args[1]}'");
                return;
            }

            if (name == "set")
            {
                if (args.Count != 3)
                    throw new FormatException(
                        $"Function 'set' expects 3 arguments, got {args.Count}");
                if (!args[0].StartsWith('$'))
                    throw new FormatException("Function 'set' expects a $variable as first argument");
                if (!SetOperators.Contains(args[1]))
                    throw new FormatException($"Unknown operator '{args[1]}'");
                return;
            }

            if (!Functions.TryGetValue(name, out var entry))
                throw new KeyNotFoundException($"Sharpex function '{name}' not found");

            if (args.Count != entry.Parameters.Length)
                throw new FormatException(
                    $"Function '{name}' expects {entry.Parameters.Length} argument(s), got {args.Count}");
        }

        public static bool Eval(string source)
        {
            var superGroups = Parse(Tokenize(source));
            if (superGroups.Any(sg => sg.Delay > 0))
                throw new InvalidOperationException("Use EvalAsync for delayed expressions");

            var result = false;
            foreach (var sg in superGroups)
                result = EvalGroups(sg.Groups);
            return result;
        }

        public static async Task<bool> EvalAsync(string source, Func<float, Task> delay)
        {
            var superGroups = Parse(Tokenize(source));
            var result = false;
            foreach (var sg in superGroups)
            {
                if (sg.Delay > 0)
                    await delay(sg.Delay);
                result = EvalGroups(sg.Groups);
            }

            return result;
        }

        private static bool EvalGroups(List<ParsedGroup> groups)
        {
            var result = false;
            foreach (var group in groups)
            {
                var condResult = EvalOrExpr(group.Condition);
                if (group.Then == null)
                    result = condResult;
                else if (condResult)
                    result = EvalOrExpr(group.Then);
                else if (group.Else != null)
                    result = EvalOrExpr(group.Else);
                else
                    result = false;
            }

            return result;
        }

        private static bool EvalOrExpr(List<List<(string Name, List<string> Args, bool Negated)>> orExpr)
        {
            var result = false;
            foreach (var andClause in orExpr)
            {
                var andResult = true;
                foreach (var (name, args, negated) in andClause)
                {
                    andResult = ExecuteCall(name, args);
                    if (negated) andResult = !andResult;
                    if (!andResult)
                        break;
                }

                result = andResult;
                if (result)
                    break;
            }

            return result;
        }

        internal static bool IsTruthy(object? value) => value switch
        {
            null => false,
            bool b => b,
            string s => s.Length > 0,
            IConvertible c => c.ToDouble(CultureInfo.InvariantCulture) != 0,
            _ => true
        };

        private static object? ResolveValue(string token, object? typeHint)
        {
            if (token.StartsWith('$'))
            {
                if (VarProvider == null)
                    throw new InvalidOperationException("VarProvider is not set");
                return VarProvider.GetValue(token[1..]);
            }

            if (typeHint == null)
                return token;
            return Convert.ChangeType(token, typeHint.GetType(), CultureInfo.InvariantCulture);
        }

        private static object ParseLiteral(string token)
        {
            if (bool.TryParse(token, out var b)) return b;
            if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) return i;
            if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return d;
            return token;
        }

        private static bool IsNumeric(object? value) =>
            value is byte or sbyte or short or ushort or int or uint or
            long or ulong or float or double or decimal;

        private static double ToDouble(object? value) =>
            ((IConvertible)value!).ToDouble(CultureInfo.InvariantCulture);

        internal static bool Compare(object? left, string op, object? right)
        {
            if (left == null)
                throw new FormatException("Cannot compare null variable");

            if (op == "==" || op == "!=")
            {
                var eq = Equals(left, right) ||
                         (IsNumeric(left) && IsNumeric(right) &&
                          ToDouble(left) == ToDouble(right));
                return op == "==" ? eq : !eq;
            }

            if (!IsNumeric(left) || !IsNumeric(right))
                throw new FormatException($"Operator '{op}' requires numeric operands");

            var l = ToDouble(left);
            var r = ToDouble(right);
            return op switch
            {
                ">" => l > r,
                "<" => l < r,
                ">=" => l >= r,
                "<=" => l <= r,
                _ => throw new FormatException($"Unknown operator '{op}'")
            };
        }

        private static bool ExecuteCall(string name, List<string> args)
        {
            if (name == "is")
            {
                if (VarProvider == null)
                    throw new InvalidOperationException("VarProvider is not set");
                if (args.Count != 1 && args.Count != 3)
                    throw new FormatException(
                        $"Function 'is' expects 1 or 3 arguments, got {args.Count}");
                if (!args[0].StartsWith('$'))
                    throw new FormatException("Function 'is' expects a $variable argument");

                if (args.Count == 1)
                    return IsTruthy(VarProvider.GetValue(args[0][1..]));

                var left = VarProvider.GetValue(args[0][1..]);
                var op = args[1];
                var right = ResolveValue(args[2], left);
                return Compare(left, op, right);
            }

            if (name == "set")
            {
                if (VarProvider == null)
                    throw new InvalidOperationException("VarProvider is not set");
                if (args.Count != 3)
                    throw new FormatException(
                        $"Function 'set' expects 3 arguments, got {args.Count}");
                if (!args[0].StartsWith('$'))
                    throw new FormatException("Function 'set' expects a $variable as first argument");

                var varName = args[0][1..];
                var op = args[1];
                var existing = VarProvider.GetValue(varName);

                if (op == "=")
                {
                    object? value;
                    if (args[2].StartsWith('$'))
                        value = ResolveValue(args[2], null);
                    else if (existing != null)
                        value = Convert.ChangeType(args[2], existing.GetType(), CultureInfo.InvariantCulture);
                    else
                        value = ParseLiteral(args[2]);
                    VarProvider.SetValue(varName, value);
                    return true;
                }

                if (existing == null)
                    throw new FormatException($"Variable '{varName}' is not set");
                if (!IsNumeric(existing))
                    throw new FormatException($"Operator '{op}' requires a numeric variable");

                var right = ResolveValue(args[2], existing);
                if (!IsNumeric(right))
                    throw new FormatException($"Operator '{op}' requires numeric operands");

                var l = ToDouble(existing);
                var r = ToDouble(right);
                var result = op switch
                {
                    "+=" => l + r,
                    "-=" => l - r,
                    "*=" => l * r,
                    "/=" => l / r,
                    _ => throw new FormatException($"Unknown operator '{op}'")
                };

                VarProvider.SetValue(varName,
                    Convert.ChangeType(result, existing.GetType(), CultureInfo.InvariantCulture));
                return true;
            }

            if (!Functions.TryGetValue(name, out var entry))
                throw new KeyNotFoundException($"Sharpex function '{name}' not found");

            if (args.Count != entry.Parameters.Length)
                throw new FormatException(
                    $"Function '{name}' expects {entry.Parameters.Length} argument(s), got {args.Count}");

            var converted = new object?[entry.Parameters.Length];
            for (var i = 0; i < converted.Length; i++)
            {
                if (args[i].StartsWith('$'))
                {
                    if (VarProvider == null)
                        throw new InvalidOperationException("VarProvider is not set");
                    var val = VarProvider.GetValue(args[i][1..]);
                    converted[i] = Convert.ChangeType(val, entry.Parameters[i].ParameterType, CultureInfo.InvariantCulture);
                }
                else
                {
                    converted[i] = Convert.ChangeType(args[i], entry.Parameters[i].ParameterType, CultureInfo.InvariantCulture);
                }
            }

            return entry.Fn(converted);
        }
    }
}
