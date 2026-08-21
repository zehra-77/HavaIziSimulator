namespace HavaIziSimulator.Llm;

/// <summary>
/// Ollama yerine kullanılan, OpenAI uyumlu barındırılan LLM ayarları.
/// Varsayılan sağlayıcı Groq'tur; API anahtarı kaynak koda yazılmaz.
/// </summary>
public sealed class HostedLlmOptions
{
    public string BaseUrl { get; set; } =
        Environment.GetEnvironmentVariable("RADAR_LLM_BASE_URL")
        ?? "https://api.groq.com/openai/v1";

    public string Model { get; set; } =
        Environment.GetEnvironmentVariable("RADAR_LLM_MODEL")
        ?? "openai/gpt-oss-20b";

    public string ApiKeyEnvironmentVariable { get; set; } = "GROQ_API_KEY";

    public double Temperature { get; set; } = 0.1;

    public int MaxCompletionTokens { get; set; } = 350;

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(45);

    public string ApiKey =>
        Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable)
        ?? throw new InvalidOperationException(
            $"{ApiKeyEnvironmentVariable} ortam değişkeni bulunamadı. " +
            "API anahtarını kaynak koda yazmadan Windows ortam değişkeni olarak ekleyin.");
}
