using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Moq;
using Helper.Runtime.Infrastructure;
using Helper.Runtime.Core;

namespace Helper.Runtime.Tests
{
    public class ProcessGuardTests
    {
        [Fact]
        public void EnsureSafeCommand_WithForbiddenCommand_ThrowsUnauthorizedAccessException()
        {
            // Arrange
            var mockAi = new Mock<AILink>("http://localhost:11434", "qwen");
            var guard = new ProcessGuard(mockAi.Object);

            // Act & Assert
            var ex = Assert.Throws<UnauthorizedAccessException>(() => guard.EnsureSafeCommand("rm -rf /"));
            Assert.Contains("BLOCK: Command 'rm' is restricted for safety", ex.Message);
        }

        [Fact]
        public void EnsureSafeCommand_WithChainedCommand_ThrowsUnauthorizedAccessException()
        {
            // Arrange
            var mockAi = new Mock<AILink>("http://localhost:11434", "qwen");
            var guard = new ProcessGuard(mockAi.Object);

            // Act & Assert
            var ex = Assert.Throws<UnauthorizedAccessException>(() => guard.EnsureSafeCommand("echo hello && del file.txt"));
            Assert.Contains("BLOCK: Command chaining or redirection detected", ex.Message);
        }

        [Fact]
        public void EnsureSafeCommand_WithProtectedPath_ThrowsUnauthorizedAccessException()
        {
            // Arrange
            var mockAi = new Mock<AILink>("http://localhost:11434", "qwen");
            var guard = new ProcessGuard(mockAi.Object);

            // Act & Assert
            var ex = Assert.Throws<UnauthorizedAccessException>(() => guard.EnsureSafeCommand("cat src/Program.cs"));
            Assert.Contains("BLOCK: Access to protected system path", ex.Message);
        }

        [Fact]
        public void EnsureSafeCommand_WithSafeCommand_DoesNotThrow()
        {
            // Arrange
            var mockAi = new Mock<AILink>("http://localhost:11434", "qwen");
            var guard = new ProcessGuard(mockAi.Object);

            // Act
            var exception = Record.Exception(() => guard.EnsureSafeCommand("dotnet build PROJECTS/myapp"));

            // Assert
            Assert.Null(exception);
        }

        [Fact]
        public void EnsureSafeCommand_AllowsQuotedPipeInsideDotnetTestFilter()
        {
            var mockAi = new Mock<AILink>("http://localhost:11434", "qwen");
            var guard = new ProcessGuard(mockAi.Object);

            var exception = Record.Exception(() => guard.EnsureSafeCommand(
                "dotnet test test\\Helper.Runtime.Tests\\Helper.Runtime.Tests.csproj --filter \"FullyQualifiedName~ConversationRuntimeTests|FullyQualifiedName~SimpleResearcherTests|FullyQualifiedName~OperatorResponseComposerTests\""));

            Assert.Null(exception);
        }

        [Fact]
        public void EnsureSafeCommand_StillBlocksRealPipelineOutsideFilter()
        {
            var mockAi = new Mock<AILink>("http://localhost:11434", "qwen");
            var guard = new ProcessGuard(mockAi.Object);

            var ex = Assert.Throws<UnauthorizedAccessException>(() => guard.EnsureSafeCommand(
                "dotnet test test\\Helper.Runtime.Tests\\Helper.Runtime.Tests.csproj --filter \"FullyQualifiedName~ConversationRuntimeTests\" | more"));

            Assert.Contains("Command chaining or redirection detected", ex.Message);
        }
    }
}

