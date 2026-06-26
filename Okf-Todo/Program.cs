using Photino.NET;
using Photino.NET.Server;
using System.Drawing;

namespace Photino.Okf_Todo
{
    internal class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            PhotinoServer
                .CreateStaticFileServer(args, out string baseUrl)
                .RunAsync();

            string appUrl = $"{baseUrl}/index.html";
            Console.WriteLine($"Serving OKF Todo app at {appUrl}");

            var window = new PhotinoWindow()
                .SetTitle("OKF Todo")
                .SetUseOsDefaultSize(false)
                .SetSize(new Size(800, 600))
                .Center()
                .SetResizable(true)
                .RegisterWebMessageReceivedHandler((object? sender, string message) =>
                {
                    if (sender is not PhotinoWindow window)
                    {
                        return;
                    }

                    window.SendWebMessage($"Received message: \"{message}\"");
                })
                .Load(appUrl);

            window.WaitForClose();
        }
    }
}
