using ArchUnitNET.Domain;
using ArchUnitNET.Fluent;
using ArchUnitNET.Fluent.Slices;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace VeloRoute.Tests.Architecture;

public class ArchitectureTests
{
    private static readonly ArchUnitNET.Domain.Architecture Architecture =
        new ArchLoader().LoadAssemblies(typeof(Program).Assembly).Build();

    [Fact]
    public void Data_Does_Not_Depend_On_Routing_Or_Auth()
    {
        IArchRule rule = Types()
            .That().ResideInNamespace("VeloRoute.Data")
            .Should().NotDependOnAny(
                Types().That().ResideInNamespace("VeloRoute.Routing")
                    .Or().ResideInNamespace("VeloRoute.Auth"))
            .Because("entities and AppDbContext must stay persistence-only, not pull in business or auth logic");

        rule.Check(Architecture);
    }

    [Fact]
    public void Routing_Does_Not_Depend_On_Data_Or_Auth()
    {
        IArchRule rule = Types()
            .That().ResideInNamespace("VeloRoute.Routing")
            .Should().NotDependOnAny(
                Types().That().ResideInNamespace("VeloRoute.Data")
                    .Or().ResideInNamespace("VeloRoute.Auth"))
            .Because("route generation must stay usable in the fully-anonymous v1 flow, independent of persistence and auth");

        rule.Check(Architecture);
    }

    [Fact]
    public void Auth_Does_Not_Depend_On_Data_Or_Routing()
    {
        IArchRule rule = Types()
            .That().ResideInNamespace("VeloRoute.Auth")
            .Should().NotDependOnAny(
                Types().That().ResideInNamespace("VeloRoute.Data")
                    .Or().ResideInNamespace("VeloRoute.Routing"))
            .Because("claims/Clerk helpers are a cross-cutting concern, not tied to a specific entity or routing feature");

        rule.Check(Architecture);
    }

    [Fact]
    public void Json_Is_A_Foundation_Layer()
    {
        IArchRule rule = Types()
            .That().ResideInNamespace("VeloRoute.Json")
            .Should().NotDependOnAny(
                Types().That().ResideInNamespace("VeloRoute.Data")
                    .Or().ResideInNamespace("VeloRoute.Routing")
                    .Or().ResideInNamespace("VeloRoute.Auth"))
            .Because("shared JSON converters (e.g. Optional<T>) must stay reusable across any layer");

        rule.Check(Architecture);
    }

    [Fact]
    public void Top_Level_Namespaces_Are_Free_Of_Cycles()
    {
        IArchRule rule = SliceRuleDefinition.Slices()
            .Matching("VeloRoute.(*).**")
            .Should().BeFreeOfCycles();

        rule.Check(Architecture);
    }
}
