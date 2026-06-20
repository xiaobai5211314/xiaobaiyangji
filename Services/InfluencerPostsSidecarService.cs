using System.Diagnostics;

namespace 小白养基.Services
{
    public sealed class InfluencerPostsSidecarService : BackgroundService
    {
        private const string DefaultStagingConfigPath = "/home/deploy/guzhi_translation_secrets.json";
        private readonly IHostEnvironment _environment;
        private readonly IConfiguration _configuration;
        private readonly ILogger<InfluencerPostsSidecarService> _logger;

        public InfluencerPostsSidecarService(
            IHostEnvironment environment,
            IConfiguration configuration,
            ILogger<InfluencerPostsSidecarService> logger)
        {
            _environment = environment;
            _configuration = configuration;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await ApplyPendingTranslationConfigAsync(stoppingToken);

            var configuredMinutes = _configuration.GetValue<int?>("InfluencerPosts:SyncIntervalMinutes") ?? 30;
            var interval = TimeSpan.FromMinutes(Math.Clamp(configuredMinutes, 5, 24 * 60));

            while (!stoppingToken.IsCancellationRequested)
            {
                await RunSidecarAsync(stoppingToken);
                try
                {
                    await Task.Delay(interval, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        private async Task ApplyPendingTranslationConfigAsync(CancellationToken cancellationToken)
        {
            var stagingPath = Environment.GetEnvironmentVariable("INFLUENCER_TRANSLATION_CONFIG_STAGING_PATH")
                ?? DefaultStagingConfigPath;
            if (!File.Exists(stagingPath)) return;

            var scriptPath = Path.Combine(
                _environment.ContentRootPath,
                "tools",
                "x_tweets_fetcher",
                "configure_translation_env.py");
            var privateEnvPath = Path.Combine(_environment.ContentRootPath, ".secrets", "influencer.env");
            if (!File.Exists(scriptPath))
            {
                _logger.LogError("Influencer translation configuration script is missing: {ScriptPath}", scriptPath);
                return;
            }

            try
            {
                var result = await RunProcessAsync(
                    "python3",
                    new[] { scriptPath, privateEnvPath },
                    stagingPath,
                    TimeSpan.FromSeconds(30),
                    cancellationToken);
                if (result.ExitCode != 0)
                {
                    _logger.LogError(
                        "Influencer translation private configuration failed with exit code {ExitCode}: {Error}",
                        result.ExitCode,
                        Tail(result.StandardError));
                    return;
                }

                File.Delete(stagingPath);
                _logger.LogInformation("Influencer translation private configuration applied.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                _logger.LogError(ex, "Influencer translation private configuration could not be applied.");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogError("Influencer translation private configuration timed out.");
            }
        }

        private async Task RunSidecarAsync(CancellationToken cancellationToken)
        {
            var sidecarDirectory = Path.Combine(_environment.ContentRootPath, "tools", "x_tweets_fetcher");
            var pythonPath = Path.Combine(sidecarDirectory, ".venv", "bin", "python");
            var scriptPath = Path.Combine(sidecarDirectory, "fetch_posts.py");
            if (!File.Exists(pythonPath) || !File.Exists(scriptPath))
            {
                _logger.LogWarning("Influencer posts sidecar is not installed; sync skipped.");
                return;
            }

            try
            {
                var result = await RunProcessAsync(
                    pythonPath,
                    new[] { scriptPath },
                    null,
                    TimeSpan.FromMinutes(10),
                    cancellationToken);
                if (result.ExitCode == 0)
                {
                    _logger.LogInformation("Influencer posts sidecar sync completed.");
                    return;
                }

                _logger.LogWarning(
                    "Influencer posts sidecar sync failed with exit code {ExitCode}: {Error}",
                    result.ExitCode,
                    Tail(result.StandardError));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Influencer posts sidecar sync timed out.");
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException)
            {
                _logger.LogWarning(ex, "Influencer posts sidecar could not be started.");
            }
        }

        private static async Task<ProcessResult> RunProcessAsync(
            string fileName,
            IEnumerable<string> arguments,
            string? standardInputPath,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            var startInfo = new ProcessStartInfo(fileName)
            {
                UseShellExecute = false,
                RedirectStandardInput = standardInputPath != null,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = new Process { StartInfo = startInfo };
            if (!process.Start()) throw new InvalidOperationException($"Could not start {fileName}.");

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            if (standardInputPath != null)
            {
                await using var input = File.OpenRead(standardInputPath);
                await input.CopyToAsync(process.StandardInput.BaseStream, cancellationToken);
                process.StandardInput.Close();
            }

            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);
            try
            {
                await process.WaitForExitAsync(timeoutSource.Token);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                throw;
            }

            return new ProcessResult(process.ExitCode, await outputTask, await errorTask);
        }

        private static string Tail(string value)
        {
            const int limit = 1500;
            var text = value.Trim();
            return text.Length <= limit ? text : text[^limit..];
        }

        private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
    }
}
