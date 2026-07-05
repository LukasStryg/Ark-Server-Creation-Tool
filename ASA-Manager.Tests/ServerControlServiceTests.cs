using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ARKServerCreationTool.Services.Rcon;
using ARKServerCreationTool.Services.Servers;
using Xunit;

namespace ARKServerCreationTool.Tests
{
    public class ServerControlServiceTests
    {
        private sealed class FakeRcon : IRconClient
        {
            public bool AuthOk = true;
            public bool ThrowOnConnect = false;
            public List<string> Commands = new();

            public Task<bool> ConnectAndAuthenticateAsync(string password, CancellationToken ct = default)
            {
                if (ThrowOnConnect) throw new Exception("connect failed");
                return Task.FromResult(AuthOk);
            }

            public Task<string> ExecuteAsync(string command, CancellationToken ct = default)
            {
                Commands.Add(command);
                return Task.FromResult(command == "SaveWorld" ? "World Saved" : "ok");
            }

            public void Dispose() { }
        }

        private sealed class FakeProcess : IServerProcessController
        {
            public bool Running = true;
            public int ForceStopCalls = 0;
            public bool IsRunning => Running;
            public bool Start() { Running = true; return true; }
            public bool ForceStop() { ForceStopCalls++; Running = false; return true; }
        }

        [Fact]
        public async Task Graceful_stop_saves_then_exits_without_force()
        {
            var rcon = new FakeRcon();
            var proc = new FakeProcess();
            var svc = new ServerControlService(() => rcon, proc, "pw");

            var task = svc.GracefulStopAsync(TimeSpan.FromSeconds(2));
            proc.Running = false; // process exits after DoExit (observed on the next poll)
            var result = await task;

            Assert.Equal(StopResult.GracefulStop, result);
            Assert.Contains("SaveWorld", rcon.Commands);
            Assert.Contains("DoExit", rcon.Commands);
            Assert.Equal(0, proc.ForceStopCalls);
        }

        [Fact]
        public async Task Falls_back_to_force_stop_when_rcon_auth_fails()
        {
            var rcon = new FakeRcon { AuthOk = false };
            var proc = new FakeProcess();
            var svc = new ServerControlService(() => rcon, proc, "pw");

            var result = await svc.GracefulStopAsync(TimeSpan.FromMilliseconds(200));

            Assert.Equal(StopResult.ForcedStop, result);
            Assert.Equal(1, proc.ForceStopCalls);
        }

        [Fact]
        public async Task Falls_back_to_force_stop_when_rcon_connect_throws()
        {
            var rcon = new FakeRcon { ThrowOnConnect = true };
            var proc = new FakeProcess();
            var svc = new ServerControlService(() => rcon, proc, "pw");

            var result = await svc.GracefulStopAsync(TimeSpan.FromMilliseconds(200));

            Assert.Equal(StopResult.ForcedStop, result);
            Assert.Equal(1, proc.ForceStopCalls);
        }

        [Fact]
        public async Task Returns_already_stopped_when_not_running()
        {
            var proc = new FakeProcess { Running = false };
            var svc = new ServerControlService(() => new FakeRcon(), proc, "pw");

            var result = await svc.GracefulStopAsync(TimeSpan.FromMilliseconds(200));

            Assert.Equal(StopResult.AlreadyStopped, result);
        }
    }
}
