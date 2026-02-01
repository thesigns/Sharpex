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
    public sealed class SharpexAttribute(string name) : Attribute
    {
        public string Name { get; } = name;
    }

    public static class Sharpex
    {
        private static readonly Dictionary<string, (Func<object?[], bool> Fn, ParameterInfo[] Parameters)> Functions = new();

        static Sharpex()
        {
            foreach (var method in AppDomain.CurrentDomain
                         .GetAssemblies()
                         .SelectMany(a =>
                         {
                             try { return a.GetTypes(); }
                             catch (ReflectionTypeLoadException) { return []; }
                         })
                         .SelectMany(t => t.GetMethods(
                             BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)))
            {
                SharpexAttribute? attr;
                try { attr = method.GetCustomAttribute<SharpexAttribute>(); }
                catch (TypeLoadException) { continue; }

                if (attr == null)
                    continue;

                if (method.ReturnType != typeof(bool))
                    throw new Exception($"{method.Name} must return bool");

                var parameters = method.GetParameters();
                var fn = BuildDelegate(method, parameters);
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

        internal record ParsedGroup(
            List<List<(string Name, List<string> Args, bool Negated)>> Condition,
            List<List<(string Name, List<string> Args, bool Negated)>>? Then,
            List<List<(string Name, List<string> Args, bool Negated)>>? Else);

        internal record ParsedSuperGroup(float Delay, List<ParsedGroup> Groups);

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
                        currentTokens = [];
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

            // split tokens by ">"
            var groupTokenLists = SplitTokens(tokens, ">");

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
                    current = [];
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
                currentAnd = [];
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
                    currentArgs = [];
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

        private static bool ExecuteCall(string name, List<string> args)
        {
            if (!Functions.TryGetValue(name, out var entry))
                throw new Exception($"Sharpex function '{name}' not found");

            var converted = new object?[entry.Parameters.Length];
            for (var i = 0; i < converted.Length; i++)
                converted[i] = Convert.ChangeType(args[i], entry.Parameters[i].ParameterType, CultureInfo.InvariantCulture);

            return entry.Fn(converted);
        }
    }
}
