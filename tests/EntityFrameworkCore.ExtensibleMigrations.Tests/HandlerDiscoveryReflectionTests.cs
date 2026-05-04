using System.Reflection;
using System.Reflection.Emit;
using EntityFrameworkCore.ExtensibleMigrations;
using Xunit;

namespace EntityFrameworkCore.ExtensibleMigrations.Tests;

public class HandlerDiscoveryReflectionTests
{
    [Fact]
    public void SafeGetTypes_returns_loadable_types_when_GetTypes_throws()
    {
        var partial = new Type?[] { typeof(string), null, typeof(int) };
        var ex = new ReflectionTypeLoadException(
            partial,
            new Exception?[] { null, new Exception(), null }
        );
        var asm = new ThrowingAssembly(ex);

        var result = HandlerDiscovery.SafeGetTypes(asm).ToList();

        Assert.Equal(new[] { typeof(string), typeof(int) }, result);
    }

    [Fact]
    public void SafeGetTypes_returns_empty_when_assembly_is_dynamic()
    {
        var ab = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("TestDyn"),
            AssemblyBuilderAccess.Run
        );

        Assert.Empty(HandlerDiscovery.SafeGetTypes(ab));
    }

    [Fact]
    public void SafeGetTypes_returns_empty_when_GetTypes_throws_unexpected()
    {
        var asm = new ThrowingAssembly(new InvalidOperationException("boom"));
        Assert.Empty(HandlerDiscovery.SafeGetTypes(asm));
    }

    private sealed class ThrowingAssembly : Assembly
    {
        private readonly Exception _ex;

        public ThrowingAssembly(Exception ex)
        {
            _ex = ex;
        }

        public override Type[] GetTypes() => throw _ex;

        public override bool IsDynamic => false;
    }
}
