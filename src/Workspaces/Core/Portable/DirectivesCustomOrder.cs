// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.CodeAnalysis.PooledObjects;

namespace Microsoft.CodeAnalysis.Editing
{
    internal class DirectivesCustomOrder
    {
        /// <summary>
        /// Pattern used to match any pattern.
        /// </summary>
        private const char JOKER = '*';

        /// <summary>
        /// Twice <see cref="JOKER"/>
        /// </summary>
        private readonly string DOUBLE_JOKER = JOKER.ToString() + JOKER.ToString();

        /// <summary>
        /// Pattern used to split names.
        /// </summary>
        private const char DELIMITER = '.';

        /// <summary>
        /// Pattern used to determine the amount of newlines between each matching group.
        /// </summary>
        private const char SEPARATOR = ';';

        private Node node = new Node();

        /// <summary>
        /// On <see langword="true"/>, unmatched namespaces are grouped based on the first <c>pattern</c> (as described by comments in <see cref="ProcessImportDirectivesCustomOrder(ReadOnlyMemory{char})"/>).<br/>
        /// This is useful when <see cref="GenerationOptions.SeparateImportDirectiveGroups"/> is <see langword="true"/>.<br/>
        /// <example>
        /// If <see cref="groupUnmatches"/> and <see cref="GenerationOptions.SeparateImportDirectiveGroups"/> are <see langword="true"/>:
        /// <code>
        /// System.Collections<br/>
        /// System.Data<br/>
        /// <br/>
        /// Microsoft.Windows<br/>
        /// <br/>
        /// Newtownsoft.Json<br/>
        /// <br/>
        /// Xamarin<br/>
        /// Xamarin.Forms
        /// </code>
        /// <br/>
        /// If <see cref="groupUnmatches"/> is <see langword="false"/> and <see cref="GenerationOptions.SeparateImportDirectiveGroups"/> is <see langword="true"/>:
        /// <code>
        /// System.Collections<br/>
        /// System.Data<br/>
        /// Microsoft.Windows<br/>
        /// Newtownsoft.Json<br/>
        /// Xamarin<br/>
        /// Xamarin.Forms
        /// </code>
        /// </example>
        /// </summary>
        /// <remark>Keep name in sync with tests.</remark>
        private bool groupUnmatches;

        internal static Optional<DirectivesCustomOrder> Parse(string args)
        {
            var configuration = new DirectivesCustomOrder();

            var patterns = configuration.ProcessImportDirectivesCustomOrder(args.AsMemory());

            if (patterns is null)
                return new Optional<DirectivesCustomOrder>();

            foreach (var kv in patterns)
            {
                configuration.node.AddNode(kv.Key.AsMemory(), kv.Value, 0);
            }

            return new Optional<DirectivesCustomOrder>(configuration);
        }

