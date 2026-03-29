#nullable enable
// Minimal API glue in these partials triggers noisy false positives from Roslyn nullability analysis.
// Keep suppressions local to endpoint registration instead of project-wide.
#pragma warning disable CS8600, CS8601, CS8602, CS8603, CS8604, CS8619, CS8622, CS8632

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Helper.Api.Backend.Configuration;
using Helper.Api.Conversation;
using Helper.Runtime.Core;
using Helper.Runtime.Generation;
using Helper.Runtime.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Helper.Api.Hosting;

public static partial class EndpointRegistrationExtensions
{
	private static readonly string[] DefaultLocalBootstrapScopes = new string[8]
	{
		"chat:read",
		"chat:write",
		"feedback:write",
		"metrics:read",
		"evolution:control",
		"tools:execute",
		"build:run",
		"fs:write"
	};

	private static readonly HashSet<string> AllowedLocalBootstrapScopes = new HashSet<string>(DefaultLocalBootstrapScopes, StringComparer.OrdinalIgnoreCase);
	private static readonly IReadOnlyDictionary<string, string[]> SessionSurfaceScopes = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
	{
		["conversation"] = new string[3]
		{
			"chat:read",
			"chat:write",
			"feedback:write"
		},
		["runtime_console"] = new string[1]
		{
			"metrics:read"
		},
		["builder"] = new string[5]
		{
			"chat:read",
			"chat:write",
			"tools:execute",
			"build:run",
			"fs:write"
		},
		["evolution"] = new string[2]
		{
			"evolution:control",
			"metrics:read"
		}
	};

	public static IEndpointRouteBuilder MapHelperEndpoints(this IEndpointRouteBuilder endpoints, ApiRuntimeConfig runtimeConfig)
	{
		MapSystemAndGovernanceEndpoints(endpoints, runtimeConfig);
		MapSmokeEndpoints(endpoints);
		MapEvolutionAndGenerationEndpoints(endpoints, runtimeConfig);
		MapConversationEndpoints(endpoints);
		return endpoints;
	}

	private static bool IsFactualPrompt(string prompt)
	{
		string[] source = new string[9] { "what", "when", "where", "who", "когда", "где", "кто", "сколько", "факт" };
		return source.Any((string token) => prompt.Contains(token, StringComparison.OrdinalIgnoreCase));
	}

	private static bool IsResearchClarificationFallback(ChatResponseDto response)
	{
		return string.Equals(response.Intent, "research", StringComparison.OrdinalIgnoreCase) &&
			string.Equals(response.GroundingStatus, "clarification_required", StringComparison.OrdinalIgnoreCase) &&
			(response.Sources?.Count ?? 0) == 0;
	}

