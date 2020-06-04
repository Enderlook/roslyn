// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.CodeAnalysis.PooledObjects;

namespace Microsoft.CodeAnalysis.Editing
{
    internal class DirectivesCustomOrder
    {
        /// <summary>
        /// Pattern used to match any pattern.
        /// </summary>
        private const string JOKER = "*";

        /// <summary>
        /// Pattern used to split names.
        /// </summary>
        private const string DELIMITER = ".";

        /// <summary>
        /// Pattern used to determine the amount of newlines between each matching group.
        /// </summary>
        private const string SEPARATOR = "\n";

        internal static Optional<DirectivesCustomOrder> Parse(string args)
        {
            var patterns = ProcessImportDirectivesCustomOrder(args.AsMemory());

            return default;
        }

        /// <summary>
        /// Check if <paramref name="toMatch"/> can be found in <paramref name="text"/> starting at index <paramref name="startIndex"/>.
        /// </summary>
        /// <param name="text"></param>
        /// <param name="startIndex"></param>
        /// <param name="toMatch"></param>
        /// <returns>Whenever <paramref name="toMatch"/> can be found in <paramref name="text"/> starting at index <paramref name="startIndex"/>.</returns>
        private static bool MatchFromIndex(ReadOnlySpan<char> text, int startIndex, string toMatch)
        {
            Debug.Assert(startIndex >= 0, $"{nameof(startIndex)} can not be negative.");
            Debug.Assert(text.Length > startIndex, $"{nameof(startIndex)} must be lower than {nameof(text)}.Length.");
            Debug.Assert(toMatch != null, $"{toMatch} can not be null.");
            for (var j = 0; j < toMatch.Length; j++)
            {
                var i = startIndex + j;
                if (i >= text.Length)
                    return false;

                if (text[i] != toMatch[j])
                    return false;
            }
            return true;
        }

