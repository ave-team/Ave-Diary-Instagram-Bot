using DiaryInstaBot.Classes;
using InstagramApiSharp;
using InstagramApiSharp.API;
using InstagramApiSharp.API.Builder;
using InstagramApiSharp.Classes;
using InstagramApiSharp.Classes.Models;
using InstagramApiSharp.Logger;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AveDiaryInstaBot
{
    public class InstaBot
    {
        private const string AveDiaryApiBaseLink = "https://avediary.online/api.php";
        private IInstaApi instaApi;
        private IRequestDelay instaApiDelay;
        private BotSettings botSettings;
        private bool isStopRequested = false;

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

        public async Task StartPolling()
        {
            await Task.Run(async () =>
            {
                while (!this.isStopRequested)
                {
                    ApprovePendingUsers();

                    var messages = await this.instaApi.MessagingProcessor
                        .GetDirectInboxAsync(PaginationParameters.MaxPagesToLoad(1));
                    var threads = messages.Value.Inbox.Threads;
                    foreach(var thread in threads)
                    {
                        foreach(var message in thread.Items)
                        {
                            ProcessMessage(message, thread.ThreadId);
                        }
                    }

                    Thread.Sleep(TimeSpan.FromSeconds(1));
                }
            });
        }
        private async void ApprovePendingUsers()
        {
            var pendingUsers = await this.instaApi.MessagingProcessor
                        .GetPendingDirectAsync(PaginationParameters.MaxPagesToLoad(1));

            foreach (var thread in pendingUsers.Value.Inbox.Threads)
            {
                await this.instaApi.MessagingProcessor
                    .ApproveDirectPendingRequestAsync(thread.ThreadId.ToString());
            }
        }
        private async void ProcessMessage(InstaDirectInboxItem message, string threadId)
        {
            if (message.ItemType == InstaDirectThreadItemType.Text)
            {
                if (message.Text.Contains(this.botSettings.Commands.Login))
                {
                    string answer = "Напиши мені логін класу.";
                    await this.instaApi.MessagingProcessor.SendDirectTextAsync(null, threadId, answer);
                }
                // To-Do:
                // Save Processing User data
                // ThreadId, IsLoginedInDiary
            }
        }
        public void StopPolling()
        {
            this.isStopRequested = true;
        }
    }
}
