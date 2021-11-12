using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using log4net.Core;
using log4net.Layout;
using log4net.Util;

namespace ImportObjectProperties.Layout
{
    public class CsvPatternLayout : PatternLayout
    {
        public override void ActivateOptions()
        {
            
            AddConverter("newfield", typeof(NewFieldConverter));
            //AddConverter(new ConverterInfo { Name = "encodedmessage", Type = typeof(LoggingEventPatternConvertor) });
            AddConverter("endrow", typeof(EndRowConverter));

            base.ActivateOptions();
        }
 
        public override void Format(TextWriter writer, LoggingEvent loggingEvent)
        {
            var ctw = new CsvTextWriter(writer);
            ctw.WriteQuote();
            base.Format(ctw, loggingEvent);
        }
    }
}
