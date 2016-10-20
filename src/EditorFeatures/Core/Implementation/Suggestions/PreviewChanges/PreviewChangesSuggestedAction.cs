// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Editor.Host;
using Microsoft.CodeAnalysis.Shared.TestHooks;
using Microsoft.VisualStudio.Text;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.Editor.Implementation.Suggestions
{
    internal partial class SuggestedActionWithFlavors
    {
        /// <summary>
        /// Suggested action for showing the preview-changes dialog.  Note: this is only used
        /// as a 'flavor' inside CodeFixSuggestionAction and CodeRefactoringSuggestedAction.
        /// </summary>
        private sealed partial class PreviewChangesSuggestedAction : SuggestedAction
        {
            private PreviewChangesSuggestedAction(
                Workspace workspace,
                ITextBuffer subjectBuffer,
                ICodeActionEditHandlerService editHandler,
                IWaitIndicator waitIndicator,
                PreviewChangesCodeAction codeAction,
                object provider,
                IAsynchronousOperationListener operationListener)
                : base(workspace, subjectBuffer, editHandler, waitIndicator, codeAction, provider, operationListener)
            {
            }

            public static async Task<SuggestedAction> CreateAsync(
                SuggestedActionWithFlavors suggestedAction, CancellationToken cancellationToken)
            {
                var previewResult = await suggestedAction.GetPreviewResultAsync(cancellationToken).ConfigureAwait(true);
                if (previewResult == null)
                {
                    return null;
                }

                var changeSummary = previewResult.ChangeSummary;
                if (changeSummary == null)
                {
                    return null;
                }

                var previewAction = new PreviewChangesCodeAction(
                    suggestedAction.Workspace, suggestedAction.CodeAction, changeSummary);
                return new PreviewChangesSuggestedAction(
                    suggestedAction.Workspace, suggestedAction.SubjectBuffer, suggestedAction.EditHandler,
                    suggestedAction.WaitIndicator, previewAction, suggestedAction.Provider, suggestedAction.OperationListener);
            }
        }
    }
}