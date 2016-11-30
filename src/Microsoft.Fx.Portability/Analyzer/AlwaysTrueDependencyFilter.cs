﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using Microsoft.Fx.Portability.ObjectModel;

namespace Microsoft.Fx.Portability.Analyzer
{
    public class AlwaysTrueDependencyFilter : IDependencyFilter
    {
        public bool IsFrameworkAssembly(AssemblyReferenceInformation assembly)
        {
            return true;
        }

        public bool IsKnownThirdPartyAssembly(AssemblyReferenceInformation assembly)
        {
            return false;
        }
    }
}
