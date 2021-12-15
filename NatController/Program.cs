using System;
using System.Threading.Tasks;
using NatController.Nat;
using NatController.Nat.Upnp;

namespace NatController;

internal class Program
{
    private static async Task Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (sender, eventArgs) =>
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(eventArgs.ExceptionObject.ToString());
            Console.ResetColor();
            Console.ReadKey(true);
            Environment.Exit((eventArgs.ExceptionObject as UpnpException)?.Code ?? 1);
        };

        Console.WriteLine(await UpnpApi.GetExternalIpAsync());

        Console.WriteLine("Press any key to continue . . .");
        Console.ReadKey(true);
    }
}