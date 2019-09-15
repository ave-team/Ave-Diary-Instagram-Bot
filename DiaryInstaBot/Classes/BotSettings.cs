using InstagramApiSharp.Classes;
using System;
using System.Collections.Generic;
using System.Text;

namespace DiaryInstaBot.Classes
{
    public class BotSettings
    {
        public string LogFileName { get; set; }
        public UserSessionData LoginData { get; set; }
        public BotCommands Commands { get; set; }
        public BotAnswers Answers { get; set; }
        public string ConnectionString { get; set; }
    }
}
