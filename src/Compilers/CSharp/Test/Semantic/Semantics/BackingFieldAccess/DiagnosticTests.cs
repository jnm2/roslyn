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
    }
}
