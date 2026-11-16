using System.Buffers.Binary;
using System.IO;
using System.Security.Principal;
using System.Text;
namespace FreeAppLock;
internal static class NativeWire
{
    internal const int Limit=65536;
    internal const string ExtensionId="ainidohpgcpmokmgmoalbjllegiapphh";
    internal static string PipeName
    {
        get
        {
            string name="FreeAppLock.Profiles."+WindowsIdentity.GetCurrent().User!.Value+"."+System.Diagnostics.Process.GetCurrentProcess().SessionId;
#if PROFILE_BRIDGE_TEST
            return name+".IntegrationTest";
#else
            return name;
#endif
        }
    }
    internal static async Task<string?> Read(Stream stream,CancellationToken token=default)
    {
        var header=new byte[4];int read=await stream.ReadAsync(header.AsMemory(0,1),token);if(read==0)return null;
        await stream.ReadExactlyAsync(header.AsMemory(1),token);
        int length=BinaryPrimitives.ReadInt32LittleEndian(header);
        if(length<1||length>Limit)throw new InvalidDataException("Invalid message size.");
        var payload=new byte[length];await stream.ReadExactlyAsync(payload,token);return new UTF8Encoding(false,true).GetString(payload);
    }
    internal static async Task Write(Stream stream,string json,CancellationToken token=default)
    {
        var payload=Encoding.UTF8.GetBytes(json);if(payload.Length>Limit)throw new InvalidDataException("Message too large.");
        var header=new byte[4];BinaryPrimitives.WriteInt32LittleEndian(header,payload.Length);
        await stream.WriteAsync(header,token);await stream.WriteAsync(payload,token);await stream.FlushAsync(token);
    }
}
