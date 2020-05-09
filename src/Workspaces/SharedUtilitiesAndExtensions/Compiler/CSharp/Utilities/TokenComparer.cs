// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Globalization;
using Microsoft.CodeAnalysis.CSharp.Extensions;

namespace Microsoft.CodeAnalysis.CSharp.Utilities
{
    internal class TokenComparer : IComparer<SyntaxToken>
    {
        public static readonly IComparer<SyntaxToken> NormalInstance = new TokenComparer(specialCaseSystem: false);
        public static readonly IComparer<SyntaxToken> SystemFirstInstance = new TokenComparer(specialCaseSystem: true);

        private static readonly string[] s_specialPriorities = new string[]
        {
            // Determines the order of special namespaces. They will be placed at the top.
            nameof(System),
            nameof(Microsoft),
            // We can't use nameof() because they don't exist in current context.
            "Windows",
            "Xamarin",
        };

        private readonly bool _specialCaseSystem;

        private TokenComparer(bool specialCaseSystem)
            => _specialCaseSystem = specialCaseSystem;

        public int Compare(SyntaxToken x, SyntaxToken y)
        {
            // Check if we must handle some namespaces with more priority than others
            if (_specialCaseSystem &&
                x.GetPreviousToken(includeSkipped: true).IsKind(SyntaxKind.UsingKeyword, SyntaxKind.StaticKeyword) &&
                y.GetPreviousToken(includeSkipped: true).IsKind(SyntaxKind.UsingKeyword, SyntaxKind.StaticKeyword))
            {
                var token1Text = x.ValueText;
                var token2Text = y.ValueText;
                // We iterate over special priorities to check if this token is a choosen one
                for (var i = 0; i < s_specialPriorities.Length; i++)
                {
                    if (s_specialPriorities[i] == token1Text)
                    {
                        // If it's a choosen one we must check if the other token is also special
                        // However, we only need to check up to i. If it's greater than i, the second token has a lower priority
                        // Note: lower values means higher priority                        
                        for (var j = 0; j < i; j++)
                        {
                            if (s_specialPriorities[j] == token2Text)
                            {
                                // At this point we know that the second token has a higher priority than the first one
                                return 1;
                            }
                        }

                        // The second token doesn't have a higher priority than the first one, but it may have the same priority
                        // At this point this is true: i == j
                        if (s_specialPriorities[i] == token2Text)
                        {
                            // At this point both tokens has same priority
                            return 0;
                        }
                        else
                        {
                            // At this point we know than the second token has a lower priority than the first one
                            return -1;
                        }
                    }
                }

                // At this point the first token in not special, but the second may still be special
                for (var i = 0; i < s_specialPriorities.Length; i++)
                {
                    if (s_specialPriorities[i] == token2Text)
                    {
                        return 1;
                    }
                }
            }

            return CompareWorker(x, y);
        }

        private int CompareWorker(SyntaxToken x, SyntaxToken y)
        {
            if (x == y)
            {
                return 0;
            }

            // By using 'ValueText' we get the value that is normalized.  i.e.
            // @class will be 'class', and Unicode escapes will be converted
            // to actual Unicode.  This allows sorting to work properly across
            // tokens that have different source representations, but which
            // mean the same thing.
            var string1 = x.ValueText;
            var string2 = y.ValueText;

            // First check in a case insensitive manner.  This will put 
            // everything that starts with an 'a' or 'A' above everything
            // that starts with a 'b' or 'B'.
            var compare = CultureInfo.InvariantCulture.CompareInfo.Compare(string1, string2,
                CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace | CompareOptions.IgnoreWidth);
            if (compare != 0)
            {
                return compare;
            }

            // Now, once we've grouped such that 'a' words and 'A' words are
            // together, sort such that 'a' words come before 'A' words.
            return CultureInfo.InvariantCulture.CompareInfo.Compare(string1, string2,
                CompareOptions.IgnoreNonSpace | CompareOptions.IgnoreWidth);
        }
    }
}
