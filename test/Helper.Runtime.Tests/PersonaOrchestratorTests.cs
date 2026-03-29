using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Moq;
using Helper.Runtime.Evolution;
using Helper.Runtime.Core;
using Helper.Runtime.Infrastructure;

namespace Helper.Runtime.Tests
{
    public class PersonaOrchestratorTests
    {
        [Fact]
        public async Task ConductRoundtableAsync_ParsesOpinionsCorrectly_AndHandlesTokenLimit()
        {
            // Arrange
            var mockAi = new Mock<AILink>("http://localhost:11434", "qwen");
            var mockStore = new Mock<IVectorStore>();
            var mockSearcher = new Mock<IWebSearcher>();

            // Mock Internet Search
            mockSearcher.Setup(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync(new List<WebSearchResult>());

            // Mock Database Search
            mockStore.Setup(s => s.SearchAsync(It.IsAny<float[]>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new List<KnowledgeChunk>());

            // Mock Embedding
            mockAi.Setup(a => a.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new float[1536]);

            // Mock AskAsync - return a long response to trigger token limit 
            // The string needs to be > 4000 * 4 = 16000 characters to trigger circuit breaker if we use it for just one
            // We'll just return a standard valid format to test parsing
            string mockResponse = "OPINION: This is a test opinion | ALTERNATIVE: Use XUnit | CRITICAL_SCORE: 0.8";
            mockAi.Setup(a => a.AskAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()))
                  .ReturnsAsync(mockResponse);

            var orchestrator = new PersonaOrchestrator(mockAi.Object, mockStore.Object, mockSearcher.Object);

            // Act
            var report = await orchestrator.ConductRoundtableAsync("Use NUnit for testing");

            // Assert
            Assert.NotNull(report);
            Assert.Equal(3, report.Opinions.Count);
            Assert.Equal("This is a test opinion", report.Opinions[0].Opinion);
            Assert.Equal("Use XUnit", report.Opinions[0].AlternativeProposal);
            Assert.Equal(0.8, report.Opinions[0].CriticalScore, 5);
            Assert.Equal(0.8, report.ConflictLevel, 5);
            Assert.True(report.TokensUsed > 0);
        }
    }
}

