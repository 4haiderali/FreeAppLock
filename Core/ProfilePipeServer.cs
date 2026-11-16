using System.IO;
using System.IO.Pipes;
using System.Text.Json;
namespace FreeAppLock;
internal sealed class ProfilePipeServer : IDisposable
{
    readonly CancellationTokenSource stop=new();
    readonly Func<ProfileRequest,Task<ProfileReply>> handle;
    readonly string name;
    static readonly JsonSerializerOptions Json=new(){PropertyNamingPolicy=JsonNamingPolicy.CamelCase,PropertyNameCaseInsensitive=true};
    internal ProfilePipeServer(Func<ProfileRequest,Task<ProfileReply>> handler,string? pipeName=null){handle=handler;name=pipeName??NativeWire.PipeName;_ = Task.Run(Listen);}
    async Task Listen()
    {
        while(!stop.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe=null;
            try
            {
                pipe=new(name,PipeDirection.InOut,8,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(stop.Token);_ = Process(pipe);pipe=null;
            }
            catch(OperationCanceledException){break;}
            catch(IOException){try{await Task.Delay(300,stop.Token);}catch(OperationCanceledException){break;}}
            finally{pipe?.Dispose();}
        }
    }
    async Task Process(NamedPipeServerStream pipe)
    {
        using(pipe)
        using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(stop.Token))
        {
            timeout.CancelAfter(2000);
            try
            {
                string? json=await NativeWire.Read(pipe,timeout.Token);
                var request=json==null?null:JsonSerializer.Deserialize<ProfileRequest>(json,Json);
                if(request==null)return;
                var reply=await handle(request).WaitAsync(timeout.Token);
                await NativeWire.Write(pipe,JsonSerializer.Serialize(reply,Json),timeout.Token);
            }
            catch(Exception ex) when(ex is IOException or OperationCanceledException or JsonException or ArgumentException){ }
        }
    }
    public void Dispose(){stop.Cancel();}
}
