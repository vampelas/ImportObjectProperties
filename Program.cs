using System;
using VDF = Autodesk.DataManagement.Client.Framework;

namespace ImportObjectProperties
{
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                ApplicationOptions options = ApplicationOptions.Parse(args);
                Application app = new Application();

                System.Net.ServicePointManager.Expect100Continue = false;
                Application.PrintHeader();
                app.Run(options);
                //SeriLog.LogInformation("TEST SERILOG");
            }
            catch (Exception ex)
            {
                if (ex is ArgumentException)
                {
                    Application.PrintHelp();
                    Console.WriteLine("Press ESC to stop");
                    do
                    {
                        while (!Console.KeyAvailable)
                        {
                            // Do something
                        }
                    } while (Console.ReadKey(true).Key != ConsoleKey.Escape);
                }
                else
                {
                    //SeriLog.LogError(VDF.Library.ExceptionParser.GetMessage(ex));
                    Console.WriteLine("ERROR: {0}", VDF.Library.ExceptionParser.GetMessage(ex));
                }
            }
        }
    }
}
