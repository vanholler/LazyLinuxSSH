using LazyLinuxSSH.Core.Models;
using LazyLinuxSSH.Core.Services;
using LazyLinuxSSH.Desktop.ViewModels;

namespace LazyLinuxSSH.Tests;

public sealed class FileEditorTests
{
    [Theory]
    [InlineData("nano /etc/hosts", "/etc/hosts")]
    [InlineData("nano .env", ".env")]
    [InlineData("nano README.md", "README.md")]
    [InlineData("nano docker-compose.yml", "docker-compose.yml")]
    [InlineData("nano nginx.conf", "nginx.conf")]
    [InlineData("nano ./.env", "./.env")]
    [InlineData("vim -u NONE '/tmp/my config.conf'", "/tmp/my config.conf")]
    [InlineData("vi -- ~/notes.txt", "~/notes.txt")]
    [InlineData("nvim", null)]
    public void EditorCommandParser_RecognizesEditorsAndQuotedPaths(string command, string? expectedPath)
    {
        Assert.True(EditorCommandParser.TryParse(command, out var path));
        Assert.Equal(expectedPath, path);
    }

    [Fact]
    public void ShellWorkingDirectory_TracksCdForRelativeEnvFiles()
    {
        var cwd = ShellWorkingDirectory.Apply(null, "cd /var/www/app");
        Assert.Equal("/var/www/app", cwd);
        Assert.Equal("/var/www/app/.env", ShellWorkingDirectory.ResolveAgainstHome(cwd, "/home/user", ".env"));

        cwd = ShellWorkingDirectory.Apply(cwd, "cd ~/project");
        Assert.Equal("~/project", cwd);
        Assert.Equal("/home/user/project/.env", ShellWorkingDirectory.ResolveAgainstHome(cwd, "/home/user", ".env"));
    }

    [Fact]
    public void ShellWorkingDirectory_ResolvesDroppedFileNameIntoCurrentDirectory()
    {
        var cwd = ShellWorkingDirectory.Apply(null, "cd /var/www/app");
        Assert.Equal(
            "/var/www/app/secret.env",
            ShellWorkingDirectory.ResolveAgainstHome(cwd, "/home/user", "secret.env"));
    }

    [Theory]
    [InlineData("README.md")]
    [InlineData("docker-compose.yml")]
    [InlineData("nginx.conf")]
    public void ShellWorkingDirectory_ResolvesCommonProjectFilesAgainstCwd(string fileName)
    {
        var cwd = ShellWorkingDirectory.Apply(null, "cd /opt/remnanode");
        Assert.Equal(
            $"/opt/remnanode/{fileName}",
            ShellWorkingDirectory.ResolveAgainstHome(cwd, "/root", fileName));
    }

    [Fact]
    public async Task DragDropUpload_SendsFileNameIntoSessionWorkingDirectory()
    {
        var (terminal, session, _, _) = await CreateSshTerminal();
        session.WorkingDirectory = "/var/www/app";
        var localPath = Path.Combine(Path.GetTempPath(), $"lazylinuxssh-upload-{Guid.NewGuid():N}.env");
        await File.WriteAllTextAsync(localPath, "KEY=value\n");

        try
        {
            await terminal.UploadDroppedFileAsync(localPath);
            var upload = Assert.Single(session.Uploads);
            Assert.Equal(localPath, upload.LocalPath);
            Assert.Equal(Path.GetFileName(localPath), upload.RemoteFileName);
            Assert.False(terminal.IsUploadInProgress);
            Assert.False(terminal.IsFileDropOverlayVisible);
            Assert.Contains("загружен", terminal.StatusMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(localPath)) File.Delete(localPath);
        }
    }

    [Theory]
    [InlineData("echo nano /etc/hosts")]
    [InlineData("sudo nano /etc/hosts")]
    [InlineData("nano /etc/hosts; reboot")]
    public void EditorCommandParser_DoesNotInterceptOtherOrCompoundCommands(string command)
    {
        Assert.False(EditorCommandParser.TryParse(command, out _));
    }

