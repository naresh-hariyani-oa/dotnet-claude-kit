# 06 — Testing Guide

## Test Project Location

All unit tests live in a single project:

```
app/LegalCloud.UnitTests/LegalCloud.UnitTests.csproj
```

Target framework: **net48** (tests run on .NET Framework 4.8 only).

---

## Testing Framework

| Tool | Role |
|---|---|
| **xUnit** | Test framework — defines test classes and assertions |
| **Moq** | Mocking library — creates fake implementations of interfaces |
| **Coverlet** | Code coverage collection — integrated into `dotnet test` |

---

## Running Tests

```bash
# Run all tests
dotnet test app/LegalCloud.UnitTests/LegalCloud.UnitTests.csproj

# Run all tests with code coverage (OpenCover XML format)
dotnet test app/LegalCloud.UnitTests/LegalCloud.UnitTests.csproj \
    --verbosity quiet \
    /p:CollectCoverage=true \
    /p:CoverletOutputFormat=opencover

# Run tests for a specific class
dotnet test app/LegalCloud.UnitTests/LegalCloud.UnitTests.csproj \
    --filter FullyQualifiedName~ClassName

# Run tests matching a name pattern
dotnet test app/LegalCloud.UnitTests/LegalCloud.UnitTests.csproj \
    --filter Name~MethodNamePattern

# Run tests in a specific namespace folder
dotnet test app/LegalCloud.UnitTests/LegalCloud.UnitTests.csproj \
    --filter FullyQualifiedName~IRIS.Law.PmsBusiness
```

---

## Test Structure and Conventions

### Base Class

All tests should inherit from `BaseTestFixture`:

```csharp
public class BrMatterTests : BaseTestFixture
{
    // your tests
}
```

Check `BaseTestFixture.cs` for what it sets up (common mocks, common infrastructure).

### File Organization

Place test files in a folder that mirrors the production code structure:

| Production code | Test folder |
|---|---|
| `IRIS.Law.PmsBusiness/` | `IRIS.Law.PmsBusiness.UnitTest/` |
| `IRIS.Law.PmsApplication/` | `IRIS.Law.PmsApplication.UnitTest/` |
| `IRIS.Law.PmsData/` | `IRIS.Law.PmsData.UnitTest/` |
| `IRISLegal.SqlRepositories/` | `IRISLegal.SqlRepositories.UnitTest/` |
| `Legal.Cloud.Office365/` | `Cloud/Legal.Cloud.Office365.UnitTests/` |

### Test Naming Convention

Follow the existing convention: `MethodName_StateUnderTest_ExpectedBehavior`.

```csharp
[Fact]
public void GetMatter_WithValidId_ReturnsMatter() { }

[Fact]
public void SaveMatter_WithNullMatter_ThrowsArgumentNullException() { }

[Theory]
[InlineData(0)]
[InlineData(-1)]
public void GetMatter_WithInvalidId_ReturnsNull(int invalidId) { }
```

---

## How to Write a New Test

### Step 1 — Identify what to test

Focus on:
- **Business layer (Br* classes)** — the most important layer to test. Business rules are the highest-value tests.
- **Repository layer** — test data mapping, not SQL execution (the actual SQL is tested by integration/manual testing).
- **Service layer** — test orchestration logic.

Do **not** write tests for:
- Designer-generated files (`.Designer.cs`)
- Simple property getters/setters with no logic
- Pure data container DTOs

### Step 2 — Mock dependencies

Use Moq to mock interfaces. Never mock concrete classes if you can avoid it.

```csharp
public class BrMatterTests : BaseTestFixture
{
    private readonly Mock<IMatterRepository> _matterRepository;
    private readonly BrMatter _sut;  // system under test

    public BrMatterTests()
    {
        _matterRepository = new Mock<IMatterRepository>();
        _sut = new BrMatter(_matterRepository.Object);
    }

    [Fact]
    public void GetMatter_WithValidId_ReturnsMatter()
    {
        // Arrange
        var expectedMatter = new Matter { Id = 42, Reference = "ABC001" };
        _matterRepository.Setup(r => r.GetById(42)).Returns(expectedMatter);

        // Act
        var result = _sut.GetMatter(42);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("ABC001", result.Reference);
    }
}
```

### Step 3 — Arrange, Act, Assert

Always structure tests with three clear sections:

```csharp
[Fact]
public void CalculateBilling_WithHourlyRate_ReturnsCorrectAmount()
{
    // Arrange — set up the test scenario
    var rate = 150m;
    var hours = 2.5m;

    // Act — call the method under test
    var result = _billingCalculator.Calculate(rate, hours);

    // Assert — verify the outcome
    Assert.Equal(375m, result);
}
```

### Step 4 — Verify mock interactions where relevant

```csharp
[Fact]
public void SaveMatter_Always_CallsRepositorySave()
{
    // Arrange
    var matter = new Matter { Id = 42 };

    // Act
    _sut.SaveMatter(matter);

    // Assert — verify the repository was called
    _matterRepository.Verify(r => r.Save(matter), Times.Once);
}
```

---

## What NOT to Mock

Do not mock the database in tests that depend on specific SQL behavior. The project has had incidents where mocked tests passed but the actual database migration failed. For data access tests:

- Test the mapping logic between domain models and repository inputs/outputs.
- Do not try to simulate SQL Server behavior with mocks — that belongs in integration tests (which are run separately against a real database in the CI environment).

---

## Handling Static Singletons in Tests

`ApplicationSettings.Instance` and `UserInformation.Instance` are static singletons. Tests that touch code depending on these should set them up carefully:

```csharp
// Set up user context for test
UserInformation.Instance.DbUid = 1;
UserInformation.Instance.UserName = "testuser";

// Set up application settings for test
ApplicationSettings.Instance.SomeSetting = true;
```

Be aware that static state persists between tests in the same test runner process. If a test modifies a singleton, **restore the original value in a cleanup method**:

```csharp
public class MyTests : BaseTestFixture, IDisposable
{
    private readonly int _originalUserId;

    public MyTests()
    {
        _originalUserId = UserInformation.Instance.DbUid;
        UserInformation.Instance.DbUid = 99;
    }

    public void Dispose()
    {
        UserInformation.Instance.DbUid = _originalUserId;
    }
}
```

---

## Code Coverage Target

The project targets **80% code coverage minimum** for new code. Coverage is collected in the CI pipeline using Coverlet with the OpenCover format. Reports are visible in SonarQube.

If you cannot reach 80% coverage for a specific area, document clearly why (e.g., code path requires UI interaction, legacy code with no injectable dependencies).

---

## Testing Wisej Forms

Wisej forms inherit from `Wisej.Web.Form`. They cannot be unit tested in isolation because they require the Wisej runtime context. Do not attempt to instantiate Wisej form classes in unit tests — they will throw.

For form-level logic:
- Extract business logic into a business class or service (which can be tested).
- Keep event handlers thin — they should only call services and update UI elements.
- Test the extracted logic through its own unit tests.

---

## Running Tests in Visual Studio

Tests are discoverable via Visual Studio's Test Explorer (View → Test Explorer). Run tests from there or use the keyboard shortcut Ctrl+R, A (run all).

To debug a failing test, right-click it in Test Explorer and select "Debug Selected Tests."
