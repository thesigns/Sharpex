using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;

namespace Sharpex;

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

    public static bool Eval(string source)
    {
        var tokens = Tokenize(source.AsSpan().TrimStart('#'));

        var name = tokens[0];

        if (!Functions.TryGetValue(name, out var entry))
            throw new Exception($"Sharpex function '{name}' not found");

        var args = new object?[entry.Parameters.Length];
        for (var i = 0; i < args.Length; i++)
            args[i] = Convert.ChangeType(tokens[i + 1], entry.Parameters[i].ParameterType);

        return entry.Fn(args);
    }
}