using System.IO.Pipes;
using System.Text.Json.Nodes;
using FreeAppLock;

if(args.Length==0 || args[0].TrimEnd('/')!="chrome-extension://"+NativeWire.ExtensionId)return 1;
string session=Guid.NewGuid().ToString();
using var input=Console.OpenStandardInput();using var output=Console.OpenStandardOutput();
try
{
    while(await NativeWire.Read(input) is {} json)
    {
        var request=JsonNode.Parse(json) as JsonObject;
        if(request==null)break;
        request["sessionId"]=session;
        string reply;bool unavailable=false;
        try
        {
            using var timeout=new CancellationTokenSource(1600);
            using var pipe=new NamedPipeClientStream(".",NativeWire.PipeName,PipeDirection.InOut,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(800,timeout.Token);
            await NativeWire.Write(pipe,request.ToJsonString(),timeout.Token);
            reply=await NativeWire.Read(pipe,timeout.Token) ?? throw new System.IO.IOException();
        }
        catch(Exception ex) when(ex is System.IO.IOException or TimeoutException or OperationCanceledException or UnauthorizedAccessException)
        {
            unavailable=true;
            reply=new JsonObject { ["requestId"]=request["requestId"]?.DeepClone(),["available"]=false,["protected"]=false,["message"]="Open FreeAppLock to connect this profile." }.ToJsonString();
        }
        await NativeWire.Write(output,reply);
        if(unavailable)return 0; // Release the executable when the desktop closes so upgrades can replace it.
    }
}
catch(Exception ex) when(ex is System.IO.IOException or System.Text.Json.JsonException or System.Text.DecoderFallbackException){return 1;}
return 0;
