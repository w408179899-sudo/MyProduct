using System.Text;

namespace Roadhog.Infrastructure.Login.Native;

// Resolve by the PE name-ordinal table, never by assuming a name index is a function
// index. The installed VMM export helper returned the adjacent CRT function live.
internal static class LoginPeExports
{
    internal static uint Find(Func<ulong,int,byte[]?> read,uint moduleBase,uint imageSize,string name)
    {
        byte[] At(uint rva,int size)
        {
            if(rva>imageSize||(ulong)rva+(uint)size>imageSize)throw new InvalidOperationException("Invalid PE export range.");
            var bytes=read((ulong)moduleBase+rva,size);
            return bytes is not null&&bytes.Length==size?bytes:throw new InvalidOperationException("Incomplete PE export identity.");
        }
        var dos=At(0,0x40);uint peAt=BitConverter.ToUInt32(dos,0x3c);var pe=At(peAt,0x80);
        if(BitConverter.ToUInt16(dos)!=0x5a4d||BitConverter.ToUInt32(pe)!=0x4550||BitConverter.ToUInt16(pe,0x18)!=0x10b)
            throw new InvalidOperationException("Unsupported login PE image.");
        uint directory=BitConverter.ToUInt32(pe,0x78),directorySize=BitConverter.ToUInt32(pe,0x7c);
        if(directory==0)return 0;
        var exports=At(directory,40);uint functions=BitConverter.ToUInt32(exports,20),count=BitConverter.ToUInt32(exports,24),
            functionTable=BitConverter.ToUInt32(exports,28),names=BitConverter.ToUInt32(exports,32),ordinals=BitConverter.ToUInt32(exports,36);
        if(count>100000||functions>100000)throw new InvalidOperationException("Invalid PE export count.");
        int low=0,high=checked((int)count)-1;
        while(low<=high)
        {
            int index=low+(high-low)/2;
            uint textAt=BitConverter.ToUInt32(At(checked(names+(uint)index*4),4));
            var text=new List<byte>();bool ended=false;
            for(int offset=0;offset<256&&!ended;)
            {
                uint at=checked(textAt+(uint)offset);
                if(at>=imageSize)throw new InvalidOperationException("Invalid PE export name range.");
                int size=Math.Min(Math.Min(64,256-offset),Math.Min(checked((int)(imageSize-at)),4096-(int)(((ulong)moduleBase+at)%4096)));
                foreach(byte value in At(at,size)){if(value==0){ended=true;break;}text.Add(value);}
                offset+=size;
            }
            if(!ended)throw new InvalidOperationException("Unterminated PE export name.");
            int order=string.CompareOrdinal(Encoding.ASCII.GetString(text.ToArray()),name);
            if(order<0)low=index+1;
            else if(order>0)high=index-1;
            else
            {
                uint ordinal=BitConverter.ToUInt16(At(checked(ordinals+(uint)index*2),2));
                if(ordinal>=functions)throw new InvalidOperationException("Invalid PE export ordinal.");
                uint rva=BitConverter.ToUInt32(At(checked(functionTable+ordinal*4),4));
                if(rva==0)return 0;
                if(rva>=directory&&(ulong)rva<(ulong)directory+directorySize)return 0; // Forwarded exports need separate proof.
                At(rva,1);return checked(moduleBase+rva);
            }
        }
        return 0;
    }
}
