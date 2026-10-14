namespace CirDeck.Core.Tests;

internal static class Program
{
    public static int Main()
    {
        DockTests.Run();
        SearchTests.Run();
        SettingsTests.Run();
        return TestHost.Finish();
    }
}

internal static class TestHost
{
    private static int _failed;

    public static void Check(bool condition, string message)
    {
        if (condition)
        {
            return;
        }

        _failed++;
        Console.Error.WriteLine("FAIL " + message);
    }

    public static void Near(double actual, double expected, string message)
    {
        Check(Math.Abs(actual - expected) < 0.001, message + " actual=" + actual + " expected=" + expected);
    }

    public static int Finish()
    {
        if (_failed == 0)
        {
            Console.WriteLine("ok");
            return 0;
        }

        Console.Error.WriteLine(_failed + " failed");
        return 1;
    }
}
