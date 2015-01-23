﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Microsoft.DotNet.CodeFormatting.Tests
{
    /// <summary>
    /// A test which runs all rules on a given piece of code 
    /// </summary>
    public sealed class CombinationTest : CodeFormattingTestBase
    {
        private static FormattingEngineImplementation s_formattingEngine;

        static CombinationTest()
        {
            s_formattingEngine = (FormattingEngineImplementation)FormattingEngine.Create(Enumerable.Empty<string>(), Enumerable.Empty<string>());
        }

        protected override async Task<Document> RewriteDocumentAsync(Document document)
        {
            var solution = await s_formattingEngine.FormatCoreAsync(
                document.Project.Solution,
                new[] { document.Id },
                CancellationToken.None);
            return solution.GetDocument(document.Id);
        }

        [Fact]
        public void FieldUse()
        {
            var text = @"
class C {
    int field;

    void M() {
        N(this.field);
    }
}";

            var expected = @"
internal class C
{
    private int _field;

    private void M()
    {
        N(_field);
    }
}";

            Verify(text, expected, runFormatter: false);
        }

        [Fact]
        public void FieldAssignment()
        {
            var text = @"
class C {
    int field;

    void M() {
        this.field = 42;
    }
}";

            var expected = @"
internal class C
{
    private int _field;

    private void M()
    {
        _field = 42;
    }
}";

            Verify(text, expected, runFormatter: false);
        }
    }
}
