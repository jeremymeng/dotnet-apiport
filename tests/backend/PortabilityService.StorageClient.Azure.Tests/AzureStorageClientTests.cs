// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace PortabilityService.StorageClient.Azure.Tests
{
    public class AzureStorageClientTests
    {
        [Fact]
        public static async Task ThrowsExceptionForInvalidConnectionString()
        {
            var exception = await Assert.ThrowsAsync<Exception>(async () =>
            {
                // Using local settings for unit tests
                var connectionString = "InvalidConnectionString=abc";
                var containerName = "non-existent";
                var client = new AzureBlobStorageClient(connectionString, containerName);
                await client.InitializeAsync(createContainerIfNotExists: false);
            });

            Assert.Equal($"Invalid cloud storage connection string 'InvalidConnectionString=abc'", exception.Message);
        }
        [Fact]
        public static async Task ThrowsExceptionForUninitializedClient()
        {
            var exception = await Assert.ThrowsAsync<Exception>(async () =>
            {
                // Using local settings for unit tests
                var connectionString = "UseDevelopmentStorage=true";
                var containerName = "non-existent";
                var client = new AzureBlobStorageClient(connectionString, containerName);
                await client.RemoveAllAsync();
            });

            Assert.Equal($"Please ensure initialization by calling 'InitializeAsync()' method.", exception.Message);
        }

        [Fact]
        public static async Task ThrowsExceptionWhenContainerDoesNotExist()
        {
            var exception = await Assert.ThrowsAsync<Exception>(async () =>
            {
                // Using local settings for unit tests
                var connectionString = "UseDevelopmentStorage=true";
                var containerName = "non-existent";
                var client = new AzureBlobStorageClient(connectionString, containerName);
                await client.InitializeAsync(createContainerIfNotExists: false);
                await client.RemoveAllAsync();
            });

            Assert.Equal($"The container 'non-existent' does not exist", exception.Message);
        }

        [Fact]
        public static async Task DownloadsTheSameContentAsUploaded()
        {
            // Using local settings for unit tests
            var connectionString = "UseDevelopmentStorage=true";
            var containerName = "test-container";
            var client = new AzureBlobStorageClient(connectionString, containerName);
            await client.InitializeAsync(createContainerIfNotExists: true);

            var dataToSave = "Hello World!";
            var submissionId = Guid.NewGuid().ToString();
            using (var stream = new MemoryStream())
            using (var writer = new StreamWriter(stream, Encoding.UTF8))
            {
                await writer.WriteAsync(dataToSave);
                await writer.FlushAsync();
                stream.Seek(0, SeekOrigin.Begin);
                var id = await client.UploadAsync(submissionId, stream);
                Assert.Equal(submissionId, id);
            }

            using (var stream = new MemoryStream())
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                await client.DownloadAsync(submissionId, stream);
                stream.Seek(0, SeekOrigin.Begin);
                var downloaded = await reader.ReadToEndAsync();
                Assert.Equal(dataToSave, downloaded);
            }

            await client.RemoveAllAsync();
        }
    }
}
