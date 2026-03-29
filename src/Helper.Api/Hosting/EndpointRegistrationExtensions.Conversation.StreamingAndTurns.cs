#nullable enable
// Minimal API glue in these partials triggers noisy false positives from Roslyn nullability analysis.
// Keep suppressions local to endpoint registration instead of project-wide.
#pragma warning disable CS8600, CS8601, CS8602, CS8603, CS8604, CS8619, CS8622, CS8632

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Helper.Api.Conversation;
using Helper.Runtime.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Helper.Api.Hosting;

public static partial class EndpointRegistrationExtensions
{
    private static void MapConversationStreamingAndTurnEndpoints(IEndpointRouteBuilder endpoints)
    {
        MapConversationChatAndStreamingEndpoints(endpoints);
        MapConversationStreamResumeEndpoints(endpoints);
    }
}

