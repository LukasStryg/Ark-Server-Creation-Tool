namespace ARKServerCreationTool.Services.Servers
{
    /// <summary>Adapts the existing (internal) GameProcessManager to IServerProcessController. Internal to match GameProcessManager's accessibility.</summary>
    internal class GameProcessControllerAdapter : IServerProcessController
    {
        private readonly GameProcessManager _manager;

        internal GameProcessControllerAdapter(GameProcessManager manager) => _manager = manager;

        public bool IsRunning => _manager.IsRunning;
        public bool Start() => _manager.Start();
        // GameProcessManager.Stop() returns IsRunning (true if still running); invert to "stopped".
        public bool ForceStop() => !_manager.Stop();
    }
}
