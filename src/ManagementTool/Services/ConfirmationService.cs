namespace Meshmakers.Octo.Frontend.ManagementTool.Services;

public class ConfirmationService : IConfirmationService
{
    public bool Confirm(string message)
    {
        if (Console.IsInputRedirected)
        {
            Console.Error.WriteLine(
                "Confirmation required but input is redirected. Use --yes (-y) to skip confirmation prompts.");
            return false;
        }

        Console.Write($"{message} (y/N): ");
        var response = Console.ReadLine();
        return response != null &&
               response.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) ||
               response != null &&
               response.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase);
    }
}
