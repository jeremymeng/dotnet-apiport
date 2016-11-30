﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.Fx.Portability.ObjectModel;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Versioning;

namespace Microsoft.Fx.Portability.Analysis
{
    public class AnalysisEngine : IAnalysisEngine
    {
        internal const string FullFrameworkIdentifier = ".NET Framework";

        private readonly IApiCatalogLookup _catalog;
        private readonly IApiRecommendations _recommendations;

        public DateTimeOffset CatalogLastUpdated { get { return _catalog.LastModified; } }

        public AnalysisEngine(IApiCatalogLookup catalog, IApiRecommendations recommendations)
        {
            _catalog = catalog;
            _recommendations = recommendations;
        }

        public IEnumerable<AssemblyInfo> FindBreakingChangeSkippedAssemblies(IEnumerable<FrameworkName> targets, IEnumerable<AssemblyInfo> userAssemblies, IEnumerable<IgnoreAssemblyInfo> assembliesToIgnore)
        {
            foreach (AssemblyInfo a in userAssemblies)
            {
                if (assembliesToIgnore.Any(i =>
                    i.AssemblyIdentity.Equals(a.AssemblyIdentity, StringComparison.OrdinalIgnoreCase) &&        // The assembly must be in assembliesToIgnore and 
                    (i.IgnoreForAllTargets ||                                                                   // either be 'IgnoreForAllTargets' or
                    targets.All(f => i.TargetsIgnored.Contains(f.FullName, StringComparer.OrdinalIgnoreCase)))  // all targeted Frameworks are in the ignore list for the assembly
                ))
                {
                    yield return a;
                }
            }
        }

        public IEnumerable<BreakingChangeDependency> FindBreakingChanges(IEnumerable<FrameworkName> targets,
                                                                         IDictionary<MemberInfo, ICollection<AssemblyInfo>> dependencies,
                                                                         IEnumerable<AssemblyInfo> assembliesToIgnore,
                                                                         IEnumerable<string> breakingChangesToSuppress,
                                                                         ICollection<string> submittedAssemblies,
                                                                         bool showRetargettingIssues = false)
        {
            // Only proceed to find breaking changes for full .NET Framework (that's where they are applicable)
            var fullFrameworkVersions = targets
                .Where(t => string.Equals(t.Identifier, FullFrameworkIdentifier, StringComparison.OrdinalIgnoreCase))
                .Select(t => t.Version)
                .ToList();

            if (!fullFrameworkVersions.Any())
            {
                yield break;
            }

            foreach (var kvp in dependencies)
            {
                if (MemberIsInFramework(kvp.Key, submittedAssemblies))
                {
                    var breakingChanges = _recommendations.GetBreakingChanges(kvp.Key.MemberDocId).Distinct();
                    foreach (var b in breakingChanges)
                    {
                        if (!showRetargettingIssues && b.IsRetargeting)
                        {
                            continue;
                        }

                        if (BreakingChangeIsInVersionRange(fullFrameworkVersions, b) && !(breakingChangesToSuppress?.Contains(b.Id) ?? false))
                        {
                            foreach (var a in kvp.Value)
                            {
                                // If the assembly referencing the broken API is on the ignore list, continue
                                if (assembliesToIgnore != null && assembliesToIgnore.Contains(a))
                                {
                                    continue;
                                }

                                yield return new BreakingChangeDependency
                                {
                                    Break = b,
                                    DependantAssembly = a,
                                    Member = kvp.Key
                                };
                            }
                        }
                    }
                }
            }
        }

        private static bool BreakingChangeIsInVersionRange(IEnumerable<Version> targetVersions, BreakingChange breakingChange)
        {
            foreach (var targetVersion in targetVersions)
            {
                // Include breaking changes that were broken before a target version and fixed after it,
                // and also breaking changes that were introduced in a targeted version, _even if they were fixed in that same version_
                //
                // Some breaking changes have VersionBroken==VersionFixed if the break was corrected in GDR-level servicing. We want to report those to
                // users who are targeting that version so that they understand the importance of updating their NetFX via WU (or whatever
                // enterprise-specific patch rollout system they have).
                if (targetVersion == breakingChange.VersionBroken ||
                    (targetVersion > breakingChange.VersionBroken && (breakingChange.VersionFixed == null || targetVersion < breakingChange.VersionFixed)))
                {
                    return true;
                }
            }

            return false;
        }

        public IList<MemberInfo> FindMembersNotInTargets(IEnumerable<FrameworkName> targets, ICollection<string> submittedAssemblies, IDictionary<MemberInfo, ICollection<AssemblyInfo>> dependencies)
        {
            //Trace.TraceInformation("Computing members not in target");
            Stopwatch sw = new Stopwatch();
            sw.Start();

            if (dependencies == null || dependencies.Keys == null || targets == null)
            {
                return new List<MemberInfo>();
            }

            // Find the missing members by:
            //  -- Find the members that are defined in framework assemblies AND which are framework members.
            //  -- For each member, identity which is the status for that docId for each of the targets.
            //  -- Keep only the members that are not supported on at least one of the targets.

            var missingMembers = dependencies.Keys
                .Where(m => MemberIsInFramework(m, submittedAssemblies) || MemberIsInKnownThirdPartyLibrary(m, submittedAssemblies))
                .AsParallel()
                .Select(memberInfo => ProcessMemberInfo(_catalog, targets, memberInfo))
                .Where(memberInfo => !memberInfo.IsSupportedAcrossTargets)
                .ToList();

            sw.Stop();
            //Trace.TraceInformation("Computing members not in target took '{0}'", sw.Elapsed);

            return missingMembers;
        }

