using System.Reflection;
using System.Runtime.CompilerServices;
using static Aggregator.ArchitectureTests.ArchitectureFixture;

namespace Aggregator.ArchitectureTests;

/// <summary>
/// Models are sealed and immutable, and their names say their layer.
/// HTTP contracts end in Request or Response; what an application service or reader takes or returns ends in Query or Result.
/// </summary>
public sealed class ModelConventionTests
{
    private const string ContractsNamespace = "Aggregator.Api.Contracts";
    private const string OptionsSuffix = "Options";
    private const string CloneMethodName = "<Clone>$";
    private static readonly string[] ContractSuffixes = ["Request", "Response"];
    private static readonly string[] BoundarySuffixes = ["Query", "Result"];

    [Fact]
    public void Records_ShouldBeSealedOrAbstract()
    {
        var records = DeclaredTypes().Where(IsRecord).ToList();

        records.ShouldNotBeEmpty();
        records.Where(type => !type.IsSealed && !type.IsAbstract).Select(type => type.FullName).ShouldBeEmpty();
    }

    [Fact]
    public void Types_ShouldHaveNoPublicSetters_WhenTheyAreNotOptions()
    {
        DeclaredTypes()
            .Where(type => !type.Name.EndsWith(OptionsSuffix, StringComparison.Ordinal))
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(property => property.SetMethod is { IsPublic: true } setter && !IsInitOnly(setter))
            .Select(property => $"{property.DeclaringType!.FullName}.{property.Name}")
            .ShouldBeEmpty();
    }

    [Fact]
    public void Contracts_ShouldEndWithRequestOrResponse_WhenTheyArePublicAndNotEnums()
    {
        var contracts = ApiAssembly.GetExportedTypes()
            .Where(type => type.Namespace == ContractsNamespace && !type.IsEnum && !type.IsNested)
            .ToList();

        contracts.ShouldNotBeEmpty();
        contracts.Where(type => !HasSuffix(type, ContractSuffixes)).Select(type => type.FullName).ShouldBeEmpty();
    }

    [Fact]
    public void ApplicationBoundary_ShouldUseQueryOrResult_WhenAServiceOrReaderTakesOrReturnsAModel()
    {
        var boundaryTypes = ApplicationAssembly.GetExportedTypes()
            .Where(IsServiceOrReader)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType).Append(method.ReturnType))
            .SelectMany(Unwrap)
            .Where(type => type.Assembly == ApplicationAssembly && !type.IsInterface && !type.IsEnum)
            .Distinct()
            .ToList();

        boundaryTypes.ShouldNotBeEmpty();
        boundaryTypes.Where(type => !HasSuffix(type, BoundarySuffixes)).Select(type => type.FullName).ShouldBeEmpty();
    }

    private static IEnumerable<Type> DeclaredTypes() =>
        ProductionAssemblies.SelectMany(assembly => assembly.GetTypes())
            .Where(type => !type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false) && !type.Name.Contains('<', StringComparison.Ordinal));

    private static bool IsRecord(Type type) =>
        type.GetMethod(CloneMethodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) is not null;

    private static bool IsInitOnly(MethodInfo setter) => setter.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(IsExternalInit));

    private static bool IsServiceOrReader(Type type) =>
        (type.IsClass && type.Name.EndsWith("Service", StringComparison.Ordinal)) || (type.IsInterface && type.Name.EndsWith("Reader", StringComparison.Ordinal));

    private static bool HasSuffix(Type type, string[] suffixes)
    {
        var name = type.IsGenericType ? type.Name[..type.Name.IndexOf('`', StringComparison.Ordinal)] : type.Name;
        return suffixes.Any(suffix => name.EndsWith(suffix, StringComparison.Ordinal));
    }

    /// <summary>The type and every generic argument inside it, so <c>Task&lt;IReadOnlyList&lt;T&gt;&gt;</c> yields <c>T</c>.</summary>
    private static IEnumerable<Type> Unwrap(Type type) =>
        type.IsGenericType ? type.GetGenericArguments().SelectMany(Unwrap).Prepend(type) : [type];
}
