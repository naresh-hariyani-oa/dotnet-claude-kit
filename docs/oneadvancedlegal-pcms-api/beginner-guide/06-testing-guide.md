# 06 — Testing Guide

## Testing Framework

| Library | Purpose |
|---|---|
| xUnit 2.8 | Test runner and assertions |
| Moq 4.20 | Mock objects for dependency isolation |
| AutoBogus | Fake data generation |
| `BaseFixture` | Shared setup: AutoMapper, IConfiguration, IRequestService |

Tests live in:
- `api/Test/Business/` — Business layer (service) tests
- `api/Test/Data/` — Data layer (repository) tests

---

## Test Project Structure

```
api/Test/Business/Legal.Pcms.Integration.Api.Business.Test/
├── Common/
│   └── BaseFixture.cs          ← shared mapper, config, logger setup
├── AccountTest/
│   ├── AccountFixture.cs       ← domain-specific fixture with test data
│   ├── AccountServiceTest.cs   ← xUnit test class
│   └── DataSource/
│       └── AccountDataSource.cs  ← [MemberData] providers
├── MatterTest/
│   ├── MatterFixture.cs
│   ├── MatterServiceTest.cs
│   └── DataSource/
└── ... (one subfolder per domain)
```

---

## The Three-File Pattern

Every domain has exactly three test artifacts:

### 1. `{Domain}Fixture.cs` — Test Data and Shared Setup

```csharp
public class AccountFixture : BaseFixture, IDisposable
{
    public List<VatRate> VatRateDataSource { get; private set; }
    public List<BillingCode> BillingCodeDataSource { get; private set; }

    public AccountFixture()
    {
        // Load pre-defined test data (often from JSON files)
        VatRateDataSource = LoadTestData<List<VatRate>>("vatRates.json");
        BillingCodeDataSource = LoadTestData<List<BillingCode>>("billingCodes.json");
    }

    public void Dispose() { /* cleanup if needed */ }
}
```

**Why a fixture?** `IClassFixture<T>` is instantiated once per test class, not once per test. Use it for data loading and shared mock setup. Each test gets the same fixture instance.

### 2. `{Domain}ServiceTest.cs` — Test Class

```csharp
public class AccountServiceTest : IClassFixture<AccountFixture>
{
    private readonly AccountFixture _fixture;
    private readonly Mock<IAccountsRepository> _accountsRepository;
    private readonly IAccountsService _accountsService;

    public AccountServiceTest(AccountFixture fixture)
    {
        _fixture = fixture;
        _accountsRepository = new Mock<IAccountsRepository>();

        _accountsService = new AccountsService(
            _fixture.Logger<AccountsService>(),
            _fixture.Mapper,
            _accountsRepository.Object);
    }

    [Fact]
    [Trait("VatRateService", "GetVatRates")]
    public async Task GetVatRates_ShouldReturnVatRates_WhenIsValid()
    {
        // Arrange
        _accountsRepository
            .Setup(repo => repo.GetVatRates())
            .ReturnsAsync(_fixture.VatRateDataSource);

        // Act
        var result = await _accountsService.GetVatRates();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.VatRate.Count);

        // Verify the repository was called exactly once
        _accountsRepository.Verify(repo => repo.GetVatRates(), Times.Once);
    }
}
```

### 3. `{Domain}DataSource.cs` — Parameterised Test Data

```csharp
public class AccountDataSource
{
    public static IEnumerable<object[]> GetVatRateFilterData()
    {
        yield return new object[] { "standard", 1 };
        yield return new object[] { "reduced", 1 };
        yield return new object[] { null, 2 };
    }
}
```

Used with `[Theory]` + `[MemberData]`:

```csharp
[Theory]
[MemberData(nameof(AccountDataSource.GetVatRateFilterData),
    MemberType = typeof(AccountDataSource))]
public async Task GetVatRates_WithFilter_ReturnsCorrectCount(
    string filter, int expectedCount)
{
    // ...
}
```

---

## `BaseFixture` — What It Provides

`BaseFixture` is the shared test foundation. It initialises once and provides:

| Member | Type | What it gives you |
|---|---|---|
| `Mapper` | `IMapper` | AutoMapper with all 24+ domain profiles loaded |
| `Configuration` | `IConfiguration` | Reads from `appsettings.json` + env vars |
| `RequestService` | `IRequestService` | Mocked with a test access token |
| `Logger<T>()` | `ILogger<T>` | Real `ILoggerFactory`-backed logger |

> **Why this matters:** Your service is tested with the same AutoMapper configuration used in production. If a mapping is missing, the test will fail exactly as production would.

---

## Naming Conventions for Tests

Tests follow this naming pattern:

```
{MethodUnderTest}_{StateOrCondition}_{ExpectedOutcome}

Examples:
GetVatRates_ShouldReturnVatRates_WhenIsValid
CreateMatter_ShouldThrowBadDataException_WhenClientIdMissing
GetMatterById_ShouldThrowNotFoundException_WhenMatterDoesNotExist
```

Traits categorise tests:

```csharp
[Trait("VatRateService", "GetVatRates")]       // [Trait("ServiceName", "MethodName")]
```

---

## Test Isolation Rules

1. **Never test a real database.** All repositories are mocked with Moq.
2. **Never share state between tests.** Create a new `Mock<>` in the test constructor.
3. **Always verify repository calls** using `_mock.Verify(...)` to confirm the service called the right method.
4. **Test the mapper too** — since `BaseFixture` loads the real profiles, any missing mapping will surface in tests.

---

## Running Specific Tests

```bash
# All tests
dotnet test api/Legal.Pcms.Integration.Api.Test.sln

# Specific project
dotnet test api/Test/Business/Legal.Pcms.Integration.Api.Business.Test.csproj

# Filter by Trait value (domain)
dotnet test --filter "VatRateService"

# Filter by method name
dotnet test --filter "GetVatRates_ShouldReturnVatRates_WhenIsValid"

# Filter by Trait category and value
dotnet test --filter "AccountsService=billingCodes"

# With verbose output
dotnet test --logger "console;verbosity=detailed"
```

---

## Adding Tests for a New Domain

Follow these steps when adding tests for a new domain:

1. Create folder: `api/Test/Business/{Domain}Test/`
2. Create `{Domain}Fixture.cs` inheriting `BaseFixture, IDisposable`
3. Create `{Domain}ServiceTest.cs` implementing `IClassFixture<{Domain}Fixture>`
4. Create `DataSource/{Domain}DataSource.cs` for parameterised test data
5. Add the new test project reference if needed (usually not — all business tests are in the shared test project)

**Minimum test coverage expectation:** 80% per new service. If a method is untested, document why explicitly.

---

## Common Test Mistakes to Avoid

| Mistake | Why it's wrong | Fix |
|---|---|---|
| Using `new Mock<>()` in `[Fact]` body | State leaks between test runs | Create mocks in the test class constructor |
| Not calling `Verify()` on repository | Service could skip the call and tests still pass | Always verify expected calls |
| Hard-coding entity IDs as integers | Real IDs are Guids — use `Guid.NewGuid()` or AutoBogus | Use proper types |
| Testing `BaseService.Get()` directly | That method is already tested by Common tests | Test domain-specific logic |
| Catching exceptions in tests without `Assert.Throws` | Exceptions swallowed silently | Use `await Assert.ThrowsAsync<T>()` |
