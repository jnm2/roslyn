// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CodeAnalysis.CSharp.Test.Utilities;
using Xunit;

namespace Microsoft.CodeAnalysis.CSharp.UnitTests.CodeGen
{
    public sealed class CodeGenBackingFieldAccessTests : CSharpTestBase
    {
        // TODO: Emit with one auto and one non-auto accessor to flush out diagnostics work

        [Fact]
        public void ExpressionBodiedProperty()
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
            // TODO: test that no field is emitted if nameofs are the only usages

            var compilation = CompileAndVerify(source, expectedOutput: "field");

            compilation.VerifyIL("C.Property.get", @"{
      // Code size        6 (0x6)
      .maxstack  1
      IL_0000:  ldstr      ""field""
      IL_0005:  ret
}");
        }
    }
}
