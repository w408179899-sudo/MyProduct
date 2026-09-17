using System.Globalization;
using System.Reflection;
using System.Text.Json;
namespace UpgradeFixture.Runner;

public sealed record ApiEntry(string Owner, string Signature, bool RequiresImplementation = false);
public static class ApiSurface
{
    public static int Compare(string baselinePath, string currentPath, string reportPath)
    {
        var baseline = JsonSerializer.Deserialize<ApiEntry[]>(File.ReadAllText(baselinePath)) ?? throw new InvalidDataException("Missing baseline API entries.");
        var current = JsonSerializer.Deserialize<ApiEntry[]>(File.ReadAllText(currentPath)) ?? throw new InvalidDataException("Missing current API entries.");
        Program.Require(baseline.Length > 100 && current.Length > 100, "API evidence did not contain the actual framework surface.");
        var oldSignatures = baseline.Select(x => x.Signature).ToHashSet(StringComparer.Ordinal);
        var newSignatures = current.Select(x => x.Signature).ToHashSet(StringComparer.Ordinal);
        var oldOwners = baseline.Select(x => x.Owner).ToHashSet(StringComparer.Ordinal);
        var missing = baseline.Where(x => !newSignatures.Contains(x.Signature)).ToArray();
        var obligations = current.Where(x => x.RequiresImplementation && oldOwners.Contains(x.Owner) && !oldSignatures.Contains(x.Signature)).ToArray();
        var passed = missing.Length == 0 && obligations.Length == 0;
        File.WriteAllText(reportPath, JsonSerializer.Serialize(new { Passed = passed, BaselineMembers = baseline.Length,
            CurrentMembers = current.Length, MissingOrChanged = missing, NewAbstractObligations = obligations }, new JsonSerializerOptions { WriteIndented = true }));
        return passed ? 0 : 2;
    }
    // Version-independent signatures, captured by the same old consumer against both package sets.
    // This is a structural compatibility guard, not a substitute for semantic or complete ABI analysis.
    public static ApiEntry[] Capture(IEnumerable<Assembly> assemblies)
    {
        List<ApiEntry> result = [];
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        foreach (var assembly in assemblies.OrderBy(x => x.GetName().Name, StringComparer.Ordinal))
        foreach (var type in assembly.GetExportedTypes().OrderBy(Name, StringComparer.Ordinal))
        {
            var owner = Name(type);
            void Add(string signature, bool obligation = false) => result.Add(new(owner, signature, obligation));
            Add("type|" + owner + "|" + type.Attributes + "|" + Generics(type.GetGenericArguments()));
            if (type.BaseType is { } parent) Add("base|" + owner + "|" + Name(parent));
            foreach (var contract in type.GetInterfaces()) Add("interface|" + owner + "|" + Name(contract));
            foreach (var method in type.GetMethods(flags).Where(Visible))
                Add("method|" + owner + "|" + method.Attributes + "|" + method.Name + "|" + Generics(method.GetGenericArguments()) +
                    "|" + Parameters(method.GetParameters()) + "|" + Name(method.ReturnType) + "|" + Modifiers(method.ReturnParameter), method.IsAbstract);
            foreach (var constructor in type.GetConstructors(flags).Where(Visible))
                Add("constructor|" + owner + "|" + constructor.Attributes + "|" + Parameters(constructor.GetParameters()));
            foreach (var field in type.GetFields(flags).Where(x => x.IsPublic || x.IsFamily || x.IsFamilyOrAssembly))
                Add("field|" + owner + "|" + field.Attributes + "|" + field.Name + "|" + Name(field.FieldType) +
                    (field.IsLiteral ? "|constant=" + Convert.ToString(field.GetRawConstantValue(), CultureInfo.InvariantCulture) : ""));
            foreach (var property in type.GetProperties(flags).Where(x => x.GetAccessors(true).Any(Visible)))
                Add("property|" + owner + "|" + property.Name + "|" + Name(property.PropertyType) + "|" + Parameters(property.GetIndexParameters()));
            foreach (var item in type.GetEvents(flags).Where(x => x.AddMethod is not null && Visible(x.AddMethod)))
                Add("event|" + owner + "|" + item.Name + "|" + Name(item.EventHandlerType!));
        }
        return result.OrderBy(x => x.Signature, StringComparer.Ordinal).ToArray();
    }
    private static bool Visible(MethodBase method) => method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly;
    private static string Name(Type type)
    {
        if (type.IsByRef) return Name(type.GetElementType()!) + "&";
        if (type.IsPointer) return Name(type.GetElementType()!) + "*";
        if (type.IsArray) return Name(type.GetElementType()!) + "[" + new string(',', type.GetArrayRank() - 1) + "]";
        if (type.IsGenericParameter) return (type.DeclaringMethod is null ? "!" : "!!") + type.GenericParameterPosition;
        if (type.IsGenericType) return type.GetGenericTypeDefinition().FullName + "<" + string.Join(",", type.GetGenericArguments().Select(Name)) + ">";
        return type.FullName ?? type.Name;
    }
    private static string Generics(Type[] arguments) => string.Join(";", arguments.Where(x => x.IsGenericParameter).Select(x =>
        Name(x) + ":" + x.GenericParameterAttributes + ":" + string.Join(",", x.GetGenericParameterConstraints().Select(Name).Order(StringComparer.Ordinal))));
    private static string Parameters(ParameterInfo[] parameters) => string.Join(";", parameters.Select(parameter =>
        parameter.Name + ":" + Name(parameter.ParameterType) + ":" + parameter.Attributes + ":" + Modifiers(parameter) +
        (parameter.HasDefaultValue ? ":default=" + (parameter.DefaultValue is null ? "null" :
            Convert.ToString(parameter.DefaultValue, CultureInfo.InvariantCulture)) : "")));
    private static string Modifiers(ParameterInfo parameter) => string.Join(",", parameter.GetRequiredCustomModifiers().Select(Name));
}
