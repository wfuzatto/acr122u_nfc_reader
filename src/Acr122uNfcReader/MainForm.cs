using System.Text;
using System.Text.Json;
using Acr122uNfcReader.Nfc;
using Acr122uNfcReader.Pcsc;

namespace Acr122uNfcReader;

public sealed class MainForm : Form
{
    private readonly PcscReader _pcsc = new();
    private readonly CardCommands _card;
    private readonly System.Windows.Forms.Timer _pollTimer = new() { Interval = 900 };

    private readonly ComboBox _readers = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 360 };
    private readonly Label _status = new() { AutoSize = true, Text = "Inicializando..." };
    private readonly RichTextBox _details = new() { Dock = DockStyle.Fill, ReadOnly = true, Font = new Font("Consolas", 10f) };

    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect
    };

    private readonly ComboBox _memoryMode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    private readonly NumericUpDown _manualCount = new() { Minimum = 1, Maximum = 256, Value = 64, Width = 80 };
    private readonly TextBox _keyA = new() { Text = "FFFFFFFFFFFF", Width = 125, Font = new Font("Consolas", 9f) };
    private readonly TextBox _keyB = new() { Text = "FFFFFFFFFFFF", Width = 125, Font = new Font("Consolas", 9f) };
    private readonly CheckBox _tryCommonKeys = new() { Text = "Tentar chaves padrão conhecidas", Checked = true, AutoSize = true };
    private readonly Label _dumpProgress = new() { AutoSize = true, Text = "" };

    private readonly NumericUpDown _editBlock = new() { Minimum = 0, Maximum = 255, Width = 80 };
    private readonly ComboBox _editLength = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
    private readonly ComboBox _editKeyType = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
    private readonly TextBox _editKey = new() { Text = "FFFFFFFFFFFF", Width = 180, Font = new Font("Consolas", 10f) };
    private readonly TextBox _editData = new() { Multiline = true, Height = 90, Dock = DockStyle.Top, Font = new Font("Consolas", 11f), ScrollBars = ScrollBars.Vertical };
    private readonly CheckBox _allowBlock0 = new() { Text = "Permitir gravação no bloco 0 / fabricante", AutoSize = true };
    private readonly CheckBox _allowTrailer = new() { Text = "Permitir gravação em Sector Trailer", AutoSize = true };

    private readonly TextBox _apduInput = new() { Text = "FF CA 00 00 00", Dock = DockStyle.Top, Font = new Font("Consolas", 11f) };
    private readonly RichTextBox _apduOutput = new() { Dock = DockStyle.Fill, ReadOnly = true, Font = new Font("Consolas", 10f) };
    private readonly RichTextBox _log = new() { Dock = DockStyle.Fill, ReadOnly = true, Font = new Font("Consolas", 9.5f) };

    private bool _busy;
    private string? _lastUid;

    private sealed record DumpRow(
        int Sector,
        int Block,
        string Kind,
        string Hex,
        string Ascii,
        string Authentication,
        string Access);

    public MainForm()
    {
        _card = new CardCommands(_pcsc);

        Text = "ACR122U NFC Reader";
        Width = 1220;
        Height = 780;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(980, 650);
        Font = new Font("Segoe UI", 9.5f);

        BuildUi();

        Shown += (_, _) =>
        {
            RefreshReaders();
            _pollTimer.Start();
        };

        FormClosed += (_, _) => _pcsc.Dispose();
        _pollTimer.Tick += (_, _) => ProbeCard();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var header = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            Padding = new Padding(10),
            WrapContents = true
        };

        var refreshReaders = new Button { Text = "Atualizar leitores", AutoSize = true };
        refreshReaders.Click += (_, _) => RefreshReaders();

        var readNow = new Button { Text = "Ler cartão agora", AutoSize = true };
        readNow.Click += (_, _) => ProbeCard(force: true);

        header.Controls.Add(new Label { Text = "Leitor:", AutoSize = true, Padding = new Padding(0, 7, 0, 0) });
        header.Controls.Add(_readers);
        header.Controls.Add(refreshReaders);
        header.Controls.Add(readNow);
        header.Controls.Add(new Label { Text = "Status:", AutoSize = true, Padding = new Padding(14, 7, 0, 0) });
        header.Controls.Add(_status);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildCardTab());
        tabs.TabPages.Add(BuildMemoryTab());
        tabs.TabPages.Add(BuildEditorTab());
        tabs.TabPages.Add(BuildApduTab());
        tabs.TabPages.Add(BuildLogTab());

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(tabs, 0, 1);
        Controls.Add(root);
    }

    private TabPage BuildCardTab()
    {
        var page = new TabPage("Cartão");
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8) };

        var copy = new Button { Text = "Copiar informações", AutoSize = true };
        copy.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(_details.Text))
                Clipboard.SetText(_details.Text);
        };

        top.Controls.Add(copy);
        page.Controls.Add(_details);
        page.Controls.Add(top);
        return page;
    }

    private TabPage BuildMemoryTab()
    {
        var page = new TabPage("Memória / Dump");

        _memoryMode.Items.AddRange(
        [
            "MIFARE Classic 1K (64 blocos)",
            "MIFARE Classic 4K (256 blocos)",
            "MIFARE Ultralight (páginas 0-15)",
            "Leitura manual sem autenticação"
        ]);
        _memoryMode.SelectedIndex = 0;

        _grid.Columns.Add("Sector", "Setor");
        _grid.Columns.Add("Block", "Bloco/Página");
        _grid.Columns.Add("Kind", "Tipo");
        _grid.Columns.Add("Hex", "HEX");
        _grid.Columns.Add("Ascii", "ASCII");
        _grid.Columns.Add("Auth", "Autenticação");
        _grid.Columns.Add("Access", "Access bits / GPB");

        var controls = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(8),
            WrapContents = true
        };

        var read = new Button { Text = "Ler memória", AutoSize = true };
        read.Click += async (_, _) => await DumpMemoryAsync();

        var export = new Button { Text = "Exportar JSON", AutoSize = true };
        export.Click += (_, _) => ExportDump();

        controls.Controls.Add(new Label { Text = "Modo:", AutoSize = true, Padding = new Padding(0, 7, 0, 0) });
        controls.Controls.Add(_memoryMode);
        controls.Controls.Add(new Label { Text = "Qtd. manual:", AutoSize = true, Padding = new Padding(10, 7, 0, 0) });
        controls.Controls.Add(_manualCount);
        controls.Controls.Add(new Label { Text = "Key A:", AutoSize = true, Padding = new Padding(10, 7, 0, 0) });
        controls.Controls.Add(_keyA);
        controls.Controls.Add(new Label { Text = "Key B:", AutoSize = true, Padding = new Padding(10, 7, 0, 0) });
        controls.Controls.Add(_keyB);
        controls.Controls.Add(_tryCommonKeys);
        controls.Controls.Add(read);
        controls.Controls.Add(export);
        controls.Controls.Add(_dumpProgress);

        page.Controls.Add(_grid);
        page.Controls.Add(controls);
        return page;
    }

    private TabPage BuildEditorTab()
    {
        var page = new TabPage("Editor");

        _editLength.Items.AddRange(["16 bytes - MIFARE Classic", "4 bytes - MIFARE Ultralight"]);
        _editLength.SelectedIndex = 0;
        _editKeyType.Items.AddRange(["Key A", "Key B"]);
        _editKeyType.SelectedIndex = 0;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(12)
        };

        var line1 = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        line1.Controls.Add(new Label { Text = "Bloco/Página:", AutoSize = true, Padding = new Padding(0, 7, 0, 0) });
        line1.Controls.Add(_editBlock);
        line1.Controls.Add(new Label { Text = "Formato:", AutoSize = true, Padding = new Padding(10, 7, 0, 0) });
        line1.Controls.Add(_editLength);
        line1.Controls.Add(new Label { Text = "Autenticação:", AutoSize = true, Padding = new Padding(10, 7, 0, 0) });
        line1.Controls.Add(_editKeyType);
        line1.Controls.Add(new Label { Text = "Chave:", AutoSize = true, Padding = new Padding(10, 7, 0, 0) });
        line1.Controls.Add(_editKey);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        var read = new Button { Text = "Ler bloco/página", AutoSize = true };
        read.Click += (_, _) => ReadEditorBlock();
        var write = new Button { Text = "GRAVAR", AutoSize = true };
        write.Click += (_, _) => WriteEditorBlock();
        buttons.Controls.Add(read);
        buttons.Controls.Add(write);
        buttons.Controls.Add(_allowBlock0);
        buttons.Controls.Add(_allowTrailer);

        var warning = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(1050, 0),
            Text = "Atenção: gravações são permanentes. Alterar bloco fabricante, UID, chaves ou access bits pode inutilizar o cartão. Sector Trailer e bloco 0 ficam protegidos por padrão."
        };

        layout.Controls.Add(line1);
        layout.Controls.Add(new Label { Text = "Dados HEX:", AutoSize = true });
        layout.Controls.Add(_editData);
        layout.Controls.Add(buttons);
        layout.Controls.Add(warning);

        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildApduTab()
    {
        var page = new TabPage("APDU");

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8) };
        var send = new Button { Text = "Enviar APDU", AutoSize = true };
        send.Click += (_, _) => SendManualApdu();

        var uid = new Button { Text = "Preset: UID", AutoSize = true };
        uid.Click += (_, _) => _apduInput.Text = "FF CA 00 00 00";

        var fw = new Button { Text = "Preset: Firmware", AutoSize = true };
        fw.Click += (_, _) => _apduInput.Text = "FF 00 48 00 00";

        top.Controls.Add(send);
        top.Controls.Add(uid);
        top.Controls.Add(fw);

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        body.Controls.Add(_apduOutput);
        body.Controls.Add(_apduInput);

        page.Controls.Add(body);
        page.Controls.Add(top);
        return page;
    }

    private TabPage BuildLogTab()
    {
        var page = new TabPage("Log");
        page.Controls.Add(_log);
        return page;
    }

    private void RefreshReaders()
    {
        try
        {
            string? selected = _readers.SelectedItem?.ToString();
            var readers = _pcsc.ListReaders();

            _readers.Items.Clear();
            foreach (string reader in readers)
                _readers.Items.Add(reader);

            if (_readers.Items.Count == 0)
            {
                _status.Text = "Nenhum leitor PC/SC encontrado.";
                Log("Nenhum leitor PC/SC encontrado.");
                return;
            }

            int preferred = -1;
            for (int i = 0; i < _readers.Items.Count; i++)
            {
                string name = _readers.Items[i]?.ToString() ?? "";
                if (!string.IsNullOrWhiteSpace(selected) && name.Equals(selected, StringComparison.OrdinalIgnoreCase))
                    preferred = i;
                if (preferred < 0 && name.Contains("ACR122", StringComparison.OrdinalIgnoreCase))
                    preferred = i;
            }

            _readers.SelectedIndex = preferred >= 0 ? preferred : 0;
            _status.Text = $"{_readers.Items.Count} leitor(es) encontrado(s). Aproxime um cartão.";
            Log($"Leitores: {string.Join(" | ", readers)}");
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
            Log(ex.ToString());
        }
    }

    private void ProbeCard(bool force = false)
    {
        if (_busy)
            return;

        string? readerName = _readers.SelectedItem?.ToString();
        if (string.IsNullOrWhiteSpace(readerName))
            return;

        try
        {
            if (!_pcsc.IsConnected || !string.Equals(_pcsc.ConnectedReader, readerName, StringComparison.Ordinal))
                _pcsc.Connect(readerName);

            PcscCardStatus status = _pcsc.GetStatus();
            ApduResult uidResult = _card.GetUid();
            byte[] uid = uidResult.Success ? uidResult.Data : Array.Empty<byte>();
            string uidHex = CardAnalyzer.ToHex(uid, false);

            if (force || uidHex != _lastUid)
            {
                _lastUid = uidHex;
                ShowCardDetails(status, uidResult);
                Log($"Cartão detectado. UID={CardAnalyzer.ToHex(uid)} ATR={CardAnalyzer.ToHex(status.Atr)}");
            }

            _status.Text = uid.Length > 0
                ? $"Cartão presente — UID {CardAnalyzer.ToHex(uid)}"
                : $"Cartão presente — UID indisponível (SW={uidResult.StatusText})";
        }
        catch (PcscException ex)
        {
            if (_pcsc.IsConnected)
                _pcsc.Disconnect();

            if (_lastUid is not null)
                Log("Cartão removido ou conexão perdida.");

            _lastUid = null;
            _status.Text = "Aguardando cartão...";
            if (force)
                Log(ex.Message);
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
            if (force)
                Log(ex.ToString());
        }
    }

    private void ShowCardDetails(PcscCardStatus status, ApduResult uidResult)
    {
        byte[] uid = uidResult.Success ? uidResult.Data : Array.Empty<byte>();
        string firmware;

        try
        {
            firmware = _card.GetFirmwareText();
        }
        catch (Exception ex)
        {
            firmware = $"Indisponível ({ex.Message})";
        }

        var sb = new StringBuilder();
        sb.AppendLine("=== LEITOR ===");
        sb.AppendLine($"Nome           : {status.ReaderName}");
        sb.AppendLine($"Firmware       : {firmware}");
        sb.AppendLine($"Protocolo      : {CardAnalyzer.ProtocolName(status.Protocol)}");
        sb.AppendLine($"Estado PC/SC   : 0x{status.State:X8}");
        sb.AppendLine();
        sb.AppendLine("=== CARTÃO ===");
        sb.AppendLine($"Tipo provável  : {CardAnalyzer.GuessCardType(status.Atr)}");
        sb.AppendLine($"UID HEX        : {(uid.Length > 0 ? CardAnalyzer.ToHex(uid) : "indisponível")}");
        sb.AppendLine($"UID sem espaços: {(uid.Length > 0 ? CardAnalyzer.ToHex(uid, false) : "indisponível")}");
        sb.AppendLine($"UID decimal    : {(uid.Length > 0 ? CardAnalyzer.UidAsDecimal(uid) : "indisponível")}");
        sb.AppendLine($"UID SW1/SW2    : {uidResult.StatusText}");
        sb.AppendLine($"ATR            : {CardAnalyzer.ToHex(status.Atr)}");
        sb.AppendLine($"ATR bytes      : {status.Atr.Length}");
        sb.AppendLine($"Detectado em   : {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
        sb.AppendLine();
        sb.AppendLine("Observação: UID/ATR e tipo de cartão não revelam automaticamente chaves secretas ou conteúdo protegido.");

        _details.Text = sb.ToString();
    }

    private void EnsureCard()
    {
        if (_pcsc.IsConnected)
            return;

        ProbeCard(force: true);

        if (!_pcsc.IsConnected)
            throw new InvalidOperationException("Aproxime um cartão do leitor antes de continuar.");
    }

    private async Task DumpMemoryAsync()
    {
        if (_busy)
            return;

        _busy = true;
        try
        {
            EnsureCard();
            _grid.Rows.Clear();
            _dumpProgress.Text = "Lendo...";

            int mode = _memoryMode.SelectedIndex;
            string keyAText = _keyA.Text;
            string keyBText = _keyB.Text;
            bool common = _tryCommonKeys.Checked;
            int manualCount = (int)_manualCount.Value;

            List<DumpRow> rows = await Task.Run(() =>
            {
                return mode switch
                {
                    0 => DumpClassic(16, keyAText, keyBText, common),
                    1 => DumpClassic(40, keyAText, keyBText, common),
                    2 => DumpUltralight(16),
                    _ => DumpManual(manualCount)
                };
            });

            foreach (DumpRow row in rows)
                _grid.Rows.Add(row.Sector, row.Block, row.Kind, row.Hex, row.Ascii, row.Authentication, row.Access);

            _dumpProgress.Text = $"Concluído: {rows.Count} linha(s).";
            Log($"Dump concluído. Modo={_memoryMode.Text}; linhas={rows.Count}");
        }
        catch (Exception ex)
        {
            _dumpProgress.Text = "Falha.";
            MessageBox.Show(this, ex.Message, "Erro ao ler memória", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Log(ex.ToString());
        }
        finally
        {
            _busy = false;
        }
    }

    private List<DumpRow> DumpClassic(int sectorCount, string keyAText, string keyBText, bool includeCommon)
    {
        var rows = new List<DumpRow>();
        var candidates = BuildKeyCandidates(keyAText, keyBText, includeCommon);

        for (int sector = 0; sector < sectorCount; sector++)
        {
            int first = CardAnalyzer.FirstBlockOfSector(sector);
            int count = CardAnalyzer.BlocksInSector(sector);

            string authDescription = "SEM AUTENTICAÇÃO";
            bool authenticated = false;

            foreach ((MifareKeyType type, byte[] key, string label) in candidates)
            {
                ApduResult loaded = _card.LoadKey(key, 0);
                if (!loaded.Success)
                    continue;

                ApduResult auth = _card.Authenticate(first, type, 0);
                if (!auth.Success)
                    continue;

                authenticated = true;
                authDescription = $"{label} / {(type == MifareKeyType.KeyA ? "Key A" : "Key B")}";
                break;
            }

            if (!authenticated)
            {
                for (int offset = 0; offset < count; offset++)
                {
                    int block = first + offset;
                    rows.Add(new DumpRow(sector, block, CardAnalyzer.BlockKind(block), "", "", authDescription, ""));
                }
                continue;
            }

            for (int offset = 0; offset < count; offset++)
            {
                int block = first + offset;
                ApduResult read = _card.ReadBinary(block, 16);

                if (!read.Success)
                {
                    rows.Add(new DumpRow(sector, block, CardAnalyzer.BlockKind(block), $"ERRO SW={read.StatusText}", "", authDescription, ""));
                    continue;
                }

                string access = CardAnalyzer.IsSectorTrailer(block)
                    ? CardAnalyzer.DecodeAccessBits(read.Data)
                    : "";

                rows.Add(new DumpRow(
                    sector,
                    block,
                    CardAnalyzer.BlockKind(block),
                    CardAnalyzer.ToHex(read.Data),
                    CardAnalyzer.ToAscii(read.Data),
                    authDescription,
                    access));
            }
        }

        return rows;
    }

    private List<(MifareKeyType Type, byte[] Key, string Label)> BuildKeyCandidates(
        string keyAText,
        string keyBText,
        bool includeCommon)
    {
        var result = new List<(MifareKeyType, byte[], string)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string text, MifareKeyType type, string label)
        {
            try
            {
                byte[] key = CardAnalyzer.ParseHex(text);
                if (key.Length != 6)
                    return;

                string id = $"{type}:{CardAnalyzer.ToHex(key, false)}";
                if (seen.Add(id))
                    result.Add((type, key, label));
            }
            catch
            {
            }
        }

        Add(keyAText, MifareKeyType.KeyA, $"Informada {NormalizeKeyLabel(keyAText)}");
        Add(keyBText, MifareKeyType.KeyB, $"Informada {NormalizeKeyLabel(keyBText)}");

        if (includeCommon)
        {
            foreach (string key in CardAnalyzer.CommonClassicKeys)
            {
                Add(key, MifareKeyType.KeyA, $"Padrão {key}");
                Add(key, MifareKeyType.KeyB, $"Padrão {key}");
            }
        }

        return result;
    }

    private static string NormalizeKeyLabel(string text)
    {
        try
        {
            return CardAnalyzer.ToHex(CardAnalyzer.ParseHex(text), false);
        }
        catch
        {
            return "(inválida)";
        }
    }

    private List<DumpRow> DumpUltralight(int pages)
    {
        var rows = new List<DumpRow>();

        for (int page = 0; page < pages; page++)
        {
            ApduResult read = _card.ReadBinary(page, 4);
            rows.Add(read.Success
                ? new DumpRow(-1, page, page <= 3 ? "Sistema / fabricante" : "Página de dados",
                    CardAnalyzer.ToHex(read.Data), CardAnalyzer.ToAscii(read.Data), "Não requerida", "")
                : new DumpRow(-1, page, "Página", $"ERRO SW={read.StatusText}", "", "Não requerida", ""));
        }

        return rows;
    }

    private List<DumpRow> DumpManual(int count)
    {
        var rows = new List<DumpRow>();

        for (int i = 0; i < count; i++)
        {
            ApduResult read = _card.ReadBinary(i, 16);
            rows.Add(read.Success
                ? new DumpRow(CardAnalyzer.SectorForBlock(i), i, "Manual",
                    CardAnalyzer.ToHex(read.Data), CardAnalyzer.ToAscii(read.Data), "Sem autenticação automática", "")
                : new DumpRow(CardAnalyzer.SectorForBlock(i), i, "Manual",
                    $"ERRO SW={read.StatusText}", "", "Sem autenticação automática", ""));
        }

        return rows;
    }

    private void ReadEditorBlock()
    {
        try
        {
            EnsureCard();

            int block = (int)_editBlock.Value;
            int length = _editLength.SelectedIndex == 1 ? 4 : 16;

            if (length == 16)
                AuthenticateEditorBlock(block);

            ApduResult r = _card.ReadBinary(block, length);
            if (!r.Success)
                throw new InvalidOperationException($"Leitura recusada. SW1/SW2={r.StatusText}");

            _editData.Text = CardAnalyzer.ToHex(r.Data);
            Log($"READ {block} ({length} bytes) => {CardAnalyzer.ToHex(r.Raw)}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Leitura", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Log(ex.ToString());
        }
    }

    private void WriteEditorBlock()
    {
        try
        {
            EnsureCard();

            int block = (int)_editBlock.Value;
            int length = _editLength.SelectedIndex == 1 ? 4 : 16;
            byte[] data = CardAnalyzer.ParseHex(_editData.Text);

            if (data.Length != length)
                throw new InvalidOperationException($"Informe exatamente {length} bytes de dados.");

            if (length == 16 && block == 0 && !_allowBlock0.Checked)
                throw new InvalidOperationException("Gravação no bloco 0 está bloqueada. Habilite explicitamente se souber exatamente o que está fazendo.");

            if (length == 16 && CardAnalyzer.IsSectorTrailer(block) && !_allowTrailer.Checked)
                throw new InvalidOperationException("Gravação em Sector Trailer está bloqueada. Habilite explicitamente para alterar chaves/access bits.");

            DialogResult confirm = MessageBox.Show(
                this,
                $"Gravar {length} bytes em {(length == 4 ? "página" : "bloco")} {block}?\n\nHEX: {CardAnalyzer.ToHex(data)}",
                "Confirmar gravação física",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);

            if (confirm != DialogResult.Yes)
                return;

            if (length == 16)
                AuthenticateEditorBlock(block);

            ApduResult write = _card.UpdateBinary(block, data);
            if (!write.Success)
                throw new InvalidOperationException($"Gravação recusada. SW1/SW2={write.StatusText}");

            ApduResult verify = _card.ReadBinary(block, length);
            string verifyText = verify.Success ? CardAnalyzer.ToHex(verify.Data) : $"falhou (SW={verify.StatusText})";

            Log($"WRITE {block} <= {CardAnalyzer.ToHex(data)}; SW={write.StatusText}; verify={verifyText}");
            MessageBox.Show(this, $"Gravação concluída.\nLeitura de conferência: {verifyText}", "Sucesso", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Gravação", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Log(ex.ToString());
        }
    }

    private void AuthenticateEditorBlock(int block)
    {
        byte[] key = CardAnalyzer.ParseHex(_editKey.Text);
        if (key.Length != 6)
            throw new InvalidOperationException("A chave precisa conter exatamente 6 bytes.");

        MifareKeyType type = _editKeyType.SelectedIndex == 1 ? MifareKeyType.KeyB : MifareKeyType.KeyA;

        ApduResult load = _card.LoadKey(key, 0);
        if (!load.Success)
            throw new InvalidOperationException($"Falha ao carregar chave. SW={load.StatusText}");

        ApduResult auth = _card.Authenticate(block, type, 0);
        if (!auth.Success)
            throw new InvalidOperationException($"Autenticação recusada. SW={auth.StatusText}");
    }

    private void SendManualApdu()
    {
        try
        {
            EnsureCard();
            byte[] apdu = CardAnalyzer.ParseHex(_apduInput.Text);
            byte[] response = _pcsc.Transmit(apdu);

            string line =
                $"{DateTime.Now:HH:mm:ss.fff}\r\n" +
                $"> {CardAnalyzer.ToHex(apdu)}\r\n" +
                $"< {CardAnalyzer.ToHex(response)}\r\n\r\n";

            _apduOutput.AppendText(line);
            Log($"APDU > {CardAnalyzer.ToHex(apdu)} | < {CardAnalyzer.ToHex(response)}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "APDU", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Log(ex.ToString());
        }
    }

    private void ExportDump()
    {
        if (_grid.Rows.Count == 0)
        {
            MessageBox.Show(this, "Faça uma leitura de memória antes de exportar.", "Exportar", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Filter = "JSON (*.json)|*.json",
            FileName = $"acr122u_dump_{DateTime.Now:yyyyMMdd_HHmmss}.json"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        var rows = _grid.Rows.Cast<DataGridViewRow>()
            .Where(r => !r.IsNewRow)
            .Select(r => new
            {
                sector = r.Cells[0].Value,
                block = r.Cells[1].Value,
                kind = r.Cells[2].Value,
                hex = r.Cells[3].Value,
                ascii = r.Cells[4].Value,
                authentication = r.Cells[5].Value,
                access = r.Cells[6].Value
            })
            .ToArray();

        var payload = new
        {
            exportedAt = DateTimeOffset.Now,
            reader = _pcsc.ConnectedReader,
            cardSummary = _details.Text,
            mode = _memoryMode.Text,
            rows
        };

        File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
        Log($"Dump exportado: {dialog.FileName}");
    }

    private void Log(string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => Log(message));
            return;
        }

        _log.AppendText($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}\r\n");
    }
}
