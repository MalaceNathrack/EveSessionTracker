using System.Linq;

namespace EveSessionTracker;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        bool adminMode = args.Any(a => a.Equals("-admin", StringComparison.OrdinalIgnoreCase));
        
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm(adminMode));
    }
}
