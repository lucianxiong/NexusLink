using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace ServiceLib.Common;

public static class Extension
{
    public static bool IsNullOrEmpty([NotNullWhen(false)] this string? value)
    {
        return string.IsNullOrWhiteSpace(value) || string.IsNullOrEmpty(value);
    }

    public static bool IsNotEmpty([NotNullWhen(false)] this string? value)
    {
        return !string.IsNullOrWhiteSpace(value);
    }

    public static string? NullIfEmpty(this string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }


    private static bool IsWhiteSpace(this string value)
    {
        return value.All(char.IsWhiteSpace);
    }


    public static string TrimEx(this string? value)
    {
        return value?.Trim() ?? string.Empty;
    }

    public static string RemovePrefix(this string value, char prefix)
    {
        return value.StartsWith(prefix) ? value[1..] : value;
    }

    public static string RemovePrefix(this string value, string prefix)
    {
        return value.StartsWith(prefix) ? value[prefix.Length..] : value;
    }


    public static string AppendQuotes(this string value)
    {
        return string.IsNullOrEmpty(value) ? string.Empty : $"\"{value}\"";
    }

    public static int ToInt(this string? value, int defaultValue = 0)
    {
        return int.TryParse(value, out var result) ? result : defaultValue;
    }


    public static bool IsGroupType(this EConfigType configType)
    {
        return configType is EConfigType.PolicyGroup or EConfigType.ProxyChain;
    }

    public static bool IsComplexType(this EConfigType configType)
    {
        return configType is EConfigType.Custom or EConfigType.Outbound or EConfigType.PolicyGroup or EConfigType.ProxyChain;
    }

    /// <summary>
    /// Safely adds elements from a collection to the list. Does nothing if the source is null.
    /// </summary>
    public static void AddRangeSafe<T>(this ICollection<T> destination, IEnumerable<T>? source)
    {
        ArgumentNullException.ThrowIfNull(destination);

        if (source is null)
        {
            return;
        }

        if (destination is List<T> list)
        {
            list.AddRange(source);
            return;
        }

        foreach (var item in source)
        {
            destination.Add(item);
        }
    }

    /// <summary>
    /// Replace all cross-platform newline characters with the specified string
    /// </summary>
    public static string ReplaceLineBreaks(this string input, string replacement)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        // You must replace \r\n first, and then replace the single characters \r and \n.
        return input.Replace("\r\n", replacement)
                    .Replace("\r", replacement)
                    .Replace("\n", replacement);
    }

    public static IObservable<TOutput> HandleSafe<TInput, TOutput>(
        this Interaction<TInput, TOutput> interaction, TInput input,
        TOutput defaultValue = default!,
        [CallerMemberName] string memberName = "",
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0)
    {
        return Signal.Defer(() => interaction.Handle(input))
            .Catch<TOutput, UnhandledInteractionException<TInput, TOutput>>(_ =>
            {
                // 没有视图注册这个交互 = 对应页面没加载（极简界面常态），属预期情况，不记日志
                return Signal.Return(defaultValue);
            })
            .Catch<TOutput, Exception>(ex =>
            {
                Logging.SaveLog($"Exception occurred while handling interaction in {memberName} at {filePath}:{lineNumber}, input: {input}", ex);
                return Signal.Return(defaultValue);
            });
    }
}
