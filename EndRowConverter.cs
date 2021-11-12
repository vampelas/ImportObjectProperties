using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using log4net.Util;

namespace ImportObjectProperties.Layout
{
    public class EndRowConverter : PatternConverter
    {
        protected override void Convert(TextWriter writer, object state)
        {
            var ctw = writer as CsvTextWriter;

            ctw?.WriteQuote();

            writer.WriteLine();

            
        }
    }
}
