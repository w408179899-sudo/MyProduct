namespace Roadhog.Infrastructure.Login.Native;

// DuiLib has both containers and leaf/custom controls. Only the optional header is
// probed; once an array is identified, missing entries must fail the whole scene.
internal static class LoginUiChildren
{
    internal static uint[] Read(Func<ulong,int,byte[]?> read,uint address,uint manager,
        uint arrayOffset,uint managerOffset,bool required,int maximum)
    {
        byte[] Exact(ulong at,int length)
        {
            var bytes=read(at,length);
            return bytes is not null&&bytes.Length==length?bytes:
                throw new InvalidOperationException("Incomplete login UI child array.");
        }
        bool Pointer(uint value)=>value>=0x10000&&value<0x80000000&&value%4==0;
        var header=read(address+arrayOffset,12);
        if(header is null||header.Length!=12)
        {
            if(required)throw new InvalidOperationException("Incomplete login UI container header.");
            return [];
        }
        uint data=BitConverter.ToUInt32(header),count=BitConverter.ToUInt32(header,4),capacity=BitConverter.ToUInt32(header,8);
        if(count>maximum||capacity<count||capacity>maximum*4||(count>0&&!Pointer(data)))
        {
            if(required)throw new InvalidOperationException("Invalid login UI container bounds.");
            return [];
        }
        var children=new uint[count];
        if(count>0)
        {
            var bytes=Exact(data,checked((int)count*4));
            for(int i=0;i<count;i++)
            {
                uint child=BitConverter.ToUInt32(bytes,i*4);
                if(!Pointer(child))throw new InvalidOperationException("Invalid login UI child pointer.");
                var relationship=Exact(child+managerOffset,8);
                if(BitConverter.ToUInt32(relationship)!=manager||BitConverter.ToUInt32(relationship,4)!=address)
                {
                    if(!required)return []; // Complete evidence that a custom control is not a container.
                    throw new InvalidOperationException("Login UI child backlink changed.");
                }
                children[i]=child;
            }
            if(!Exact(data,bytes.Length).AsSpan().SequenceEqual(bytes))
                throw new InvalidOperationException("Login UI child identities changed during traversal.");
        }
        if(!Exact(address+arrayOffset,12).AsSpan().SequenceEqual(header))
            throw new InvalidOperationException("Login UI children changed during traversal.");
        return children;
    }
}
