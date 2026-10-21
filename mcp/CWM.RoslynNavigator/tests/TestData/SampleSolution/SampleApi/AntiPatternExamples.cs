using Microsoft.Extensions.Logging;

namespace SampleApi;

/// <summary>
/// Intentional anti-pattern examples for testing detect_antipatterns tool.
/// Each method or block demonstrates a specific anti-pattern.
/// DO NOT fix these — they are test fixtures.
/// </summary>
public class AntiPatternExamples
{
    private readonly ILogger<AntiPatternExamples> _logger;

    public AntiPatternExamples(ILogger<AntiPatternExamples> logger)
    {
        _logger = logger;
    }

    // AP001: async void (not an event handler)
    public async void FireAndForget()
    {
        await Task.Delay(100);
    }

    // AP001: Should NOT be flagged — event handler signature
    public async void OnButtonClick(object sender, EventArgs e)
    {
        await Task.Delay(100);
    }

    // AP002: .Result
    public string GetResultSync()
    {
        var task = Task.FromResult("hello");
        return task.Result;
    }

    // AP002: .GetAwaiter().GetResult()
    public string GetResultViaAwaiter()
    {
        var task = Task.FromResult("world");
        return task.GetAwaiter().GetResult();
    }

    // AP003: new HttpClient()
    public void CreateHttpClient()
    {
        var client = new HttpClient();
        _ = client.BaseAddress;
    }

    // AP004: DateTime.Now / DateTime.UtcNow
    public void UseDateTime()
    {
        var now = DateTime.Now;
        var utcNow = DateTime.UtcNow;
        _ = now.AddDays(1);
        _ = utcNow.AddDays(1);
    }

    // AP005: catch(Exception)
    public void BroadCatch()
    {
        try
        {
            _ = 1 + 1;
        }
        catch (Exception ex)
        {
            _ = ex.Message;
        }
    }

    // AP006: Interpolation in logging
    public void LogWithInterpolation(string name)
    {
        _logger.LogInformation($"User {name} logged in");
    }

    // AP006: Concatenation in logging
    public void LogWithConcatenation(string name)
    {
        _logger.LogWarning("User " + name + " logged in");
    }

    // AP007: Empty catch block
    public void EmptyCatch()
    {
        try
        {
            _ = 1 + 1;
        }
        catch (InvalidOperationException)
        {
        }
    }

    // AP008: Pragma without restore
#pragma warning disable CS0219
    public void PragmaWithoutRestore()
    {
        int x = 42;
    }

    // AP009: Public async method missing CancellationToken
    public async Task DoWorkAsync()
    {
        await Task.Delay(100);
    }

    // AP009: Should NOT be flagged — has CancellationToken
    public async Task DoWorkWithTokenAsync(CancellationToken ct = default)
    {
        await Task.Delay(100, ct);
    }

    // AP010: materializing read query with no AsNoTracking and no SaveChanges
    public Task<List<Customer>> ListCustomersAsync(FakeDbContext db) =>
        db.Customers.Where(c => c.Name != "").ToListAsync();

    // AP010: Should NOT be flagged — already AsNoTracking
    public Task<List<Customer>> ListCustomersReadOnlyAsync(FakeDbContext db) =>
        db.Customers.AsNoTracking().ToListAsync();
}
