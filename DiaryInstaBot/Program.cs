using AveDiaryInstaBot;
using System;
using System.Text;

namespace DiaryInstaBot
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            try
            { 
                InstaBot bot = new InstaBot();
                bot.StartPolling();

                Console.ReadLine();
                bot.StopPolling();
            }
            catch(Exception ex)
            {
                Console.WriteLine($"{ex.Message}\nGlobal handler.");
                Console.WriteLine($"{ex.InnerException}\nGlobal handler.");
                Console.WriteLine($"{ex.StackTrace}\nGlobal handler.");
                Environment.Exit(1);
            }
        }
    }
}
