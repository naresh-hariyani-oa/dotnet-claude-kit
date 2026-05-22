---
name: testing
description: >
  Testing strategy for .NET applications. Covers xUnit, Moq-based unit tests,
  WebApplicationFactory for integration tests, Testcontainers for real database testing,
  Verify for snapshot testing, and the AAA pattern.
  Load this skill when writing tests, setting up test infrastructure, reviewing
  test coverage, or when the user mentions "test", "xUnit", "Moq", "Mock",
  "WebApplicationFactory", "Testcontainers", "integration test", "unit test",
  "snapshot test", "Verify", "test coverage", "AAA pattern", "IClassFixture",
  "fixture", "WireMock", or "FakeTimeProvider".
---

# Testing (.NET)

## Core Principles

1. **Match the project's established testing strategy** — For Controller-based APIs with Moq-based service unit tests (the pcms-api pattern), extend that pattern. For new greenfield projects, `WebApplicationFactory` + Testcontainers integration tests are the highest-value approach.
2. **Moq is appropriate for service-layer unit tests** — In layered APIs with repository interfaces, mock the repository; test service logic in isolation using `IClassFixture<T>` and a shared fixture.
3. **Real databases when integration testing** — Never use `UseInMemoryDatabase` for EF Core integration tests. Use Testcontainers for new integration test suites.
4. **AAA pattern is mandatory** — Every test has three clearly separated sections: Arrange, Act, Assert. No mixing.
5. **Test behavior, not implementation** — Tests should survive refactoring. Test what the system does, not how it does it.

## Patterns

### Moq Unit Test Pattern (for layered Controller-based APIs)

The standard pattern for pcms-api style projects: fixture provides Mapper + Logger + test data, service is constructed with mocked repositories.

```csharp
// {Domain}Fixture.cs — shared setup and test data
public class OrderFixture : BaseFixture
{
    public Order SampleOrder { get; } = new Order { Id = 1, Status = "Active" };
}

// {Domain}ServiceTest.cs
public class OrderServiceTest : IClassFixture<OrderFixture>
{
    private readonly OrderFixture _fixture;

    public OrderServiceTest(OrderFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task GetOrderAsync_ValidId_ReturnsOrder()
    {
        // Arrange
        var mockRepo = new Mock<IOrderRepository>();
        mockRepo.Setup(r => r.GetByIdAsync(It.IsAny<int>()))
                .ReturnsAsync(_fixture.SampleOrder);
        var service = new OrderService(mockRepo.Object, _fixture.Mapper, _fixture.Logger);

        // Act
        var result = await service.GetOrderAsync(1);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(_fixture.SampleOrder.Id, result.Id);
    }

    [Theory]
    [MemberData(nameof(OrderDataSource.InvalidIds), MemberType = typeof(OrderDataSource))]
    public async Task GetOrderAsync_InvalidId_ThrowsResourceNotFoundException(int id)
    {
        // Arrange
        var mockRepo = new Mock<IOrderRepository>();
        mockRepo.Setup(r => r.GetByIdAsync(id)).ReturnsAsync((Order?)null);
        var service = new OrderService(mockRepo.Object, _fixture.Mapper, _fixture.Logger);

        // Act & Assert
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => service.GetOrderAsync(id));
    }
}

// {Domain}DataSource.cs — parameterized test data
public static class OrderDataSource
{
    public static IEnumerable<object[]> InvalidIds =>
    [
        [0],
        [-1],
        [int.MinValue]
    ];
}
```

### Integration Tests with WebApplicationFactory

The highest-value test pattern. Tests the full HTTP pipeline.

```csharp
// Fixtures/ApiFixture.cs
public class ApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17")
        .Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Replace the real DB with Testcontainers
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.AddDbContext<AppDbContext>(options =>
                options.UseNpgsql(_postgres.GetConnectionString()));
        });
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        // Apply migrations
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
    }

    public new async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await base.DisposeAsync();
    }
}
```

```csharp
// Tests/Orders/CreateOrderTests.cs
public class CreateOrderTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private readonly HttpClient _client = fixture.CreateClient();

    [Fact]
    public async Task CreateOrder_ReturnsCreated_WithValidRequest()
    {
        // Arrange
        var request = new CreateOrderRequest("customer-1", [new("product-1", 2)]);

        // Act
        var response = await _client.PostAsJsonAsync("/api/orders", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var order = await response.Content.ReadFromJsonAsync<OrderResponse>();
        Assert.NotNull(order);
        Assert.NotEqual(Guid.Empty, order.Id);
        Assert.Contains("/api/orders/", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task CreateOrder_ReturnsValidationProblem_WithEmptyItems()
    {
        // Arrange
        var request = new CreateOrderRequest("customer-1", []);

        // Act
        var response = await _client.PostAsJsonAsync("/api/orders", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
```

### Testcontainers for Real Database Testing

```csharp
// For SQL Server
private readonly MsSqlContainer _mssql = new MsSqlBuilder()
    .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
    .Build();

// For PostgreSQL
private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
    .WithImage("postgres:17")
    .Build();

// For Redis
private readonly RedisContainer _redis = new RedisBuilder()
    .WithImage("redis:7")
    .Build();
```

### Verify Snapshot Testing

Use Verify for complex response objects where manual assertions would be fragile.

