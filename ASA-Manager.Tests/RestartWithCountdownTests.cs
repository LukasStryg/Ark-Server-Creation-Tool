using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ARKServerCreationTool.Services.Rcon;
using ARKServerCreationTool.Services.Reliability;
using ARKServerCreationTool.Services.Servers;
using Xunit;

namespace ARKServerCreationTool.Tests
{
    public class RestartWithCountdownTests
    {
        private sealed class FakeRcon : IRconClient
        {
            public List<string> Commands = new();
            public Action? OnDoExit;   // in reality, DoExit terminates the server process
            public Task<bool> ConnectAndAuthenticateAsync(string password, CancellationToken ct = default) => Task.FromResult(true);
            public Task<string> ExecuteAsync(string command, CancellationToken ct = default)
            {
                Commands.Add(command);
                if (command == "DoExit") OnDoExit?.Invoke();
                return Task.FromResult("ok");
            }
            public void Dispose() { }
        }

        private sealed class FakeProcess : IServerProcessController
        {
            public bool Running = true;
            public int StartCalls = 0;
            public bool IsRunning => Running;
            public bool Start() { StartCalls++; Running = true; return true; }
            public bool ForceStop() { Running = false; return true; }
        }

        [Fact]
        public async Task Broadcasts_each_step_then_saves_exits_and_restarts()
        {
            var rcon = new FakeRcon();
            var proc = new FakeProcess();
            rcon.OnDoExit = () => proc.Running = false;   // the server stops in response to DoExit, not before
            var svc = new ServerControlService(() => rcon, proc, "pw");
            var steps = new List<RestartStep>
            {
                new RestartStep("Server restarting in 1 minute", TimeSpan.FromMinutes(1)),
            };

            // The countdown delay is a no-op in the test; the process stays running until DoExit.
            Func<TimeSpan, CancellationToken, Task> noWait = (_, __) => Task.CompletedTask;

            var ok = await svc.RestartWithCountdownAsync(steps, TimeSpan.FromSeconds(2), noWait);

            Assert.True(ok);
            Assert.Contains("Broadcast Server restarting in 1 minute", rcon.Commands);
            Assert.Contains("SaveWorld", rcon.Commands);
            Assert.Contains("DoExit", rcon.Commands);
            Assert.Equal(1, proc.StartCalls);
        }
    }
}
