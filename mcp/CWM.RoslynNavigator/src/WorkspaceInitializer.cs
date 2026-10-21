using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CWM.RoslynNavigator;

/// <summary>
/// Background service that initializes the workspace on application startup.
/// This runs the potentially slow solution loading in the background so the
/// MCP server can start accepting connections immediately.
/// </summary>
internal sealed class WorkspaceInitializer(
    WorkspaceManager workspaceManager,
    ILogger<WorkspaceInitializer> logger) : BackgroundService
{
    /// <summary>
    /// The solution path to load. Set by Program.cs from command-line args or discovery.
    /// </summary>
    public static string? SolutionPath { get; set; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrEmpty(SolutionPath))
        {
            logger.LogWarning("No solution path configured. Workspace will not be initialized. " +
                              "Pass --solution <path> or ensure a .sln/.slnx file exists in the working directory.");
            return;
        }

        try
        {
            logger.LogInformation("Initializing workspace with solution: {SolutionPath}", SolutionPath);
            await workspaceManager.LoadSolutionAsync(SolutionPath, stoppingToken);
            logger.LogInformation("Workspace initialization complete. {ProjectCount} projects loaded.",
                workspaceManager.ProjectCount);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Workspace initialization cancelled.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Workspace initialization failed.");
            // Don't rethrow — the server should still run, tools will return error status
        }
    }
}
