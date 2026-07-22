using System.Diagnostics;

namespace VSModifier.Core.Saves;

public interface IGameProcessDetector
{
    bool IsGameRunning();
}

public sealed class GameProcessDetector : IGameProcessDetector
{
    public const string ProcessName = "VampireSurvivors";

    public bool IsGameRunning()
    {
        Process[] processes = Process.GetProcessesByName(ProcessName);
        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (Process process in processes)
            {
                process.Dispose();
            }
        }
    }
}
