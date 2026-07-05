namespace ARKServerCreationTool.Services.Servers
{
    /// <summary>Abstraction over a server's OS process lifecycle, so control logic is testable.</summary>
    public interface IServerProcessController
    {
        bool IsRunning { get; }
        bool Start();
        /// <summary>Force-kill the process. Returns true if the process is stopped afterwards.</summary>
        bool ForceStop();
    }
}
