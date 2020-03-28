// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Xunit;

namespace Microsoft.CodeAnalysis.CSharp.UnitTests.CodeGen
{
    public class CodeGenModuleInitializerTests : EmitMetadataTestBase
    {
        [Fact]
        public void ModuleTypeStaticConstructorTriggersUserModuleInitializerClassStaticConstructor()
        {
            string source = @"
[module: System.Runtime.CompilerServices.ModuleInitializerAttribute(typeof(MyModuleInitializer))]

internal static class MyModuleInitializer
{
    static MyModuleInitializer()
    {
    }
}

namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Module, AllowMultiple = false)]
    public sealed class ModuleInitializerAttribute : Attribute
    {
        public ModuleInitializerAttribute(Type type) { }
    }
}";
            var verifier = CompileAndVerify(source);

            verifier.VerifyIL("<Module>..cctor", @"
{
  // Code size        6 (0x6)
  .maxstack  0
  IL_0000:  call       ""void MyModuleInitializer.<TriggerClassConstructor>()""
  IL_0005:  ret
}");

            verifier.VerifyIL("MyModuleInitializer.<TriggerClassConstructor>", @"
{
  // Code size        1 (0x1)
  .maxstack  0
  IL_0000:  ret
}");
        }
    }
}