        private bool MemberIsInFramework(MemberInfo dep, ICollection<string> submittedAssemblies)
        {
            if (submittedAssemblies.Contains(dep.DefinedInAssemblyIdentity))
            {
                return false;
            }

            // A null 'DefinedInAssemblyIdentity is indicative of a primitive
            // type, which should always be considered within the Framework.
            // For non-primitive types, consult the catalog to determine whether the
            // assembly and API in question are part of the Framework.
            return (dep.DefinedInAssemblyIdentity == null) || (_catalog.IsFrameworkAssembly(GetAssemblyIdentityWithoutCultureAndVersion(dep.DefinedInAssemblyIdentity)) && _catalog.IsFrameworkMember(dep.MemberDocId));
        }

        private bool MemberIsInKnownThirdPartyLibrary(MemberInfo m, ICollection<string> submittedAssemblies)
        {
            return true;
        }

        /// <summary>
        /// Identitifies the status of an API for all of the targets
        /// </summary>
        private MemberInfo ProcessMemberInfo(IApiCatalogLookup catalog, IEnumerable<FrameworkName> targets, MemberInfo member)
        {
            List<Version> targetStatus;

            member.IsSupportedAcrossTargets = IsSupportedAcrossTargets(catalog, member.MemberDocId, targets, out targetStatus);
            member.TargetStatus = targetStatus;
            member.RecommendedChanges = _recommendations.GetRecommendedChanges(member.MemberDocId);
            member.SourceCompatibleChange = _recommendations.GetSourceCompatibleChanges(member.MemberDocId);

            return member;
        }

        /// <summary>
        /// Computes a list of strings that describe the status of the api on all the targets (not supported or version when it was introduced)
        /// </summary>
        private bool IsSupportedAcrossTargets(IApiCatalogLookup catalog, string memberDocId, IEnumerable<FrameworkName> targets, out List<Version> targetStatus)
        {
            targetStatus = new List<Version>();
            bool isSupported = true;
            foreach (var target in targets)
            {
                // For each target we should get the status of the api:
                //   - 'null' if not supported
                //   - Version introduced in
                Version status;

                if (!IsSupportedOnTarget(catalog, memberDocId, target, out status))
                {
                    isSupported = false;
                }

                targetStatus.Add(status);
            }

            return isSupported;
        }

        public IEnumerable<string> FindUnreferencedAssemblies(IEnumerable<string> unreferencedAssemblies, IEnumerable<AssemblyInfo> specifiedUserAssemblies)
        {
            if (unreferencedAssemblies == null)
                yield break;

            // Find the unreferenced assemblies that are not framework assemblies.
            var userUnreferencedAssemblies = unreferencedAssemblies.AsParallel().
                Where(asm => asm != null && !_catalog.IsFrameworkAssembly(GetAssemblyIdentityWithoutCultureAndVersion(asm)))
                .ToList();

            // For each of the user unreferenced assemblies figure out if it was actually specified as an input
            foreach (var userAsm in userUnreferencedAssemblies)
            {
                // if somehow a null made it through...
                if (userAsm == null)
                    continue;

                // If the unresolved assembly was not actually specified, we need to tell the user that.
                if (specifiedUserAssemblies != null && specifiedUserAssemblies.Any(ua => ua != null && StringComparer.OrdinalIgnoreCase.Equals(ua.AssemblyIdentity, userAsm)))
                    continue;

                yield return userAsm;
            }
        }

        private static string GetAssemblyIdentityWithoutCultureAndVersion(string assemblyIdentity)
        {
            return new System.Reflection.AssemblyName(assemblyIdentity)
            {
                Version = null,
#if NET45
                CultureInfo = null
#else
                CultureName = null
#endif
            }.ToString();
        }

        private static bool IsSupportedOnTarget(IApiCatalogLookup catalog, string memberDocId, FrameworkName target, out Version status)
        {
            status = null;

            // If the member is part of the API surface, no need to report it.
            if (catalog.IsMemberInTarget(memberDocId, target, out status))
            {
                return true;
            }

            var sourceEquivalent = catalog.GetSourceCompatibilityEquivalent(memberDocId);

            if (!string.IsNullOrEmpty(sourceEquivalent) && catalog.IsMemberInTarget(sourceEquivalent, target, out status))
            {
                return true;
            }

            return false;
        }

        private class MemberInfoBreakingChangeComparer : IEqualityComparer<Tuple<MemberInfo, BreakingChange>>
        {
            public static IEqualityComparer<Tuple<MemberInfo, BreakingChange>> Instance = new MemberInfoBreakingChangeComparer();

            public bool Equals(Tuple<MemberInfo, BreakingChange> x, Tuple<MemberInfo, BreakingChange> y)
            {
                return x.Item1.Equals(y.Item1);
            }

            public int GetHashCode(Tuple<MemberInfo, BreakingChange> obj)
            {
                return obj.Item1.GetHashCode();
            }
        }
    }
}
