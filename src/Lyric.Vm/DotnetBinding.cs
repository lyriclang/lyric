using System.Globalization;
using System.Reflection;
using Lyric.Bytecode;
using Lyric.Core;

namespace Lyric.Vm;

/// <summary>
/// Binds an <c>extern "dotnet"</c> import to a public static .NET method by reflection.
///
/// <para>The import's name is <c>dotnet:&lt;type&gt;::&lt;method&gt;</c>, written by the compiler
/// from the declaration's symbol; the type may be assembly-qualified in the ordinary .NET
/// spelling (<c>System.Net.Dns, System.Net.NameResolution::GetHostName</c>). The signature the
/// module declares is what the overload is chosen by: each Lyric wire type maps to exactly one
/// .NET parameter type, and the one public static method whose parameters match is the binding.
/// Two candidates are an ambiguity the declaration has to resolve, never a guess.</para>
///
/// <para>This is the runtime half of the ABI's stage 1. Nothing here consults the
/// <see cref="NativeRegistry"/>'s table: a host that registers a native under a
/// <c>dotnet:</c> name wins, because an explicit registration is a decision and reflection is a
/// default.</para>
///
/// <para>Values cross by copy: integers are range-checked against the .NET width rather than
/// truncated, a <c>char</c> outside the BMP is refused rather than split, and a result the
/// method returns as <c>null</c> for a <c>string</c> arrives as the empty string — a Lyric string
/// is never a null reference. A .NET exception thrown by the method ends the program as a
/// panic (<see cref="VmDiagnostics.HostCallFailed"/>); mapping it onto a Lyric <c>throws</c> is
/// stage 1's open item, and until it is decided the exception is not swallowed into a value.</para>
/// </summary>
internal static class DotnetBinding
{
    /// <summary>The bridge for one import, or <c>null</c> when the name does not carry the
    /// <c>dotnet:</c> prefix. A prefix with nothing bindable behind it throws.</summary>
    /// <exception cref="LyricRuntimeException">No such type, no such method, no overload with the
    /// declared signature, or more than one.</exception>
    public static Func<LyrValue[], LyrValue>? TryBind(BytecodeImport import)
    {
        if (!import.Name.StartsWith(CapabilityTable.DotnetPrefix, StringComparison.Ordinal))
            return null;

        var symbol = import.Name[CapabilityTable.DotnetPrefix.Length..];
        var separator = symbol.LastIndexOf("::", StringComparison.Ordinal);
        if (separator <= 0 || separator == symbol.Length - 2)
            throw Unbound(import, $"the symbol '{symbol}' is not of the form 'Type::Method'");

        var typeName = symbol[..separator];
        var methodName = symbol[(separator + 2)..];

        var type = ResolveType(typeName)
            ?? throw Unbound(import, $"no .NET type '{typeName}' is loaded — an assembly-qualified "
                + "name ('Namespace.Type, Assembly') loads the assembly it names");

        var wanted = new Type[import.ParamTypes.Count];
        for (var i = 0; i < wanted.Length; i++)
            wanted[i] = ClrTypeOf(import.ParamTypes[i].Tag)
                ?? throw Unbound(import, $"parameter {i + 1} has a type that does not cross the "
                    + "\"dotnet\" boundary");

        var returnClr = import.ReturnType.Tag == TypeTag.Void
            ? typeof(void)
            : ClrTypeOf(import.ReturnType.Tag)
                ?? throw Unbound(import, "the return type does not cross the \"dotnet\" boundary");

        var candidates = type
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => string.Equals(m.Name, methodName, StringComparison.Ordinal)
                && !m.IsGenericMethodDefinition
                && m.GetParameters().Select(p => p.ParameterType).SequenceEqual(wanted)
                && m.ReturnType == returnClr)
            .ToArray();