        private static PooledDictionary<ReadOnlyMemory<char>, (int lines, int order)> ProcessImportDirectivesCustomOrder(ReadOnlyMemory<char> importDirectivesCustomOrder)
        {
            // Note: for the purpose of the documentation of this method and its examples
            // we consider that JOKER is "*", DELIMITER is "." and SEPARATOR is "\n".
            // A joker pattern is pattern which only contains JOKER, such as "*"
            //
            // Additionally, an input configuration can be split in the following parts (EBNF):
            // patternGroup = [{name, DELIMITER}, name, DELIMITER], JOKER;
            // configuration = {SEPARATOR}, {patternGroup, SEPARATOR, {SEPARATOR}}, [patternGroup], {SEPARATOR}
            // Examples:
            // "\n\n\nSystem.Collection.Generic.*\nMicrosoft.*\nWindows.*\n\n*\n\nXamarin.*\n\n"
            // "*"
            // "System.*\nMicrosoft.*\n\nSystem.IO.*"
            // "System.Collection.Generic.List<int>.*"
            //
            // Note that no pattern group can be duplirepeatedcate:
            // "System.*\nMicrosoft.*\nWindows.*\nSystem.*"
            //  ^^^^^^^^                          ^^^^^^^^
            //
            // Finally, if the joker pattern is not found, we must add it to the end (prepended by a SEPARATOR)
            // ignoring any other leading SEPARATORs
            // Example:
            // "System.*\n\n\n\n" -> "System.*\n*"
            // "Microsoft.*" -> "Microsoft.*\n*"

            var importDirectivesCustomOrderSpan = importDirectivesCustomOrder.Span;

            var i = 0;
            // Ignore trailing separators
            // Example:
            // "\n\n\nSystem.*\nMicrosoft.*\nWindows.*\n\n*\n\nXamarin.*" -> "System.*\nMicrosoft.*\nWindows.*\n\n*\n\nXamarin.*"
            while (MatchFromIndex(importDirectivesCustomOrderSpan, i, SEPARATOR))
            {
                i += SEPARATOR.Length;
            }

            var patterns = PooledDictionary<ReadOnlyMemory<char>, (int lines, int order)>.GetInstance();

            var startIndex = i;
            var order = 0;
            ReadOnlyMemory<char> pattern;
            ReadOnlyMemory<char> lastFoundPattern = default;
            // Whenever a joker pattern was found in the user provided configuration
            var foundJoker = false;
            while (i < importDirectivesCustomOrder.Length)
            {
                // Check if we are facing the end of a pattern group
                if (MatchFromIndex(importDirectivesCustomOrderSpan, i, SEPARATOR))
                {
                    var length = i - startIndex;
                    Debug.Assert(length > 0, "A pattern can not be empty.");

                    i += SEPARATOR.Length;

                    pattern = importDirectivesCustomOrder.Slice(startIndex, length);
                    var patternSpan = pattern.Span;

                    // The last characters of a pattern must always be a JOKER
                    // Example:
                    // System.IO.*
                    //           ^
                    // *
                    // ^
                    if (!MatchFromIndex(patternSpan, pattern.Length - JOKER.Length, JOKER))
                    {
                        return null;
                    }

                    // If pattern has the same length as JOKER it must be a joker pattern.
                    // We know that because we checked above if it ends with the same characters,
                    // and now we would also know that it has the same length.
                    if (pattern.Length == JOKER.Length)
                    {
                        foundJoker = true;
                    }
                    else
                    {
                        // So, if this is not a joker pattern, we must check additional requisites

                        // Previous to the JOKER there must be a DELIMITER
                        // Example:
                        // System.IO.*
                        //          ^
                        if (!MatchFromIndex(patternSpan, pattern.Length - JOKER.Length - DELIMITER.Length, DELIMITER))
                        {
                            return null;
                        }

                        // Previous to the DELIMITER (which is previous to the JOKER) there must be something
                        // Example
                        // System.*
                        //      ^
                        if (pattern.Length == JOKER.Length + DELIMITER.Length)
                        {
                            return null;
                        }

                        var foundDelimiter = false;
                        for (var j = 0; j < pattern.Length - JOKER.Length - DELIMITER.Length; j++)
                        {
                            // In the middle of a pattern, multiple DELIMITERs are not allowed, nor any JOKER
                            // Example:
                            // System..IO.*
                            //        ^
                            // System.*.IO.*
                            //        ^
                            // System.Coll*ction
                            //            ^
                            if (MatchFromIndex(patternSpan, j, JOKER))
                            {
                                return null;
                            }

                            if (MatchFromIndex(patternSpan, j, SEPARATOR))
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
                        }
                    }

                    // We don't allow duplicated entries
                    // Example:
                    // "System.*\nMicrosoft.*\nWindows.*\nSystem.*"
                    //  ^^^^^^^^                          ^^^^^^^^
                    // "*\nSystem.*\n*Microsoft.*"
                    //  ^            ^
                    if (patterns.ContainsKey(pattern))
                        return null;

                    // We calculate how many SEPARATORs are between this pattern and the next
                    var lines = 0;
                    while (i < importDirectivesCustomOrder.Length && MatchFromIndex(importDirectivesCustomOrderSpan, i, SEPARATOR))
                    {
                        lines++;
                        i += SEPARATOR.Length;
                    }
                    patterns.Add(pattern, (lines, order++));
                    startIndex = i;
                    lastFoundPattern = pattern;
                }
                else
                {
                    i++;
                }
            }

            // We update the last found pattern to remove any leading newlines:
            // Example:
            // "System.*\nMicrosoft.*\nWindows.*\n\n*\n\nXamarin.*\n\n\n" -> "System.*\nMicrosoft.*\nWindows.*\n\n*\n\nXamarin.*"
            if (patterns.TryGetValue(lastFoundPattern, out var tuple))
            {
                tuple.lines = 0;
                patterns[lastFoundPattern] = tuple;
            }

            // Additionally we add the joker pattern if this wasn't provided by the user
            if (!foundJoker)
            {
                // If that pattern isn't found, we add it with a fake priority (higher values goes to the bottom).
                patterns.Add(JOKER.AsMemory(), (1, int.MaxValue));
            }

            Debug.Assert(patterns.Count > 0, "Patterns can't be empty.");
            return patterns;
        }

        internal static string Serialize(DirectivesCustomOrder arg)
        {
            throw new NotImplementedException();
        }
    }
}
