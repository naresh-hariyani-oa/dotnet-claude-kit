---
alwaysApply: true
description: >
  Enforces testing patterns, AAA structure, naming conventions, and fixture usage
  for .NET projects. Compatible with both Moq-based unit tests and Testcontainers-based
  integration tests depending on the project's established approach.
---

# Testing Rules

## Strategy

- **Match the project's established test approach.** For projects using Moq-based service unit tests (e.g., `IClassFixture<T>` + `Mock<IRepository>`), extend that pattern. For new greenfield projects, prefer `WebApplicationFactory` + Testcontainers for integration tests.
- **No in-memory database for EF Core integration tests.** `UseInMemoryDatabase` has different behavior from real providers (no constraints, no transactions, no SQL translation). Use Testcontainers for new integration test suites.
- **Moq is appropriate for service-layer unit tests** in Controller-based APIs with repository interfaces. Mock repository dependencies; test service logic in isolation.

## Test Structure

- **AAA pattern with clear separation.** Arrange, Act, Assert — separated by blank lines. Each section should be immediately identifiable.

```csharp
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
```

- **One assertion concept per test.** You may assert multiple properties of the same result, but do not test two separate behaviors in one test.

## Naming

- **Test naming: `MethodName_Scenario_ExpectedResult`.** Clear, searchable, and self-documenting.

```
GetOrderAsync_OrderDoesNotExist_ReturnsNull
CreateOrder_DuplicateSku_ThrowsConflictException
```

## Fixtures and Mocking

- **Shared fixtures for expensive setup.** Database containers, HTTP servers, and test configuration should be shared using `IClassFixture<T>` or `ICollectionFixture<T>`. Starting expensive resources per test is wasteful.
- **Mock at the boundary your test owns.** In a service unit test, mock the repository. In an integration test using Testcontainers, use the real database — do not mock EF Core.

## Behavior Over Implementation

- **Test behavior, not implementation details.** Assert on the observable outcome (return value, thrown exception, database state), not on which internal methods were called.
