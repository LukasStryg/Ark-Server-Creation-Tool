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
            public Task<bool> ConnectAndAuthenticateAsync(string password, CancellationToken ct = default) => Task.FromResult(true);
            public Task<string> ExecuteAsync(string command, CancellationToken ct = default) { Commands.Add(command); return Task.FromResult("ok"); }
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
            var svc = new ServerControlService(() => rcon, proc, "pw");
            var steps = new List<RestartStep>
            {
                new RestartStep("Server restarting in 1 minute", TimeSpan.FromMinutes(1)),
            };

            // process exits promptly once DoExit is issued; delay is a no-op in the test
            Func<TimeSpan, CancellationToken, Task> noWait = (_, __) => { proc.Running = false; return Task.CompletedTask; };

            var ok = await svc.RestartWithCountdownAsync(steps, TimeSpan.FromSeconds(2), noWait);

            Assert.True(ok);
            Assert.Contains("Broadcast Server restarting in 1 minute", rcon.Commands);
            Assert.Contains("SaveWorld", rcon.Commands);
            Assert.Contains("DoExit", rcon.Commands);
            Assert.Equal(1, proc.StartCalls);
        }
    }
}
