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
                this.logger.Write(LogType.Info, "InstaBot() ctor — Bot settings successfully read;");
            }

            ConnectToDb();
            InitializeInstaApi();
            Authorize().Wait();
        }
        private void ConnectToDb()
        {
            this.logger.Write(LogType.Info, $"InstaBot.ConnectToDb() — Trying connect to DB (ConnectionString=\"{this.botSettings.ConnectionString}\");");
            try
            {
                this.dbContext.Database.EnsureCreated();
                this.logger.Write(LogType.Info, $"InstaBot.ConnectToDb() — Successfully connected to DB (ConnectionString=\"{this.botSettings.ConnectionString}\");");
            }
            catch (AggregateException ex)
            {
                var errorBuilder = new StringBuilder("Unable to connect to Database. Check is it running now or verify connection string");
                errorBuilder.Append("Additional error data:\n");
                errorBuilder.Append($"Error message: {ex.Message}");
                errorBuilder.Append($"Error trace: {ex.StackTrace}");
                errorBuilder.Append($"Error innerException: {ex.InnerException}");
                Console.WriteLine(errorBuilder.ToString());
                this.logger.Write(LogType.Error, $"InstaBot.ConnectToDb() — Can't connect to DB (ConnectionString=\"{this.botSettings.ConnectionString}\");");
                this.logger.Write(LogType.Error, $"InstaBot.ConnectToDb() — Error info:\n {errorBuilder.ToString()};");
                Environment.Exit(1);
            }
        }
        private void InitializeInstaApi()
        {
            this.logger.Write(LogType.Info, "InstaBot.InitializeInstaApi() — Trying initialize private instagram api;");
            try
            {
                this.instaApiDelay = RequestDelay.FromSeconds(2, 2);
                this.instaApi = InstaApiBuilder.CreateBuilder()
                     .SetUser(this.botSettings.LoginData)
                     .UseLogger(new DebugLogger(LogLevel.Exceptions))
                     .SetRequestDelay(this.instaApiDelay)
                     .SetSessionHandler(new FileSessionHandler() { FilePath = SessionFilename })
                     .Build();
                this.logger.Write(LogType.Info, "InstaBot.InitializeInstaApi() — Private instagram api successfully initialized;");
            }
            catch(Exception ex)
            {
                var errorBuilder = new StringBuilder("Unable to inialize instagram api.");
                errorBuilder.Append("Additional error data:\n");
                errorBuilder.Append($"Error message: {ex.Message}");
                errorBuilder.Append($"Error trace: {ex.StackTrace}");
                errorBuilder.Append($"Error innerException: {ex.InnerException}");
                Console.WriteLine(errorBuilder.ToString());
                this.logger.Write(LogType.Error, "InstaBot.InitializeInstaApi() — Failed to initialize private instagram api;");
                this.logger.Write(LogType.Error, $"InstaBot.InitializeInstaApi() — Error info:\n {errorBuilder.ToString()}");
                Environment.Exit(1);
            }
        }
        private async Task Authorize()
        {
            this.logger.Write(LogType.Info, "InstaBot.Authorize() — Trying to auth in bot account;");
            bool isAuthorized = await Login();
            if (isAuthorized)
            {
                SaveSession();
            }
            else
            {
                Console.WriteLine("FAILED TO LOG IN");
                this.logger.Write(LogType.Error, "InstaBot.Authorize() — Failed to log in;");
            }
        }
        private async Task<bool> Login()
        {
            this.logger.Write(LogType.Info, "InstaBot.Login() — Trying to load previous session;");
            if(File.Exists(Path.Combine(Directory.GetCurrentDirectory(), SessionFilename)))
                LoadSession();

            if (!this.instaApi.IsUserAuthenticated)
            {
                this.logger.Write(LogType.Info, $"InstaBot.Login() — Logging in as @{botSettings.LoginData.UserName};");
                Console.WriteLine($"Logging in as @{botSettings.LoginData.UserName}");

                this.instaApiDelay.Disable();
                var logInResult = await instaApi.LoginAsync();
                this.instaApiDelay.Enable();

                if (!logInResult.Succeeded)
                {
                    if (logInResult.Value == InstaLoginResult.ChallengeRequired)
                    {
                        this.logger.Write(LogType.Info, "InstaBot.Login() — Challenge is required;");
                        this.logger.Write(LogType.Info, "InstaBot.Login() — Getting challenge verify method;");
                        var challenge = await instaApi.GetChallengeRequireVerifyMethodAsync();
                        if (challenge.Succeeded)
                        {
                            this.logger.Write(LogType.Info, "InstaBot.Login() — Got challenge verify method;");
                            if (challenge.Value.SubmitPhoneRequired)
                            {
                                this.logger.Write(LogType.Info, "InstaBot.Login() — Is SubmitPhoneRequired challenge. Starting process prone number challenge;");
                                await ProcessPhoneNumberChallenge();
                            }
                            else
                            {
                                this.logger.Write(LogType.Info, "InstaBot.Login() — Instagram requested select challenge type;");
                                if (challenge.Value.StepData != null)
                                {
                                    this.logger.Write(LogType.Info, "InstaBot.Login() — Trying to select phone challenge;");
                                    await SelectPhoneChallenge();
                                }
                            }
                        }
                        else
                        {
                            Console.WriteLine($"ERROR: {challenge.Info.Message}");
                            this.logger.Write(LogType.Error, "InstaBot.Login() — Can't get challenge;");
                        }
                    }
                    else if (logInResult.Value == InstaLoginResult.TwoFactorRequired)
                    {
                        this.logger.Write(LogType.Info, "InstaBot.Login() — Requested two factor auth;");
                        await ProcessTwoFactorAuth();
                    }
                    else
                    {
                        Console.WriteLine($"Unable to login: {logInResult.Info.Message}\nTry enable Two Factor Auth.");
                        this.logger.Write(LogType.Error, $"InstaBot.Login() — Unable to login: {logInResult.Info.Message};");
                        
                        return false;
                    }

                }
            }
            this.logger.Write(LogType.Info, "InstaBot.Login() — Successfully authorized;");
            Console.WriteLine("Successfully authorized!");
            return true;
        }
        private async Task ProcessPhoneNumberChallenge()
        {
            this.logger.Write(LogType.Info, "InstaBot.ProcessPhoneNumberChallenge() — Waiting entering phone number;");
            Console.Write("Enter mobile phone for challenge\n(Example +380951234568): ");
            var enteredPhoneNumber = Console.ReadLine();
            this.logger.Write(LogType.Info, $"InstaBot.ProcessPhoneNumberChallenge() — Entered number: '{enteredPhoneNumber}';");
            try
            {
                if (string.IsNullOrWhiteSpace(enteredPhoneNumber))
                {
                    this.logger.Write(LogType.Error, "InstaBot.ProcessPhoneNumberChallenge() — Entered number is not valid;");
                    Console.WriteLine("Please type a valid phone number(with country code).\r\ni.e: +380951234568");
                    return;
                }
                var phoneNumber = enteredPhoneNumber;
                if (!phoneNumber.StartsWith("+"))
                    phoneNumber = $"+{phoneNumber}";

                this.logger.Write(LogType.Info, "InstaBot.ProcessPhoneNumberChallenge() — Submitting phone number for challenge;");
                var submitPhone = await instaApi.SubmitPhoneNumberForChallengeRequireAsync(phoneNumber);
                if (submitPhone.Succeeded)
                {
                    this.logger.Write(LogType.Info, "InstaBot.ProcessPhoneNumberChallenge() — Requesting SMS code;");
                    Console.Write("Enter code, that you got: ");
                    var code = Console.ReadLine();

                    this.logger.Write(LogType.Info, $"InstaBot.ProcessPhoneNumberChallenge() — Entered code: '{code}';");
                    this.logger.Write(LogType.Info, "InstaBot.ProcessPhoneNumberChallenge() — Starting verifying code;");
                    await VerifyCode(code);
                }
                else
                {
                    Console.WriteLine($"ERROR: {submitPhone.Info.Message}");
                    this.logger.Write(LogType.Error, $"InstaBot.ProcessPhoneNumberChallenge() — Wrong phone number.\nError message:\n{submitPhone.Info.Message};");
                }

            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: {ex.Message}");
                this.logger.Write(LogType.Error, $"InstaBot.ProcessPhoneNumberChallenge() — Error details:\n{ex.Message}\n{ex.StackTrace}\n{ex.InnerException};");
            }
        }
        private async Task VerifyCode(string code)
        {
            this.logger.Write(LogType.Info, "InstaBot.VerifyCode() — Trimming verification code;");
            code = code.Trim();
            code = code.Replace(" ", "");
            var regex = new Regex(@"^-*[0-9,\.]+$");
            if (!regex.IsMatch(code))
            {
                Console.WriteLine("Verification code is numeric!");
                this.logger.Write(LogType.Error, "InstaBot.VerifyCode() — Entered verification code is not valid (Verification code must be numeric!);");
                return;
            }
            if (code.Length != 6)
            {
                Console.WriteLine("Verification code must be 6 digits!");
                this.logger.Write(LogType.Error, "InstaBot.VerifyCode() — Entered verification code is not valid (Verification code must be 6 digits!);");
                return;
            }
            try
            {
                // Note: calling VerifyCodeForChallengeRequireAsync function, 
                // if user has two factor enabled, will wait 15 seconds and it will try to
                // call LoginAsync.

                this.logger.Write(LogType.Info, "InstaBot.VerifyCode() — Verification code sent;");
                var verifyLogin = await instaApi.VerifyCodeForChallengeRequireAsync(code);
                if (verifyLogin.Succeeded)
                {
                    this.logger.Write(LogType.Info, "InstaBot.VerifyCode() — Verification code is valid. Challenge complete;");
                    SaveSession();
                }
                else
                {
                    // two factor is required
                    if (verifyLogin.Value == InstaLoginResult.TwoFactorRequired)
                    {
                        this.logger.Write(LogType.Info, "InstaBot.VerifyCode() — Requested two factor auth;");
                        await ProcessTwoFactorAuth();
                    }
                    else
                    {
                        Console.WriteLine($"ERROR: {verifyLogin.Info.Message}");
                        this.logger.Write(LogType.Error, $"InstaBot.VerifyCode() — Error details:\n{verifyLogin.Info.Message};");
                    }
                }

            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: {ex.Message}");
                this.logger.Write(LogType.Error, $"InstaBot.VerifyCode() — Error details:\n{ex.Message}\n{ex.StackTrace}\n{ex.InnerException};");
            }
        }
        private async Task SelectPhoneChallenge()
        {
            this.logger.Write(LogType.Info, "InstaBot.SelectPhoneChallenge() — Requesting SMS Challenge;");
            var phoneNumber = await instaApi.RequestVerifyCodeToSMSForChallengeRequireAsync();
            if (phoneNumber.Succeeded)
            {
                this.logger.Write(LogType.Info, "InstaBot.SelectPhoneChallenge() — SMS code sent;");
                Console.WriteLine($"We sent verify code to this phone number(it's end with this): {phoneNumber.Value.StepData.ContactPoint}");
                Console.Write("Enter code, that you got: ");
                var code = Console.ReadLine();

                this.logger.Write(LogType.Info, $"InstaBot.SelectPhoneChallenge() — Entered code: '{code}';");
                this.logger.Write(LogType.Info, "InstaBot.SelectPhoneChallenge() — Starting verifying code;");
                await VerifyCode(code);
            }
            else
            {
                Console.WriteLine($"ERROR: {phoneNumber.Info.Message}");
                this.logger.Write(LogType.Error, $"InstaBot.SelectPhoneChallenge() — Error message:\n{phoneNumber.Info.Message};");
            }
        }
        private async Task ProcessTwoFactorAuth()
        {
            this.logger.Write(LogType.Info, "InstaBot.ProcessTwoFactorAuth() — Entering two factor code;");
            Console.WriteLine("Detected Two Factor Auth. Please, enter your two factor code:");
            var authCode = Console.ReadLine();

            this.logger.Write(LogType.Info, $"InstaBot.ProcessTwoFactorAuth() — Entered code: '{authCode}';");
            this.logger.Write(LogType.Info, "InstaBot.ProcessTwoFactorAuth() — Sending code for verification;");
            var twoFactorLogin = await instaApi.TwoFactorLoginAsync(authCode);

            if (twoFactorLogin.Succeeded)
            {
                this.logger.Write(LogType.Info, "InstaBot.ProcessTwoFactorAuth() — Success, two factor auth passed;");
                SaveSession();
            }
            else
            {
                Console.WriteLine("Can't login. May be you entered expired code?");
                this.logger.Write(LogType.Error, "InstaBot.ProcessTwoFactorAuth() — Two factor code denied;");
            }
        }
        private void SaveSession(string stateFile = SessionFilename)
        {
            if (this.instaApi == null)
                return;
            if (!this.instaApi.IsUserAuthenticated)
                return;
            this.instaApi.SessionHandler.Save();
            this.logger.Write(LogType.Info, "InstaBot.SaveSession() — Session saved;");
        }
        private void LoadSession()
        {
            this.instaApi?.SessionHandler?.Load();
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
