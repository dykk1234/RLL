using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using LiteLauncher.Core;

namespace LiteLauncher
{
    /// <summary>
    /// 1.7.0（F-1701）：光影包页（MainForm 拆分 M2 的一部分，独立 partial 文件）。
    /// 本地：列出 shaderpacks 目录、启用/禁用（.disabled 后缀）、删除、导入（仅 .zip，拒绝覆盖同名）。
    /// 远程：内置推荐清单（Modrinth 项目），实时查询版本与哈希后交给下载中心
    /// （HTTPS 白名单 + SHA1 校验 + 断点续传），不解析 ZIP 内部、不执行任何文件。
    /// </summary>
    public sealed partial class MainForm
    {
        private ListView _shaderPacksList;
        private ListView _recommendedShadersList;
        private Label _shaderStatusLabel;
        private Label _shaderDirLabel;
        private Button _shaderDownloadButton;
        private readonly ModrinthService _shaderModrinth = new ModrinthService();

        private void BuildShaderPacksPage(Panel host)
        {
            TableLayoutPanel page = CreatePage(
                "光影",
                "下载并管理光影包（写入游戏目录 shaderpacks）。内置清单来自 Modrinth 项目，下载全程走白名单与哈希校验；启用/禁用只切换 .zip 后缀，不会修改游戏 options.txt。需要游戏内使用 Iris 或 OptiFine。");

            TableLayoutPanel body = new TableLayoutPanel();
            body.Dock = DockStyle.Fill;
            body.BackColor = UiTheme.Background;
            body.ColumnCount = 1;
            body.RowCount = 3;
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            FlowLayoutPanel top = new FlowLayoutPanel();
            top.Dock = DockStyle.Fill;
            top.AutoSize = true;
            top.WrapContents = true;  // 1.7.5（B-18）
            top.BackColor = UiTheme.Background;
            top.Margin = new Padding(0, 0, 0, 8);
            Button openDir = UiTheme.CreateSecondaryButton("打开目录");
            openDir.Margin = new Padding(0, 3, 8, 3);
            openDir.Click += ShaderOpenDirClick;
            top.Controls.Add(openDir);
            Button import = UiTheme.CreateSecondaryButton("导入本地包");
            import.Margin = new Padding(0, 3, 8, 3);
            import.Click += ShaderImportClick;
            top.Controls.Add(import);
            Button enable = UiTheme.CreateSecondaryButton("启用");
            enable.Width = 64;
            enable.Margin = new Padding(0, 3, 8, 3);
            enable.Click += ShaderEnableClick;
            top.Controls.Add(enable);
            Button disable = UiTheme.CreateSecondaryButton("禁用");
            disable.Width = 64;
            disable.Margin = new Padding(0, 3, 8, 3);
            disable.Click += ShaderDisableClick;
            top.Controls.Add(disable);
            Button delete = UiTheme.CreateSecondaryButton("删除");
            delete.Width = 64;
            delete.Margin = new Padding(0, 3, 8, 3);
            delete.Click += ShaderDeleteClick;
            top.Controls.Add(delete);
            Button refresh = UiTheme.CreateSecondaryButton("刷新");
            refresh.Width = 64;
            refresh.Margin = new Padding(0, 3, 8, 3);
            refresh.Click += ShaderRefreshClick;
            top.Controls.Add(refresh);
            _shaderDirLabel = UiTheme.CreateCaptionLabel("");
            _shaderDirLabel.Margin = new Padding(4, 9, 3, 3);
            top.Controls.Add(_shaderDirLabel);
            body.Controls.Add(top, 0, 0);

            SplitContainer split = new SplitContainer();
            split.Dock = DockStyle.Fill;
            split.Orientation = Orientation.Vertical;
            split.Panel1.Padding = new Padding(0, 0, 6, 0);
            split.Panel2.Padding = new Padding(6, 0, 0, 0);

            TableLayoutPanel left = new TableLayoutPanel();
            left.Dock = DockStyle.Fill;
            left.BackColor = UiTheme.Background;
            left.ColumnCount = 1;
            left.RowCount = 2;
            left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            Label localCaption = UiTheme.CreateCaptionLabel("已安装光影");
            localCaption.Margin = new Padding(0, 0, 0, 4);
            left.Controls.Add(localCaption, 0, 0);
            _shaderPacksList = UiFactory.CreateDetailList();
            _shaderPacksList.View = View.Details;
            _shaderPacksList.Columns.Add("名称", 210);
            _shaderPacksList.Columns.Add("大小", 84);
            _shaderPacksList.Columns.Add("状态", 70);
            left.Controls.Add(_shaderPacksList, 0, 1);
            split.Panel1.Controls.Add(left);

            TableLayoutPanel right = new TableLayoutPanel();
            right.Dock = DockStyle.Fill;
            right.BackColor = UiTheme.Background;
            right.ColumnCount = 1;
            right.RowCount = 3;
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            FlowLayoutPanel recRow = new FlowLayoutPanel();
            recRow.Dock = DockStyle.Fill;
            recRow.AutoSize = true;
            recRow.WrapContents = true;  // 1.7.5（B-18）
            recRow.BackColor = UiTheme.Background;
            Label recCaption = UiTheme.CreateCaptionLabel("推荐光影（Modrinth）");
            recCaption.Margin = new Padding(0, 0, 8, 4);
            recRow.Controls.Add(recCaption);
            _shaderDownloadButton = UiTheme.CreateSecondaryButton("下载所选");
            _shaderDownloadButton.Margin = new Padding(4, 0, 4, 4);
            _shaderDownloadButton.Click += ShaderDownloadClick;
            recRow.Controls.Add(_shaderDownloadButton);
            right.Controls.Add(recRow, 0, 0);
            _recommendedShadersList = UiFactory.CreateDetailList();
            _recommendedShadersList.View = View.Details;
            _recommendedShadersList.Columns.Add("名称", 180);
            _recommendedShadersList.Columns.Add("作者", 80);
            _recommendedShadersList.Columns.Add("兼容", 74);
            _recommendedShadersList.Columns.Add("说明", 250);
            _recommendedShadersList.FullRowSelect = true;
            right.Controls.Add(_recommendedShadersList, 0, 1);
            _shaderStatusLabel = UiTheme.CreateCaptionLabel("");
            _shaderStatusLabel.Margin = new Padding(0, 4, 0, 0);
            right.Controls.Add(_shaderStatusLabel, 0, 2);
            split.Panel2.Controls.Add(right);

            body.Controls.Add(split, 0, 1);
            // 1.7.2 修复：正文必须放在内容行（第 2 行）。此前误放到第 1 行（提示条行），
            // 与提示条同格会把正文压成 0 高，导致「光影」页整页空白。
            page.Controls.Add(body, 0, 2);
            _pages.Add(page);
            // 1.7.2 修复：此前漏了这一步，页面从未加入宿主，导航点「光影」切不到任何控件。
            host.Controls.Add(page);
        }

