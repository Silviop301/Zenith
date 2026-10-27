using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using Zenith.Core.Ipc;

namespace Zenith.Service;

/// <summary>Accepts local connections on the Zenith pipe and answers one request at a time per connection.</summary>
public sealed class PipeServer(CommandHandler handler, ILogger<PipeServer> logger) : BackgroundService
{
    private const int MaxConnections = 4;

    // One lock for all connections: commands are quick, and serializing them keeps journal and settings consistent.
    private readonly SemaphoreSlim _gate = new(1, 1);

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(Enumerable.Range(0, MaxConnections).Select(_ => AcceptLoop(stoppingToken)));

    private async Task AcceptLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var pipe = CreatePipe();
                await pipe.WaitForConnectionAsync(ct);
                await Serve(pipe, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Pipe connection ended with an error.");
            }
        }
    }

    private async Task Serve(NamedPipeServerStream pipe, CancellationToken ct)
    {
        string? callerSid = null;
        while (pipe.IsConnected && await Protocol.ReadAsync<Request>(pipe, ct) is { } request)
        {
            // Windows only allows identifying the client after reading from the pipe, so this waits for the first request.
            callerSid ??= CallerSid(pipe);

            Response response;
            await _gate.WaitAsync(ct);
            try
            {
                response = handler.Handle(request, callerSid);
            }
            finally
            {
                _gate.Release();
            }
            logger.LogInformation("{Command} from {Sid}: {Result}", request.Command, callerSid, response.Ok ? "ok" : response.Error);
            await Protocol.WriteAsync(pipe, response, ct);
        }
    }

    private string? CallerSid(NamedPipeServerStream pipe)
    {
        try
        {
            string? sid = null;
            pipe.RunAsClient(() => sid = WindowsIdentity.GetCurrent(TokenAccessLevels.Query).User?.Value);
            if (sid is not null) return sid;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not identify the pipe client by impersonation; trying its process token.");
        }

        // Fallback: the token of the client process. Unknown caller means read-only commands still work and changes are refused.
        try
        {
            return ClientProcess.UserSid(pipe.SafePipeHandle);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not identify the pipe client.");
            return null;
        }
    }

    /// <summary>Signed-in users may connect; nobody over the network.</summary>
    private static NamedPipeServerStream CreatePipe()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null),
            PipeAccessRights.FullControl, AccessControlType.Deny));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            PipeAccessRights.ReadWrite, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(Protocol.PipeName, PipeDirection.InOut, MaxConnections,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, security);
    }
}
