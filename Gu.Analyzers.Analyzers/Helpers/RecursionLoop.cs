﻿namespace Gu.Analyzers
{
    using System;
    using System.Collections.Generic;
    using Microsoft.CodeAnalysis;

    internal class RecursionLoop
    {
        private readonly List<SyntaxNode> nodes = new List<SyntaxNode>();

        private bool IsRecursion
        {
            get
            {
                if (this.nodes.Count > 100)
                {
                    throw new InvalidOperationException();
                }

                if (this.nodes.Count < 2)
                {
                    return false;
                }

                var offset = 1;
                while (!this.EqualsAtOffset(0, offset))
                {
                    if (offset >= this.nodes.Count / 2)
                    {
                        return false;
                    }

                    offset++;
                }

                var start = 1;
                while (start < offset)
                {
                    if (!this.EqualsAtOffset(start, offset))
                    {
                        return false;
                    }

                    start++;
                }

                return true;
            }
        }

        public bool Add(SyntaxNode node)
        {
            this.nodes.Add(node);
            return !this.IsRecursion;
        }

        public void Clear() => this.nodes.Clear();

        private bool EqualsAtOffset(int start, int offset)
        {
            return ReferenceEquals(
                this.nodes[this.nodes.Count - 1 - start],
                this.nodes[this.nodes.Count - 1 - start - offset]);
        }
    }
}
