﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Composition;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.Host.Mef;

namespace Microsoft.CodeAnalysis.Execution
{
    [ExportWorkspaceServiceFactory(typeof(ISolutionChecksumService)), Shared]
    internal class SolutionChecksumServiceFactory : IWorkspaceServiceFactory
    {
        private readonly ChecksumTreeCollection _storages = new ChecksumTreeCollection();

        public IWorkspaceService CreateService(HostWorkspaceServices workspaceServices)
        {
            return new Service(workspaceServices, _storages);
        }

        internal class Service : ISolutionChecksumService
        {
            private readonly HostWorkspaceServices _workspaceServices;
            private readonly ChecksumTreeCollection _storages;

            public Service(HostWorkspaceServices workspaceServices, ChecksumTreeCollection storages)
            {
                _workspaceServices = workspaceServices;
                _storages = storages;
            }

            public Serializer Serializer_TestOnly => new Serializer(_workspaceServices);

            public void AddGlobalAsset(object value, Asset asset, CancellationToken cancellationToken)
            {
                _storages.AddGlobalAsset(value, asset, cancellationToken);
            }

            public Asset GetGlobalAsset(object value, CancellationToken cancellationToken)
            {
                return _storages.GetGlobalAsset(value, cancellationToken);
            }

            public void RemoveGlobalAsset(object value, CancellationToken cancellationToken)
            {
                _storages.RemoveGlobalAsset(value, cancellationToken);
            }

            public async Task<ChecksumScope> CreateChecksumAsync(Solution solution, CancellationToken cancellationToken)
            {
                // TODO: add logging mechanism
                var checksumTree = _storages.CreateChecksumTree(solution);

                var builder = new SnapshotBuilder(checksumTree);
                var snapshot = new ChecksumScope(_storages, checksumTree, await builder.BuildAsync(solution, cancellationToken).ConfigureAwait(false));

                return snapshot;
            }

            public ChecksumObject GetChecksumObject(Checksum checksum, CancellationToken cancellationToken)
            {
                return _storages.GetChecksumObject(checksum, cancellationToken);
            }
        }
    }
}
