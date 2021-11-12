using System.IO;
using log4net.Util;

namespace ImportObjectProperties.Layout
{
    public class NewFieldConverter : PatternConverter
    {
        protected override void Convert(TextWriter writer, object state)
        {
            var ctw = writer as CsvTextWriter;
            ctw?.WriteQuote();

            writer.Write(',');

            ctw?.WriteQuote();
        }
    }
}
