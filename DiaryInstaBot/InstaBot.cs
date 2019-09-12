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
using System.Text.RegularExpressions;
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
                    if (logInResult.Value == InstaLoginResult.ChallengeRequired)
                    {
                        var challenge = await instaApi.GetChallengeRequireVerifyMethodAsync();
                        if (challenge.Succeeded)
                        {
                            if (challenge.Value.SubmitPhoneRequired)
                            {
                                await ProcessPhoneNumberChallenge();
                            }
                            else
                            {
                                if (challenge.Value.StepData != null)
                                {
                                    await SelectPhoneChallenge();
                                }
                            }
                        }
                        else
                            Console.WriteLine($"ERROR: {challenge.Info.Message}");
                    }
                    else if (logInResult.Value == InstaLoginResult.TwoFactorRequired)
                    {
                        await ProcessTwoFactorAuth();
                    }
                    else
                    {
                        Console.WriteLine($"Unable to login: {logInResult.Info.Message}\nTry enable Two Factor Auth.");
                        return false;
                    }

                }

                Console.WriteLine("Successfully authorized!");
            }
            
            return true;
        }
        private async Task SelectPhoneChallenge()
        {
            var phoneNumber = await instaApi.RequestVerifyCodeToSMSForChallengeRequireAsync();
            if (phoneNumber.Succeeded)
            {
                Console.WriteLine($"We sent verify code to this phone number(it's end with this):\n{phoneNumber.Value.StepData.ContactPoint}");
                Console.WriteLine("Enter code, that you got:");
                var code = Console.ReadLine();
                await VerifyCode(code);
            }
            else
                Console.WriteLine($"ERROR: {phoneNumber.Info.Message}");
        }
        private async Task ProcessTwoFactorAuth()
        {
            Console.WriteLine("Detected Two Factor Auth. Please, enter your two factor code:");
            var authCode = Console.ReadLine();
            var twoFactorLogin = await instaApi.TwoFactorLoginAsync(authCode);

            if (!twoFactorLogin.Succeeded)
            {
                SaveSession();
            }
            else
            {
                Console.WriteLine("Can't login. May be you entered expired code?");
            }
        }
        private async Task ProcessPhoneNumberChallenge()
        {
            Console.Write("Enter mobile phone for challenge\n(Example +380951234568): ");
            var enteredPhoneNumber = Console.ReadLine();
            try
            {
                if (string.IsNullOrWhiteSpace(enteredPhoneNumber))
                {
                    Console.WriteLine("Please type a valid phone number(with country code).\r\ni.e: +380951234568");
                    return;
                }
                var phoneNumber = enteredPhoneNumber;
                if (!phoneNumber.StartsWith("+"))
                    phoneNumber = $"+{phoneNumber}";

                var submitPhone = await instaApi.SubmitPhoneNumberForChallengeRequireAsync(phoneNumber);
                if (submitPhone.Succeeded)
                {
                    Console.WriteLine("Enter code, that you got:");
                    var code = Console.ReadLine();
                    await VerifyCode(code);
                }
                else
                    Console.WriteLine($"ERROR: {submitPhone.Info.Message}");

            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: {ex.Message}");
            }
        }
        private async Task VerifyCode(string code)
        {
            code = code.Trim();
            code = code.Replace(" ", "");
            var regex = new Regex(@"^-*[0-9,\.]+$");
            if (!regex.IsMatch(code))
            {
                Console.WriteLine("Verification code is numeric!");
                return;
            }
            if (code.Length != 6)
            {
                Console.WriteLine("Verification code must be 6 digits!");
                return;
            }
            try
            {
                // Note: calling VerifyCodeForChallengeRequireAsync function, 
                // if user has two factor enabled, will wait 15 seconds and it will try to
                // call LoginAsync.

                var verifyLogin = await instaApi.VerifyCodeForChallengeRequireAsync(code);
                if (verifyLogin.Succeeded)
                {
                    // you are logged in sucessfully.
                    // Save session
                    SaveSession();
                }
                else
                {
                    // two factor is required
                    if (verifyLogin.Value == InstaLoginResult.TwoFactorRequired)
                    {
                        await ProcessTwoFactorAuth();
                    }
                    else
                        Console.WriteLine($"ERROR: {verifyLogin.Info.Message}");
                }

            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: {ex.Message}");
            }
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
                throw new Exception("TEST EXP");
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
                    this.instaApi.MessagingProcessor.SendDirectTextAsync(null, threadId, this.botSettings.Answers.UnknownCommand);
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
                if (IsStudentExistsInDatabase(threadId))
                    UpdateStudent(threadId, classLogin);
                else
                    AddNewStudent(threadId, classLogin);
                answer = this.botSettings.Answers.LoginSaved;
            }
            else
                answer = this.botSettings.Answers.WrongLogin.Replace("{classLogin}", classLogin);

            await this.instaApi.MessagingProcessor.SendDirectTextAsync(null, threadId, answer);
        }
        private bool IsStudentExistsInDatabase(string threadId)
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
            StringBuilder answer = new StringBuilder(this.botSettings.Answers.Help);
            string[] replacementPhrases = { "{helpCommands}", "{loginCommands}", "{tomorrowHomeworkCommands}", "{allHomeworkCommands}" };
            string[] commandProperties = { "Help", "Login", "TomorrowHomework", "AllHomework" };
            for(int commandIndex = 0; commandIndex < commandProperties.Length; commandIndex++)
            {
                StringBuilder avialableCommands = new StringBuilder();
                var propertyCommands = this.botSettings.Commands[commandProperties[commandIndex]] as List<string>;
                propertyCommands.ForEach(command => avialableCommands.Append($"{command}\n"));
                answer.Replace(replacementPhrases[commandIndex], avialableCommands.ToString());
            }

            await this.instaApi.MessagingProcessor.SendDirectTextAsync(null, threadId, answer.ToString());
        }
        private async void ProcessTomorrowHomeworkCommand(string threadId)
        {
            var dbStudent = this.dbContext.Students.SingleOrDefault(student => student.ThreadId == threadId);
            if (dbStudent == null)
            {
                string answer = this.botSettings.Answers.EmptyLogin;
                await this.instaApi.MessagingProcessor.SendDirectTextAsync(null, threadId, answer);
                return;
            }

            string homework = await this.diaryApi.GetTomorrowHomework(dbStudent.ClassLogin);
            homework = homework.Replace("\\n", Environment.NewLine);
            if (string.IsNullOrWhiteSpace(homework))
            {
                string answer = this.botSettings.Answers.EmptyTomorrowHomework;
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
                string answer = this.botSettings.Answers.EmptyLogin;
                await this.instaApi.MessagingProcessor.SendDirectTextAsync(null, threadId, answer);
                return;
            }

            string homework = await this.diaryApi.GetAllHomework(dbStudent.ClassLogin);
            homework = homework.Replace("\\n", Environment.NewLine);
            if (string.IsNullOrWhiteSpace(homework))
            {
                string answer = this.botSettings.Answers.EmptyAllHomework;
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
