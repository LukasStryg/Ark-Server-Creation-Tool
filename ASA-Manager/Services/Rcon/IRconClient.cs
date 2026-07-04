using System;
using System.Threading;
using System.Threading.Tasks;

namespace ARKServerCreationTool.Services.Rcon
{
    public interface IRconClient : IDisposable
    {
        Task<bool> ConnectAndAuthenticateAsync(string password, CancellationToken ct = default);
        Task<string> ExecuteAsync(string command, CancellationToken ct = default);
    }
}
