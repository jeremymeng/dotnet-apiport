// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using WorkflowManagement;
using Xunit;

namespace Functions.Tests
{
    public class AnalyzeTests
    {
        [Fact]
        public static async Task ReturnsBadRequestForMalformedContent()
        {
            var request = PostFromConsoleApiPort;
            request.Content = new StringContent("{ \"json\": \"json\" }");

            var mockBinder = Substitute.For<IBinder>();
            var response = await Analyze.Run(request, Substitute.For<ICollector<WorkflowQueueMessage>>(), mockBinder, NullLogger.Instance);

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
            Stream stream = new MemoryStream();
            var mockBinder = Substitute.For<IBinder>();
            mockBinder.BindAsync<Stream>(Arg.Any<BlobAttribute>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(stream));
            var response = await Analyze.Run(request, workflowQueue, mockBinder, NullLogger.Instance);
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
            const string BlobFileName = "bloboutput.gz";
            Stream stream = new FileStream(BlobFileName, FileMode.Create);
            var mockBinder = Substitute.For<IBinder>();
            mockBinder.BindAsync<Stream>(Arg.Any<BlobAttribute>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(stream));
            var response = await Analyze.Run(gzippedRequest, workflowQueue, mockBinder, NullLogger.Instance);
            using (stream = new FileStream(BlobFileName, FileMode.Open))
            {
                Assert.Equal(expectedStream.Length, stream.Length);
                var bytes = new byte[stream.Length];
                await stream.ReadAsync(bytes, 0, (int)stream.Length);
                Assert.Equal(expectedStream.ToArray(), bytes);
            }
            File.Delete(BlobFileName);
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
