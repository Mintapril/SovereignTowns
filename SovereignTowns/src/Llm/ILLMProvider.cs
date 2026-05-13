using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SovereignTowns.Llm;

/// <summary>
/// Strongly-typed configuration for the LLM subsystem.
/// <para>
/// Architectural constraint: LLM calls are <b>forbidden on the hot path</b>
/// (HourlyTick, save/load, UI render). They are only permitted on
/// <c>DailyTick</c> long-term planning and explicit user-initiated advice
/// requests. The two boolean toggles below act as kill-switches that the
/// reasoning service must respect before issuing any provider call.
/// </para>
/// <para>
/// This POCO uses only <see cref="string"/>/<see cref="int"/>/<see cref="float"/>/<see cref="bool"/>
/// properties with public getters and setters so it can be round-tripped
/// through the mod's hand-rolled JSON serializer without reflection helpers
/// or attributes.
/// </para>
/// </summary>
public sealed class LLMConfig
{
    /// <summary>
    /// Provider identifier. Recognised values: <c>"noop"</c> (disabled),
    /// <c>"ollama"</c> (local Ollama HTTP API), <c>"openai_compatible"</c>
    /// (any OpenAI Chat Completions compatible endpoint).
    /// </summary>
    public string Provider { get; set; } = "noop";

    /// <summary>
    /// Base HTTP endpoint for the provider.
    /// Default <c>http://localhost:11434</c> targets a local Ollama daemon;
    /// for OpenAI-compatible remote services use e.g. <c>https://api.openai.com</c>.
    /// </summary>
    public string Endpoint { get; set; } = "http://localhost:11434";

    /// <summary>
    /// Model identifier passed verbatim to the provider (e.g.
    /// <c>llama3.2</c>, <c>qwen2.5:7b</c>, <c>gpt-4o-mini</c>).
    /// </summary>
    public string Model { get; set; } = "llama3.2";

    /// <summary>
    /// Name of the environment variable that holds the API key for remote
    /// providers. Empty string indicates the provider requires no
    /// authentication (typical for local Ollama).
    /// </summary>
    public string ApiKeyEnvVar { get; set; } = "";

    /// <summary>
    /// Sampling temperature in <c>[0.0, 2.0]</c>. Lower values produce more
    /// deterministic output, which is desirable for structured planning
    /// responses parsed by the mod.
    /// </summary>
    public float Temperature { get; set; } = 0.2f;

    /// <summary>
    /// Hard upper bound on tokens the provider may emit per response.
    /// </summary>
    public int MaxTokens { get; set; } = 1024;

    /// <summary>
    /// Per-request timeout in seconds. Provider implementations must abort
    /// the underlying <see cref="HttpClient"/> request and return a failed
    /// <see cref="LLMResponse"/> when this elapses.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// Master toggle for DailyTick long-term garrison / settlement planning
    /// calls. When <c>false</c> the reasoning service must short-circuit and
    /// fall back to deterministic heuristics.
    /// </summary>
    public bool EnableForLongTermPlanning { get; set; } = false;

    /// <summary>
    /// Master toggle for synchronous, user-initiated advice requests
    /// (e.g. clicking an "Ask advisor" button in the UI).
    /// </summary>
    public bool EnableForUserAdvice { get; set; } = false;
}

/// <summary>
/// Immutable-by-convention request payload describing a single completion
/// call. Holds the system instructions, the user-supplied prompt, and an
/// audit-friendly <see cref="Context"/> tag used by the logger and metrics
/// to classify the call site.
/// </summary>
public sealed class LLMPrompt
{
    /// <summary>System / role instructions sent to the model.</summary>
    public string SystemPrompt { get; set; } = "";

    /// <summary>Concrete user prompt for this invocation.</summary>
    public string UserPrompt { get; set; } = "";

    /// <summary>
    /// Audit category tag (e.g. <c>"garrison_planning"</c>,
    /// <c>"user_advice"</c>). Used for logging and rate-limiting buckets,
    /// not passed to the underlying provider.
    /// </summary>
    public string Context { get; set; } = "garrison_planning";

