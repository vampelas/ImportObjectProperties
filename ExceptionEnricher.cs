using Serilog.Events;
using Serilog.Core;

namespace ImportObjectProperties
{
    public class ExceptionEnricher : ILogEventEnricher
    {
        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        {
            if (logEvent.Exception == null)
                return;

            // Two lines below, remove the exception line return and return everything after : char
            var logEventProperty1 = logEvent.Exception.ToString().Replace("\r\n", "\\r\\n");
            var logEventProperty = propertyFactory.CreateProperty("EscapedException", logEventProperty1.ToString().Substring(logEventProperty1.ToString().LastIndexOf(":") + 2));

            //var logEventProperty = propertyFactory.CreateProperty("EscapedException", logEvent.Exception.ToString().Replace("\r\n", "\\r\\n"));

            logEvent.AddPropertyIfAbsent(logEventProperty);
        }
    }

}

