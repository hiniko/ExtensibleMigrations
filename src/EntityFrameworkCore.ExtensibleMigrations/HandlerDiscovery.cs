using System.Reflection;

namespace EntityFrameworkCore.ExtensibleMigrations;

internal static class HandlerDiscovery
{
    internal static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        if (assembly.IsDynamic)
        {
            yield break;
        }

        Type?[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types;
        }
        catch
        {
            yield break;
        }

        foreach (var t in types)
        {
            if (t is not null)
            {
                yield return t;
            }
        }
    }
}