        /// <summary>
        /// 光影包目标目录。
        ///
        /// 1.7.5 修复（B-09）：此前用「模组页选中的 Fabric 版本」解析实例路径。
        /// 光影与 Fabric 无关，模组页没选版本时它返回空串，隔离模式下就会落到共享
        /// &lt;GameDir&gt;\shaderpacks，而游戏实际读的是
        /// versions\&lt;id&gt;\instance\shaderpacks —— 光影"装成功但游戏里看不到"。
        /// 现在改为用「启动页当前选中版本」（即真正会被启动的那个版本）解析；
        /// 只有在完全没有选中版本时才退回共享目录（游戏此时也没有实例目录可读）。
        /// </summary>
        private string GetShaderpacksDirectoryFromUi()
        {
            return InstancePaths.GetShaderpacksDirectory(
                GetGameDirFromUi(),
                GetLaunchTargetVersionId(),
                _config.IsolateVersions);
        }

        /// <summary>
        /// 1.7.5（B-09）：启动页当前选中的版本 ID（真正会被启动的那个）。
        /// 与 GetSelectedFabricVersionId（模组页下拉框）无关。
        /// </summary>
        private string GetLaunchTargetVersionId()
        {
            if (_versionCombo != null && !_versionCombo.IsDisposed &&
                _versionCombo.SelectedItem != null)
            {
                return (_versionCombo.SelectedItem as string) ?? "";
            }
            return "";
        }

