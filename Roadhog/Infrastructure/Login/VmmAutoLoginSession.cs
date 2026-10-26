using System.Text.Json;
using Roadhog.Application.Login;
using Roadhog.Core.Accounts;
using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Input;
using Roadhog.Infrastructure.Input;
using Roadhog.Infrastructure.Login.Native;
using Roadhog.Infrastructure.Vmm;

namespace Roadhog.Infrastructure.Login;

internal sealed class VmmAutoLoginSession(AionVmmGameApi api, KmBoxNetKeyboardInput input, AccountConfig account,
    IRoadhogLogger? logger = null) : IAutoLoginSession
{
    private readonly LoginSnapshotPublisher _publication = new(new(account.AccountName,0,"",account.VmmDeviceName,true),logger);
    private readonly LoginInputRecovery _recovery = new(account.AccountName,logger);
    private OnnxCaptchaRecognizer? _recognizer;
    private bool _inputUsed;
    private bool _awaitingClickOutcome;
    private int _captchaRound;
    private CancellationToken _operationToken;

    public async Task<LoginSnapshot> ReadAsync(CancellationToken cancellationToken) =>
        (await ObserveAsync(cancellationToken).ConfigureAwait(false)).Snapshot;

    private async Task<LoginObservation> ObserveAsync(CancellationToken token)
    {
        while(true)
        {
            token.ThrowIfCancellationRequested();
            var value=await CaptureAsync(token).ConfigureAwait(false);
            if(value is not null)return value;
            await Task.Delay(300,token).ConfigureAwait(false);
        }
    }

    // Both reads and input binding use the same capture/validate/publish pipeline.
    // A failed attempt still returns the official scene to readers, but cannot bind input.
    private Task<LoginObservation?> CaptureAsync(CancellationToken token,Action<LoginObservation>? bindInputTarget=null) =>
        Task.Run(()=>_publication.Capture(lifecycle=>api.WithLoginConnection(account.VmmDeviceName,vmm=>
            {
                var key=LoginNativeReader.SessionKey(vmm,account);
                lifecycle(key);
                var observed=LoginNativeReader.Capture(vmm,account,key);
                var verify=LoginNativeReader.Capture(vmm,account,key);
                if(observed.Snapshot!=verify.Snapshot||observed.Window!=verify.Window||observed.Bounds!=verify.Bounds||key!=LoginNativeReader.SessionKey(vmm,account))
                    throw new InvalidOperationException("Login scene changed during capture.");
                if(observed.Snapshot.Action==LoginAction.SolveCaptcha)
                    observed=observed with{Snapshot=observed.Snapshot with{ActionIdentity=observed.Snapshot.ActionIdentity+":"+_captchaRound}};
                return observed;
            }),bindInputTarget),token);

    private async Task<LoginObservation> BindInputAsync(CancellationToken token)
    {
        LoginObservation? target=null;
        await CaptureAsync(token,value=>target=value).ConfigureAwait(false);
        return target??throw new InvalidOperationException("等待登录输入目标重新绑定。");
    }

    public async Task<bool> ExecuteAsync(LoginSnapshot expected,LoginAction action,CancellationToken token)
    {
        _operationToken=token;
        _awaitingClickOutcome=false;
        // Configuration/model errors are terminal and are deliberately outside input recovery.
        string? password=null;
        if(action==LoginAction.SubmitCredentials)
        {
            if(string.IsNullOrWhiteSpace(account.GameLoginAccount)||string.IsNullOrWhiteSpace(account.ProtectedGameLoginPassword))
                throw new InvalidOperationException("请在设备/角色中填写游戏账号、密码并保存。");
            password=LoginPasswordProtector.Unprotect(account.ProtectedGameLoginPassword);
            if(password.Length==0||!account.GameLoginAccount.All(c=>c>=32&&c<=126)||!password.All(c=>c>=32&&c<=126))
                throw new InvalidOperationException("登录账号或密码为空，或包含当前输入设备不支持的字符。");
        }
        if(action==LoginAction.SolveCaptcha)
            _recognizer??=new OnnxCaptchaRecognizer(Path.Combine(AppContext.BaseDirectory,"Source","Login"));
        return await _recovery.ExecuteAsync(()=>ExecuteInputAsync(expected,action,password,token),
            releaseToken=>_inputUsed?input.ReleaseAllAsync(releaseToken):Task.FromResult(OperationResult.Ok()),token,
            ()=>_awaitingClickOutcome).ConfigureAwait(false);
    }

    private async Task<bool> ExecuteInputAsync(LoginSnapshot expected,LoginAction action,string? password,CancellationToken token)
    {
        var current=await BindInputAsync(token).ConfigureAwait(false);
        if(current.Snapshot!=expected)return false;
        if(action==LoginAction.OpenAccelerator)
        {
            await HotkeyAsync([Keys.LWin,Keys.M],token).ConfigureAwait(false);
            await Task.Delay(600,token).ConfigureAwait(false);
            if(!Read(vmm=>SessionInputProbe.Foreground(vmm,current.SessionPid).GetProperty("Name").GetString()!.Equals("explorer.exe",StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("未能显示目标电脑桌面，未点击图标。");
            await MoveAsync(current.SessionPid,new(35,35),token).ConfigureAwait(false);
            if(!Read(vmm=>SessionInputProbe.Foreground(vmm,current.SessionPid).GetProperty("Name").GetString()!.Equals("explorer.exe",StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("目标桌面已失去焦点，未点击图标。");
            LoginInputTarget.Validate(current,await BindInputAsync(token).ConfigureAwait(false));
            await ClickAsync(token).ConfigureAwait(false);await Task.Delay(100,token).ConfigureAwait(false);await ClickAsync(token).ConfigureAwait(false);
            await Task.Delay(2000,token).ConfigureAwait(false);return true;
        }
        await FocusAsync(current,token).ConfigureAwait(false);
        if(action==LoginAction.SubmitCredentials)
        {
            await FillSdoAsync(current,"AccountCombox",account.GameLoginAccount,token).ConfigureAwait(false);
            await FillSdoAsync(current,"PasswordEdit",password!,token).ConfigureAwait(false);
            var verified=Read(vmm=>LoginNativeReader.Json(SdoUiProbe.Run(vmm.GetProcessByPID(current.Pid),true,account.GameLoginAccount,password!.Length)));
            var controls=ActiveSdoControls(verified,current.Window);
            if(!LoginNativeReader.Flag(controls.Single(c=>LoginNativeReader.Text(c,"Name")=="AccountCombox"),"MatchesExpectedInputIgnoringCase")
                ||!LoginNativeReader.Flag(controls.Single(c=>LoginNativeReader.Text(c,"Name")=="PasswordEdit"),"MatchesExpectedLength"))
                throw new InvalidOperationException("输入框内容未确认，未提交登录。");
            await ClickSdoAsync(current,"LoginBtn",token).ConfigureAwait(false);return true;
        }
        if(action==LoginAction.SolveCaptcha){await SolveCaptchaAsync(current,token).ConfigureAwait(false);return true;}
        await ClickTargetAsync(current,current.Point,token).ConfigureAwait(false);
        return true;
    }

    private T Read<T>(Func<Vmmsharp.Vmm,T> read)
    {
        _operationToken.ThrowIfCancellationRequested();
        // The attempt boundary retries from the current scene, never from an old PID/address.
        return api.WithLoginConnection(account.VmmDeviceName,read);
    }
    internal Task FocusAsync(LoginObservation observation,CancellationToken token)=>LoginWindowFocus.EnsureAsync(observation.Pid,
        ()=>Read(vmm=>SessionInputProbe.Foreground(vmm,observation.SessionPid).GetProperty("Pid").GetUInt32()),
        async()=>LoginInputTarget.Validate(observation,await BindInputAsync(token).ConfigureAwait(false)),
        HotkeyAsync,t=>Task.Delay(250,t),
        (attempt,foreground,method)=>logger?.Info("login.focus.switch",new Dictionary<string,object?>
        { ["account"]=account.AccountName,["targetPid"]=observation.Pid,["foregroundPid"]=foreground,
            ["window"]=observation.Window,["attempt"]=attempt,["method"]=method }),token);
    private async Task MoveAsync(uint sessionPid,Point target,CancellationToken token)
    {
        if(target.X<0||target.Y<0||target.X>4096||target.Y>2160)throw new InvalidOperationException("登录坐标超出已支持的桌面范围。");
        for(int i=0;i<16;i++)
        {
            token.ThrowIfCancellationRequested();
            var position=Read(vmm=>
            {
                var kernel=vmm.GetProcessByPID(sessionPid|0x80000000);var module=kernel.MapModuleFromName("win32kbase.sys");
                if(!kernel.Pdb(module.vaBase).SymbolAddress("gptCursorAsync",out var symbol)||symbol<module.vaBase||symbol>=module.vaBase+module.cbImageSize)
                    throw new InvalidOperationException("目标桌面鼠标位置无法定位。");
                var bytes=LoginNativeReader.Exact(kernel,symbol,8);return new Point(BitConverter.ToInt32(bytes),BitConverter.ToInt32(bytes,4));
            });
            int dx=target.X-position.X,dy=target.Y-position.Y;
            if(Math.Abs(dx)<=2&&Math.Abs(dy)<=2)return;
            _inputUsed=true;Check(await input.MoveMouseRelativeAsync(dx,dy,token).ConfigureAwait(false));
            await Task.Delay(120,token).ConfigureAwait(false);
        }
        throw new InvalidOperationException("鼠标未到达登录按钮，已停止输入。");
    }
    private async Task ClickTargetAsync(LoginObservation expected,Point point,CancellationToken token,string? controlName=null)
    {
        await MoveAsync(expected.SessionPid,point,token).ConfigureAwait(false);
        var now=await BindInputAsync(token).ConfigureAwait(false);
        LoginInputTarget.Validate(expected,now,compareAction:true);
        if(controlName is not null)
        {
            var control=ActiveSdoControls(now.Ui,now.Window).Single(c=>LoginNativeReader.Text(c,"Name")==controlName);
            var rect=control.GetProperty("Rect");
            var target=new Point(now.Bounds.Left+(rect[0].GetInt32()+rect[2].GetInt32())/2,now.Bounds.Top+(rect[1].GetInt32()+rect[3].GetInt32())/2);
            if(target!=point)throw new InvalidOperationException("登录控件位置已改变，未点击旧坐标。");
        }
        if(Read(vmm=>SessionInputProbe.Foreground(vmm,expected.SessionPid).GetProperty("Pid").GetUInt32())!=expected.Pid)
            throw new InvalidOperationException("登录窗口失去焦点，已停止输入。");
        bool finalClick=controlName is null&&(expected.Snapshot.Action is LoginAction.LaunchGame or LoginAction.StartLauncher or LoginAction.Reaccelerate
            or LoginAction.SelectServer or LoginAction.EnterCharacter or LoginAction.ConfirmEntryAgreement or LoginAction.ConfirmGameExit)
            ||controlName=="LoginBtn"&&expected.Snapshot.Action==LoginAction.SubmitCredentials;
        await ClickAsync(token,finalClick?()=>_awaitingClickOutcome=true:null).ConfigureAwait(false);
    }
    private async Task ClickAsync(CancellationToken token,Action? pressed=null)
    {
        _inputUsed=true;Check(await input.MouseDownAsync(RoadhogMouseButton.Left,token).ConfigureAwait(false));
        try{pressed?.Invoke();await Task.Delay(70,token).ConfigureAwait(false);}
        finally{Check(await input.MouseUpAsync(RoadhogMouseButton.Left,CancellationToken.None).ConfigureAwait(false));}
    }
    private async Task HotkeyAsync(Keys[] keys,CancellationToken token)
    {_inputUsed=true;Check(await input.LoginHotkeyAsync(keys,token).ConfigureAwait(false));}
    private static void Check(OperationResult result){if(!result.Success)throw new InvalidOperationException(result.Error);}
    private static JsonElement[] ActiveSdoControls(JsonElement ui,string hwnd)=>ui.GetProperty("Windows").EnumerateArray()
        .Single(w=>LoginNativeReader.Text(w.GetProperty("Window"),"Hwnd")==hwnd).GetProperty("Managers").EnumerateArray()
        .SelectMany(m=>m.GetProperty("Controls").EnumerateArray()).Where(c=>LoginNativeReader.Flag(c,"VisibleThroughRoot")&&LoginNativeReader.Flag(c,"EnabledThroughRoot")).ToArray();
    private async Task ClickSdoAsync(LoginObservation observation,string name,CancellationToken token)
    {
        var now=await BindInputAsync(token).ConfigureAwait(false);
        LoginInputTarget.Validate(observation,now,compareAction:true);
        var c=ActiveSdoControls(now.Ui,now.Window).Single(c=>LoginNativeReader.Text(c,"Name")==name);var r=c.GetProperty("Rect");
        var point=new Point(now.Bounds.Left+(r[0].GetInt32()+r[2].GetInt32())/2,now.Bounds.Top+(r[1].GetInt32()+r[3].GetInt32())/2);
        await ClickTargetAsync(now,point,token,name).ConfigureAwait(false);
    }
    private async Task FillSdoAsync(LoginObservation observation,string name,string value,CancellationToken token)
    {
        await ClickSdoAsync(observation,name,token).ConfigureAwait(false);
        await ValidateTextTargetAsync(observation,name,token).ConfigureAwait(false);
        await HotkeyAsync([Keys.ControlKey,Keys.A],token).ConfigureAwait(false);
        await ValidateTextTargetAsync(observation,name,token).ConfigureAwait(false);
        Check(await input.PressKeyAsync("Back",TimeSpan.FromMilliseconds(50),token).ConfigureAwait(false));
        await ValidateTextTargetAsync(observation,name,token).ConfigureAwait(false);
        Check(await input.TypeLoginTextAsync(value,token).ConfigureAwait(false));
        await Task.Delay(150,token).ConfigureAwait(false);
    }
    private async Task ValidateTextTargetAsync(LoginObservation expected,string name,CancellationToken token)
    {
        var now=await BindInputAsync(token).ConfigureAwait(false);
        LoginInputTarget.Validate(expected,now,compareAction:true);
        if(!ActiveSdoControls(now.Ui,now.Window).Any(c=>LoginNativeReader.Text(c,"Name")==name)
            ||Read(vmm=>SessionInputProbe.Foreground(vmm,now.SessionPid).GetProperty("Pid").GetUInt32())!=now.Pid)
            throw new InvalidOperationException("登录输入页面或焦点已变化，重新定位输入框。");
    }
    private async Task SolveCaptchaAsync(LoginObservation observation,CancellationToken token)
    {
        void Trace(string phase,int attempt,params (string Key,object? Value)[] values)
        {
            var fields=new Dictionary<string,object?> { ["account"]=account.AccountName,["attempt"]=attempt };
            foreach(var value in values)fields[value.Key]=value.Value;
            logger?.Info("login.captcha."+phase,fields);
        }
        var before=Read(vmm=>CaptchaMemoryReader.Discover(vmm.GetProcessByPID(observation.Pid),token));
        Trace("discovered",0,("candidateCount",before.Count));
        // Bind by a positive change at an already observed encoded buffer, never by an arbitrary old image.
        for(int attempt=0;attempt<8;attempt++)
        {
            token.ThrowIfCancellationRequested();
            Trace("refresh",attempt+1,("candidateCount",before.Count));
            await ClickSdoAsync(observation,"ChangeCodeBtn",token).ConfigureAwait(false);
            await Task.Delay(1300,token).ConfigureAwait(false);
            var after=Read(vmm=>
            {
                var merged=new Dictionary<ulong,CaptchaImage>(before);
                foreach(var address in before.Keys)
                {
                    var image=CaptchaMemoryReader.Read(vmm.GetProcessByPID(observation.Pid),address);
                    if(image is not null)merged[address]=image;
                }
                return merged;
            });
            var changed=CaptchaMemoryReader.Changed(before,after.Values);
            if(changed is null)
            {
                var discovered=Read(vmm=>CaptchaMemoryReader.Discover(vmm.GetProcessByPID(observation.Pid),token));
                Trace("image_not_bound",attempt+1,("candidateCount",before.Count),("discoveredCount",discovered.Count),
                    ("knownChangedHashes",after.Values.Where(image=>before.TryGetValue(image.Address,out var old)&&old.Hash!=image.Hash).Select(image=>image.Hash).Distinct().Count()),
                    ("newAddresses",discovered.Keys.Count(address=>!before.ContainsKey(address))));
                before=discovered;continue;
            }
            before=after;
            string text;
            try{text=_recognizer!.Predict(changed.Png);}
            catch(InvalidOperationException ex){Trace("image_rejected",attempt+1,("reason",ex.Message),("imageHash",changed.Hash));continue;}
            if(text.Length!=6){Trace("recognition_rejected",attempt+1,("length",text.Length),("imageHash",changed.Hash));continue;}
            Trace("input",attempt+1,("length",text.Length),("imageHash",changed.Hash));
            await FillSdoAsync(observation,"IdentifyingCodeEdit",text,token).ConfigureAwait(false);
            var still=Read(vmm=>CaptchaMemoryReader.Read(vmm.GetProcessByPID(observation.Pid),changed.Address));
            if(still?.Hash!=changed.Hash)throw new InvalidOperationException("验证码图片在输入时已刷新，未提交。");
            await ClickSdoAsync(observation,"OkBtn",token).ConfigureAwait(false);
            Trace("submitted",attempt+1,("imageHash",changed.Hash));
            _captchaRound++;
            await Task.Delay(1800,token).ConfigureAwait(false);return;
        }
        // Re-entering password login obtains a new image session instead of retrying black pictures forever.
        Trace("refresh_exhausted",8);
        await ClickSdoAsync(observation,"CancelBtn",token).ConfigureAwait(false);_captchaRound++;
    }
    public async Task ReleaseInputAsync()
    {
        if(!_inputUsed)return;
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try{await input.ReleaseAllAsync(timeout.Token).ConfigureAwait(false);}catch(Exception){ }
    }
    public ValueTask DisposeAsync(){_recognizer?.Dispose();_publication.Dispose();return ValueTask.CompletedTask;}
}
