using DiaryInstaBot.Enumerations;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DiaryInstaBot
{
    public class Logger
    {
        private string logFilePath;
        private StreamWriter logStreamWriter;

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
                this.logStreamWriter = new StreamWriter(logFile, true, Encoding.UTF8);
                this.logStreamWriter.AutoFlush = true;
            }
        }

        public void Write(LogType type, string message)
        {
            var now = DateTime.Now;
            string logMessage = $"{now.ToLongTimeString()} - {type.ToString().ToUpper()}: {message}";
            this.logStreamWriter.WriteLine(logMessage);
        }
    }
}
