using System.Threading.Tasks;

namespace SESMDiscord
{
    class Program
    {
        public static Task Main(string[] args)
            => Startup.RunAsync(args);
    }

}
