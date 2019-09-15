using DiaryInstaBot.Enumerations;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace DiaryInstaBot
{
    public class Logger
    {
        private string logFilePath;
        //private StreamWriter logStreamWriter;
        private FileStream logFileStream;

        public Logger(string logFileName)
        {
            var now = DateTime.Now;
            string prefix = $"{now.ToLongTimeString()}-{now.ToShortDateString()}".Replace(':', '-');

            Directory.CreateDirectory("Logs");
            var logsDir = Path.Combine(Directory.GetCurrentDirectory(), "Logs");
            var logFile = Path.Combine(logsDir, $"{logFileName}-{prefix}").Replace(".txt", "");
            logFile += ".txt";
            if(!File.Exists(logFile))
            {
                var fileStream = File.Create(logFile);
                fileStream.Close();

                this.logFilePath = logFile;
                 
            }
        }

        public async Task WriteAsync(LogType type, string message)
        {
            var now = DateTime.Now;
            string logMessage = $"{now.ToLongTimeString()} - {type.ToString().ToUpper()}: {message}";
            using (var fs = new FileStream(this.logFilePath, FileMode.Append, FileAccess.Write, FileShare.None, 4096, true))
                using(var writer = new StreamWriter(fs))
                    await writer.WriteLineAsync(logMessage);
        }
    }
}
