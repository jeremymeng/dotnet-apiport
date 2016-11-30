﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.Fx.Portability.ObjectModel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection.Metadata;

namespace Microsoft.Fx.Portability.Analyzer
{
    internal class DependencyFinderEngineHelper
    {
        private readonly IDependencyFilter _assemblyFilter;
        private readonly MetadataReader _reader;
        private readonly string _assemblyLocation;

        private readonly AssemblyReferenceInformation _currentAssemblyInfo;
        private readonly string _currentAssemblyName;

        public DependencyFinderEngineHelper(IDependencyFilter assemblyFilter, MetadataReader metadataReader, IAssemblyFile file)
        {
            _assemblyFilter = assemblyFilter;
            _reader = metadataReader;
            _assemblyLocation = file.Name;

            MemberDependency = new List<MemberDependency>();
            CallingAssembly = new AssemblyInfo
            {
                AssemblyIdentity = metadataReader.FormatAssemblyInfo().ToString(),
                FileVersion = file.Version ?? string.Empty,
                TargetFrameworkMoniker = metadataReader.GetTargetFrameworkMoniker() ?? string.Empty
            };

            // Get assembly info
            var assemblyDefinition = _reader.GetAssemblyDefinition();

            _currentAssemblyInfo = _reader.FormatAssemblyInfo(assemblyDefinition);
            _currentAssemblyName = _reader.GetString(assemblyDefinition.Name);
        }

        public AssemblyInfo CallingAssembly { get; }

        public IList<MemberDependency> MemberDependency { get; }

        public void ComputeData()
        {
            // Get type references
            foreach (var handle in _reader.TypeReferences)
            {
                try
                {
                    var entry = _reader.GetTypeReference(handle);

                    var typeReferenceMemberDependency = GetTypeReferenceMemberDependency(entry);
                    if (typeReferenceMemberDependency != null)
                    {
                        MemberDependency.Add(typeReferenceMemberDependency);
                    }
                }
                catch (BadImageFormatException)
                {
                    // Some obfuscators will inject dead types that break decompilers
                    // (for example, types that serve as each others' scopes).
                    //
                    // For portability/compatibility analysis purposes, though,
                    // we can skip such malformed references and just analyze those
                    // that we can successfully decode.
                }
            }

            // Get member references
            foreach (var handle in _reader.MemberReferences)
            {
                try
                {
                    var entry = _reader.GetMemberReference(handle);

                    var memberReferenceMemberDependency = GetMemberReferenceMemberDependency(entry);
                    if (memberReferenceMemberDependency != null)
                    {
                        this.MemberDependency.Add(memberReferenceMemberDependency);
                    }
                }
                catch (BadImageFormatException)
                {
                    // Some obfuscators will inject dead types that break decompilers
                    // (for example, types that serve as each others' scopes).
                    //
                    // For portability/compatibility analysis purposes, though,
                    // we can skip such malformed references and just analyze those
                    // that we can successfully decode.
                }
            }
        }

        private MemberDependency GetTypeReferenceMemberDependency(TypeReference typeReference)
        {
            var provider = new MemberMetadataInfoTypeProvider(_reader);
            var typeInfo = provider.GetFullName(typeReference);

            return CreateMemberDependency(typeInfo);
        }

        private MemberDependency CreateMemberDependency(MemberMetadataInfo type)
        {
            var definedInAssembly = type.DefinedInAssembly.HasValue ? _reader.FormatAssemblyInfo(type.DefinedInAssembly.Value) : _currentAssemblyInfo;

            // Apply heuristic to determine if API is most likely defined in a framework assembly
            if (!_assemblyFilter.IsFrameworkAssembly(definedInAssembly) && !_assemblyFilter.IsKnownThirdPartyAssembly(definedInAssembly))
            {
                return null;
            }

            return new MemberDependency
            {
                CallingAssembly = CallingAssembly,
                MemberDocId = $"T:{type}",
                DefinedInAssemblyIdentity = definedInAssembly
            };
        }

        private MemberDependency GetMemberReferenceMemberDependency(MemberReference memberReference)
        {
            var provider = new MemberMetadataInfoTypeProvider(_reader);
            var memberRefInfo = provider.GetMemberRefInfo(memberReference);

            AssemblyReferenceInformation definedInAssemblyIdentity = null;
            if (memberRefInfo.ParentType.DefinedInAssembly.HasValue)
            {
                definedInAssemblyIdentity = _reader.FormatAssemblyInfo(memberRefInfo.ParentType.DefinedInAssembly.Value);
            }
            // If no assembly is set, then the type is either a primitive type or it's in the current assembly.
            // Mscorlib is special-cased for testing purposes.
            else if (!memberRefInfo.ParentType.IsPrimitiveType || string.Equals(_currentAssemblyName, "mscorlib", StringComparison.OrdinalIgnoreCase))
            {
                definedInAssemblyIdentity = _currentAssemblyInfo;
            }

            // Apply heuristic to determine if API is most likely defined in a framework assembly
            if (!_assemblyFilter.IsFrameworkAssembly(definedInAssemblyIdentity) && !_assemblyFilter.IsKnownThirdPartyAssembly(definedInAssemblyIdentity))
            {
                return null;
            }

            // Add the parent type to the types list (only needed when we want to report memberrefs defined in the current assembly)
            if (memberRefInfo.ParentType.IsTypeDef || (memberRefInfo.ParentType.IsPrimitiveType && _currentAssemblyName.Equals("mscorlib", StringComparison.OrdinalIgnoreCase)))
            {
                var memberDependency = CreateMemberDependency(memberRefInfo.ParentType);

                if (memberDependency != null)
                {
                    MemberDependency.Add(memberDependency);
                }
            }

            return new MemberDependency
            {
                CallingAssembly = CallingAssembly,
                MemberDocId = $"{GetPrefix(memberReference)}:{memberRefInfo}",
                TypeDocId = $"T:{memberRefInfo.ParentType}",
                IsPrimitive = memberRefInfo.ParentType.IsPrimitiveType,
                DefinedInAssemblyIdentity = definedInAssemblyIdentity
            };
        }

        private string GetPrefix(MemberReference memberReference)
        {
            switch (memberReference.GetKind())
            {
                case MemberReferenceKind.Field:
                    return "F";
                case MemberReferenceKind.Method:
                    return "M";
                default:
                    return memberReference.GetKind().ToString();
            }
        }
    }
}
