using PhoneSimulator.Scenarios;

namespace PhoneSimulator;

public class Program
{
    public static async Task Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("Usage: PhoneSimulator <console-log-path> [phonePort=8443] [apiPort=8780]");
            Environment.ExitCode = 2;
            return;
        }

        var phonePort = args.Length > 1 ? int.Parse(args[1]) : 8443;
        var apiPort = args.Length > 2 ? int.Parse(args[2]) : 8780;
        var smokeTest = new SmokeTest(args[0], phonePort, apiPort);
        Environment.ExitCode = await smokeTest.RunAsync() ? 0 : 1;
    }
}
