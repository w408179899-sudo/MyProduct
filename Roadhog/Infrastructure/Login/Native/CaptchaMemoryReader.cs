using System.Drawing.Imaging;
using System.Security.Cryptography;
using Vmmsharp;

namespace Roadhog.Infrastructure.Login.Native;

internal sealed record CaptchaImage(ulong Address, byte[] Jpeg, byte[] Png, string Hash);

internal static class CaptchaMemoryReader
{
    internal static Dictionary<ulong,CaptchaImage> Discover(VmmProcess process,CancellationToken token)
    {
        var regions=process.MapVAD();var pages=new SortedSet<ulong>();
        foreach(var pte in process.MapPTE(false))
        {
            if(!pte.fR||pte.vaBase>=0x80000000)continue;
            for(ulong address=pte.vaBase;address<=pte.vaEnd&&address<0x80000000;address+=4096)
            {
                if(pages.Count>=131072)throw new InvalidOperationException("验证码候选扫描超过范围。");
                var region=regions.FirstOrDefault(r=>address>=r.vaStart&&address<=r.vaEnd);
                if(region.cbSize>0&&!region.fImage&&!region.fStack&&!region.fTeb&&!region.fFile)pages.Add(address);
            }
        }
        var found=new Dictionary<ulong,CaptchaImage>();
        foreach(var batch in pages.Chunk(256))
        {
            token.ThrowIfCancellationRequested();
            foreach(var read in process.MemReadScatter(1,batch))
            {
                // Failed scan pages cannot establish absence. Only positively revalidated image buffers are returned.
                if(!read.f||read.pb is not{Length:4096})continue;
                for(int i=0;i<4093;i++)if(read.pb[i]==255&&read.pb[i+1]==216&&read.pb[i+2]==255)
                {
                    var image=Read(process,read.qwA+(ulong)i);if(image is not null)found[image.Address]=image;
                }
            }
        }
        return found;
    }
    internal static CaptchaImage? Read(VmmProcess process,ulong address)
    {
        using var bytes=new MemoryStream();
        for(int n=0;n<65536;)
        {
            int length=Math.Min(4096-(int)((address+(ulong)n)%4096),65536-n);
            var part=process.MemRead(address+(ulong)n,(uint)length,1);
            if(part is null||part.Length!=length)break;
            bytes.Write(part);n+=length;
            if(bytes.Length>2&&bytes.GetBuffer().AsSpan(2,(int)bytes.Length-2).IndexOf(new byte[]{255,217})>=0)break;
        }
        var data=bytes.ToArray();if(data.Length<4||data[0]!=255||data[1]!=216)return null;
        int end=data.AsSpan(2).IndexOf(new byte[]{255,217});if(end<0)return null;
        data=data[..(end+4)];
        try
        {
            if(!LoginNativeReader.Exact(process,address,data.Length).AsSpan().SequenceEqual(data))return null;
            using var stream=new MemoryStream(data,false);using var image=new Bitmap(stream);
            if(image.Width!=120||image.Height!=60)return null;
            using var png=new MemoryStream();image.Save(png,ImageFormat.Png);
            return new(address,data,png.ToArray(),Convert.ToHexString(SHA256.HashData(data)));
        }
        catch(Exception exception) when(exception is ArgumentException or System.Runtime.InteropServices.ExternalException or InvalidOperationException){return null;}
    }
    internal static CaptchaImage? Changed(IReadOnlyDictionary<ulong,CaptchaImage> before,IEnumerable<CaptchaImage> after)
    {
        var changed=after.Where(image=>before.TryGetValue(image.Address,out var old)&&image.Hash!=old.Hash)
            .GroupBy(image=>image.Hash).ToArray();
        return changed.Length==1?changed[0].First():null;
    }
}