        private PooledDictionary<string, int> ProcessImportDirectivesCustomOrder(ReadOnlyMemory<char> importDirectivesCustomOrder)
        {
            // Note: for the purpose of the documentation of this method and its examples
            // we consider that JOKER is '*', DELIMITER is '.' and SEPARATOR is ';'.
            //
            // A joker pattern is a pattern which only contains JOKER, such as "*". This pattern can contain either 1 or 2 JOKER.
            // Only one joker pattern can be used at the same time.
            //
            // Additionally, an input configuration can be split in the following parts (EBNF):
            // patternGroup = name, {DELIMITER, name}
            // configuration = { whitespace }, [patternGroup], {{ whitespace }, SEPARATOR, { whitespace }, patternGroup}, { whitespace }
            // Examples:
            // "  System;Microsoft.Xyz;Microsoft;*;Xamarin"
            // "System  ;  Microsoft.Xyz  ;  Microsoft  ;  *  ;  Xamarin"
            // "*  "
            // "System.Collection.Generic.List<int>"
            // ""
            // "   "
            //
            // Rules:
            //
            // No pattern group can be duplicated:
            // "System ; Microsoft ; Windows ; System"
            //  ^^^^^^                         ^^^^^^
            //
            // name can't be empty nor be whitespace:
            // "System ; Microsoft ; ; Windows"
            //                      ^
            // "System ; Microsoft ;; Windows"
            //                     ^^
            //
            // Whitespaces are not allowed inside a patternGroup
            // "System . Coll ections ; Microsoft ; Windows"
            //        ^ ^    ^
            // The same for JOKER
            // "System*.*Coll*ections ; Microsoft ; Windows"
            //        ^ ^    ^
            //
            // Whitespaces are defined by char.IsWhiteSpace(character)
            //
            // If the joker pattern is not found, we must add a double joker pattern to the end
            // Example:
            // "System" -> "System;**"
            // "System ; Microsoft ; Windows" -> "System ; Microsoft ; Windows;**"

            var patterns = PooledDictionary<string, int>.GetInstance();

            var importDirectivesCustomOrderSpan = importDirectivesCustomOrder.Span;

            var startIndex = 0;
            var order = 0;
            ReadOnlyMemory<char> pattern;

            // Whenever a joker pattern was found in the user provided configuration
            var foundJoker = false;

            var doubleJokerSpan = DOUBLE_JOKER.AsSpan();

            for (var i = 0; i < importDirectivesCustomOrderSpan.Length; i++)
            {
                // Check if we are facing the end of a pattern group
                if (importDirectivesCustomOrderSpan[i] == SEPARATOR)
                {
                    var length = i - startIndex;

                    // Pattern can't be empty
                    // "System ; Microsoft ;; Windows"
                    //                     ^^
                    if (length == 0)
                        return null;

                    pattern = importDirectivesCustomOrder.Slice(startIndex, length);
                    var patternSpan = pattern.Span;

                    // Check if it's a ungrouped joker pattern
                    if (patternSpan.Length == 1 && patternSpan[0] == JOKER)
                    {
                        // No pattern can be twice. Usually this is handled bellow with a Dictionary,
                        // but since there are two variations of joker (single and double JOKER)
                        // we must check it here
                        if (foundJoker)
                            return null;
                        foundJoker = true;
                    }
                    // Check if it's a grouped joker pattern
                    else if (patternSpan.Length == 2 && patternSpan.SequenceEqual(doubleJokerSpan))
                    {
                        // No pattern can be twice. Usually this is handled bellow with a Dictionary,
                        // but since there are two variations of joker (single and double JOKER)
                        // we must check it here
                        if (foundJoker)
                            return null;
                        foundJoker = true;
                        groupUnmatches = true;
                    }
                    else
                    {
                        // Trim whitespace
                        var start = 0;
                        for (; start < patternSpan.Length && char.IsWhiteSpace(patternSpan[start]); start++)
                            ;

                        // A pattern can't be only whitespaces nor empty
                        // "  "
                        //  ^^
                        if (start == patternSpan.Length)
                            return null;

                        var end = patternSpan.Length - 1;
                        for (; end > 0 && char.IsWhiteSpace(patternSpan[end]); end--)
                            ;

                        pattern = pattern[start..(end + 1)];
                        patternSpan = pattern.Span;

                        // Since this is not a joker pattern we must check additional requisites

                        // In the middle of a pattern, multiple DELIMITERs are not allowed, nor any JOKER not whitespace
                        // Example:
                        // System..IO
                        //       ^^
                        // System.*.IO
                        //        ^
                        // Sy stem. Coll*ction
                        //   ^     ^    ^
                        // System*. .Collection
                        //       ^ ^
                        var foundDelimiter = false;
                        for (var j = 0; j < pattern.Length; j++)
                        {
                            if (patternSpan[j] == JOKER || char.IsWhiteSpace(patternSpan[j]))
                            {
                                return null;
                            }

                            if (patternSpan[j] == DELIMITER)
                            {
                                if (foundDelimiter)
                                {
                                    return null;
                                }
                                else
                                {
                                    foundDelimiter = true;
                                }
                            }
                            else
                            {
                                foundDelimiter = false;
                            }
                        }
                    }

                    // We don't allow duplicated entries
                    // Example:
                    // "System;*;Microsoft;*"
                    //         ^           ^
                    // "System;Microsoft;Windows;System"
                    //  ^^^^^^                   ^^^^^^
                    var stringPattern = pattern.ToString();
                    if (patterns.ContainsKey(stringPattern))
                        return null;
                    else
                        patterns.Add(stringPattern, order++);

                    // + 1 to exclude the SEPARATOR from the next pattern
                    startIndex = i + 1;
                }
            }

            // If not joker pattern was found we add one
            if (!foundJoker)
            {
                // If that pattern isn't found, we add an grouped joker pattern.
                // This will have a higher order than the last pattern, which means it will go to the bottom.
                patterns.Add(doubleJokerSpan.ToString(), order);
                groupUnmatches = true;
            }

            Debug.Assert(patterns.Count > 0, "Patterns can't be empty.");
            return patterns;
        }

