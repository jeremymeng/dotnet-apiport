// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using Xunit;
using WorkflowManagement;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Microsoft.Azure.WebJobs;
using System.IO;

namespace Functions.Tests
{
    public class AnalyzeTests
    {
        [Fact]
        public static async Task ReturnsBadRequestForMalformedContent()
        {
            var request = PostFromConsoleApiPort;
            request.Content = new StringContent("{ \"json\": \"json\" }");

            var response = await Analyze.Run(request, Substitute.For<ICollector<WorkflowQueueMessage>>(), NullLogger.Instance);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public static async Task ReturnsGuidForCompressedAnalyzeRequest()
        {
            var gzippedAnalyzeRequestStream = typeof(AnalyzeTests).Assembly
                .GetManifestResourceStream("Functions.Tests.Resources.apiport.exe.AnalyzeRequest.json.gz");

            var request = GetGzipppedPostRequest(gzippedAnalyzeRequestStream);

            WorkflowManager.Initialize();
            var workflowQueue = Substitute.For<ICollector<WorkflowQueueMessage>>();
            var response = await Analyze.Run(request, workflowQueue, NullLogger.Instance);
            var body = await response.Content.ReadAsStringAsync();

            Assert.True(Guid.TryParse(body, out var submissionId));

            workflowQueue.Received().Add(Arg.Is<WorkflowQueueMessage>(x => x.SubmissionId == submissionId.ToString() && x.Stage == WorkflowStage.Analyze));
        }

        [Fact]
        public static async Task SavesAnalyzeRequest()
        {
            var gzippedAnalyzeRequestStream = typeof(AnalyzeTests).Assembly
                .GetManifestResourceStream("Functions.Tests.Resources.apiport.exe.AnalyzeRequest.json.gz");

            var expectedStream = new MemoryStream();
            gzippedAnalyzeRequestStream.CopyTo(expectedStream);
            gzippedAnalyzeRequestStream.Seek(0, SeekOrigin.Begin);

            var gzippedRequest = GetGzipppedPostRequest(gzippedAnalyzeRequestStream);

            WorkflowManager.Initialize();
            var workflowQueue = Substitute.For<ICollector<WorkflowQueueMessage>>();
            var response = await Analyze.Run(gzippedRequest, workflowQueue, NullLogger.Instance);
        }

        private static HttpRequestMessage PostFromConsoleApiPort
        {
            get
            {
                var request = new HttpRequestMessage(HttpMethod.Post, "");
                request.Headers.Add("Accept", "application/json");
                request.Headers.Add("Accept-Encoding", "gzip, deflate");
                request.Headers.Add("Client-Type", "ApiPort_Console");
                request.Headers.Add("Client-Version", "2.4.0.2");
                request.Headers.Add("Expect", "100-continue");

                return request;
            }
        }

        private static HttpRequestMessage GetGzipppedPostRequest(Stream content)
        {
            var request = PostFromConsoleApiPort;
            request.SetConfiguration(new HttpConfiguration());
            request.Content = new StreamContent(content);
            request.Content.Headers.Add("Content-Encoding", "gzip");
            request.Content.Headers.Add("Content-Type", "application/json");
            return request;
        }
    }
}