        private void RefreshShaderPacks()
        {
            if (_shaderPacksList == null || _shaderPacksList.IsDisposed)
            {
                return;
            }
            string dir = GetShaderpacksDirectoryFromUi();
            _shaderDirLabel.Text = string.IsNullOrEmpty(dir) ? "" : "目录：" + dir;
            _shaderPacksList.BeginUpdate();
            try
            {
                _shaderPacksList.Items.Clear();
                List<ShaderPackEntry> packs = ShaderPackManager.ListShaderPacks(dir);
                for (int i = 0; i < packs.Count; i++)
                {
                    ShaderPackEntry pack = packs[i];
                    ListViewItem item = new ListViewItem(pack.Name);
                    item.SubItems.Add(PathUtil.FormatBytes(pack.SizeBytes));
                    item.SubItems.Add(pack.Enabled ? "已启用" : "已禁用");
                    if (!pack.Enabled)
                    {
                        item.ForeColor = UiTheme.TextMuted;
                    }
                    item.Tag = pack;
                    _shaderPacksList.Items.Add(item);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("列出光影包失败：" + ex.Message);
            }
            finally
            {
                _shaderPacksList.EndUpdate();
            }
        }

        private void RefreshRecommendedShaders()
        {
            if (_recommendedShadersList == null || _recommendedShadersList.IsDisposed)
            {
                return;
            }
            _recommendedShadersList.BeginUpdate();
            try
            {
                _recommendedShadersList.Items.Clear();
                List<RecommendedShaderPack> recs = ShaderPackManager.GetRecommended();
                for (int i = 0; i < recs.Count; i++)
                {
                    RecommendedShaderPack rec = recs[i];
                    ListViewItem item = new ListViewItem(rec.Name);
                    item.SubItems.Add(rec.Author);
                    item.SubItems.Add(rec.IrisBased ? "Iris 主推" : "OptiFine/Iris");
                    item.SubItems.Add(rec.Description);
                    item.Tag = rec;
                    _recommendedShadersList.Items.Add(item);
                }
            }
            finally
            {
                _recommendedShadersList.EndUpdate();
            }
        }

        private void ShaderOpenDirClick(object sender, EventArgs e)
        {
            string dir = GetShaderpacksDirectoryFromUi();
            try
            {
                Directory.CreateDirectory(dir);
            }
            catch (Exception ex)
            {
                Log.Warn("创建光影目录失败：" + ex.Message);
            }
            OpenFolder(dir);
        }

        private void ShaderImportClick(object sender, EventArgs e)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "选择光影包（zip）";
                dialog.Filter = "光影包 (*.zip)|*.zip";
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
                if (ShaderPackManager.ImportLocal(dialog.FileName, GetShaderpacksDirectoryFromUi()))
                {
                    _shaderStatusLabel.Text = "已导入：" + Path.GetFileName(dialog.FileName);
                }
                else
                {
                    _shaderStatusLabel.Text = "导入失败：仅支持 .zip，且目录中已有同名文件时不覆盖。";
                }
                RefreshShaderPacks();
            }
        }

        private ShaderPackEntry GetSelectedShaderPack()
        {
            if (_shaderPacksList == null || _shaderPacksList.SelectedItems.Count == 0)
            {
                return null;
            }
            return _shaderPacksList.SelectedItems[0].Tag as ShaderPackEntry;
        }

        private void ShaderEnableClick(object sender, EventArgs e)
        {
            ShaderPackEntry pack = GetSelectedShaderPack();
            if (pack == null)
            {
                _shaderStatusLabel.Text = "请先在「已安装光影」里选择一个包。";
                return;
            }
            ShaderPackManager.SetEnabled(pack.FilePath, true);
            RefreshShaderPacks();
        }

        private void ShaderDisableClick(object sender, EventArgs e)
        {
            ShaderPackEntry pack = GetSelectedShaderPack();
            if (pack == null)
            {
                _shaderStatusLabel.Text = "请先在「已安装光影」里选择一个包。";
                return;
            }
            ShaderPackManager.SetEnabled(pack.FilePath, false);
            RefreshShaderPacks();
        }

