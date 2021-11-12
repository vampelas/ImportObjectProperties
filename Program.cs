using System;
using VDF = Autodesk.DataManagement.Client.Framework;

namespace ImportObjectProperties
{
    class Program
    {
        static void Main(string[] args)
        {


            //ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
            //{
            //    LoggerConfiguration loggerConfiguration = new LoggerConfiguration()
            //        .WriteTo.Console(outputTemplate:
            //            "{Timestamp:HH:mm:ss} [{ThreadId}] [{Level:u3}] {Message:lj} {Exception}{NewLine}")
            //        .Enrich.With<ExceptionEnricher>()
            //        .WriteTo.RollingFile(@"C:\Program Files\Autodesk\VAO 2020\Logs\ImportObjectProperties.txt", retainedFileCountLimit: 10);

            //    builder.AddSerilog(loggerConfiguration.CreateLogger());
            //});

            //ILogger<Application> SeriLog = loggerFactory.CreateLogger<Application>();


            

            //Serilog.Debugging.SelfLog.Enable(msg => Debug.WriteLine(msg));
            //Serilog.Debugging.SelfLog.Enable(Console.Error);

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
