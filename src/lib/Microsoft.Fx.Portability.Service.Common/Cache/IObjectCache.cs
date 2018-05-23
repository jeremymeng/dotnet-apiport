﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Fx.Portability.Cache
{
    public interface IObjectCache : IDisposable
    {
        string Identifier { get; }

        Task<CacheUpdateStatus> UpdateAsync(CancellationToken token);

        DateTimeOffset LastUpdated { get; }
    }
}
