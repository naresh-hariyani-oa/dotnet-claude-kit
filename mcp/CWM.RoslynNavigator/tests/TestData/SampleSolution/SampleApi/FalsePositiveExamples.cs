using Microsoft.Extensions.Logging;

namespace SampleApi;

/// <summary>
/// Correct code that naive detectors misreport. Every member here must produce either no
/// finding at all, or a Medium-confidence finding — never a High-confidence one.
/// DO NOT "fix" these — they are the regression suite for detector false positives.
/// </summary>
public class FalsePositiveExamples
{
    private const string MessagePrefix = "Goods receipt ";

    private readonly ILogger<FalsePositiveExamples> _logger;
    private readonly FakeDbContext _db;

    public FalsePositiveExamples(ILogger<FalsePositiveExamples> logger, FakeDbContext db)
    {
        _logger = logger;
        _db = db;
    }

    // AP006 must NOT fire: adjacent string literals are folded by the compiler into one
    // constant template. This is the correct way to wrap a long template across lines.
    public void LogWithWrappedTemplate(string grNumber, int itemId)
    {
        _logger.LogWarning(
            "Goods receipt {GrNumber} aborted: "
            + "item {ItemId} could not be reserved",
            grNumber, itemId);
    }

    // AP006 must NOT fire: a const prefix still folds to a compile-time constant.
    public void LogWithConstPrefix(string grNumber)
    {
        _logger.LogInformation(MessagePrefix + "{GrNumber} completed", grNumber);
    }

    // AP006 must NOT fire: concatenation in a *value* argument is ordinary code —
    // the template itself is a plain literal.
    public void LogWithConcatenatedValue(string first, string last)
    {
        _logger.LogInformation("Customer {Name} created", first + " " + last);
    }

    // AP007 must NOT fire: the comment is exactly what the suggestion asks for.
    public void EmptyCatchWithExplanation()
    {
        try
        {
            _ = 1 + 1;
        }
        catch (InvalidOperationException)
        {
            // Intentionally ignored: the cache entry is optional and a miss is not an error.
        }
    }

    // AP005 must be Medium, not High: this logs and rethrows, so nothing is swallowed.
    public void BoundedResilienceWrapper()
    {
        try
        {
            _ = 1 + 1;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Outbox drain failed");
            throw;
        }
    }

    // AP005 must be Medium, not High: an exception filter narrows the catch.
    public void FilteredCatch()
    {
        try
        {
            _ = 1 + 1;
        }
        catch (Exception ex) when (ex.Message.Length > 0)
        {
            _ = ex.Message;
        }
    }

    // AP002 must NOT fire: this is a domain Result type, not a Task.
    public string ReadDomainResult()
    {
        var outcome = new DomainOutcome("ok");
        return outcome.Result;
    }

    // AP010 must NOT fire: aggregates return scalars and never populate the change tracker,
    // so AsNoTracking() would change nothing.
    public Task<int> CountCustomers() => _db.Customers.CountAsync();

    public Task<bool> AnyCustomers() => _db.Customers.Where(c => c.Name != "").AnyAsync();

    // AP010 must NOT fire: load-to-mutate. Tracking is what makes SaveChanges detect the edit.
    public async Task ResetTwoFactorAsync(Guid id)
    {
        var customer = await _db.Customers
            .Include(c => c.Notes)
            .Where(c => c.Id == id)
            .FirstOrDefaultAsync();

        if (customer is not null)
        {
            customer.TwoFactorEnabled = false;
            await _db.SaveChangesAsync();
        }
    }

    // AP010 must NOT fire: raw SQL returns scalars. DatabaseFacade lives in the EF Core
    // assembly but is not a query over tracked entities.
    public Task<Customer?> RawSqlScalarQuery() =>
        _db.Database.SqlQuery<Customer>($"SELECT pg_try_advisory_xact_lock(1)").FirstOrDefaultAsync();

    // AP010 must NOT fire: ChangeTracker.Entries() is tracker state, not an entity query.
    public Task<List<Customer>> ReadChangeTrackerEntries() =>
        _db.ChangeTracker.Entries().ToListAsync();

    // AP010 must NOT fire: the entity is mutated here even though SaveChanges lives elsewhere
    // (a pipeline behaviour or interceptor commits it).
    public async Task RenameCustomerAsync(Guid id, string name)
    {
        var customer = await _db.Customers.Where(c => c.Id == id).FirstOrDefaultAsync();

        if (customer is not null)
            customer.Name = name;
    }

    // AP010 must NOT fire: a mutating call on the DbSet marks the whole scope as a write.
    public async Task ArchiveCustomerAsync(Guid id)
    {
        var customer = await _db.Customers.Where(c => c.Id == id).FirstOrDefaultAsync();

        if (customer is not null)
            _db.Customers.Update(customer);
    }

    // AP010 must be Medium, not High: a command-shaped method may edit downstream, so the
    // detector cannot be sure this read is safe to make no-tracking.
    public Task<List<Customer>> ProcessCustomerBatch() =>
        _db.Customers.Where(c => c.Name != "").ToListAsync();

    // AP004 must be suppressed by the inline marker, and counted as suppressed in the summary.
    public DateTime PresignedUrlExpiry() =>
        DateTime.UtcNow.AddMinutes(15); // cwm:ignore AP004 — SigV4 requires wall-clock time

    // AP007 must NOT fire: swallowing cancellation is the cooperative-shutdown idiom.
    public async Task RunUntilStoppedAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(1000, ct);
        }
        catch (OperationCanceledException)
        {
        }
    }

    // AP009 must NOT fire: the signature is fixed by the ASP.NET Core pipeline and the token
    // is reachable through the context.
    public async Task HandleRequestAsync(HttpContext context)
    {
        await Task.Delay(1);
        _ = context;
    }

    // AP009 must NOT fire: this method sources its own token from application lifetime,
    // which is the correct pattern for startup work.
    public async Task RunStartupTasksAsync(FakeHostLifetime lifetime)
    {
        await Task.Delay(1, lifetime.ApplicationStopping);
    }

    // AP003 must be Medium, not High: composing over an injected handler is the documented
    // IHttpClientFactory pattern, not ad-hoc instantiation.
    public HttpClient ComposeClient(HttpMessageHandler handler) => new HttpClient(handler);
}

/// <summary>AP009 must not fire on a pipeline-fixed middleware signature.</summary>
public sealed class AuditMiddleware
{
    public async Task InvokeAsync(HttpContext context)
    {
        await Task.Delay(1);
        _ = context;
    }
}

/// <summary>A domain result type whose <c>Result</c> property does not block on a Task.</summary>
public sealed class DomainOutcome(string value)
{
    public string Result { get; } = value;
}

/// <summary>Stand-in for the ASP.NET Core request context, matched by name.</summary>
public sealed class HttpContext
{
    public CancellationToken RequestAborted { get; } = CancellationToken.None;
}

/// <summary>Stand-in for IHostApplicationLifetime, matched by member name.</summary>
public sealed class FakeHostLifetime
{
    public CancellationToken ApplicationStopping { get; } = CancellationToken.None;
}