        internal static string Serialize(DirectivesCustomOrder arg)
        {
            throw new NotImplementedException();
        }

        private class Node
        {
            private List<Node> _nodes;

            public readonly ReadOnlyMemory<char> pattern;

            private int _order;

            private bool _isTerminal;

            private Node(ReadOnlyMemory<char> pattern)
            {
                this.pattern = pattern;
                _isTerminal = false;
            }

            private Node(ReadOnlyMemory<char> pattern, int order) : this(pattern)
            {
                _order = order;
                _isTerminal = true;
            }

            public Node()
            {
            }

            public void AddNode(ReadOnlyMemory<char> patternGroup, int order, int startIndex)
            {
                Debug.Assert(startIndex <= patternGroup.Length, $"{nameof(startIndex)} can not be higher than {nameof(patternGroup)}.Length.");

                // If we already reached the end of patternGroup there is nothing we can do
                if (startIndex == patternGroup.Length)
                    return;

                var pattern = GetPattern(patternGroup, startIndex);
                // +1 due DELIMITER length
                startIndex += pattern.Length + 1;

                var patternSpan = pattern.Span;

                if (_nodes is null)
                {
                    _nodes = new List<Node>();
                }
                else
                {
                    for (var i = 0; i < _nodes.Count; i++)
                    {
                        var child = _nodes[i];
                        if (patternSpan.SequenceEqual(child.pattern.Span))
                        {
                            // If we still have patterns we must add a new node
                            // Otherwise we must edit the last matched node to turn it into terminal
                            // We use > instead of == due to DELIMITER length ((startIndex - 1) == patternGroup.Length)
                            if (startIndex > patternGroup.Length)
                            {
                                // At this point we are not adding a new node but turning into terminal one
                                Debug.Assert(!child._isTerminal, "The node is already terminal");
                                child._isTerminal = true;
                                child._order = order;
                            }
                            else
                            {
                                child.AddNode(patternGroup, order, startIndex);
                            }
                            return;
                        }
                    }
                }

                // If we still have patterns we must add a new node and then gather those patterns
                // Otherwise we add a terminal node
                // We use > instead of == due to DELIMITER length ((startIndex - 1) == patternGroup.Length)
                if (startIndex > patternGroup.Length)
                {
                    // At this point there are no more patterns to add
                    _nodes.Add(new Node(pattern, order));
                }
                else
                {
                    // At this point there are additional patterns to add
                    var node = new Node(pattern);
                    _nodes.Add(node);
                    node.AddNode(patternGroup, order, startIndex);
                }
            }

            protected static ReadOnlyMemory<char> GetPattern(ReadOnlyMemory<char> patternGroup, int startIndex)
            {
                // We extract a pattern from the patternGroup
                // Example:
                // System.Collection.Generic.*
                //       ^          ^
                //       |^^^^^^^^^^|
                //       |       |  |
                //   startIndex  |  |
                //       |   pattern|
                //       |-----> DELIMITER

                var patternGroupSpan = patternGroup.Span;
                var i = startIndex;
                for (; i < patternGroup.Length && patternGroupSpan[i] != DELIMITER; i++)
                    ;
                return patternGroup[startIndex..i];
            }
        }
    }
}
