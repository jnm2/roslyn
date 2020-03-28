// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis.Emit;

namespace Microsoft.CodeAnalysis.CSharp.Emit
{
    internal sealed class CSharpRootModuleType : Cci.RootModuleType
    {
        private int _frozen;

        private ImmutableArray<Cci.IMethodDefinition> _orderedSynthesizedMethods;
        private readonly ConcurrentDictionary<string, Cci.IMethodDefinition> _synthesizedMethods =
            new ConcurrentDictionary<string, Cci.IMethodDefinition>();

        public override IEnumerable<Cci.IMethodDefinition> GetMethods(EmitContext context)
        {
            Debug.Assert(IsFrozen);
            return _orderedSynthesizedMethods;
        }

        // Add a new synthesized method indexed by its name if the method isn't already present.
        internal bool TryAddSynthesizedMethod(Cci.IMethodDefinition method)
        {
            Debug.Assert(!IsFrozen);
#nullable disable // Can 'method.Name' be null? https://github.com/dotnet/roslyn/issues/39166
            return _synthesizedMethods.TryAdd(method.Name, method);
#nullable enable
        }

        private bool IsFrozen => _frozen != 0;

        internal void Freeze()
        {
            var wasFrozen = Interlocked.Exchange(ref _frozen, 1);
            if (wasFrozen != 0)
            {
                throw new InvalidOperationException();
            }

            _orderedSynthesizedMethods = _synthesizedMethods.OrderBy(kvp => kvp.Key).Select(kvp => kvp.Value).AsImmutable();
        }
    }
}
