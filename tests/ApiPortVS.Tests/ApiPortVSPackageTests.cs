﻿using ApiPortVS.Analyze;
using Microsoft.Fx.Portability.Reporting;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;
using NSubstitute.Core;
using System.IO;

namespace ApiPortVS.Tests
{
    [TestClass]
    public class ApiPortVSPackageTests
    {
        private class TestableApiPortVSPackage : ProjectAnalyzer
        {
            public TestableApiPortVSPackage()
                : base(null, null, null, null, null, null, CreateFileSystem(), null, null, null, null, null, null, null)
            { }

            private static IFileSystem CreateFileSystem()
            {
                var fileSystem = Substitute.For<IFileSystem>();

                fileSystem.GetFileExtension(Arg.Any<string>()).Returns(GetFileExtension);

                return fileSystem;
            }

            private static string GetFileExtension(CallInfo info)
            {
                var path = info.Arg<string>();

                return Path.GetExtension(path);
            }

            public new bool FileHasAnalyzableExtension(string fileName)
            {
                return base.FileHasAnalyzableExtension(fileName);
            }
        }

        [TestMethod]
        public void FileHasAnalyzableExtension_FileIsExe_ReturnsTrue()
        {
            var filename = "analyzable.exe";

            var package = new TestableApiPortVSPackage();

            var result = package.FileHasAnalyzableExtension(filename);

            Assert.IsTrue(result);
        }

        [TestMethod]
        public void FileHasAnalyzableExtension_FileIsDll_ReturnsTrue()
        {
            var filename = "analyzable.dll";

            var package = new TestableApiPortVSPackage();

            var result = package.FileHasAnalyzableExtension(filename);

            Assert.IsTrue(result);
        }

        [TestMethod]
        public void FileHasAnalyzableExtension_FilenameContainsVshost_ReturnsFalse()
        {
            var filename = "analyzable.vshost.exe";

            var package = new TestableApiPortVSPackage();

            var result = package.FileHasAnalyzableExtension(filename);

            Assert.IsFalse(result);
        }
    }
}