        private void ShaderDeleteClick(object sender, EventArgs e)
        {
            ShaderPackEntry pack = GetSelectedShaderPack();
            if (pack == null)
            {
                _shaderStatusLabel.Text = "请先在「已安装光影」里选择一个包。";
                return;
            }
            string prompt = "确定删除光影「" + pack.Name + "」吗？\n" + pack.FilePath;
            if (MessageBox.Show(this, prompt, "删除光影", MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                return;
            }
            ShaderPackManager.Delete(pack.FilePath);
            RefreshShaderPacks();
        }

        private void ShaderRefreshClick(object sender, EventArgs e)
        {
            RefreshShaderPacks();
        }

        private void ShaderDownloadClick(object sender, EventArgs e)
        {
            if (_recommendedShadersList == null || _recommendedShadersList.SelectedItems.Count == 0)
            {
                _shaderStatusLabel.Text = "请先在「推荐光影」里选择一个项目。";
                return;
            }
            RecommendedShaderPack rec = _recommendedShadersList.SelectedItems[0].Tag as RecommendedShaderPack;
            if (rec == null)
            {
                return;
            }
            // 1.7.4：光影包不依赖 Fabric，没有选中版本时也能下载最新兼容版。
            // 有版本时按该版本过滤，没有时退回"不限版本"，而不是直接拒绝。
            string gameVersion = GetSelectedFabricVersionId();
            _shaderDownloadButton.Enabled = false;
            _shaderStatusLabel.Text = "正在查询「" + rec.Name + "」的可用版本…";
            RunTrackedShaderLookup(rec, gameVersion);
        }

        private void RunTrackedShaderLookup(RecommendedShaderPack rec, string gameVersion)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                ModVersion chosen = null;
                string error = null;
                try
                {
                    List<ModVersion> versions = _shaderModrinth.GetShaderVersionsAsync(
                        rec.Slug, gameVersion, CancellationToken.None).Result;
                    // 1.7.4：光影不是 Fabric 模组，必须用 requireFabric=false 才会匹配到。
                    chosen = ModrinthService.SelectBestVersion(versions, gameVersion, false);
                    if (chosen == null && !string.IsNullOrEmpty(gameVersion))
                    {
                        // 该光影可能未标注当前 MC 版本：退回不限版本再查一次，
                        // 光影包通常跨版本通用，比直接报"找不到"更有用。
                        List<ModVersion> any = _shaderModrinth.GetShaderVersionsAsync(
                            rec.Slug, "", CancellationToken.None).Result;
                        chosen = ModrinthService.SelectBestVersion(any, "", false);
                    }
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }
                string recName = rec.Name;
                string resultError = error;
                BeginInvoke(new Action(delegate
                {
                    _shaderDownloadButton.Enabled = true;
                    if (resultError != null)
                    {
                        _shaderStatusLabel.Text = "查询失败：" + resultError;
                        return;
                    }
                    if (chosen == null || chosen.Files == null || chosen.Files.Count == 0)
                    {
                        // 1.7.4：已回退查询过"不限版本"，仍为空说明该项目确实没有可下载文件。
                        _shaderStatusLabel.Text = string.IsNullOrEmpty(gameVersion)
                            ? "该项目当前没有可下载的文件（" + recName + "）。"
                            : "未找到与 " + gameVersion + " 兼容的版本，且不限版本回退也为空（" +
                              recName + "）。可稍后重试或到 Modrinth 手动下载。";
                        return;
                    }
                    ModVersionFile file = SelectPrimaryShaderFile(chosen);
                    if (file == null)
                    {
                        _shaderStatusLabel.Text = "该光影没有可下载文件（" + recName + "）。";
                        return;
                    }
                    string dir = GetShaderpacksDirectoryFromUi();
                    try
                    {
                        Directory.CreateDirectory(dir);
                    }
                    catch (Exception ex2)
                    {
                        _shaderStatusLabel.Text = "创建光影目录失败：" + ex2.Message;
                        return;
                    }
                    string target = Path.Combine(dir, PathUtil.SanitizeFileName(recName + ".zip"));
                    DownloadRequest request = new DownloadRequest();
                    request.TaskName = "光影 " + recName;
                    request.Url = file.Url;
                    request.TargetPath = target;
                    request.HashAlgorithm = "sha1";
                    request.ExpectedHash = file.Sha1;
                    request.ExpectedSize = file.Size;
                    request.Source = "modrinth";
                    request.Priority = 1;
                    request.AllowResume = true;
                    bool accepted = _downloadManager.Enqueue(request) != null;
                    if (accepted)
                    {
                        _shaderStatusLabel.Text = "已加入下载任务（校验通过后自动完成）：" + recName;
                        RefreshDownloadTasks();
                    }
                    else
                    {
                        _shaderStatusLabel.Text = "任务已存在或无法入队：" + recName;
                    }
                }));
            });
        }

        private static ModVersionFile SelectPrimaryShaderFile(ModVersion version)
        {
            for (int i = 0; i < version.Files.Count; i++)
            {
                if (version.Files[i].Primary)
                {
                    return version.Files[i];
                }
            }
            return version.Files[0];
        }
    }
}