```csharp
[Fact]
public async Task GetOrder_MatchesSnapshot()
{
    // Arrange
    await SeedOrder(fixture);

    // Act
    var response = await _client.GetAsync("/api/orders/known-id");
    var content = await response.Content.ReadAsStringAsync();

    // Assert — compares against a stored .verified.txt file
    await Verify(content);
}
```

On first run, Verify creates a `.verified.txt` file. On subsequent runs, it compares output. If the output changes, the test fails and shows a diff.

### Test Data Builders

```csharp
public class OrderBuilder
{
    private string _customerId = "default-customer";
    private List<OrderItem> _items = [new("product-1", 1, 9.99m)];
    private OrderStatus _status = OrderStatus.Pending;

    public OrderBuilder WithCustomer(string customerId)
    {
        _customerId = customerId;
        return this;
    }

    public OrderBuilder WithItems(params OrderItem[] items)
    {
        _items = [..items];
        return this;
    }

    public OrderBuilder WithStatus(OrderStatus status)
    {
        _status = status;
        return this;
    }

    public Order Build() => Order.Create(_customerId, _items, _status);
}

// Usage in tests
var order = new OrderBuilder()
    .WithCustomer("vip-customer")
    .WithStatus(OrderStatus.Confirmed)
    .Build();
```

### Testing Time-Dependent Code

Use `TimeProvider` (built into .NET 8+) and `FakeTimeProvider` from `Microsoft.Extensions.TimeProvider.Testing`.

```csharp
[Fact]
public async Task ExpireOrders_MarksOldPendingOrdersAsExpired()
{
    // Arrange
    var clock = new FakeTimeProvider(new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero));
    var db = CreateDb();
    var order = Order.Create("customer-1", items, clock.GetUtcNow());
    db.Orders.Add(order);
    await db.SaveChangesAsync();

    // Advance time past expiry threshold
    clock.Advance(TimeSpan.FromDays(31));

    var handler = new ExpireOrders.Handler(db, clock);

    // Act
    await handler.Handle(new ExpireOrders.Command(), CancellationToken.None);

    // Assert
    var updated = await db.Orders.FindAsync(order.Id);
    Assert.Equal(OrderStatus.Expired, updated!.Status);
}
```

### Test Naming Convention

Use the pattern: `MethodName_StateUnderTest_ExpectedBehavior`

```csharp
[Fact] public async Task CreateOrder_WithValidItems_ReturnsSuccessResult() { }
[Fact] public async Task CreateOrder_WithEmptyItems_ReturnsValidationError() { }
[Fact] public async Task GetOrder_WithNonExistentId_ReturnsNotFound() { }
[Fact] public async Task CancelOrder_WhenAlreadyShipped_ReturnsConflict() { }
```

## Anti-patterns

### Don't Use In-Memory Database for EF Core Integration Tests

```csharp
// BAD — hides real SQL behavior, transactions, constraints
services.AddDbContext<AppDbContext>(options =>
    options.UseInMemoryDatabase("TestDb"));

// GOOD — Testcontainers with real database (for new integration test suites)
services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(testContainer.GetConnectionString()));
```

### Don't Mock at the Wrong Layer

```csharp
// BAD — in a service unit test, mocking the DbContext directly is fragile
var mockDb = new Mock<AppDbContext>();
mockDb.Setup(x => x.Orders).Returns(mockDbSet);

// GOOD — mock the repository interface; test service logic
var mockRepo = new Mock<IOrderRepository>();
mockRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(order);
var service = new OrderService(mockRepo.Object, _fixture.Mapper, _fixture.Logger);
```

### Don't Test Implementation Details

```csharp
// BAD — test verifies a specific method call count (couples to implementation)
mock.Verify(x => x.GetByIdAsync(It.IsAny<int>()), Times.Exactly(2));

// GOOD — test the observable outcome (return value, exception thrown)
var result = await service.GetOrderAsync(1);
Assert.NotNull(result);
Assert.Equal(OrderStatus.Active, result.Status);
```

### Don't Share Mutable State Between Tests

```csharp
// BAD — static shared state
private static readonly AppDbContext SharedDb = CreateDb();

// GOOD — fresh state per test (or use IAsyncLifetime for shared fixtures)
private AppDbContext CreateDb() => new(new DbContextOptionsBuilder<AppDbContext>()...);
```

### Don't Write Assertion-Free Tests

```csharp
// BAD — no assertion, only checks it doesn't throw
[Fact]
public async Task CreateOrder_Works()
{
    await service.CreateAsync(request);
    // "it didn't throw, so it works!" — NO
}

// GOOD — assert the expected outcome
[Fact]
public async Task CreateOrder_PersistsOrderToDatabase()
{
    var result = await service.CreateAsync(request);

    var persisted = await db.Orders.FindAsync(result.Value.Id);
    Assert.NotNull(persisted);
    Assert.Equal(request.CustomerId, persisted.CustomerId);
}
```

## Decision Guide

| Scenario | Recommendation |
|----------|---------------|
| Testing an API endpoint | `WebApplicationFactory` integration test |
| Testing business logic in isolation | Unit test with fakes/stubs |
| Database-dependent tests | Testcontainers (real DB) |
| Complex response validation | Verify snapshot testing |
| Time-dependent logic | `FakeTimeProvider` |
| External API dependency | `WireMock.Net` or `HttpMessageHandler` stub |
| Parameterized test cases | `[Theory]` with `[InlineData]` or `[MemberData]` |
| Test data setup | Builder pattern |
| Shared expensive fixture | `IClassFixture<T>` with `IAsyncLifetime` |
