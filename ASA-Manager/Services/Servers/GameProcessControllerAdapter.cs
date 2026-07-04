namespace ARKServerCreationTool.Services.Servers
{
    /// <summary>Adapts the existing GameProcessManager to IServerProcessController.</summary>
    public class GameProcessControllerAdapter : IServerProcessController
    {
        private readonly GameProcessManager _manager;

        public GameProcessControllerAdapter(GameProcessManager manager) => _manager = manager;

        public bool IsRunning => _manager.IsRunning;
        public bool Start() => _manager.Start();
        // GameProcessManager.Stop() returns IsRunning (true if still running); invert to "stopped".
        public bool ForceStop() => !_manager.Stop();
    }
}
