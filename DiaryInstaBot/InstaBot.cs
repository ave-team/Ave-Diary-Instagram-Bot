using DiaryInstaBot;
using DiaryInstaBot.Classes;
using DiaryInstaBot.Entities;
using DiaryInstaBot.Enumerations;
using InstagramApiSharp;
using InstagramApiSharp.API;
using InstagramApiSharp.API.Builder;
using InstagramApiSharp.Classes;
using InstagramApiSharp.Classes.Models;
using InstagramApiSharp.Classes.SessionHandlers;
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
        private const string BotUsername = "avediarybot";
        private const string SessionFilename = "Session.bin";

        private IInstaApi instaApi;
        private IRequestDelay instaApiDelay;
        private BotSettings botSettings;
        private bool isStopRequested = false;
        private DatabaseContext dbContext = new DatabaseContext();
        private DiaryApiHelper diaryApi = new DiaryApiHelper();
        private Task pollingTask;
        private Logger logger;

        public InstaBot()
        {
            using (var reader = new StreamReader("settings.json"))
            {
                string json = reader.ReadToEnd();
                this.botSettings = JsonConvert.DeserializeObject<BotSettings>(json);
                this.logger = new Logger(this.botSettings.LogFileName);
            }

            ConnectToDb();
            InitializeInstaApi();
            Authorize().Wait();
        }
        private async void ConnectToDb()
        {
            await this.logger.WriteAsync(LogType.Info, $"InstaBot.ConnectToDb() — Trying connect to DB (ConnectionString=\"{this.botSettings.ConnectionString}\");");
            try
            {
                this.dbContext.Database.EnsureCreated();
                await this.logger.WriteAsync(LogType.Info, $"InstaBot.ConnectToDb() — Successfully connected to DB (ConnectionString=\"{this.botSettings.ConnectionString}\");");
            }
            catch (AggregateException ex)
            {
                var errorBuilder = new StringBuilder("Unable to connect to Database. Check is it running now or verify connection string");
                errorBuilder.Append("Additional error data:\n");
                errorBuilder.Append($"Error message: {ex.Message}");
                errorBuilder.Append($"Error trace: {ex.StackTrace}");
                errorBuilder.Append($"Error innerException: {ex.InnerException}");
                Console.WriteLine(errorBuilder.ToString());
                await this.logger.WriteAsync(LogType.Error, $"InstaBot.ConnectToDb() — Can't connect to DB (ConnectionString=\"{this.botSettings.ConnectionString}\");");
                await this.logger.WriteAsync(LogType.Error, $"InstaBot.ConnectToDb() — Error info:\n {errorBuilder.ToString()};");
                Environment.Exit(1);
            }
        }
        private async void InitializeInstaApi()
        {
            await this.logger.WriteAsync(LogType.Info, "InstaBot.InitializeInstaApi() — Trying initialize private instagram api;");
            try
            {
                this.instaApiDelay = RequestDelay.FromSeconds(2, 2);
                this.instaApi = InstaApiBuilder.CreateBuilder()
                     .SetUser(this.botSettings.LoginData)
                     .UseLogger(new DebugLogger(LogLevel.Exceptions))
                     .SetRequestDelay(this.instaApiDelay)
                     .SetSessionHandler(new FileSessionHandler() { FilePath = SessionFilename })
                     .Build();
                await this.logger.WriteAsync(LogType.Info, "InstaBot.InitializeInstaApi() — Private instagram api successfully initialized;");
            }
            catch(Exception ex)
            {
                var errorBuilder = new StringBuilder("Unable to inialize instagram api.");
                errorBuilder.Append("Additional error data:\n");
                errorBuilder.Append($"Error message: {ex.Message}");
                errorBuilder.Append($"Error trace: {ex.StackTrace}");
                errorBuilder.Append($"Error innerException: {ex.InnerException}");
                Console.WriteLine(errorBuilder.ToString());
                await this.logger.WriteAsync(LogType.Error, "InstaBot.InitializeInstaApi() — Failed to initialize private instagram api;");
                await this.logger.WriteAsync(LogType.Error, $"InstaBot.InitializeInstaApi() — Error info:\n {errorBuilder.ToString()}");
                Environment.Exit(1);
            }
        }
        private async Task Authorize()
        {
            await this.logger.WriteAsync(LogType.Info, "InstaBot.Authorize() — Trying to auth in bot account;");
            bool isAuthorized = await Login();
            if (isAuthorized)
            {
                SaveSession();
            }
            else
            {
                Console.WriteLine("FAILED TO LOG IN");
                await this.logger.WriteAsync(LogType.Error, "InstaBot.Authorize() — Failed to log in;");
            }
        }
        private async Task<bool> Login()
        {
            await this.logger.WriteAsync(LogType.Info, "InstaBot.Login() — Trying to load previous session;");
            if(File.Exists(Path.Combine(Directory.GetCurrentDirectory(), SessionFilename)))
                LoadSession();

            if (!this.instaApi.IsUserAuthenticated)
            {
                await this.logger.WriteAsync(LogType.Info, $"InstaBot.Login() — Logging in as @{botSettings.LoginData.UserName};");
                Console.WriteLine($"Logging in as @{botSettings.LoginData.UserName}");

                this.instaApiDelay.Disable();
                var logInResult = await instaApi.LoginAsync();
                this.instaApiDelay.Enable();

                if (!logInResult.Succeeded)
                {
                    if (logInResult.Value == InstaLoginResult.ChallengeRequired)
                    {
                        await this.logger.WriteAsync(LogType.Info, "InstaBot.Login() — Challenge is required;");
                        await this.logger.WriteAsync(LogType.Info, "InstaBot.Login() — Getting challenge verify method;");
                        var challenge = await instaApi.GetChallengeRequireVerifyMethodAsync();
                        if (challenge.Succeeded)
                        {
                            await this.logger.WriteAsync(LogType.Info, "InstaBot.Login() — Got challenge verify method;");
                            if (challenge.Value.SubmitPhoneRequired)
                            {
                                await this.logger.WriteAsync(LogType.Info, "InstaBot.Login() — Is SubmitPhoneRequired challenge. Starting process prone number challenge;");
                                await ProcessPhoneNumberChallenge();
                            }
                            else
                            {
                                await this.logger.WriteAsync(LogType.Info, "InstaBot.Login() — Instagram requested select challenge type;");
                                if (challenge.Value.StepData != null)
                                {
                                    await this.logger.WriteAsync(LogType.Info, "InstaBot.Login() — Trying to select phone challenge;");
                                    await SelectPhoneChallenge();
                                }
                            }
                        }
                        else
                        {
                            Console.WriteLine($"ERROR: {challenge.Info.Message}");
                            await this.logger.WriteAsync(LogType.Error, "InstaBot.Login() — Can't get challenge;");
                        }
                    }
                    else if (logInResult.Value == InstaLoginResult.TwoFactorRequired)
                    {
                        await this.logger.WriteAsync(LogType.Info, "InstaBot.Login() — Requested two factor auth;");
                        await ProcessTwoFactorAuth();
                    }
                    else
                    {
                        Console.WriteLine($"Unable to login: {logInResult.Info.Message}\nTry enable Two Factor Auth.");
                        await this.logger.WriteAsync(LogType.Error, $"InstaBot.Login() — Unable to login: {logInResult.Info.Message};");
                        
                        return false;
                    }

                }
            }
            await this.logger.WriteAsync(LogType.Info, "InstaBot.Login() — Successfully authorized;");
            Console.WriteLine("Successfully authorized!");
            return true;
        }
        private async Task ProcessPhoneNumberChallenge()
        {
            await this.logger.WriteAsync(LogType.Info, "InstaBot.ProcessPhoneNumberChallenge() — Waiting entering phone number;");
            Console.Write("Enter mobile phone for challenge\n(Example +380951234568): ");
            var enteredPhoneNumber = Console.ReadLine();
            await this.logger.WriteAsync(LogType.Info, $"InstaBot.ProcessPhoneNumberChallenge() — Entered number: '{enteredPhoneNumber}';");
            try
            {
                if (string.IsNullOrWhiteSpace(enteredPhoneNumber))
                {
                    await this.logger.WriteAsync(LogType.Error, "InstaBot.ProcessPhoneNumberChallenge() — Entered number is not valid;");
                    Console.WriteLine("Please type a valid phone number(with country code).\r\ni.e: +380951234568");
                    return;
                }
                var phoneNumber = enteredPhoneNumber;
                if (!phoneNumber.StartsWith("+"))
                    phoneNumber = $"+{phoneNumber}";

                await this.logger.WriteAsync(LogType.Info, "InstaBot.ProcessPhoneNumberChallenge() — Submitting phone number for challenge;");
                var submitPhone = await instaApi.SubmitPhoneNumberForChallengeRequireAsync(phoneNumber);
                if (submitPhone.Succeeded)
                {
                    await this.logger.WriteAsync(LogType.Info, "InstaBot.ProcessPhoneNumberChallenge() — Requesting SMS code;");
                    Console.Write("Enter code, that you got: ");
                    var code = Console.ReadLine();

                    await this.logger.WriteAsync(LogType.Info, $"InstaBot.ProcessPhoneNumberChallenge() — Entered code: '{code}';");
                    await this.logger.WriteAsync(LogType.Info, "InstaBot.ProcessPhoneNumberChallenge() — Starting verifying code;");
                    await VerifyCode(code);
                }
                else
                {
                    Console.WriteLine($"ERROR: {submitPhone.Info.Message}");
                    await this.logger.WriteAsync(LogType.Error, $"InstaBot.ProcessPhoneNumberChallenge() — Wrong phone number.\nError message:\n{submitPhone.Info.Message};");
                }

            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: {ex.Message}");
                await this.logger.WriteAsync(LogType.Error, $"InstaBot.ProcessPhoneNumberChallenge() — Error details:\n{ex.Message}\n{ex.StackTrace}\n{ex.InnerException};");
            }
        }
        private async Task VerifyCode(string code)
        {
            await this.logger.WriteAsync(LogType.Info, "InstaBot.VerifyCode() — Trimming verification code;");
            code = code.Trim();
            code = code.Replace(" ", "");
            var regex = new Regex(@"^-*[0-9,\.]+$");
            if (!regex.IsMatch(code))
            {
                Console.WriteLine("Verification code is numeric!");
                await this.logger.WriteAsync(LogType.Error, "InstaBot.VerifyCode() — Entered verification code is not valid (Verification code must be numeric!);");
                return;
            }
            if (code.Length != 6)
            {
                Console.WriteLine("Verification code must be 6 digits!");
                await this.logger.WriteAsync(LogType.Error, "InstaBot.VerifyCode() — Entered verification code is not valid (Verification code must be 6 digits!);");
                return;
            }
            try
            {
                // Note: calling VerifyCodeForChallengeRequireAsync function, 
                // if user has two factor enabled, will wait 15 seconds and it will try to
                // call LoginAsync.

                await this.logger.WriteAsync(LogType.Info, "InstaBot.VerifyCode() — Verification code sent;");
                var verifyLogin = await instaApi.VerifyCodeForChallengeRequireAsync(code);
                if (verifyLogin.Succeeded)
                {
                    await this.logger.WriteAsync(LogType.Info, "InstaBot.VerifyCode() — Verification code is valid. Challenge complete;");
                    SaveSession();
                }
                else
                {
                    // two factor is required
                    if (verifyLogin.Value == InstaLoginResult.TwoFactorRequired)
                    {
                        await this.logger.WriteAsync(LogType.Info, "InstaBot.VerifyCode() — Requested two factor auth;");
                        await ProcessTwoFactorAuth();
                    }
                    else
                    {
                        Console.WriteLine($"ERROR: {verifyLogin.Info.Message}");
                        await this.logger.WriteAsync(LogType.Error, $"InstaBot.VerifyCode() — Error details:\n{verifyLogin.Info.Message};");
                    }
                }

            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: {ex.Message}");
                await this.logger.WriteAsync(LogType.Error, $"InstaBot.VerifyCode() — Error details:\n{ex.Message}\n{ex.StackTrace}\n{ex.InnerException};");
            }
        }
        private async Task SelectPhoneChallenge()
        {
            await this.logger.WriteAsync(LogType.Info, "InstaBot.SelectPhoneChallenge() — Requesting SMS Challenge;");
            var phoneNumber = await instaApi.RequestVerifyCodeToSMSForChallengeRequireAsync();
            if (phoneNumber.Succeeded)
            {
                await this.logger.WriteAsync(LogType.Info, "InstaBot.SelectPhoneChallenge() — SMS code sent;");
                Console.WriteLine($"We sent verify code to this phone number(it's end with this): {phoneNumber.Value.StepData.ContactPoint}");
                Console.Write("Enter code, that you got: ");
                var code = Console.ReadLine();

                await this.logger.WriteAsync(LogType.Info, $"InstaBot.SelectPhoneChallenge() — Entered code: '{code}';");
                await this.logger.WriteAsync(LogType.Info, "InstaBot.SelectPhoneChallenge() — Starting verifying code;");
                await VerifyCode(code);
            }
            else
            {
                Console.WriteLine($"ERROR: {phoneNumber.Info.Message}");
                await this.logger.WriteAsync(LogType.Error, $"InstaBot.SelectPhoneChallenge() — Error message:\n{phoneNumber.Info.Message};");
            }
        }
        private async Task ProcessTwoFactorAuth()
        {
            await this.logger.WriteAsync(LogType.Info, "InstaBot.ProcessTwoFactorAuth() — Entering two factor code;");
            Console.WriteLine("Detected Two Factor Auth. Please, enter your two factor code:");
            var authCode = Console.ReadLine();

            await this.logger.WriteAsync(LogType.Info, $"InstaBot.ProcessTwoFactorAuth() — Entered code: '{authCode}';");
            await this.logger.WriteAsync(LogType.Info, "InstaBot.ProcessTwoFactorAuth() — Sending code for verification;");
            var twoFactorLogin = await instaApi.TwoFactorLoginAsync(authCode);

            if (twoFactorLogin.Succeeded)
            {
                await this.logger.WriteAsync(LogType.Info, "InstaBot.ProcessTwoFactorAuth() — Success, two factor auth passed;");
                SaveSession();
            }
            else
            {
                Console.WriteLine("Can't login. May be you entered expired code?");
                await this.logger.WriteAsync(LogType.Error, "InstaBot.ProcessTwoFactorAuth() — Two factor code denied;");
            }
        }
        private async void SaveSession(string stateFile = SessionFilename)
        {
            if (this.instaApi == null)
                return;
            if (!this.instaApi.IsUserAuthenticated)
                return;
            this.instaApi.SessionHandler.Save();
            await this.logger.WriteAsync(LogType.Info, "InstaBot.SaveSession() — Session saved;");
        }
        private void LoadSession()
        {
            this.instaApi?.SessionHandler?.Load();
        }
        
        private async void ApprovePendingUsers()
        {
            await this.logger.WriteAsync(LogType.Info, "InstaBot.ApprovePendingUsers() — Checking new chats;");
            var pendingUsers = await this.instaApi.MessagingProcessor
                        .GetPendingDirectAsync(PaginationParameters.MaxPagesToLoad(1));

            if (pendingUsers.Value.PendingRequestsCount > 0)
            {
                await this.logger.WriteAsync(LogType.Info, "InstaBot.ApprovePendingUsers() — Got new chats. Starting approving;");
                foreach (var thread in pendingUsers.Value.Inbox.Threads)
                {
                    await this.instaApi.MessagingProcessor
                        .ApproveDirectPendingRequestAsync(thread.ThreadId.ToString());
                }
                await this.logger.WriteAsync(LogType.Info, "InstaBot.ApprovePendingUsers() — New chats has been approved;");
            }
        }
        private async Task ProcessMessage(InstaDirectInboxItem message, string threadId)
        {
            await this.instaApi.MessagingProcessor.MarkDirectThreadAsSeenAsync(threadId, message.ItemId);
            if (message.ItemType == InstaDirectThreadItemType.Text)
            {
                var userInfo = await this.instaApi.UserProcessor.GetUserInfoByIdAsync(message.UserId);
                if (userInfo.Value.Username != BotUsername)
                {
                    if (IsCommand(message.Text, CommandType.Login))
                    {
                        await this.logger.WriteAsync(LogType.Info, $"InstaBot.ProcessMessage() — Got login command ('{message.Text}'), from @{userInfo.Value.Username};");
                        await ProcessLoginCommand(message.Text, threadId);
                    }
                    else if (IsCommand(message.Text, CommandType.Help))
                    {
                        await this.logger.WriteAsync(LogType.Info, $"InstaBot.ProcessMessage() — Got help command ('{message.Text}'), from @{userInfo.Value.Username};");
                        await ProcessHelpCommand(threadId);
                    }
                    else if (IsCommand(message.Text, CommandType.TomorrowHomework))
                    {
                        await this.logger.WriteAsync(LogType.Info, $"InstaBot.ProcessMessage() — Got tomorrow homework command ('{message.Text}'), from @{userInfo.Value.Username};");
                        await ProcessTomorrowHomeworkCommand(threadId);
                    }
                    else if (IsCommand(message.Text, CommandType.AllHomework))
                    {
                        await this.logger.WriteAsync(LogType.Info, $"InstaBot.ProcessMessage() — Got all homework command ('{message.Text}'), from @{userInfo.Value.Username};");
                        await ProcessAllHomeworkCommand(threadId);
                    }
                    else
                    {
                        await this.logger.WriteAsync(LogType.Info, $"InstaBot.ProcessMessage() — Got unknown command ('{message.Text}'), from @{userInfo.Value.Username};");
                        await this.instaApi.MessagingProcessor.SendDirectTextAsync(null, threadId, this.botSettings.Answers.UnknownCommand);
                    }
                }
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
        private async Task ProcessLoginCommand(string messageText, string threadId)
        {
            string answer = string.Empty;

            var words = messageText.Split();
            string classLogin = words.Last();
            await this.logger.WriteAsync(LogType.Info, $"InstaBot.ProcessLoginCommand() — Checking is class login ('{classLogin}') exists in database;");
            bool isClassLoginExists = await this.diaryApi.IsClassLoginExists(classLogin);
            if (isClassLoginExists)
            {
                await this.logger.WriteAsync(LogType.Info, $"InstaBot.ProcessLoginCommand() — Found class login ('{classLogin}') in database;");
                if (IsStudentExistsInDatabase(threadId))
                {
                    await this.logger.WriteAsync(LogType.Info, $"InstaBot.ProcessLoginCommand() — Updating user's class to ('{classLogin}');");
                    UpdateStudent(threadId, classLogin);
                }
                else
                {
                    await this.logger.WriteAsync(LogType.Info, $"InstaBot.ProcessLoginCommand() — Adding user to class '{classLogin}';");
                    AddNewStudent(threadId, classLogin);
                }
                answer = this.botSettings.Answers.LoginSaved;
            }
            else
            {
                await this.logger.WriteAsync(LogType.Info, $"InstaBot.ProcessLoginCommand() — Given class login ('{classLogin}') does not exist in database;");
                answer = this.botSettings.Answers.WrongLogin.Replace("{classLogin}", classLogin);
            }

            await this.logger.WriteAsync(LogType.Info, $"InstaBot.ProcessLoginCommand() — Sending answer to user;");
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
        private async Task ProcessHelpCommand(string threadId)
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

            await this.logger.WriteAsync(LogType.Info, $"InstaBot.ProcessHelpCommand() — Sending help list to user;");
            await this.instaApi.MessagingProcessor.SendDirectTextAsync(null, threadId, answer.ToString());
        }
        private async Task ProcessTomorrowHomeworkCommand(string threadId)
        {
            var dbStudent = this.dbContext.Students.SingleOrDefault(student => student.ThreadId == threadId);
            if (dbStudent == null)
            {
                await this.logger.WriteAsync(LogType.Info, $"InstaBot.ProcessTomorrowHomeworkCommand() — User doesn't have class;");
                string answer = this.botSettings.Answers.EmptyLogin;
                await this.instaApi.MessagingProcessor.SendDirectTextAsync(null, threadId, answer);
                return;
            }

            string homework = await this.diaryApi.GetTomorrowHomework(dbStudent.ClassLogin);
            homework = homework.Replace("\\n", Environment.NewLine);
            if (string.IsNullOrWhiteSpace(homework))
            {
                await this.logger.WriteAsync(LogType.Info, $"InstaBot.ProcessTomorrowHomeworkCommand() — User doesn't have tomorrow homework;");
                string answer = this.botSettings.Answers.EmptyTomorrowHomework;
                await this.instaApi.MessagingProcessor.SendDirectTextAsync(null, threadId, answer);
                return;
            }

            await this.logger.WriteAsync(LogType.Info, $"InstaBot.ProcessTomorrowHomeworkCommand() — Sending tomorrow homework to user;");
            await this.instaApi.MessagingProcessor.SendDirectTextAsync(null, threadId, homework);
        }
        private async Task ProcessAllHomeworkCommand(string threadId)
        {
            var dbStudent = this.dbContext.Students.SingleOrDefault(student => student.ThreadId == threadId);
            if (dbStudent == null)
            {
                await this.logger.WriteAsync(LogType.Info, $"InstaBot.ProcessAllHomeworkCommand() — User doesn't have class;");
                string answer = this.botSettings.Answers.EmptyLogin;
                await this.instaApi.MessagingProcessor.SendDirectTextAsync(null, threadId, answer);
                return;
            }

            string homework = await this.diaryApi.GetAllHomework(dbStudent.ClassLogin);
            homework = homework.Replace("\\n", Environment.NewLine);
            if (string.IsNullOrWhiteSpace(homework))
            {
                await this.logger.WriteAsync(LogType.Info, $"InstaBot.ProcessTomorrowHomeworkCommand() — User doesn't have any homework;");
                string answer = this.botSettings.Answers.EmptyAllHomework;
                await this.instaApi.MessagingProcessor.SendDirectTextAsync(null, threadId, answer);
                return;
            }

            await this.logger.WriteAsync(LogType.Info, $"InstaBot.ProcessTomorrowHomeworkCommand() — Sending all homework to user;");
            await this.instaApi.MessagingProcessor.SendDirectTextAsync(null, threadId, homework);
        }
        private void ProcessChats(List<InstaDirectInboxThread> chats)
        {
            var result = Parallel.ForEach(chats, (chat) =>
            {

                var messages = chat.Items;
                messages.ForEach(async (message) =>
                {
                    await instaApi.MessagingProcessor.MarkDirectThreadAsSeenAsync(chat.ThreadId, message.ItemId);
                    if (message.UserId != BotId)
                        await ProcessMessage(message, chat.ThreadId);
                });
            });

            double seconds = 0;
            while (!result.IsCompleted)
            {
                seconds += 0.5;
                Console.WriteLine($"Waiting {seconds}");
                Thread.Sleep(TimeSpan.FromSeconds(0.5));
            }
        }

        public async void StartPolling()
        {
            await this.logger.WriteAsync(LogType.Info, "InstaBot.StartPolling() — Bot started to listen chat updates;");
            this.pollingTask = Task.Run(async () =>
            {
                Console.WriteLine("Polling was started. Press Enter to stop.");
                while (!this.isStopRequested)
                {
                    ApprovePendingUsers();

                    await this.logger.WriteAsync(LogType.Info, "InstaBot.StartPolling() — Getting new messages;");
                    var inbox = await this.instaApi.MessagingProcessor
                        .GetDirectInboxAsync(PaginationParameters.MaxPagesToLoad(2));
                    var chats = inbox.Value.Inbox.Threads;
                    foreach (var chat in chats)
                    {
                        foreach (var message in chat.Items)
                        {
                            if (message.UserId != BotId)
                                await ProcessMessage(message, chat.ThreadId);
                        }
                    }
                }
                await this.logger.WriteAsync(LogType.Info, "InstaBot.StartPolling() — Bot was stoped;");
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
