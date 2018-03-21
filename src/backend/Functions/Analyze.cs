// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Fx.Portability;
using Microsoft.Fx.Portability.ObjectModel;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using WorkflowManagement;

namespace Functions
{
    public static class Analyze
    {
        [FunctionName("analyze")]
        public static async Task<HttpResponseMessage> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequestMessage req,
            [Queue("apiportworkflowqueue")]ICollector<WorkflowQueueMessage> workflowMessageQueue,
            IBinder binder,
            ILogger log)
        {
            var analyzeRequest = await DeserializeRequest(req.Content);
            if (analyzeRequest == null)
            {
                log.LogError("invalid request");
                return req.CreateResponse(HttpStatusCode.BadRequest);
            }

            var submissionId = Guid.NewGuid().ToString();
            log.LogInformation("Created submission id {SubmissionId}", submissionId);

            log.LogInformation("Saving request to blob storage for {SubmissionId}", submissionId);

            try
            {
                // TODO: replace container name with value from configuration service provider
                var containerName = "apiport-analyze-requests";
                var blobAttribute = new BlobAttribute($"{containerName}/{submissionId}", FileAccess.Write)
                {
                    // TODO: uncomment and retrieve value from configuration service provider
                    // Connection = ""
                };

                using (var stream = await binder.BindAsync<Stream>(blobAttribute))
                using (var binaryWriter = new BinaryWriter(stream))
                {
                    var compressedBytes = DataExtensions.SerializeAndCompress(analyzeRequest);
                    binaryWriter.Write(compressedBytes);
                }
            }
            catch (Exception exception)
            {
                log.LogError("Error uploading request for submission {submission} due to {error}", submissionId, exception);
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

        public static async Task<AnalyzeRequest> DeserializeRequest(HttpContent content)
        {
            try
            {
                var stream = await content.ReadAsStreamAsync();
                return DataExtensions.DecompressToObject<AnalyzeRequest>(stream);
            }
            catch
            {
                return null;
            }
        }
    }
}
