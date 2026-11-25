using System.Reflection;

namespace Gomoku.Checks;

[AttributeUsage(AttributeTargets.Method)]
internal sealed class CheckAttribute : Attribute
{
}

internal static partial class Program
{
    private static int Main()
    {
        var checks = typeof(Program).GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Where(method => method.IsDefined(typeof(CheckAttribute)))
            .OrderBy(method => method.Name, StringComparer.Ordinal)
            .Select(method => method.CreateDelegate<Action>())
            .ToArray();

        var failures = 0;
        foreach (var check in checks)
        {
            try
            {
                check();
                Console.WriteLine($"PASS {check.Method.Name}");
            }
            catch (Exception error)
            {
                failures++;
                Console.Error.WriteLine($"FAIL {check.Method.Name}: {error.Message}");
            }
        }

        Console.WriteLine($"{checks.Length - failures}/{checks.Length} checks passed.");
        return failures == 0 ? 0 : 1;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
