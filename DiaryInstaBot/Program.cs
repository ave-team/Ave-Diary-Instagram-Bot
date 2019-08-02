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

            if (!IsValidParams(ref args))
                return;

            InstaBot bot = new InstaBot(args[0], args[1]);
        }

        static bool IsValidParams(ref string[] args)
        {
            if (args.Length != 2)
            {
                Console.WriteLine("Wrong startup argumetns. Use by next template:");
                Console.WriteLine("dotnet .\\AveDiaryInstaBot.dll Username Password");
                return false;
            }
            return true;
        }
    }
}
