// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Fx.Portability;
using Microsoft.Fx.Portability.ObjectModel;
using PortabilityService.StorageClient;
using WorkflowManagement;

namespace Functions
{
    public static class Analyze
    {
        [FunctionName("analyze")]
        public static async Task<HttpResponseMessage> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequestMessage req,
            [Queue("apiportworkflowqueue")]ICollector<WorkflowQueueMessage> workflowMessageQueue, 
            ILogger log)
        {
            var copy = new MemoryStream();
            AnalyzeRequest analyzeRequest = null;
            try
            {
                var stream = await req.Content.ReadAsStreamAsync();
                stream.CopyTo(copy);
                stream.Seek(0, SeekOrigin.Begin);
                analyzeRequest = DataExtensions.DecompressToObject<AnalyzeRequest>(stream);
            }
            catch
            {
                log.LogError("invalid request");
                return req.CreateResponse(HttpStatusCode.BadRequest);
            }

            var submissionId = Guid.NewGuid().ToString();
            log.LogInformation("Created submission id {SubmissionId}", submissionId);

            // TODO: replace with values from configuration provider
            var connectionString = "UseDevelopmentStorage=true";
            var containerName = "apiport-analyze-requests";
            // TODO: use DI to get a instance of IStorageClient
            var client = new AzureBlobStorageClient(connectionString, containerName);
            await client.InitializeAsync(createContainerIfNotExists: false);

            try
            {
                copy.Seek(0, SeekOrigin.Begin);
                await client.UploadAsync(submissionId, copy);
            }
            catch (Exception ex)
            {
                log.LogError("Error occurs when saving analyze request to storage for submission {submissionId}: {exception}", submissionId, ex);
                return req.CreateResponse(HttpStatusCode.InternalServerError);
            }

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Content = new StringContent(submissionId);

            var workflowMgr = WorkflowManager.Initialize();
            var msg = WorkflowManager.GetFirstStage(submissionId);
            workflowMessageQueue.Add(msg);
            log.LogInformation("Queuing new message {SubmissionId}, stage {Stage}", msg.SubmissionId, msg.Stage);

            return response;
        }
    }
}
