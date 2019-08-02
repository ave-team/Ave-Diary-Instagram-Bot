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
using System.Threading.Tasks;

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
            Authorize().Wait();
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
        private async Task<bool> Authenticate()
        {
            if (!this.instaApi.IsUserAuthenticated)
            {
                Console.WriteLine($"Logging in as @{botSettings.LoginData.UserName}");

                this.instaApiDelay.Disable();
                var logInResult = await instaApi.LoginAsync();
                this.instaApiDelay.Enable();

                if (!logInResult.Succeeded)
                {
                    Console.WriteLine($"Unable to login: {logInResult.Info.Message}");
                    return false;
                }

                Console.WriteLine("Successfully authorized!");
            }
            return true;
        }
        private void SaveSession(string stateFile = "state.bin")
        {
            var state = instaApi.GetStateDataAsStream();
            using (var fileStream = File.Create(stateFile))
            {
                state.Seek(0, SeekOrigin.Begin);
                state.CopyTo(fileStream);
            }
        }
        private async Task Authorize()
        {
            bool isAuthorized = await Authenticate();
            if (isAuthorized)
                SaveSession();
        }
    }
}
