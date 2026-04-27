using System.Windows.Forms;

namespace EveSessionTracker;

public class TaxRateDialog : Form
{
    private readonly NumericUpDown _numTaxRate;
    private readonly Button _btnOK;
    private readonly Button _btnCancel;

    public double TaxRatePercent => (double)_numTaxRate.Value;

    public TaxRateDialog(string characterName, double currentRate = 10.0)
    {
        Text = "Set Corporation Tax Rate";
        Size = new System.Drawing.Size(400, 180);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = System.Drawing.Color.FromArgb(18, 21, 26);

        var lblPrompt = new Label
        {
            Text = $"Enter corporation tax rate for {characterName}:\n(You can change this later in the Characters tab)",
            Left = 20,
            Top = 20,
            Width = 360,
            Height = 40,
            ForeColor = System.Drawing.Color.FromArgb(200, 206, 215),
            Font = new System.Drawing.Font("Segoe UI", 9f)
        };
        Controls.Add(lblPrompt);

        var lblPercent = new Label
        {
            Text = "Tax Rate (%):",
            Left = 20,
            Top = 60,
            Width = 120,
            ForeColor = System.Drawing.Color.FromArgb(200, 206, 215),
            Font = new System.Drawing.Font("Segoe UI", 9f)
        };
        Controls.Add(lblPercent);

        _numTaxRate = new NumericUpDown
        {
            Left = 140,
            Top = 58,
            Width = 100,
            Value = (decimal)currentRate,
            DecimalPlaces = 1,
            Increment = 0.5M,
            Minimum = 0M,
            Maximum = 100M,
            BackColor = System.Drawing.Color.FromArgb(30, 35, 42),
            ForeColor = System.Drawing.Color.FromArgb(200, 206, 215),
            Font = new System.Drawing.Font("Segoe UI", 10f)
        };
        Controls.Add(_numTaxRate);

        _btnOK = new Button
        {
            Text = "OK",
            Left = 200,
            Top = 100,
            Width = 80,
            Height = 32,
            BackColor = System.Drawing.Color.FromArgb(42, 130, 218),
            ForeColor = System.Drawing.Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new System.Drawing.Font("Segoe UI", 9f, System.Drawing.FontStyle.Bold),
            DialogResult = DialogResult.OK
        };
        _btnOK.FlatAppearance.BorderSize = 0;
        Controls.Add(_btnOK);

        _btnCancel = new Button
        {
            Text = "Cancel",
            Left = 290,
            Top = 100,
            Width = 80,
            Height = 32,
            BackColor = System.Drawing.Color.FromArgb(50, 55, 62),
            ForeColor = System.Drawing.Color.FromArgb(200, 206, 215),
            FlatStyle = FlatStyle.Flat,
            Font = new System.Drawing.Font("Segoe UI", 9f),
            DialogResult = DialogResult.Cancel
        };
        _btnCancel.FlatAppearance.BorderSize = 0;
        Controls.Add(_btnCancel);

        AcceptButton = _btnOK;
        CancelButton = _btnCancel;
    }
}
