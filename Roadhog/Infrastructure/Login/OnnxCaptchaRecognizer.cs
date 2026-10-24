using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Roadhog.Infrastructure.Login;

internal sealed class OnnxCaptchaRecognizer : IDisposable
{
    private readonly InferenceSession _session;
    private readonly string[] _tokens;
    public OnnxCaptchaRecognizer(string directory)
    {
        var modelPath = Path.Combine(directory,"captcha.onnx");
        using var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory,"captcha.json")));
        using var stream = File.OpenRead(modelPath);
        if (!Convert.ToHexString(SHA256.HashData(stream)).Equals(metadata.RootElement.GetProperty("model_sha256").GetString(),StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("验证码模型文件校验失败，请重新安装完整程序。");
        _tokens = metadata.RootElement.GetProperty("tokens").EnumerateArray().Select(x => x.GetString()!).ToArray();
        using var options = new SessionOptions { IntraOpNumThreads = 2, InterOpNumThreads = 1 };
        _session = new InferenceSession(modelPath,options);
    }
    public string Predict(byte[] image)
    {
        var input = new DenseTensor<float>(Preprocess(image),new[]{1,3,32,128});
        using var output = _session.Run(new[]{NamedOnnxValue.CreateFromTensor("images",input)});
        var logits = output.Single().AsTensor<float>(); var text = new System.Text.StringBuilder(); bool eos=false;
        for(int i=0;i<logits.Dimensions[1];i++)
        {
            int best=0; for(int j=1;j<logits.Dimensions[2];j++) if(logits[0,i,j]>logits[0,i,best])best=j;
            if(best==0){eos=true;break;}
            text.Append(_tokens[best]);
        }
        var value=text.ToString();
        return eos && value.Length==6 && value.All(char.IsAsciiLetterOrDigit) ? value : string.Empty;
    }
    internal static float[] Preprocess(byte[] data)
    {
        using var stream = new MemoryStream(data,false); using var image = new Bitmap(stream);
        if(image.Width!=120 || image.Height!=60)throw new InvalidOperationException("验证码图片尺寸不符。");
        var source=new byte[120*60*3]; int pale=0,dark=0;
        for(int y=0;y<60;y++)for(int x=0;x<120;x++)
        {
            var c=image.GetPixel(x,y);int i=(y*120+x)*3;source[i]=c.R;source[i+1]=c.G;source[i+2]=c.B;
            if(c.R>150&&c.G>150&&c.B>150)pale++;if(c.R<120&&c.G<120&&c.B<120)dark++;
        }
        if(pale<2000 || dark<50)throw new InvalidOperationException("验证码图片为空或尚未加载。");
        var horizontal=Resize(source,120,60,128,true);var pixels=Resize(horizontal,128,60,32,false);
        var result=new float[3*32*128];
        for(int i=0;i<4096;i++)for(int c=0;c<3;c++)result[c*4096+i]=(pixels[i*3+c]/255f-.5f)/.5f;
        return result;
    }
    // Pillow's separable, antialiased bicubic filter with 22-bit coefficients and 8-bit intermediate pixels.
    private static byte[] Resize(byte[] source,int width,int height,int size,bool horizontal)
    {
        int input=horizontal?width:height,other=horizontal?height:width;
        double scale=(double)input/size,filter=Math.Max(scale,1),support=2*filter;
        var output=new byte[size*other*3];
        for(int target=0;target<size;target++)
        {
            double center=(target+.5)*scale;int min=Math.Max(0,(int)(center-support+.5)),max=Math.Min(input,(int)(center+support+.5));
            var weights=new double[max-min];double sum=0;
            for(int i=min;i<max;i++){double x=Math.Abs((i-center+.5)/filter);double w=x<1?((1.5*x-2.5)*x)*x+1:x<2?((-.5*x+2.5)*x-4)*x+2:0;weights[i-min]=w;sum+=w;}
            var coefficients=weights.Select(w=>(int)(w/sum*(1<<22)+(w<0?-.5:.5))).ToArray();
            for(int line=0;line<other;line++)for(int c=0;c<3;c++)
            {
                int value=1<<21;for(int i=min;i<max;i++)value+=source[(horizontal?line*width+i:i*width+line)*3+c]*coefficients[i-min];
                output[(horizontal?line*size+target:target*width+line)*3+c]=(byte)Math.Clamp(value>>22,0,255);
            }
        }
        return output;
    }
    public void Dispose()=>_session.Dispose();
}
