using Roadhog.Application.Licensing;
using Roadhog.Core.Licensing;

namespace Roadhog;

internal sealed class LicenseActivationForm : Form
{
    private readonly LicenseCoordinator _coordinator;
    private readonly RoundedTextBox _cdkeyInput;
    private readonly Label _statusLabel;
    private readonly RoundedButton _activateButton;
    private readonly RoundedButton _closeButton;
    private readonly CancellationTokenSource _closingCancellation = new();

    public LicenseActivationForm(LicenseCoordinator coordinator, LicenseRuntimeState initialState)
    {
        _coordinator = coordinator;

        Text = "Roadhog 授权";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(500, 206);
        BackColor = Color.FromArgb(248, 253, 250);
        Font = new Font("Microsoft YaHei UI", 9F);

        var panel = new RoundedPanel
        {
            BackColor = Color.White,
            BorderColor = Color.FromArgb(187, 247, 208),
            CornerRadius = 8,
            ShadowDepth = 3,
            Location = new Point(16, 16),
            Size = new Size(468, 174)
        };

        var titleLabel = new Label
        {
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold),
            ForeColor = Color.FromArgb(20, 83, 45),
            Location = new Point(20, 18),
            Text = "客户端激活"
        };

        var cdkeyLabel = new Label
        {
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
            ForeColor = Color.FromArgb(22, 101, 52),
            Location = new Point(20, 57),
            Text = "CDKEY"
        };

        _cdkeyInput = new RoundedTextBox
        {
            BackColor = Color.FromArgb(229, 245, 235),
            BorderColor = Color.FromArgb(134, 239, 172),
            CornerRadius = 7,
            Font = new Font("Consolas", 10F, FontStyle.Bold),
            ForeColor = Color.FromArgb(20, 83, 45),
            Location = new Point(82, 49),
            Size = new Size(362, 32)
        };

        _statusLabel = new Label
        {
            AutoEllipsis = true,
            Font = new Font("Microsoft YaHei UI", 8.5F),
            ForeColor = Color.FromArgb(166, 40, 40),
            Location = new Point(20, 91),
            Size = new Size(424, 22),
            Text = initialState.Kind == LicenseRuntimeStateKind.ActivationRequired
                ? LicenseUiText.Describe(initialState)
                : string.Empty
        };

        _activateButton = new RoundedButton
        {
            BackColor = Color.FromArgb(22, 163, 74),
            BorderColor = Color.FromArgb(21, 128, 61),
            CornerRadius = 7,
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
            ForeColor = Color.White,
            Location = new Point(248, 124),
            Size = new Size(94, 32),
            Text = "激活"
        };
        _activateButton.Click += ActivateButton_Click;

        _closeButton = new RoundedButton
        {
            BackColor = Color.FromArgb(107, 114, 128),
            BorderColor = Color.FromArgb(75, 85, 99),
            CornerRadius = 7,
            DialogResult = DialogResult.Cancel,
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
            ForeColor = Color.White,
            Location = new Point(350, 124),
            Size = new Size(94, 32),
            Text = "关闭"
        };

        panel.Controls.Add(titleLabel);
        panel.Controls.Add(cdkeyLabel);
        panel.Controls.Add(_cdkeyInput);
        panel.Controls.Add(_statusLabel);
        panel.Controls.Add(_activateButton);
        panel.Controls.Add(_closeButton);
        Controls.Add(panel);

        AcceptButton = _activateButton;
        CancelButton = _closeButton;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _closingCancellation.Cancel();
        _closingCancellation.Dispose();
        base.OnFormClosed(e);
    }

    private async void ActivateButton_Click(object? sender, EventArgs e)
    {
        var cdkey = LicenseCredential.NormalizeCdkey(_cdkeyInput.Text);
        if (!LicenseCredential.IsValidCdkey(cdkey))
        {
            _statusLabel.Text = LicenseUiText.DescribeError("INVALID_CDKEY_FORMAT");
            return;
        }

        SetBusy(true);
        _statusLabel.ForeColor = Color.FromArgb(22, 101, 52);
        _statusLabel.Text = "正在验证...";
        try
        {
            var state = await _coordinator
                .ActivateAsync(cdkey, _closingCancellation.Token)
                .ConfigureAwait(true);
            if (state.IsAuthorized)
            {
                DialogResult = DialogResult.OK;
                Close();
                return;
            }

            _statusLabel.ForeColor = Color.FromArgb(166, 40, 40);
            _statusLabel.Text = LicenseUiText.Describe(state);
        }
        catch (OperationCanceledException) when (_closingCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            if (!IsDisposed)
            {
                SetBusy(false);
            }
        }
    }

    private void SetBusy(bool busy)
    {
        _cdkeyInput.Enabled = !busy;
        _activateButton.Enabled = !busy;
        _closeButton.Enabled = !busy;
    }
}
