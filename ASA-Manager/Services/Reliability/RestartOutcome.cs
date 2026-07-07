namespace ARKServerCreationTool.Services.Reliability
{
    public readonly record struct RestartResult(bool MarkStarted, string LogLine);

    /// <summary>
    /// Pure mapping from a scheduled restart's start result to the coordinator's response. Only a
    /// server that actually started is marked started and logged as "done"; a failed start is logged
    /// as a failure and left not-started, so crash detection notices it is down and retries — rather
    /// than being silently masked as a successful restart (the old behaviour ignored Start()'s bool).
    /// </summary>
    public static class RestartOutcome
    {
        public static RestartResult From(string serverName, bool started) =>
            started
                ? new RestartResult(true, $"Scheduled restart done: {serverName}")
                : new RestartResult(false, $"Scheduled restart failed: {serverName}");
    }
}
