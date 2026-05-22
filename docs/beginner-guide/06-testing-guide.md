# 06 — Testing Guide

This document explains how the MCP server is tested, how tests are structured, and the testing conventions the kit enforces for user projects.

---

## Part 1: Testing the MCP Server

### Test Framework

- **xunit v3** (xunit.v3 1.0.0) — not xUnit v2. xUnit v3 has native async/await support, meaning test methods can be `async Task` without workarounds.
- **No mocking frameworks** — Tests call real tool code against a real loaded Roslyn workspace.

### The TestSolutionFixture

The fixture at `mcp/CWM.RoslynNavigator/tests/Fixtures/TestSolutionFixture.cs` is the heart of the test suite.

It implements `IAsyncLifetime`, which means:
- `InitializeAsync()` runs once before any test in the collection — loads the sample solution
- `DisposeAsync()` runs once after all tests — disposes the workspace

```csharp
// Simplified structure of TestSolutionFixture
public sealed class TestSolutionFixture : IAsyncLifetime
{
    public WorkspaceManager WorkspaceManager { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        // Register MSBuild (must be first)
        // Create WorkspaceManager
        // Load the TestData/SampleSolution/SampleSolution.sln
        WorkspaceManager = ...;
        await WorkspaceManager.LoadSolutionAsync(solutionPath);
    }

    public async Task DisposeAsync()
    {
        // Dispose workspace
    }
}
```

### Sample Solution Structure

The test data lives at `mcp/CWM.RoslynNavigator/tests/TestData/SampleSolution/`:

```
SampleSolution.sln
├── SampleDomain/               (net10.0)
│   ├── BaseEntity.cs
│   ├── Order.cs                ← Aggregate root
│   ├── OrderItem.cs
│   ├── OrderStatus.cs          ← Enum
│   ├── Product.cs
│   ├── IOrderRepository.cs     ← Interface (for find_implementations tests)
│   ├── IProductRepository.cs
│   └── UnusedHelper.cs         ← Intentionally unused (for find_dead_code tests)
├── SampleApi/                  (net10.0)
│   ├── OrderService.cs         ← Uses IOrderRepository
│   ├── ProductService.cs
│   ├── AntiPatternExamples.cs  ← Contains async void, .Result, new HttpClient() etc.
│   └── OrderServiceTests.cs    ← So get_test_coverage_map has data
└── SampleInfrastructure/       (net10.0)
    ├── InMemoryOrderRepository.cs      ← Implements IOrderRepository
    ├── InMemoryProductRepository.cs
    └── CachedOrderRepository.cs        ← Decorator over IOrderRepository
```

**Why this structure matters:** Each file is crafted to trigger specific tool behaviors. `AntiPatternExamples.cs` deliberately contains every anti-pattern the detectors look for. `UnusedHelper.cs` is unused so `find_dead_code` returns results. `IOrderRepository.cs` has two implementations so `find_implementations` returns a list.

**Warning:** Do not "fix" anti-patterns in `AntiPatternExamples.cs`. Those patterns are intentional test fixtures.

### Test Structure

Each test file follows this pattern:

```csharp
// Tools/FindSymbolTests.cs
public sealed class FindSymbolTests(TestSolutionFixture fixture) : IClassFixture<TestSolutionFixture>
{
    [Fact]
    public async Task FindSymbol_ByClassName_ReturnsMatch()
    {
        // Arrange — nothing to arrange; fixture holds the workspace

        // Act
        var json = await FindSymbolTool.ExecuteAsync(
            fixture.WorkspaceManager,
            "Order",
            kind: "class",
            ct: TestContext.Current.CancellationToken);

        // Assert
        var result = JsonSerializer.Deserialize<SymbolSearchResult>(json)!;
        Assert.NotEmpty(result.Symbols);
        Assert.Contains(result.Symbols, s => s.Name == "Order" && s.Kind == "class");
    }
}
```

**Pattern:** `IClassFixture<TestSolutionFixture>` shares one fixture instance across all tests in the class. `TestContext.Current.CancellationToken` is xUnit v3's idiomatic way to get the test-scoped cancellation token.

### Run All Tests

```bash
dotnet test mcp/CWM.RoslynNavigator/CWM.RoslynNavigator.slnx
```

### Run Tests for a Specific Tool

```bash
dotnet test mcp/CWM.RoslynNavigator/CWM.RoslynNavigator.slnx \
  --filter "FullyQualifiedName~FindSymbolTests"

dotnet test mcp/CWM.RoslynNavigator/CWM.RoslynNavigator.slnx \
  --filter "FullyQualifiedName~DetectAntiPatternsTests"
```

### Run Tests with Detailed Output

```bash
dotnet test mcp/CWM.RoslynNavigator/CWM.RoslynNavigator.slnx --logger "console;verbosity=detailed"
```

### Writing a New Test