    [Fact]
    public void EditorCommandParser_MultiFileRequiresSinglePath()
    {
        Assert.True(EditorCommandParser.TryParse("nano reshala.conf nano resh", out var path));
        Assert.Null(path);
    }

    [Theory]
    [InlineData("root@vanholler1:/opt/remnanode# nano ./docker-compose.yml", "/opt/remnanode", "nano ./docker-compose.yml")]
    [InlineData("root@vanholler1:~/config# nano reshala.conf", "~/config", "nano reshala.conf")]
    [InlineData("(venv) user@host:~/app$ nano .env", "~/app", "nano .env")]
    public void ShellPrompt_ReadsVisibleCommandAndDirectory(string line, string directory, string command)
    {
        Assert.True(ShellPrompt.TryReadInput(line, out var cwd, out var parsed));
        Assert.Equal(directory, cwd);
        Assert.Equal(command, parsed);
    }

    [Fact]
    public async Task NanoInput_OpensEditorWithoutSendingNanoToShell()
    {
        var (terminal, session, _, _) = await CreateSshTerminal();
        session.Files["/etc/app.conf"] = "port = 8080\n";

        await terminal.SendTextAsync("nano /etc/app.conf");
        await terminal.SendTextAsync("\r");

        var editor = Assert.IsType<FileEditorViewModel>(terminal.FileEditor);
        Assert.True(terminal.IsFileEditorOpen);
        Assert.Equal("port = 8080\n", editor.Content);
        Assert.Equal("/etc/app.conf", editor.FilePath);
        Assert.DoesNotContain("nano /etc/app.conf\r", session.Sent);
        Assert.Contains(session.Sent, sent => sent.Contains('\u0015'));
        Assert.DoesNotContain(session.Sent, sent => sent.Contains("\u0015\r", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NanoOnScreen_UsesPromptPathInsteadOfPartialKeystrokes()
    {
        var (terminal, session, _, _) = await CreateSshTerminal();
        session.WorkingDirectory = "/tmp";
        session.Files["/opt/remnanode/docker-compose.yml"] = "services: {}\n";
        terminal.Buffer.Append("root@vanholler1:/opt/remnanode# nano ./docker-compose.yml");

        await terminal.SendTextAsync("nano ./do");
        await terminal.SendTextAsync("\r");

        var editor = Assert.IsType<FileEditorViewModel>(terminal.FileEditor);
        Assert.Equal("/opt/remnanode/docker-compose.yml", editor.FilePath);
        Assert.Equal("services: {}\n", editor.Content);
        Assert.Equal("/opt/remnanode", session.WorkingDirectory);
        Assert.DoesNotContain(session.Sent, sent => sent.Contains("LLSSH", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NanoRelativePath_ResolvesAgainstTrackedWorkingDirectory()
    {
        var (terminal, session, _, _) = await CreateSshTerminal();
        session.WorkingDirectory = "/opt/reshala/config";
        session.Files["/opt/reshala/config/reshala.conf"] = "mode = production\n";

        await terminal.SendTextAsync("nano reshala.conf\r");

        var editor = Assert.IsType<FileEditorViewModel>(terminal.FileEditor);
        Assert.Equal("mode = production\n", editor.Content);
        Assert.Equal("/opt/reshala/config/reshala.conf", editor.FilePath);
        Assert.True(editor.Exists);
    }

    [Fact]
    public async Task CdThenNano_TracksDirectoryForRelativeOpen()
    {
        var (terminal, session, _, _) = await CreateSshTerminal();
        session.Files["/opt/reshala/config/updater.conf"] = "interval = 60\n";

        await terminal.SendTextAsync("cd /opt/reshala/config\r");
        await terminal.SendTextAsync("nano updater.conf\r");

        Assert.Equal("/opt/reshala/config", session.WorkingDirectory);
        var editor = Assert.IsType<FileEditorViewModel>(terminal.FileEditor);
        Assert.Equal("/opt/reshala/config/updater.conf", editor.FilePath);
        Assert.Equal("interval = 60\n", editor.Content);
    }

    [Fact]
    public async Task Editor_SaveCopyDownloadAndClose_UpdateStateAndServices()
    {
        var (terminal, session, clipboard, saveDialog) = await CreateSshTerminal();
        session.Files["/etc/app.conf"] = "port = 8080\n";
        saveDialog.PathToReturn = "/tmp/app-downloaded.conf";
        await terminal.ExecuteCommandsAsync(["vim /etc/app.conf"]);
        var editor = Assert.IsType<FileEditorViewModel>(terminal.FileEditor);

        editor.Content = "port = 9090\n";
        Assert.True(editor.IsDirty);
        await editor.SaveCommand.ExecuteAsync(null);
        Assert.False(editor.IsDirty);
        Assert.Equal(("/etc/app.conf", "port = 9090\n"), Assert.Single(session.Writes));

        await editor.CopyCommand.ExecuteAsync(null);
        Assert.Equal("port = 9090\n", clipboard.Text);

        await editor.DownloadCommand.ExecuteAsync(null);
        Assert.True(editor.IsDownloaded);
        Assert.Equal("✓", editor.DownloadIcon);
        Assert.Equal("app.conf", saveDialog.SuggestedFileName);
        Assert.Equal(("/etc/app.conf", "/tmp/app-downloaded.conf"), Assert.Single(session.Downloads));

        editor.CloseCommand.Execute(null);
        Assert.False(terminal.IsFileEditorOpen);
        Assert.Null(terminal.FileEditor);
    }

    [Fact]
    public async Task DownloadAfterEditing_WritesCurrentContentWithoutRemoteDownload()
    {
        var (terminal, session, _, saveDialog) = await CreateSshTerminal();
        session.Files["/tmp/a.txt"] = "old";
        var localPath = $"/tmp/lazylinuxssh-editor-test-{Guid.NewGuid():N}.txt";
        saveDialog.PathToReturn = localPath;
        await terminal.ExecuteCommandsAsync(["nano /tmp/a.txt"]);
        var editor = terminal.FileEditor!;
        editor.Content = "current editor text";

        try
        {
            await editor.DownloadCommand.ExecuteAsync(null);
            Assert.True(editor.IsDownloaded);
            Assert.Empty(session.Downloads);
            Assert.Equal("current editor text", await File.ReadAllTextAsync(localPath));
        }
        finally
        {
            if (File.Exists(localPath)) File.Delete(localPath);
        }
    }

    [Fact]
    public async Task LocalNano_UsesSameEditorAndLocalFileSession()
    {
        var factory = new FakeLocalShellSessionFactory();
        factory.Session.Files["/tmp/local.conf"] = "local = true";
        var terminal = new LocalTerminalViewModel(
            factory,
            new FakeHistoryService(),
            new FakeClipboardService(),
            new FakeFileSaveDialogService(),
            _ => Task.CompletedTask,
            _ => { });
        await terminal.StartAsync();

        await terminal.SendTextAsync("nano /tmp/local.conf\r");

        Assert.Equal("local = true", terminal.FileEditor?.Content);
        Assert.DoesNotContain(factory.Session.Sent, text => text.Contains("nano", StringComparison.Ordinal));
    }

    private static async Task<(
        TerminalSessionViewModel Terminal,
        FakeSshSession Session,
        FakeClipboardService Clipboard,
        FakeFileSaveDialogService SaveDialog)> CreateSshTerminal()
    {
        var manager = new FakeSessionManager();
        var clipboard = new FakeClipboardService();
        var saveDialog = new FakeFileSaveDialogService();
        var terminal = new TerminalSessionViewModel(
            new ConnectionProfile { Name = "API", Host = "localhost", Username = "root" },
            manager,
            new FakeSequenceExecutor(),
            new FakeHistoryService(),
            clipboard,
            saveDialog,
            _ => Task.CompletedTask,
            _ => { });
        await terminal.ConnectAsync();
        return (terminal, manager.LastSession ?? throw new InvalidOperationException("SSH session was not created."), clipboard, saveDialog);
    }
}
