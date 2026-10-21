using System.Text.Json;
using CWM.RoslynNavigator.Responses;
using CWM.RoslynNavigator.Tests.Fixtures;
using CWM.RoslynNavigator.Tools;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CWM.RoslynNavigator.Tests.Tools;

public class GetDiRegistrationsTests(TestSolutionFixture fixture) : IClassFixture<TestSolutionFixture>
{
    private static List<DiRegistration> Analyze(string source)
    {
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        return GetDiRegistrationsTool.AnalyzeRoot(root, "Test.cs");
    }

    [Fact]
    public void Analyze_GenericRegistrations_ExtractServiceImplementationLifetime()
    {
        var registrations = Analyze("""
            services.AddScoped<IOrderRepository, OrderRepository>();
            services.AddSingleton<ICache, MemoryCache>();
            services.AddTransient<IEmailSender, EmailSender>();
            """);

        Assert.Equal(3, registrations.Count);

        var scoped = registrations.Single(r => r.Lifetime == "scoped");
        Assert.Equal("IOrderRepository", scoped.Service);
        Assert.Equal("OrderRepository", scoped.Implementation);
        Assert.False(scoped.Keyed);
        Assert.False(scoped.TryAdd);

        Assert.Equal("singleton", registrations.Single(r => r.Service == "ICache").Lifetime);
        Assert.Equal("transient", registrations.Single(r => r.Service == "IEmailSender").Lifetime);
    }

    [Fact]
    public void Analyze_SingleTypeArgument_ServiceIsImplementation()
    {
        var registrations = Analyze("services.AddSingleton<AppState>();");

        var registration = Assert.Single(registrations);
        Assert.Equal("AppState", registration.Service);
        Assert.Equal("AppState", registration.Implementation);
    }

    [Fact]
    public void Analyze_FactoryLambda_ReportsFactoryImplementation()
    {
        var registrations = Analyze("services.AddScoped<IQux>(sp => new Qux());");

        Assert.Equal("(factory)", Assert.Single(registrations).Implementation);
    }

    [Fact]
    public void Analyze_TypeofForm_ExtractsBothTypes()
    {
        var registrations = Analyze("services.AddSingleton(typeof(IBaz), typeof(Baz));");

        var registration = Assert.Single(registrations);
        Assert.Equal("IBaz", registration.Service);
        Assert.Equal("Baz", registration.Implementation);
    }

    [Fact]
    public void Analyze_KeyedAndTryAdd_AreFlagged()
    {
        var registrations = Analyze("""
            services.AddKeyedSingleton<IBar, Bar>("key");
            services.TryAddScoped<IFoo, Foo>();
            """);

        Assert.True(registrations.Single(r => r.Service == "IBar").Keyed);
        Assert.True(registrations.Single(r => r.Service == "IFoo").TryAdd);
    }

    [Fact]
    public void FindCaptiveRisks_SingletonDependingOnScoped_IsFlagged()
    {
        var source = """
            services.AddSingleton<IWorker, Worker>();
            services.AddScoped<IOrderRepository, OrderRepository>();

            public class Worker(IOrderRepository repository) : IWorker { }
            """;
        var root = CSharpSyntaxTree.ParseText(source, cancellationToken: TestContext.Current.CancellationToken).GetRoot(TestContext.Current.CancellationToken);
        var registrations = GetDiRegistrationsTool.AnalyzeRoot(root, "Test.cs");
        var typeDeclarations = root.DescendantNodes().OfType<TypeDeclarationSyntax>()
            .ToDictionary(t => t.Identifier.Text, t => t);

        var risks = GetDiRegistrationsTool.FindCaptiveRisks(registrations, typeDeclarations);

        var risk = Assert.Single(risks);
        Assert.Equal("IWorker", risk.Service);
        Assert.Equal("Worker", risk.Implementation);
        Assert.Equal("IOrderRepository", risk.DependsOn);
    }

    [Fact]
    public void FindCaptiveRisks_SingletonDependingOnSingleton_NotFlagged()
    {
        var source = """
            services.AddSingleton<IWorker, Worker>();
            services.AddSingleton<ICache, MemoryCache>();

            public class Worker(ICache cache) : IWorker { }
            """;
        var root = CSharpSyntaxTree.ParseText(source, cancellationToken: TestContext.Current.CancellationToken).GetRoot(TestContext.Current.CancellationToken);
        var registrations = GetDiRegistrationsTool.AnalyzeRoot(root, "Test.cs");
        var typeDeclarations = root.DescendantNodes().OfType<TypeDeclarationSyntax>()
            .ToDictionary(t => t.Identifier.Text, t => t);

        Assert.Empty(GetDiRegistrationsTool.FindCaptiveRisks(registrations, typeDeclarations));
    }

    [Fact]
    public void FindDuplicates_SameServiceTwice_IsFlagged_TryAddIgnored()
    {
        var registrations = Analyze("""
            services.AddScoped<IDup, FirstImpl>();
            services.AddScoped<IDup, SecondImpl>();
            services.TryAddScoped<ISafe, SafeImpl>();
            services.TryAddScoped<ISafe, SafeImpl>();
            """);

        var duplicate = Assert.Single(GetDiRegistrationsTool.FindDuplicates(registrations));
        Assert.Equal("IDup", duplicate.Service);
        Assert.Equal(2, duplicate.Count);
    }

    [Theory]
    [InlineData("My.Ns.IFoo<T>", "IFoo")]
    [InlineData("IFoo", "IFoo")]
    [InlineData("(factory)", "(factory)")]
    public void SimpleTypeName_StripsNamespaceAndGenerics(string input, string expected)
    {
        Assert.Equal(expected, GetDiRegistrationsTool.SimpleTypeName(input));
    }

    [Fact]
    public async Task GetDiRegistrations_SampleSolutionHasNone_ReturnsEmpty()
    {
        var json = await GetDiRegistrationsTool.ExecuteAsync(
            fixture.WorkspaceManager, ct: TestContext.Current.CancellationToken);
        var result = JsonSerializer.Deserialize<DiRegistrationsResult>(json)!;

        Assert.Equal(0, result.Count);
        Assert.Empty(result.Duplicates);
        Assert.Empty(result.CaptiveRisks);
    }
}