1. Add your test method to the relevant `Tools/*Tests.cs` file, or create a new file if testing a new tool.
2. Use `IClassFixture<TestSolutionFixture>` to get the shared workspace.
3. Call the tool's `ExecuteAsync` method directly.
4. Deserialize the JSON result to the appropriate response type.
5. Assert on the deserialized object.

If your test needs a symbol, type, or pattern that doesn't exist in the sample solution, add it to the appropriate `SampleSolution` project — then add a test that verifies your new tool finds it.

**Do not** add new sample code to `AntiPatternExamples.cs` for non-anti-pattern purposes. Create a dedicated file if needed.

---

## Part 2: Testing Standards the Kit Enforces for User Projects

The kit's rules and skills encode specific testing standards that apply when using dotnet-claude-kit templates. Understanding them helps you write tests that comply with the kit's expectations.

### Integration Tests First

The kit's primary rule (`.claude/rules/testing.md`):

> **Integration tests first.** Use `WebApplicationFactory` + Testcontainers to test real HTTP pipelines against real databases.

This means: before writing unit tests for a service, write an integration test that tests the HTTP endpoint end-to-end with a real database.

**Why:** Integration tests catch serialization bugs, middleware ordering issues, DI wiring failures, and query behavior — none of which unit tests would catch.

### No In-Memory Database

```csharp
// NEVER do this in tests
options.UseInMemoryDatabase("TestDb");

// ALWAYS do this
public sealed class DatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder().Build();
    public string ConnectionString => _container.GetConnectionString();
    public Task InitializeAsync() => _container.StartAsync();
    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}
```

**Why:** `UseInMemoryDatabase` has fundamentally different behavior from real providers — no constraints, no transactions, no SQL translation. Tests that pass against in-memory databases can fail against PostgreSQL or SQL Server.

### AAA Pattern

Every test must have clear Arrange / Act / Assert sections separated by blank lines:

```csharp
[Fact]
public async Task CreateOrder_ValidRequest_ReturnsCreated()
{
    // Arrange
    var client = _factory.CreateClient();
    var request = new CreateOrderRequest("SKU-1", Quantity: 2);

    // Act
    var response = await client.PostAsJsonAsync("/api/orders", request);

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.Created);
    var body = await response.Content.ReadFromJsonAsync<CreateOrderResponse>();
    body!.OrderId.Should().NotBeEmpty();
}
```

### Test Naming Convention

Format: `MethodName_Scenario_ExpectedResult`

```
GetOrderAsync_OrderDoesNotExist_ReturnsNull
CreateOrder_DuplicateSku_ReturnsConflict
PlaceOrder_InsufficientStock_ReturnsBadRequest
GetOrders_NoOrders_ReturnsEmptyList
```

This format makes test failures self-describing. You should be able to understand what broke from the test name alone.

### One Behavior Per Test

A test should verify one behavior. You can assert multiple properties of the same result, but do not test two unrelated behaviors in one test.

```csharp
// GOOD — tests one behavior (order creation returns 201 with body)
[Fact]
public async Task CreateOrder_ValidRequest_ReturnsCreated() { ... }

// GOOD — tests a different behavior (duplicate order returns 409)
[Fact]
public async Task CreateOrder_DuplicateOrderId_ReturnsConflict() { ... }

// BAD — tests two unrelated behaviors
[Fact]
public async Task CreateOrderAndGetOrder_Works() { ... }
```

### Shared Fixtures for Expensive Setup

Database containers and web application factories are expensive to start. Share them:

```csharp
// IClassFixture<T> — shared within one test class
public class OrderEndpointTests(WebApplicationFixture fixture) : IClassFixture<WebApplicationFixture>

// ICollectionFixture<T> — shared across multiple test classes
[Collection("Database")]
public class OrderEndpointTests(DatabaseFixture db) { }

[Collection("Database")]
public class ProductEndpointTests(DatabaseFixture db) { }
```

### No Mocking Your Own Code

If you control the code, use a real implementation or a test double:

```csharp
// BAD — mocking your own DbContext
var mockDb = new Mock<AppDbContext>();
mockDb.Setup(x => x.Orders).Returns(mockDbSet);

// GOOD — real database via Testcontainers
public sealed class DatabaseFixture : IAsyncLifetime { ... }
```

**Exception:** Third-party code you cannot control (payment gateway, email service) — mock those at the boundary with a test stub class that implements the interface.

---

## Testing Quick Reference

| Task | Command |
|------|---------|
| Run all MCP server tests | `dotnet test mcp/CWM.RoslynNavigator/CWM.RoslynNavigator.slnx` |
| Run specific test class | `dotnet test ... --filter "FullyQualifiedName~ClassName"` |
| Run tests with verbose output | `dotnet test ... --logger "console;verbosity=detailed"` |
| Check test data for anti-pattern | `mcp/.../TestData/SampleSolution/SampleApi/AntiPatternExamples.cs` |
| See fixture implementation | `mcp/.../tests/Fixtures/TestSolutionFixture.cs` |
| See example test structure | `mcp/.../tests/Tools/FindSymbolTests.cs` |
