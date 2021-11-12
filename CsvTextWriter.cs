using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using log4net.Util;

namespace ImportObjectProperties.Layout
{
    public class CsvTextWriter : TextWriter
    {
        private readonly TextWriter _textWriter;

        public CsvTextWriter(TextWriter textWriter)
         {
             _textWriter = textWriter;
         }

        public override Encoding Encoding => _textWriter.Encoding;
 
         public override void Write(char value)
         {
             _textWriter.Write(value);
             if (value == '"')
                 _textWriter.Write(value);

         }
 
         public void WriteQuote()
         {
             _textWriter.Write('"');
         }
    }
}