    /// <summary>Parameterless constructor required by simple serializers.</summary>
    public LLMPrompt() {}

    /// <summary>Constructs a prompt with all fields populated.</summary>
    /// <param name="systemPrompt">System / role instructions.</param>
    /// <param name="userPrompt">User prompt body.</param>
    /// <param name="context">Audit category; defaults to <c>"garrison_planning"</c>.</param>
    public LLMPrompt(string systemPrompt, string userPrompt, string context = "garrison_planning")
    {
        SystemPrompt = systemPrompt;
        UserPrompt = userPrompt;
        Context = context;
    }
}

/// <summary>
/// Result of a single <see cref="ILLMProvider.CompleteAsync"/> call.
/// On failure <see cref="Success"/> is <c>false</c>, <see cref="Error"/>
/// is populated, and <see cref="Content"/> is the empty string.
/// </summary>
public sealed class LLMResponse
{
    /// <summary><c>true</c> when the provider returned a usable completion.</summary>
    public bool Success { get; set; }

    /// <summary>Raw completion text from the model. Empty on failure.</summary>
    public string Content { get; set; } = "";

    /// <summary>Human-readable error message on failure; <c>null</c> on success.</summary>
    public string? Error { get; set; }

    /// <summary>Wall-clock latency of the call in milliseconds.</summary>
    public int LatencyMs { get; set; }

    /// <summary>Name of the provider that produced this response.</summary>
    public string ProviderName { get; set; } = "";

    /// <summary>Convenience factory for a failed response.</summary>
    /// <param name="err">Error message.</param>
    /// <param name="providerName">Originating provider name; defaults to <c>"?"</c>.</param>
    public static LLMResponse Fail(string err, string providerName = "?") =>
        new LLMResponse { Success = false, Error = err, ProviderName = providerName };

    /// <summary>Convenience factory for a successful response.</summary>
    /// <param name="content">Completion text from the model.</param>
    /// <param name="latencyMs">Measured latency in milliseconds.</param>
    /// <param name="providerName">Originating provider name.</param>
    public static LLMResponse Ok(string content, int latencyMs, string providerName) =>
        new LLMResponse { Success = true, Content = content, LatencyMs = latencyMs, ProviderName = providerName };
}

/// <summary>
/// Provider-agnostic contract for issuing a single chat-style completion.
/// Implementations must honour <paramref name="ct"/> and the configured
/// timeout, and must never throw across this boundary - all failure modes
/// are returned as a <see cref="LLMResponse"/> with <c>Success = false</c>.
/// </summary>
public interface ILLMProvider
{
    /// <summary>Stable, short identifier for this provider (e.g. <c>"noop"</c>, <c>"ollama"</c>).</summary>
    string Name { get; }

    /// <summary>
    /// <c>true</c> when the provider has everything it needs to attempt a
    /// real call (endpoint reachable shape, API key present if required,
    /// master toggles satisfied at construction time).
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Issues a single completion request.
    /// </summary>
    /// <param name="prompt">Prompt payload.</param>
    /// <param name="ct">Cooperative cancellation token.</param>
    /// <returns>A <see cref="LLMResponse"/> describing success or failure; never throws.</returns>
    Task<LLMResponse> CompleteAsync(LLMPrompt prompt, CancellationToken ct);
}

/// <summary>
/// Null-object provider used as the default and as a safe fallback when
/// the LLM subsystem is disabled by configuration. Every call returns
/// a failed <see cref="LLMResponse"/> synchronously without performing
/// any I/O.
/// </summary>
public sealed class NoOpLLMProvider : ILLMProvider
{
    /// <inheritdoc/>
    public string Name => "noop";

    /// <inheritdoc/>
    public bool IsConfigured => true;

    /// <inheritdoc/>
    public Task<LLMResponse> CompleteAsync(LLMPrompt prompt, CancellationToken ct)
    {
        return Task.FromResult(LLMResponse.Fail("LLM disabled (NoOp)", Name));
    }
}
