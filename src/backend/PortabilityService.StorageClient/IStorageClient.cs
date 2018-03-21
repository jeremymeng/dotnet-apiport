// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using System.Threading.Tasks;

namespace PortabilityService.StorageClient
{
    /// <summary>
    /// Provides an interface to interact with storage options
    /// </summary>
    public interface IStorageClient
    {
        /// <summary>
        /// Initializes storage client.
        /// </summary>
        /// <param name="createContainerIfNotExists">specifies whether to create container if it doesn't exist</param>
        /// <returns></returns>
        Task InitializeAsync(bool createContainerIfNotExists);

        /// <summary>
        /// Uploads a stream to the storage
        /// </summary>
        /// <param name="id"></param>
        /// <param name="content">conent to upload</param>
        /// <returns>a unique id to retrieve the content later</returns>
        Task<string> UploadAsync(string id, Stream content);

        /// <summary>
        /// Downloads a stream from the storage
        /// </summary>
        /// <param name="downloadId">the key to identify a blob to download</param>
        /// <param name="target">the target stream to which the blob is downloaded</param>
        /// <returns>the downloaded stream</returns>
        Task DownloadAsync(string downloadId, Stream target);

        /// <summary>
        /// Removes all stored items
        /// </summary>
        /// <returns></returns>
        Task RemoveAllAsync();
    }
}
