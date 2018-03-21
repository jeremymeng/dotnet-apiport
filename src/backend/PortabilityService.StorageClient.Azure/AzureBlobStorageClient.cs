// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using System;
using System.IO;
using System.Threading.Tasks;

namespace PortabilityService.StorageClient
{
    public class AzureBlobStorageClient : IStorageClient
    {
        private CloudStorageAccount _storageAccount;
        private CloudBlobClient _blobClient;
        private CloudBlobContainer _blobContainer;
        private readonly string _connectionString;
        private readonly string _containerName;

        public AzureBlobStorageClient(string connectionString, string containerName)
        {
            _connectionString = connectionString;
            _containerName = containerName;
        }

        public async Task InitializeAsync(bool createContainerIfNotExists)
        {
            if (!CloudStorageAccount.TryParse(_connectionString, out _storageAccount))
            {
                throw new Exception($"Invalid cloud storage connection string '{_connectionString}'");
            }

            _blobClient = _storageAccount.CreateCloudBlobClient();
            if (_blobClient == null)
            {
                throw new Exception("Error creating cloud blob client");
            }

            _blobContainer = _blobClient.GetContainerReference(_containerName);

            if (createContainerIfNotExists)
            {
                await _blobContainer.CreateIfNotExistsAsync();
            }
            else if (!await _blobContainer.ExistsAsync())
            {
                throw new Exception($"The container '{_containerName}' does not exist");
            }
        }

        public async Task DownloadAsync(string downloadId, Stream target)
        {
            EnsureInitialization();

            var blockBlob = _blobContainer.GetBlockBlobReference(downloadId);
            await (blockBlob.DownloadToStreamAsync(target));
        }

        public async Task<string> UploadAsync(string id, Stream content)
        {
            EnsureInitialization();

            var blockBlob = _blobContainer.GetBlockBlobReference(id);
            await blockBlob.UploadFromStreamAsync(content);
            return id;
        }

        public async Task RemoveAllAsync()
        {
            EnsureInitialization();

            await _blobContainer.DeleteAsync();
        }

        private void EnsureInitialization()
        {
            if (_blobContainer == null)
            {
                throw new Exception($"Please ensure initialization by calling '{nameof(InitializeAsync)}()' method.");
            }
        }
    }
}
