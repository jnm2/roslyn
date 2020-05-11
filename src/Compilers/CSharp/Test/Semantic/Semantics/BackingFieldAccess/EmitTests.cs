// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CodeAnalysis.CSharp.Symbols;
using Microsoft.CodeAnalysis.CSharp.Test.Utilities;
using Xunit;

namespace Microsoft.CodeAnalysis.CSharp.UnitTests.Semantics.BackingFieldAccess
{
    public sealed class EmitTests : CSharpTestBase
    {
        [Fact]
        public void ExpressionBodiedStaticProperty()
        {
            var source = @"
class C
{
    static int Property => field++;

    static void Main()
    {
        System.Console.WriteLine(Property);
        System.Console.WriteLine(Property);
        System.Console.WriteLine(Property);
    }
}";
            var compilation = CompileAndVerify(source, expectedOutput: @"
0
1
2");
            compilation.VerifyIL("C.Property.get", @"{
    // Code size       14 (0xe)
    .maxstack  3
    IL_0000:  ldsfld     ""int C.<Property>k__BackingField""
    IL_0005:  dup
    IL_0006:  ldc.i4.1
    IL_0007:  add
    IL_0008:  stsfld     ""int C.<Property>k__BackingField""
    IL_000d:  ret
}");
        }

        [Fact]
        public void ExpressionBodiedClassProperty()
        {
            var source = @"
class C
{
    int Property => field++;

    static void Main()
    {
        var c = new C();
        System.Console.WriteLine(c.Property);
        System.Console.WriteLine(c.Property);
        System.Console.WriteLine(c.Property);
    }
}";
            var compilation = CompileAndVerify(source, expectedOutput: @"
0
1
2");
            compilation.VerifyIL("C.Property.get", @"{
    // Code size       18 (0x12)
    .maxstack  3
    .locals init (int V_0)
    IL_0000:  ldarg.0
    IL_0001:  ldarg.0
    IL_0002:  ldfld      ""int C.<Property>k__BackingField""
    IL_0007:  stloc.0
    IL_0008:  ldloc.0
    IL_0009:  ldc.i4.1
    IL_000a:  add
    IL_000b:  stfld      ""int C.<Property>k__BackingField""
    IL_0010:  ldloc.0
    IL_0011:  ret
}");
        }

        [Fact]
        public void ExpressionBodiedStructProperty()
        {
            var source = @"
struct S
{
    int Property => field++;

    static void Main()
    {
        var s = new S();
        System.Console.WriteLine(s.Property);
        System.Console.WriteLine(s.Property);
        System.Console.WriteLine(s.Property);
    }
}";
            var compilation = CompileAndVerify(source, expectedOutput: @"
0
1
2");
            compilation.VerifyIL("S.Property.get", @"{
    // Code size       18 (0x12)
    .maxstack  3
    .locals init (int V_0)
    IL_0000:  ldarg.0
    IL_0001:  ldarg.0
    IL_0002:  ldfld      ""int S.<Property>k__BackingField""
    IL_0007:  stloc.0
    IL_0008:  ldloc.0
    IL_0009:  ldc.i4.1
    IL_000a:  add
    IL_000b:  stfld      ""int S.<Property>k__BackingField""
    IL_0010:  ldloc.0
    IL_0011:  ret
}");
        }

        [Fact]
        public void RefToClassField()
        {
            var source = @"
class C
{
    int Property => System.Threading.Interlocked.Increment(ref field);

    static void Main()
    {
        var c = new C();
        System.Console.WriteLine(c.Property);
        System.Console.WriteLine(c.Property);
        System.Console.WriteLine(c.Property);
    }
}";
            var compilation = CompileAndVerify(source, expectedOutput: @"
1
2
3");
            compilation.VerifyIL("C.Property.get", @"{
    // Code size       12 (0xc)
    .maxstack  1
    IL_0000:  ldarg.0
    IL_0001:  ldflda     ""int C.<Property>k__BackingField""
    IL_0006:  call       ""int System.Threading.Interlocked.Increment(ref int)""
    IL_000b:  ret
}");
        }

        [Fact]
        public void NameofField()
        {
            var source = @"
class C
{
    static string Property => nameof(field);

    static void Main()
    {
        System.Console.WriteLine(Property);
    }
}";
            var compilation = CompileAndVerify(source, expectedOutput: "field", symbolValidator: AssertNoFieldsInClassC);

            compilation.VerifyIL("C.Property.get", @"{
    // Code size        6 (0x6)
    .maxstack  1
    IL_0000:  ldstr      ""field""
    IL_0005:  ret
}");
        }

        private static void AssertNoFieldsInClassC(ModuleSymbol module)
        {
            var classSymbol = (TypeSymbol)module.GlobalNamespace.GetMember("C");

            Assert.Empty(classSymbol.GetMembers().OfType<FieldSymbol>());
        }
    }
}
