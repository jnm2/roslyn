﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.PooledObjects;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis
{
    public readonly struct IncrementalValueSources
    {
        private readonly ArrayBuilder<ISyntaxInputNode> _syntaxInputBuilder;
        private readonly ArrayBuilder<IIncrementalGeneratorOutputNode> _outputNodes;

        internal IncrementalValueSources(ArrayBuilder<ISyntaxInputNode> syntaxInputBuilder, ArrayBuilder<IIncrementalGeneratorOutputNode> outputNodes)
        {
            _syntaxInputBuilder = syntaxInputBuilder;
            _outputNodes = outputNodes;
        }

        public SyntaxValueSources Syntax => new SyntaxValueSources(_syntaxInputBuilder, RegisterOutput);

        public IncrementalValueSource<Compilation> Compilation => new IncrementalValueSource<Compilation>(SharedInputNodes.Compilation.WithRegisterOutput(RegisterOutput));

        public IncrementalValueSource<ParseOptions> ParseOptions => new IncrementalValueSource<ParseOptions>(SharedInputNodes.ParseOptions.WithRegisterOutput(RegisterOutput));

        public IncrementalValueSource<AdditionalText> AdditionalTexts => new IncrementalValueSource<AdditionalText>(SharedInputNodes.AdditionalTexts.WithRegisterOutput(RegisterOutput));

        public IncrementalValueSource<AnalyzerConfigOptionsProvider> AnalyzerConfigOptions => new IncrementalValueSource<AnalyzerConfigOptionsProvider>(SharedInputNodes.AnalyzerConfigOptions.WithRegisterOutput(RegisterOutput));

        private void RegisterOutput(IIncrementalGeneratorOutputNode outputNode)
        {
            if (!_outputNodes.Contains(outputNode))
            {
                _outputNodes.Add(outputNode);
            }
        }
    }

    /// <summary>
    /// Holds input nodes that are shared between generators and always exist
    /// </summary>
    internal static class SharedInputNodes
    {
        public static readonly InputNode<Compilation> Compilation = new InputNode<Compilation>(b => ImmutableArray.Create(b.Compilation));

        public static readonly InputNode<ParseOptions> ParseOptions = new InputNode<ParseOptions>(b => ImmutableArray.Create(b.DriverState.ParseOptions));

        public static readonly InputNode<AdditionalText> AdditionalTexts = new InputNode<AdditionalText>(b => b.DriverState.AdditionalTexts);

        public static readonly InputNode<SyntaxTree> SyntaxTrees = new InputNode<SyntaxTree>(b => b.Compilation.SyntaxTrees.ToImmutableArray());

        public static readonly InputNode<AnalyzerConfigOptionsProvider> AnalyzerConfigOptions = new InputNode<AnalyzerConfigOptionsProvider>(b => ImmutableArray.Create(b.DriverState.OptionsProvider));
    }
}