	private static string TruncateForPreview(string? value, int maxLength)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return string.Empty;
		}
		string text = value.Trim();
		if (text.Length <= maxLength)
		{
			return text;
		}
		return text.Substring(0, Math.Max(1, maxLength)).TrimEnd() + "...";
	}

	private static object BuildDoneStreamEvent(ChatResponseDto response)
	{
		return new
		{
			type = "done",
			conversationId = response.ConversationId,
			timestamp = response.Timestamp,
			fullResponse = response.Response,
			turnId = response.TurnId,
			confidence = response.Confidence,
			sources = response.Sources,
			toolCalls = response.ToolCalls,
			requiresConfirmation = response.RequiresConfirmation,
			nextStep = response.NextStep,
			groundingStatus = response.GroundingStatus,
			citationCoverage = response.CitationCoverage,
			verifiedClaims = response.VerifiedClaims,
			totalClaims = response.TotalClaims,
			claimGroundings = response.ClaimGroundings,
			uncertaintyFlags = response.UncertaintyFlags,
			branchId = response.BranchId,
			availableBranches = response.AvailableBranches,
			executionMode = response.ExecutionMode,
			budgetExceeded = response.BudgetExceeded,
			estimatedTokensGenerated = response.EstimatedTokensGenerated,
			inputMode = response.InputMode,
			intent = response.Intent,
			intentConfidence = response.IntentConfidence,
			resumeCursor = response.Response?.Length ?? 0,
			executionTrace = response.ExecutionTrace,
			lifecycleTrace = response.LifecycleTrace,
			reasoningMetrics = response.ReasoningMetrics,
            searchTrace = response.SearchTrace
		};
	}

	private static void PrepareSseResponse(HttpContext httpContext)
	{
		httpContext.Response.Headers.CacheControl = "no-cache";
		httpContext.Response.Headers.Connection = "keep-alive";
		httpContext.Response.ContentType = "text/event-stream";
	}

	private static async Task WriteSseEventAsync(HttpContext httpContext, SemaphoreSlim writeLock, string payload, CancellationToken ct)
	{
		await writeLock.WaitAsync(ct);
		try
		{
			await httpContext.Response.WriteAsync("data: " + payload + "\n\n", ct);
			await httpContext.Response.Body.FlushAsync(ct);
		}
		finally
		{
			writeLock.Release();
		}
	}

	private static TimeSpan ReadStreamHeartbeatInterval(IBackendOptionsCatalog? options = null)
	{
		int? configuredSeconds = options?.Transport.StreamHeartbeatSeconds;
		if (configuredSeconds.HasValue)
		{
			return TimeSpan.FromSeconds(Math.Clamp(configuredSeconds.Value, 2, 60));
		}

		string environmentVariable = Environment.GetEnvironmentVariable("HELPER_STREAM_HEARTBEAT_MS");
		if (!int.TryParse(environmentVariable, out var result))
		{
			return TimeSpan.FromSeconds(3.0);
		}
		return TimeSpan.FromMilliseconds(Math.Clamp(result, 500, 15000));
	}

	private static CancellationTokenSource CreateStreamDeadlineScope(IBackendOptionsCatalog? options, CancellationToken ct)
	{
		var deadlineSeconds = options?.Transport.StreamDeadlineSeconds ?? 90;
		var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
		linked.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(deadlineSeconds, 10, 1800)));
		return linked;
	}

	private static async Task RunHeartbeatLoopAsync(HttpContext httpContext, SemaphoreSlim writeLock, TimeSpan interval, CancellationToken ct)
	{
		if (interval <= TimeSpan.Zero)
		{
			return;
		}
		while (!ct.IsCancellationRequested)
		{
			try
			{
				await Task.Delay(interval, ct);
				string heartbeat = JsonSerializer.Serialize(new
				{
					type = "heartbeat",
					timestamp = DateTimeOffset.UtcNow
				});
				await WriteSseEventAsync(httpContext, writeLock, heartbeat, ct);
			}
			catch (OperationCanceledException) when (ct.IsCancellationRequested)
			{
				break;
			}
			catch (IOException)
			{
				break;
			}
		}
	}

	private static async Task AwaitBackgroundTaskAsync(Task task)
	{
		try
		{
			await task;
		}
		catch (OperationCanceledException)
		{
		}
		catch (IOException)
		{
		}
	}

	private static ApiPrincipal BuildLocalBootstrapPrincipal()
	{
		HashSet<string> scopes = ResolveLocalBootstrapScopes();
		return new ApiPrincipal("local-ui", "session_user", scopes, "session");
	}

	private static bool IsLocalBootstrapAllowed()
	{
		return BackendOptionsCatalog.ResolveLocalBootstrapAllowed();
	}

	private static bool IsLocalRequest(HttpContext context)
	{
		IPAddress remoteIpAddress = context.Connection.RemoteIpAddress;
		if (remoteIpAddress == null)
		{
			return true;
		}
		return IPAddress.IsLoopback(remoteIpAddress);
	}

	private static int ResolveSessionTtlMinutes(int? requestedTtlMinutes, IBackendOptionsCatalog? options = null)
	{
		int min = options?.Auth.MinSessionTtlMinutes ?? 2;
		int max = options?.Auth.MaxSessionTtlMinutes ?? 240;
		int num = Math.Clamp(30, min, max);
		string environmentVariable = Environment.GetEnvironmentVariable("HELPER_SESSION_TOKEN_TTL_MINUTES");
		if (int.TryParse(environmentVariable, out var result))
		{
			num = result;
		}
		int value = requestedTtlMinutes ?? num;
		return Math.Clamp(value, min, max);
	}

	private static ApiPrincipal ApplySessionExchangePolicy(ApiPrincipal principal, string? requestedSurface, IReadOnlyList<string>? requestedScopes, out string normalizedSurface, out string? error)
	{
		normalizedSurface = NormalizeSessionSurface(requestedSurface);
		error = null;
		if (!SessionSurfaceScopes.TryGetValue(normalizedSurface, out string[] value))
		{
			error = $"Unknown session surface '{requestedSurface}'.";
			return principal;
		}

		string[] array = value
			.Where(principal.Scopes.Contains)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray();
		if (array.Length == 0)
		{
			error = $"No scopes from surface '{normalizedSurface}' are permitted for current principal.";
			return principal;
		}

		if (requestedScopes == null || requestedScopes.Count == 0)
		{
			return principal with
			{
				Scopes = new HashSet<string>(array, StringComparer.OrdinalIgnoreCase)
			};
		}

		string[] array2 = (from x in requestedScopes
			where !string.IsNullOrWhiteSpace(x)
			select x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
		if (array2.Length == 0)
		{
			return principal with
			{
				Scopes = new HashSet<string>(array, StringComparer.OrdinalIgnoreCase)
			};
		}

		string[] array3 = array2
			.Where(scope => array.Contains(scope, StringComparer.OrdinalIgnoreCase))
			.ToArray();
		if (array3.Length == 0)
		{
			error = $"Requested scopes are not permitted for surface '{normalizedSurface}'.";
			return principal;
		}

		return principal with
		{
			Scopes = new HashSet<string>(array3, StringComparer.OrdinalIgnoreCase)
		};
	}

	private static string NormalizeSessionSurface(string? requestedSurface)
	{
		if (string.IsNullOrWhiteSpace(requestedSurface))
		{
			return "conversation";
		}

		string text = requestedSurface.Trim().ToLowerInvariant();
		return SessionSurfaceScopes.ContainsKey(text) ? text : requestedSurface.Trim();
	}

	private static HashSet<string> ResolveLocalBootstrapScopes()
	{
		string environmentVariable = Environment.GetEnvironmentVariable("HELPER_LOCAL_BOOTSTRAP_SCOPES");
		if (string.IsNullOrWhiteSpace(environmentVariable))
		{
			return new HashSet<string>(DefaultLocalBootstrapScopes, StringComparer.OrdinalIgnoreCase);
		}
		string[] array = (from x in environmentVariable.Split(',', StringSplitOptions.RemoveEmptyEntries)
			select x.Trim() into x
			where !string.IsNullOrWhiteSpace(x) && AllowedLocalBootstrapScopes.Contains(x)
			select x).Distinct<string>(StringComparer.OrdinalIgnoreCase).ToArray();
		return (array.Length == 0) ? new HashSet<string>(DefaultLocalBootstrapScopes, StringComparer.OrdinalIgnoreCase) : new HashSet<string>(array, StringComparer.OrdinalIgnoreCase);
	}

	}

