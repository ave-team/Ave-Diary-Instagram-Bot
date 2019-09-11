using DiaryInstaBot;
using DiaryInstaBot.Classes;
using DiaryInstaBot.Entities;
using DiaryInstaBot.Enumerations;
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
        private Task pollingTask;

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
                Environment.Exit(1);
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
        private async Task<bool> Login()
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
            bool isAuthorized = await Login();
            if (isAuthorized)
                SaveSession();
            else
            {
                Console.WriteLine("FAILED TO LOG IN");
                Environment.Exit(1);
            }
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
                if (IsCommand(message.Text, CommandType.Login))
                    ProcessLoginCommand(message.Text, threadId);
                else if (IsCommand(message.Text, CommandType.Help))
                    ProcessHelpCommand(threadId);
                else if (IsCommand(message.Text, CommandType.TomorrowHomework))
                    ProcessTomorrowHomeworkCommand(threadId);
                else if (IsCommand(message.Text, CommandType.AllHomework))
                    ProcessAllHomeworkCommand(threadId);
                else
                    this.instaApi.MessagingProcessor.SendDirectTextAsync(null, threadId, "Перепрошую, але я не розумію...");
            }
        }
        private bool IsCommand(string messageText, CommandType commandType)
        {
            messageText = messageText.ToLower();

            switch (commandType)
            {
                case CommandType.Login:
                    return this.botSettings.Commands.Login.Any(loginWord => messageText.Contains(loginWord));
                case CommandType.Help:
                    return this.botSettings.Commands.Help.Any(helpWord => messageText.Contains(helpWord));
                case CommandType.TomorrowHomework:
                    return this.botSettings.Commands.TomorrowHomework.Any(homeworkWord => messageText.Contains(homeworkWord));
                case CommandType.AllHomework:
                    return this.botSettings.Commands.AllHomework.Any(allHomeworkWord => messageText.Contains(allHomeworkWord));
                default:
                    return false;
            }
        }
        private async void ProcessLoginCommand(string messageText, string threadId)
        {
            string answer = string.Empty;

            var words = messageText.Split();
            string classLogin = words.Last();
            bool isClassLoginExists = await this.diaryApi.IsClassLoginExists(classLogin);
            if (isClassLoginExists)
            {
                if (await IsStudentExistsInDatabase(threadId))
                    UpdateStudent(threadId, classLogin);
                else
                    AddNewStudent(threadId, classLogin);
                answer = "Я запам’ятала! Щоб змінити клас використайте повторно команду /login";
            }
            else
                answer = $"Перепрошую, але класу із логіном {classLogin} не існує";

            await this.instaApi.MessagingProcessor.SendDirectTextAsync(null, threadId, answer);
        }
        private async Task<bool> IsStudentExistsInDatabase(string threadId)
        {
            var student = this.dbContext.Students.FirstOrDefault(dbStudent => dbStudent.ThreadId == threadId);
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
        private async void ProcessHelpCommand(string threadId)
        {
            StringBuilder answer = new StringBuilder("Список ключевих слів для команд:\n");
            answer.Append("Help (Допомога):\n");
            this.botSettings.Commands.Help.ForEach(command => answer.Append($"{command}\n"));
            answer.Append("Login (Вхід):\n");
            this.botSettings.Commands.Login.ForEach(command => answer.Append($"{command}\n"));
            answer.Append("Tomorrow H/W (Д/З на завтра):\n");
            this.botSettings.Commands.TomorrowHomework.ForEach(command => answer.Append($"{command}\n"));
            answer.Append("All H/W (Усе Д/З):\n");
            this.botSettings.Commands.AllHomework.ForEach(command => answer.Append($"{command}\n"));

            await this.instaApi.MessagingProcessor.SendDirectTextAsync(null, threadId, answer.ToString());
        }
        private async void ProcessTomorrowHomeworkCommand(string threadId)
        {
            var dbStudent = this.dbContext.Students.SingleOrDefault(student => student.ThreadId == threadId);
            if (dbStudent == null)
            {
                string answer = "Перепрошую, але я не знаю логін твого класу. Добав його командою: долучитися classLogin";
                await this.instaApi.MessagingProcessor.SendDirectTextAsync(null, threadId, answer);
                return;
            }

            string homework = await this.diaryApi.GetTomorrowHomework(dbStudent.ClassLogin);
            homework = homework.Replace("\\n", Environment.NewLine);
            if (string.IsNullOrWhiteSpace(homework))
            {
                string answer = "На завтра нічого не задано!";
                await this.instaApi.MessagingProcessor.SendDirectTextAsync(null, threadId, answer);
                return;
            }

            await this.instaApi.MessagingProcessor.SendDirectTextAsync(null, threadId, homework);
        }
        private async void ProcessAllHomeworkCommand(string threadId)
        {
            var dbStudent = this.dbContext.Students.SingleOrDefault(student => student.ThreadId == threadId);
            if (dbStudent == null)
            {
                string answer = "Перепрошую, але я не знаю логін твого класу. Добав його командою: долучитися classLogin";
                await this.instaApi.MessagingProcessor.SendDirectTextAsync(null, threadId, answer);
                return;
            }

            string homework = await this.diaryApi.GetAllHomework(dbStudent.ClassLogin);
            homework = homework.Replace("\\n", Environment.NewLine);
            if (string.IsNullOrWhiteSpace(homework))
            {
                string answer = "На завтра нічого не задано!";
                await this.instaApi.MessagingProcessor.SendDirectTextAsync(null, threadId, answer);
                return;
            }

            await this.instaApi.MessagingProcessor.SendDirectTextAsync(null, threadId, homework);
        }

        public void StartPolling()
        {
            this.pollingTask = Task.Run(async () =>
            {
                Console.WriteLine("Polling was started. Press Enter to stop.");
                while (!this.isStopRequested)
                {
                    ApprovePendingUsers();

                    var messages = await this.instaApi.MessagingProcessor
                        .GetDirectInboxAsync(PaginationParameters.MaxPagesToLoad(2));
                    var threads = messages.Value.Inbox.Threads;
                    foreach (var thread in threads)
                    {
                        foreach (var message in thread.Items)
                        {
                            if (message.UserId != BotId)
                                ProcessMessage(message, thread.ThreadId);
                        }
                    }

                    Thread.Sleep(TimeSpan.FromSeconds(0.5));
                }
                Console.WriteLine("Polling was stoped.");
            });
        }
        public void StopPolling()
        {
            this.isStopRequested = true;
            this.pollingTask.Wait();
        }
    }
}