        if (candidates.Length == 0)
        {
            var overloads = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.Name == methodName).Select(Describe).ToArray();
            throw Unbound(import, overloads.Length == 0
                ? $"'{type.FullName}' has no public static method '{methodName}'"
                : $"'{type.FullName}.{methodName}' has no overload "
                  + $"({string.Join(", ", wanted.Select(t => t.Name))}) -> {returnClr.Name}; "
                  + $"it has: {string.Join("; ", overloads)}");
        }

        if (candidates.Length > 1)
            throw Unbound(import, $"'{type.FullName}.{methodName}' is ambiguous between "
                + string.Join(" and ", candidates.Select(Describe)));

        var method = candidates[0];
        var tags = import.ParamTypes.Select(p => p.Tag).ToArray();
        var returnTag = import.ReturnType.Tag;

        return arguments =>
        {
            var boxed = new object?[tags.Length];
            for (var i = 0; i < tags.Length; i++)
                boxed[i] = ToClr(arguments[i], tags[i], wanted[i], import.Name, i);

            object? produced;
            try
            {
                produced = method.Invoke(null, boxed);
            }
            catch (TargetInvocationException wrapped) when (wrapped.InnerException is { } cause)
            {
                throw new LyricPanic(VmDiagnostics.HostCallFailed,
                    $"host call '{symbol}' threw {cause.GetType().Name}: {cause.Message}");
            }

            return returnTag == TypeTag.Void ? default : ToLyric(produced, returnTag, import.Name);
        };
    }

    private static string Describe(MethodInfo m) =>
        $"({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name))}) -> {m.ReturnType.Name}";

    private static LyricRuntimeException Unbound(BytecodeImport import, string why) =>
        new(VmDiagnostics.ImportsNotBound, $"extern '{import.Name}' cannot be bound: {why}");

    /// <summary>
    /// The type behind a name: <c>Type.GetType</c> first, which handles assembly-qualified names
    /// and loads what they name; then every assembly already in the process, so the common
    /// <c>System.Math</c> needs no qualification.
    /// </summary>
    private static Type? ResolveType(string name)
    {
        if (Type.GetType(name, throwOnError: false) is { } direct) return direct;

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic) continue;
            if (assembly.GetType(name, throwOnError: false) is { } found) return found;
        }

        return null;
    }

    /// <summary>The one .NET type a wire tag binds against. Widths are exact so that an
    /// <c>int32</c> parameter meets an <c>Int32</c> overload and nothing else.</summary>
    private static Type? ClrTypeOf(TypeTag tag) => tag switch
    {
        TypeTag.I8 => typeof(sbyte),
        TypeTag.I16 => typeof(short),
        TypeTag.I32 => typeof(int),
        TypeTag.I64 => typeof(long),
        TypeTag.U8 => typeof(byte),
        TypeTag.U16 => typeof(ushort),
        TypeTag.U32 => typeof(uint),
        TypeTag.U64 => typeof(ulong),
        TypeTag.F32 => typeof(float),
        TypeTag.F64 => typeof(double),
        TypeTag.Bool => typeof(bool),
        TypeTag.Char => typeof(char),
        TypeTag.String => typeof(string),
        _ => null,
    };

    private static object? ToClr(LyrValue value, TypeTag tag, Type clr, string import, int index)
    {
        switch (tag)
        {
            case TypeTag.String: return value.AsString;
            case TypeTag.Bool: return value.AsBool;
            case TypeTag.F64: return value.AsF64;
            case TypeTag.F32: return value.AsF32;
            case TypeTag.Char:
                // A Lyric char is a code point up to 0x10FFFF; a .NET char is one UTF-16 unit.
                // Refused rather than split: half a character is not a smaller character.
                if (value.AsI64 is < 0 or > 0xFFFF)
                    throw new LyricPanic(VmDiagnostics.HostCallFailed,
                        $"argument {index + 1} of '{import}': code point U+{value.AsI64:X} does not "
                        + "fit a .NET char (UTF-16 unit)");
                return (char)value.AsI64;
            case TypeTag.U64: return value.AsU64;
            default:
                // The narrow widths are normalised by the VM already, so the conversion cannot
                // overflow; the checked cast documents that rather than trusting it.
                return Convert.ChangeType(value.AsI64, clr, CultureInfo.InvariantCulture);
        }
    }

    private static LyrValue ToLyric(object? produced, TypeTag tag, string import)
    {
        switch (tag)
        {
            case TypeTag.String:
                // A Lyric string is never null; a host that returns null has said "nothing", and
                // the empty string is the value that says it on this side.
                return LyrValue.FromString(produced as string ?? string.Empty);
            case TypeTag.Bool: return LyrValue.FromBool((bool)produced!);
            case TypeTag.F64: return LyrValue.FromF64((double)produced!);
            case TypeTag.F32: return LyrValue.FromF32((float)produced!);
            case TypeTag.Char: return LyrValue.FromI64((char)produced!);
            case TypeTag.U64: return LyrValue.FromBits((ulong)produced!);
            default:
                return LyrValue.FromI64(Convert.ToInt64(produced, CultureInfo.InvariantCulture));
        }
    }
}
