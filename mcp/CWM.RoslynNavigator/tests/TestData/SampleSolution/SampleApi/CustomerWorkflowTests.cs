namespace SampleApi;

/// <summary>
/// Test-classified by filename (*Tests.cs) even though it lives in a production project.
/// Every anti-pattern here is correct in a test and must produce no production finding.
/// DO NOT rename — the filename is what drives classification in this fixture.
/// </summary>
public class CustomerWorkflowTests
{
    // AP003 must NOT fire: integration tests routinely build a client per test.
    public void CreatesClientPerTest()
    {
        var client = new HttpClient();
        _ = client.BaseAddress;
    }

    // AP009 must NOT fire: test methods correctly take no CancellationToken.
    public async Task CreateCustomer_ValidRequest_ReturnsCreated()
    {
        await Task.Delay(1);
    }

    // AP002 must NOT fire: blocking in a test harness is harmless.
    public string BlocksOnSetup()
    {
        var task = Task.FromResult("seeded");
        return task.Result;
    }

    // AP004 must NOT fire in test code.
    public DateTime StampedAt() => DateTime.UtcNow;

    // AP001 MUST still fire under scope:"all" — async void swallows exceptions everywhere,
    // including in tests, so it is one of the few detectors that applies to test code.
    public async void FireAndForgetInTest()
    {
        await Task.Delay(1);
    }
}
