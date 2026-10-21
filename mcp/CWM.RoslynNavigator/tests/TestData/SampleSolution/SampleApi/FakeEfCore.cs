namespace SampleApi;

/// <summary>
/// Minimal stand-in for the EF Core query surface, so AP010 can be exercised without adding
/// a real EF Core package reference to the fixture solution. The detector matches the chain
/// root by type name containing "DbSet" and the chain methods by name, so these shapes
/// reproduce real EF Core queries faithfully.
/// DO NOT rename — these names are what the detector matches on.
/// </summary>
public sealed class DbSet<T>
{
    public DbSet<T> Where(Func<T, bool> predicate) => this;

    public DbSet<T> Include(Func<T, object> selector) => this;

    public DbSet<T> AsNoTracking() => this;

    public DbSet<TResult> Select<TResult>(Func<T, TResult> selector) => new();

    public Task<List<T>> ToListAsync() => Task.FromResult(new List<T>());

    public Task<T?> FirstOrDefaultAsync() => Task.FromResult<T?>(default);

    public Task<int> CountAsync() => Task.FromResult(0);

    public Task<bool> AnyAsync() => Task.FromResult(false);

    public Task<decimal> SumAsync(Func<T, decimal> selector) => Task.FromResult(0m);

    public void Update(T entity) { }
}

public sealed class FakeDbContext
{
    public DbSet<Customer> Customers { get; } = new();

    public FakeDatabaseFacade Database { get; } = new();

    public FakeChangeTracker ChangeTracker { get; } = new();

    public Task<int> SaveChangesAsync() => Task.FromResult(0);
}

/// <summary>Stand-in for DatabaseFacade — raw SQL returns scalars, never tracked entities.</summary>
public sealed class FakeDatabaseFacade
{
    public DbSet<T> SqlQuery<T>(FormattableString sql) => new();
}

/// <summary>Stand-in for ChangeTracker — Entries() is not a query over an entity set.</summary>
public sealed class FakeChangeTracker
{
    public DbSet<Customer> Entries() => new();
}

public sealed class Customer
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public bool TwoFactorEnabled { get; set; }
    public List<CustomerNote> Notes { get; set; } = [];
}

public sealed class CustomerNote
{
    public string Text { get; set; } = "";
}
