using DiaryInstaBot;
using DiaryInstaBot.Classes;
using DiaryInstaBot.Entities;
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
        private const long BotId = 17389287231;

        private IInstaApi instaApi;
        private IRequestDelay instaApiDelay;
        private BotSettings botSettings;
        private bool isStopRequested = false;
        private DatabaseContext dbContext = new DatabaseContext();
        private DiaryApiHelper diaryApi = new DiaryApiHelper();

        public InstaBot()
        {
            using (var reader = new StreamReader("settings.json"))
            {
                string json = reader.ReadToEnd();
                this.botSettings = JsonConvert.DeserializeObject<BotSettings>(json);
            }

            ConnectToDb();
            InitializeInstaApi();
            Authorize().Wait();
        }
        private void ConnectToDb()
        {
            try
            {
                this.dbContext.Database.EnsureCreated();
            }
            catch (AggregateException ex)
            {
                Console.WriteLine("Unable to connect to Database. Check is it running now or verify connection string");
                Console.WriteLine("Additional error data:\n");
                Console.WriteLine($"Error message: {ex.Message}");
                Console.WriteLine($"Error trace: {ex.StackTrace}");
                Console.WriteLine($"Error innerException: {ex.InnerException}");
            }
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
        private async void ApprovePendingUsers()
        {
            var pendingUsers = await this.instaApi.MessagingProcessor
                        .GetPendingDirectAsync(PaginationParameters.MaxPagesToLoad(1));

            if (pendingUsers.Value.PendingRequestsCount > 0)
            {
                foreach (var thread in pendingUsers.Value.Inbox.Threads)
                {
                    await this.instaApi.MessagingProcessor
                        .ApproveDirectPendingRequestAsync(thread.ThreadId.ToString());
                }
            }
        }
        private void ProcessMessage(InstaDirectInboxItem message, string threadId)
        {
            if (message.ItemType == InstaDirectThreadItemType.Text)
            {
                if (IsLoginCommand(message.Text))
                    ProcessLoginCommand(message.Text, threadId);
            }
        }
        private bool IsLoginCommand(string messageText)
        {
            return this.botSettings.Commands.Login
                .Any(loginWord => messageText.ToLower().Contains(loginWord));
        }
        private async void ProcessLoginCommand(string messageText, string threadId)
        {
            string answer = string.Empty;

            var words = messageText.Split();
            if (words.Count() != 2)
                answer = "Перепрошую, але я не розумію. Увійдіть до свого класу за наступним шаблоном:\nувійти myClassLogin";
            else
            {
                string classLogin = words.Last();
                bool isClassLoginExists = await this.diaryApi.IsClassLoginExists(classLogin);
                if (isClassLoginExists)
                {
                    if (IsStudentExistsInDatabase(threadId))
                        UpdateStudent(threadId, classLogin);
                    else
                        AddNewStudent(threadId, classLogin);
                    answer = "Я запам’ятала! Щоб змінити клас використайте повторно команду /login";
                }
                else
                    answer = $"Перепрошую, але класу із логіном {classLogin} не існує";
            }

            await this.instaApi.MessagingProcessor.SendDirectTextAsync(null, threadId, answer);
        }
        private bool IsStudentExistsInDatabase(string threadId)
        {
            var student = this.dbContext.Students.SingleOrDefault(dbStudent => dbStudent.ThreadId == threadId);
            return student != null;
        }
        private void AddNewStudent(string threadId, string classLogin)
        {
            var newStudent = new Student
            {
                ThreadId = threadId,
                ClassLogin = classLogin
            };

            this.dbContext.Students.Add(newStudent);
            this.dbContext.SaveChanges();
        }
        private void UpdateStudent(string threadId, string newClassLogin)
        {
            var dbStudent = this.dbContext.Students.First(student => student.ThreadId == threadId);
            dbStudent.ClassLogin = newClassLogin;
            this.dbContext.SaveChanges();
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
                            if(message.UserId != BotId)
                                ProcessMessage(message, thread.ThreadId);
                        }
                    }

                    Thread.Sleep(TimeSpan.FromSeconds(0.5));
                }
            });
        }
        public void StopPolling()
        {
            this.isStopRequested = true;
        }
    }
}
