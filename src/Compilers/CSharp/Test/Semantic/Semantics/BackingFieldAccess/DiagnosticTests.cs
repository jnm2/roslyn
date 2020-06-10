// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CodeAnalysis.CSharp.Test.Utilities;
using Microsoft.CodeAnalysis.Test.Utilities;
using Roslyn.Test.Utilities;
using Xunit;

namespace Microsoft.CodeAnalysis.CSharp.Semantic.UnitTests.Semantics.BackingFieldAccess
{
    public sealed class DiagnosticTests : CSharpTestBase
    {
        [Fact]
        public void PermittedInInterfaceStaticProperty()
        {
            var source = @"
interface I
{
    static int Property => field++;

    static void Main()
    {
        System.Console.WriteLine(Property);
        System.Console.WriteLine(Property);
        System.Console.WriteLine(Property);
    }
}";
            CompileAndVerify(
                source,
                targetFramework: TargetFramework.NetStandardLatest,
                expectedOutput: ExecutionConditionUtil.IsMonoOrCoreClr ? @"
0
1
2" : null,
                verify: ExecutionConditionUtil.IsMonoOrCoreClr ? Verification.Passes : Verification.Skipped);
        }

        [Fact]
        public void NotPermittedInInterfaceInstanceProperty()
        {
            var source = @"
interface I
{
    int Property => field;
}";
            var compilation = CreateCompilation(source, targetFramework: TargetFramework.NetStandardLatest);

            compilation.VerifyEmitDiagnostics(
                // (4,21): error CS0525: Interfaces cannot contain instance fields
                //     int Property => field;
                Diagnostic(ErrorCode.ERR_InterfacesCantContainFields, "field").WithLocation(4, 21));
        }

        [Fact]
        public void DiagnosticForEachOccurrenceInInterfaceInstanceProperty()
        {
            var source = @"
interface I
{
    int Property { get => field + field; set => field = field + 1; }
}";
            var compilation = CreateCompilation(source, targetFramework: TargetFramework.NetStandardLatest);

            compilation.VerifyEmitDiagnostics(
                // (4,27): error CS0525: Interfaces cannot contain instance fields
                //     int Property { get => field + field; set => field = field + 1; }
                Diagnostic(ErrorCode.ERR_InterfacesCantContainFields, "field").WithLocation(4, 27),
                // (4,35): error CS0525: Interfaces cannot contain instance fields
                //     int Property { get => field + field; set => field = field + 1; }
                Diagnostic(ErrorCode.ERR_InterfacesCantContainFields, "field").WithLocation(4, 35),
                // (4,49): error CS0525: Interfaces cannot contain instance fields
                //     int Property { get => field + field; set => field = field + 1; }
                Diagnostic(ErrorCode.ERR_InterfacesCantContainFields, "field").WithLocation(4, 49),
                // (4,57): error CS0525: Interfaces cannot contain instance fields
                //     int Property { get => field + field; set => field = field + 1; }
                Diagnostic(ErrorCode.ERR_InterfacesCantContainFields, "field").WithLocation(4, 57));
        }

        [Fact]
        public void Getter_using_field_and_auto_setter()
        {
            var source = @"
class C
{
    static int Property { get => field * 10; set; }

    static void Main()
    {
        Property = 1;
        System.Console.WriteLine(Property);
        Property = 2;
        System.Console.WriteLine(Property);
    }
}";
            CompileAndVerify(
                source,
                expectedOutput: @"
10
20");
        }

        [Fact]
        public void Setter_using_field_and_auto_getter()
        {
            var source = @"
class C
{
    static int Property { get; set => field = value * 10; }

    static void Main()
    {
        Property = 1;
        System.Console.WriteLine(Property);
        Property = 2;
        System.Console.WriteLine(Property);
    }
}";
            CompileAndVerify(
                source,
                expectedOutput: @"
10
20");
        }
    }
}
