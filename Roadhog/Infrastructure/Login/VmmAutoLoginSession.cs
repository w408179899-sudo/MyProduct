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
    private readonly UuLaunchFallback _uuFallback = new();
    private readonly LoginOperationExecutor _reads = new();
    private readonly LoginOperationExecutor _inputOperations = new();
    private OnnxCaptchaRecognizer? _recognizer;
    private bool _inputUsed;
    private bool _awaitingClickOutcome;
    private int _captchaRound;

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
        _reads.RunAsync(readToken=>_publication.Capture(lifecycle=>api.WithLoginConnection(account.VmmDeviceName,vmm=>
            {
                var key=LoginNativeReader.SessionKey(vmm,account);
                lifecycle(key);
                LoginObservation Capture() => _uuFallback.Capture(key,
                    () => LoginNativeReader.Capture(vmm,account,key),
                    () => UuLaunchFallbackWindow.Capture(vmm,key),readToken);
                var observed=Capture();
                var verify=Capture();
                if(observed.Snapshot!=verify.Snapshot||observed.Window!=verify.Window||observed.Bounds!=verify.Bounds||key!=LoginNativeReader.SessionKey(vmm,account))
                    throw new InvalidOperationException("Login scene changed during capture.");
                if(observed.Snapshot.Action==LoginAction.SolveCaptcha)
                    observed=observed with{Snapshot=observed.Snapshot with{ActionIdentity=observed.Snapshot.ActionIdentity+":"+_captchaRound}};
                return observed;
            }),bindInputTarget,readToken),"页面读取",token);

    private async Task<LoginObservation> BindInputAsync(CancellationToken token)
    {
        LoginObservation? target=null;
        await CaptureAsync(token,value=>target=value).ConfigureAwait(false);
        return target??throw new InvalidOperationException("等待登录输入目标重新绑定。");
    }

    public async Task<bool> ExecuteAsync(LoginSnapshot expected,LoginAction action,CancellationToken token)
    {
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
            await _reads.RunAsync(_=>_recognizer??=new OnnxCaptchaRecognizer(Path.Combine(AppContext.BaseDirectory,"Source","Login")),"验证码模型加载",token).ConfigureAwait(false);
        return await _recovery.ExecuteAsync(()=>ExecuteInputAsync(expected,action,password,token),
            releaseToken=>_inputUsed?ReleaseAllAsync(releaseToken):Task.FromResult(OperationResult.Ok()),token,
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
            if(!await ReadAsync(vmm=>SessionInputProbe.Foreground(vmm,current.SessionPid).GetProperty("Name").GetString()!.Equals("explorer.exe",StringComparison.OrdinalIgnoreCase),token).ConfigureAwait(false))
                throw new InvalidOperationException("未能显示目标电脑桌面，未点击图标。");
            await MoveAsync(current.SessionPid,new(35,35),token).ConfigureAwait(false);
            if(!await ReadAsync(vmm=>SessionInputProbe.Foreground(vmm,current.SessionPid).GetProperty("Name").GetString()!.Equals("explorer.exe",StringComparison.OrdinalIgnoreCase),token).ConfigureAwait(false))
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
            var verified=await ReadAsync(vmm=>LoginNativeReader.Json(SdoUiProbe.Run(vmm.GetProcessByPID(current.Pid),true,account.GameLoginAccount,password!.Length)),token).ConfigureAwait(false);
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

    private Task<T> ReadAsync<T>(Func<Vmmsharp.Vmm,T> read,CancellationToken token) =>
        _reads.RunAsync(_=>api.WithLoginConnection(account.VmmDeviceName,read),"目标读取",token);
    private Task<T> ReadAsync<T>(Func<Vmmsharp.Vmm,CancellationToken,T> read,CancellationToken token) =>
        _reads.RunAsync(t=>api.WithLoginConnection(account.VmmDeviceName,vmm=>read(vmm,t)),"验证码读取",token);
    private Task<OperationResult> InputAsync(Func<CancellationToken,Task<OperationResult>> send,CancellationToken token) =>
        _inputOperations.RunTaskAsync(send,"设备输入",token);
    internal Task FocusAsync(LoginObservation observation,CancellationToken token)=>LoginWindowFocus.EnsureAsync(observation.Pid,
        ()=>ReadAsync(vmm=>SessionInputProbe.Foreground(vmm,observation.SessionPid).GetProperty("Pid").GetUInt32(),token),
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
            var position=await ReadAsync(vmm=>
            {
                var kernel=SessionInputProbe.SessionProcess(vmm,sessionPid);
                ulong symbol=SessionSymbolResolver.Resolve(vmm,sessionPid).Cursor;
                var bytes=LoginNativeReader.Exact(kernel,symbol,8);return new Point(BitConverter.ToInt32(bytes),BitConverter.ToInt32(bytes,4));
            },token).ConfigureAwait(false);
            int dx=target.X-position.X,dy=target.Y-position.Y;
            if(Math.Abs(dx)<=2&&Math.Abs(dy)<=2)return;
            _inputUsed=true;Check(await InputAsync(t=>input.MoveMouseRelativeAsync(dx,dy,t),token).ConfigureAwait(false));
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
        if(await ReadAsync(vmm=>SessionInputProbe.Foreground(vmm,expected.SessionPid).GetProperty("Pid").GetUInt32(),token).ConfigureAwait(false)!=expected.Pid)
            throw new InvalidOperationException("登录窗口失去焦点，已停止输入。");
        bool finalClick=controlName is null&&(expected.Snapshot.Action is LoginAction.LaunchGame or LoginAction.StartLauncher or LoginAction.Reaccelerate
            or LoginAction.SelectServer or LoginAction.EnterCharacter or LoginAction.ConfirmEntryAgreement or LoginAction.ConfirmGameExit)
            ||controlName=="LoginBtn"&&expected.Snapshot.Action==LoginAction.SubmitCredentials;
        if (UuLaunchFallback.IsFallback(expected.Snapshot))
        {
            if (!_uuFallback.TryReserveAttempt(token)) throw new InvalidOperationException("UU 固定位置启动已尝试，等待启动结果。");
            // A lost acknowledgement must not cause another fixed click.
            _awaitingClickOutcome=true;
            logger?.Warn("login.uu_fallback.click_attempt",new Dictionary<string,object?>
            { ["account"]=account.AccountName,["pid"]=expected.Pid,["window"]=expected.Window,
                ["x"]=point.X,["y"]=point.Y,["readFailureSeconds"]=30 });
        }
        else if (expected.Snapshot.Action is LoginAction.LaunchGame or LoginAction.StartLauncher)
            _uuFallback.SuppressAfterNormalLaunch();
        await ClickAsync(token,finalClick?()=>_awaitingClickOutcome=true:null).ConfigureAwait(false);
    }
    private async Task ClickAsync(CancellationToken token,Action? pressed=null)
    {
        _inputUsed=true;Check(await InputAsync(t=>input.MouseDownAsync(RoadhogMouseButton.Left,t),token).ConfigureAwait(false));
        try{pressed?.Invoke();await Task.Delay(70,token).ConfigureAwait(false);}
        finally
        {
            using var release=new CancellationTokenSource(TimeSpan.FromSeconds(3));
            Check(await InputAsync(t=>input.MouseUpAsync(RoadhogMouseButton.Left,t),release.Token).ConfigureAwait(false));
        }
    }
    private async Task HotkeyAsync(Keys[] keys,CancellationToken token)
    {_inputUsed=true;Check(await InputAsync(t=>input.LoginHotkeyAsync(keys,t),token).ConfigureAwait(false));}
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
        Check(await InputAsync(t=>input.PressKeyAsync("Back",TimeSpan.FromMilliseconds(50),t),token).ConfigureAwait(false));
        await ValidateTextTargetAsync(observation,name,token).ConfigureAwait(false);
        Check(await InputAsync(t=>input.TypeLoginTextAsync(value,t),token).ConfigureAwait(false));
        await Task.Delay(150,token).ConfigureAwait(false);
    }
    private async Task ValidateTextTargetAsync(LoginObservation expected,string name,CancellationToken token)
    {
        var now=await BindInputAsync(token).ConfigureAwait(false);
        LoginInputTarget.Validate(expected,now,compareAction:true);
        if(!ActiveSdoControls(now.Ui,now.Window).Any(c=>LoginNativeReader.Text(c,"Name")==name)
            ||await ReadAsync(vmm=>SessionInputProbe.Foreground(vmm,now.SessionPid).GetProperty("Pid").GetUInt32(),token).ConfigureAwait(false)!=now.Pid)
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
        Trace("waiting_initial_image",0);
        var before=await CaptchaImageWaiter.WaitAsync(t=>ReadAsync((vmm,readToken)=>
        {
            var candidates=CaptchaMemoryReader.Discover(vmm.GetProcessByPID(observation.Pid),readToken);
            return candidates.Values.Any(IsCaptchaImageReady)?candidates:null;
        },t),token).ConfigureAwait(false)
            ??throw new TimeoutException("验证码图片尚未加载完成，等待超时，已停止本次登录输入。");
        Trace("discovered",0,("candidateCount",before.Count));
        // Bind by a positive change at an already observed encoded buffer, never by an arbitrary old image.
        for(int attempt=0;attempt<8;attempt++)
        {
            token.ThrowIfCancellationRequested();
            Trace("refresh",attempt+1,("candidateCount",before.Count));
            await ClickSdoAsync(observation,"ChangeCodeBtn",token).ConfigureAwait(false);
            await Task.Delay(1300,token).ConfigureAwait(false);
            Dictionary<ulong,CaptchaImage> after=before;
            var changed=await CaptchaImageWaiter.WaitAsync(t=>ReadAsync(vmm=>
            {
                var merged=new Dictionary<ulong,CaptchaImage>(before);
                foreach(var address in before.Keys)
                {
                    var image=CaptchaMemoryReader.Read(vmm.GetProcessByPID(observation.Pid),address);
                    if(image is not null)merged[address]=image;
                }
                after=merged;
                var candidate=CaptchaMemoryReader.Changed(before,merged.Values);
                return candidate is not null&&IsCaptchaImageReady(candidate)?candidate:null;
            },t),token).ConfigureAwait(false);
            if(changed is null)
            {
                var discovered=await ReadAsync((vmm,readToken)=>CaptchaMemoryReader.Discover(vmm.GetProcessByPID(observation.Pid),readToken),token).ConfigureAwait(false);
                Trace("image_not_bound",attempt+1,("candidateCount",before.Count),("discoveredCount",discovered.Count),
                    ("knownChangedHashes",after.Values.Where(image=>before.TryGetValue(image.Address,out var old)&&old.Hash!=image.Hash).Select(image=>image.Hash).Distinct().Count()),
                    ("newAddresses",discovered.Keys.Count(address=>!before.ContainsKey(address))));
                before=discovered;continue;
            }
            before=after;
            string text;
            try{text=await _reads.RunAsync(_=>_recognizer!.Predict(changed.Png),"验证码识别",token).ConfigureAwait(false);}
            catch(InvalidOperationException ex){Trace("image_rejected",attempt+1,("reason",ex.Message),("imageHash",changed.Hash));continue;}
            if(text.Length!=6){Trace("recognition_rejected",attempt+1,("length",text.Length),("imageHash",changed.Hash));continue;}
            Trace("input",attempt+1,("length",text.Length),("imageHash",changed.Hash));
            await FillSdoAsync(observation,"IdentifyingCodeEdit",text,token).ConfigureAwait(false);
            var still=await ReadAsync(vmm=>CaptchaMemoryReader.Read(vmm.GetProcessByPID(observation.Pid),changed.Address),token).ConfigureAwait(false);
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
        Check(await ReleaseAllAsync(CancellationToken.None).ConfigureAwait(false));
    }
    private async Task<OperationResult> ReleaseAllAsync(CancellationToken token)
    {
        // Release is allowed even after an input timeout, but can never stall shutdown.
        var release=new LoginOperationExecutor(TimeSpan.FromSeconds(3));
        return await release.RunTaskAsync(input.ReleaseAllAsync,"释放输入",token).ConfigureAwait(false);
    }
    private static bool IsCaptchaImageReady(CaptchaImage image)
    {
        try { _=OnnxCaptchaRecognizer.Preprocess(image.Png);return true; }
        catch(InvalidOperationException) { return false; }
    }
    public ValueTask DisposeAsync()
    {
        _publication.Dispose();
        _reads.Close(()=>_recognizer?.Dispose());
        _inputOperations.Close(()=>{});
        return ValueTask.CompletedTask;
    }
}
