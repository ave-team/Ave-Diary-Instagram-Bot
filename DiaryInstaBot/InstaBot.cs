using DiaryInstaBot.Classes;
using InstagramApiSharp.API;
using InstagramApiSharp.API.Builder;
using InstagramApiSharp.Classes;
using InstagramApiSharp.Logger;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AveDiaryInstaBot
{
    public class InstaBot
    {
        private const string AveDiaryApiBaseLink = "https://avediary.online/api.php";
        private IInstaApi instaApi;
        private IRequestDelay instaApiDelay;
        private BotSettings botSettings;


        public InstaBot()
        {
            using (var reader = new StreamReader("settings.json"))
            {
                string json = reader.ReadToEnd();
                this.botSettings = JsonConvert.DeserializeObject<BotSettings>(json);
            }

            InitializeInstaApi();
        }
        private void InitializeInstaApi()
        {
            this.instaApiDelay = RequestDelay.FromSeconds(2, 2);
            this.instaApi = InstaApiBuilder.CreateBuilder()
                 .SetUser(this.botSettings.LoginData)
                 .UseLogger(new DebugLogger(LogLevel.Exceptions))
                 .SetRequestDelay(this.instaApiDelay)
                 .Build();
        }
    }
}
