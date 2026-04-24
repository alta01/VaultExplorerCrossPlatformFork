using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Secrets;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Windowing;
using KeyVaultExplorer.Exceptions;
using KeyVaultExplorer.Models;
using KeyVaultExplorer.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

//#if WINDOWS
//using Windows.Data.Xml.Dom;
//using Windows.UI.Notifications;
//#endif

namespace KeyVaultExplorer.ViewModels;

public partial class VaultPageViewModel : ViewModelBase
{
    private readonly AuthService _authService;

    private readonly ClipboardService _clipboardService;

    private readonly StorageProviderService _storageProviderService;

    private readonly VaultService _vaultService;

    private NotificationViewModel _notificationViewModel;

    private SettingsPageViewModel _settingsPageViewModel;
    public string VaultTotalString => VaultContents.Count == 0 || VaultContents.Count > 1 ? $"{VaultContents.Count} items" : "1 item";

    [ObservableProperty]
    private string authorizationMessage;

    [ObservableProperty]
    private bool hasAuthorizationError = false;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VaultTotalString))]
    private bool isBusy = false;

    [ObservableProperty]
    private string searchQuery;

    [ObservableProperty]
    private KeyVaultContentsAmalgamation selectedRow;

    [ObservableProperty]
    private TabStripItem selectedTab;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VaultTotalString))]
    private ObservableCollection<KeyVaultContentsAmalgamation> vaultContents;

    [ObservableProperty]
    private Uri vaultUri;

    [ObservableProperty] private bool showLastModifiedColumn = true;
    [ObservableProperty] private bool showExpiresColumn      = true;
    [ObservableProperty] private bool showTagsColumn         = true;
    [ObservableProperty] private bool showIdentifierColumn   = true;
    [ObservableProperty] private bool showUpdatedColumn      = true;
    [ObservableProperty] private bool showCreatedColumn      = true;
    [ObservableProperty] private bool showValueUriColumn     = true;
    [ObservableProperty] private bool showContentTypeColumn  = true;

    private readonly Lazy<Bitmap> BitmapImage;

    public VaultPageViewModel()
    {
        _vaultService = Defaults.Locator.GetRequiredService<VaultService>();
        _authService = Defaults.Locator.GetRequiredService<AuthService>();
        _settingsPageViewModel = Defaults.Locator.GetRequiredService<SettingsPageViewModel>();
        _notificationViewModel = Defaults.Locator.GetRequiredService<NotificationViewModel>();
        _clipboardService = Defaults.Locator.GetRequiredService<ClipboardService>();
        _storageProviderService = Defaults.Locator.GetRequiredService<StorageProviderService>();
        vaultContents = [];
        BitmapImage = new Lazy<Bitmap>(() => LoadImage("avares://KeyVaultExplorer/Assets/AppIcon.ico"));

        Dispatcher.UIThread.Invoke(async () =>
        {
            var s = await _settingsPageViewModel.GetAppSettings();
            ShowLastModifiedColumn = s.ShowLastModifiedColumn;
            ShowExpiresColumn      = s.ShowExpiresColumn;
            ShowTagsColumn         = s.ShowTagsColumn;
            ShowIdentifierColumn   = s.ShowIdentifierColumn;
            ShowUpdatedColumn      = s.ShowUpdatedColumn;
            ShowCreatedColumn      = s.ShowCreatedColumn;
            ShowValueUriColumn     = s.ShowValueUriColumn;
            ShowContentTypeColumn  = s.ShowContentTypeColumn;
        }, DispatcherPriority.Normal);

#if DEBUG
        for (int i = 0; i < 5; i++)
        {
            var sp = (new SecretProperties($"{i}_Demo__Key_Token") { ContentType = "application/json", Enabled = true, ExpiresOn = new System.DateTime(), });
            var item = new KeyVaultContentsAmalgamation
            {
                CreatedOn = new System.DateTime(),
                UpdatedOn = new System.DateTime(),
                Version = "version 1",
                VaultUri = new Uri("https://stackoverflow.com/"),
                ContentType = "application/json",
                Id = new Uri("https://stackoverflow.com/"),
                SecretProperties = sp
            };

            switch (i % 3)
            {
                case 0:
                    item.Name = $"{i}_Secret";
                    item.Type = KeyVaultItemType.Secret;
                    break;

                case 1:

                    item.Name = $"{i}__Key";
                    item.Type = KeyVaultItemType.Key;
                    break;

                case 2:
                    item.Name = $"{i}_Certificate";
                    item.Type = KeyVaultItemType.Key;
                    break;
            }
            VaultContents.Add(item);
        }
        _vaultContents = VaultContents;
#endif
    }

    public Bitmap LazyLoadedImage => BitmapImage.Value.CreateScaledBitmap(new Avalonia.PixelSize(24, 24), BitmapInterpolationMode.HighQuality);

    private static Bitmap LoadImage(string uri)
    {
        var asset = AssetLoader.Open(new Uri(uri));
        return new Bitmap(asset);
    }

    public Dictionary<KeyVaultItemType, bool> LoadedItemTypes { get; set; } = new() { };
    private IEnumerable<KeyVaultContentsAmalgamation> _vaultContents { get; set; } = [];

    public async Task ClearClipboardAsync()
    {
        await Task.Delay(_settingsPageViewModel.ClearClipboardTimeout * 1000); // convert to seconds
        await _clipboardService.ClearAsync();
    }

    public async Task FilterAndLoadVaultValueType(KeyVaultItemType item)
    {
        try
        {
            HasAuthorizationError = false;

            if (!LoadedItemTypes.ContainsKey(item))
            {
                IsBusy = true;

                switch (item)
                {
                    case KeyVaultItemType.Certificate:
                        await LoadAndMarkAsLoaded(GetCertificatesForVault, KeyVaultItemType.Certificate);
                        break;

                    case KeyVaultItemType.Key:
                        await LoadAndMarkAsLoaded(GetKeysForVault, KeyVaultItemType.Key);
                        break;

                    case KeyVaultItemType.Secret:
                        await LoadAndMarkAsLoaded(GetSecretsForVault, KeyVaultItemType.Secret);
                        break;

                    case KeyVaultItemType.All:
                        VaultContents.Clear();
                        var loadTasks = new List<Task>
                            {
                                LoadAndMarkAsLoaded(GetSecretsForVault, KeyVaultItemType.Secret),
                                LoadAndMarkAsLoaded(GetKeysForVault, KeyVaultItemType.Key),
                                LoadAndMarkAsLoaded(GetCertificatesForVault, KeyVaultItemType.Certificate)
                            };
                        await Task.WhenAny(loadTasks);
                        LoadedItemTypes.TryAdd(KeyVaultItemType.All, true);
                        break;

                    default:
                        break;
                }
            }
        }
        catch (Exception ex) when (ex.Message.Contains("403"))
        {
            //_notificationViewModel.AddMessage(new Avalonia.Controls.Notifications.Notification
            //{
            //    Message = string.Concat(ex.Message.AsSpan(0, 90), "..."),
            //    Title = $"Insufficient Privileges on type '{item}'",
            //    Type = NotificationType.Error,
            //});
            if (!item.HasFlag(KeyVaultItemType.All))
            {
                HasAuthorizationError = true;
                AuthorizationMessage = ex.Message;
            }
        }
        catch { }
        finally
        {
            var contents = item == KeyVaultItemType.All ? _vaultContents : _vaultContents.Where(x => item == x.Type);

            VaultContents = KeyVaultFilterHelper.FilterByQuery(contents, SearchQuery, item => item.Name, item => item.Tags, item => item.ContentType);

            await DelaySetIsBusy(false);
        }
    }

    public async Task GetCertificatesForVault(Uri kvUri)
    {
        var certs = _vaultService.GetVaultAssociatedCertificates(kvUri);
        await foreach (var val in certs)
        {
            VaultContents.Add(new KeyVaultContentsAmalgamation
            {
                Name = val.Name,
                Id = val.Id,
                Type = KeyVaultItemType.Certificate,
                VaultUri = val.VaultUri,
                ValueUri = val.Id,
                Version = val.Version,
                CertificateProperties = val,
                Tags = val.Tags,
                UpdatedOn = val.UpdatedOn,
                CreatedOn = val.CreatedOn,
                ExpiresOn = val.ExpiresOn,
                Enabled = val.Enabled,
                NotBefore = val.NotBefore,
                RecoverableDays = val.RecoverableDays,
                RecoveryLevel = val.RecoveryLevel
            });
        }
        _vaultContents = VaultContents;
    }

    public async Task GetKeysForVault(Uri kvUri)
    {
        var keys = _vaultService.GetVaultAssociatedKeys(kvUri);
        await foreach (var val in keys)
        {
            VaultContents.Add(new KeyVaultContentsAmalgamation
            {
                Name = val.Name,
                Id = val.Id,
                Type = KeyVaultItemType.Key,
                VaultUri = val.VaultUri,
                ValueUri = val.Id,
                Version = val.Version,
                KeyProperties = val,
                Tags = val.Tags,
                UpdatedOn = val.UpdatedOn,
                CreatedOn = val.CreatedOn,
                ExpiresOn = val.ExpiresOn,
                Enabled = val.Enabled,
                NotBefore = val.NotBefore,
                RecoverableDays = val.RecoverableDays,
                RecoveryLevel = val.RecoveryLevel
            });
        }
        _vaultContents = VaultContents;
    }

    public async Task GetSecretsForVault(Uri kvUri)
    {
        var values = _vaultService.GetVaultAssociatedSecrets(kvUri);
        await foreach (var val in values)
        {
            VaultContents.Add(new KeyVaultContentsAmalgamation
            {
                Name = val.Name,
                Id = val.Id,
                Type = KeyVaultItemType.Secret,
                ContentType = val.ContentType,
                VaultUri = val.VaultUri,
                ValueUri = val.Id,
                Version = val.Version,
                SecretProperties = val,
                Tags = val.Tags,
                UpdatedOn = val.UpdatedOn,
                CreatedOn = val.CreatedOn,
                ExpiresOn = val.ExpiresOn,
                Enabled = val.Enabled,
                NotBefore = val.NotBefore,
                RecoverableDays = val.RecoverableDays,
                RecoveryLevel = val.RecoveryLevel
            });
        }

        _vaultContents = VaultContents;
    }

    [RelayCommand]
    private void CloseError() => HasAuthorizationError = false;

    [RelayCommand]
    private async Task Copy(KeyVaultContentsAmalgamation keyVaultItem)
    {
        if (keyVaultItem is null) return;

        try
        {
            string value = string.Empty;
            //_ = keyVaultItem.Type switch
            //{
            //    KeyVaultItemType.Key => value = (await _vaultService.GetKey(keyVaultItem.VaultUri, keyVaultItem.Name)).Key.ToRSA().ToXmlString(true),
            //    KeyVaultItemType.Secret => value = (await _vaultService.GetSecret(keyVaultItem.VaultUri, keyVaultItem.Name)).Value,
            //    KeyVaultItemType.Certificate => value = (await _vaultService.GetCertificate(keyVaultItem.VaultUri, keyVaultItem.Name)).Name,
            //    _ => throw new NotImplementedException()
            //};

            if (keyVaultItem.Type == KeyVaultItemType.Key)
            {
                var key = await _vaultService.GetKey(keyVaultItem.VaultUri, keyVaultItem.Name);
                if (key.KeyType == KeyType.Rsa)
                {
                    using var rsa = key.Key.ToRSA();
                    var publicKey = rsa.ExportRSAPublicKey();
                    string pem = "-----BEGIN PUBLIC KEY-----\n" + Convert.ToBase64String(publicKey) + "\n-----END PUBLIC KEY-----";
                    value = pem;
                }
            }

            if (keyVaultItem.Type == KeyVaultItemType.Secret)
            {
                var sv = await _vaultService.GetSecret(keyVaultItem.VaultUri, keyVaultItem.Name);
                value = sv.Value;
            }
            if (keyVaultItem.Type == KeyVaultItemType.Certificate)
            {
                var certValue = await _vaultService.GetCertificate(keyVaultItem.VaultUri, keyVaultItem.Name);
            }

            // TODO: figure out why set data object async fails here.
            var dataObject = new DataObject();
            dataObject.Set(DataFormats.Text, value);
            await _clipboardService.SetTextAsync(value);
            ShowInAppNotification("Copied", $"The value of '{keyVaultItem.Name}' has been copied to the clipboard.", NotificationType.Success);
            _ = Task.Run(async () => await ClearClipboardAsync().ConfigureAwait(false));

        }
        catch (KeyVaultItemNotFoundException ex)
        {
            ShowInAppNotification($"A value was not found for '{keyVaultItem.Name}'", $"The value of was not able to be retrieved.\n {ex.Message}", NotificationType.Error);
        }
        catch (KeyVaultInsufficientPrivilegesException ex)
        {
            ShowInAppNotification($"Insufficient Privileges to access '{keyVaultItem.Name}'.", $"The value of was not able to be retrieved.\n {ex.Message}", NotificationType.Error);
        }
        catch (Exception ex)
        {
            ShowInAppNotification($"There was an error attempting to access '{keyVaultItem.Name}'.", $"The value of was not able to be retrieved.\n {ex.Message}", NotificationType.Error);
        }
    }

    [RelayCommand]
    private async Task CopyUri(KeyVaultContentsAmalgamation keyVaultItem)
    {
        if (keyVaultItem is null) return;
        await _clipboardService.SetTextAsync(keyVaultItem.Id.ToString());
    }

    [RelayCommand]
    private async Task RotateKey(KeyVaultContentsAmalgamation keyVaultItem)
    {
        if (keyVaultItem is null) return;
        if (keyVaultItem.Type != KeyVaultItemType.Key)
        {
            ShowInAppNotification("Not supported", "Key rotation is only available for keys.", NotificationType.Warning);
            return;
        }

        var lifetime = App.Current.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        var confirmBtn = new TaskDialogButton("Rotate Key", "RotateKeyConfirmed") { IsDefault = true };
        var dialog = new TaskDialog
        {
            Title = "Rotate Key",
            Header = $"Rotate '{keyVaultItem.Name}'?",
            Content = "A new cryptographic version will be created. The previous version remains available but inactive. This cannot be undone.",
            XamlRoot = lifetime?.Windows.Last() as AppWindow,
            Buttons = { confirmBtn, TaskDialogButton.CancelButton },
        };
        var result = await dialog.ShowAsync(true);
        if (result is not "RotateKeyConfirmed") return;

        IsBusy = true;
        try
        {
            await _vaultService.RotateKey(keyVaultItem.VaultUri, keyVaultItem.Name);
            ShowInAppNotification("Rotated", $"'{keyVaultItem.Name}' has been rotated to a new version.", NotificationType.Success);
        }
        catch (KeyVaultInsufficientPrivilegesException ex)
        {
            ShowInAppNotification($"Insufficient Privileges to rotate '{keyVaultItem.Name}'.", ex.Message, NotificationType.Error);
        }
        catch (Exception ex)
        {
            ShowInAppNotification($"There was an error rotating '{keyVaultItem.Name}'.", ex.Message, NotificationType.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CopyAsEnvVar(KeyVaultContentsAmalgamation keyVaultItem)
    {
        if (keyVaultItem is null) return;
        try
        {
            if (keyVaultItem.Type != KeyVaultItemType.Secret)
            {
                ShowInAppNotification("Not supported", "Copy as environment variable is only available for secrets.", NotificationType.Warning);
                return;
            }
            var sv = await _vaultService.GetSecret(keyVaultItem.VaultUri, keyVaultItem.Name);
            string envName = keyVaultItem.Name.Replace('-', '_').ToUpperInvariant();
            await _clipboardService.SetTextAsync($"{envName}={sv.Value}");
            ShowInAppNotification("Copied", $"'{keyVaultItem.Name}' copied as environment variable.", NotificationType.Success);
            _ = Task.Run(async () => await ClearClipboardAsync().ConfigureAwait(false));
        }
        catch (KeyVaultItemNotFoundException ex)
        {
            ShowInAppNotification($"A value was not found for '{keyVaultItem.Name}'", ex.Message, NotificationType.Error);
        }
        catch (KeyVaultInsufficientPrivilegesException ex)
        {
            ShowInAppNotification($"Insufficient Privileges to access '{keyVaultItem.Name}'.", ex.Message, NotificationType.Error);
        }
        catch (Exception ex)
        {
            ShowInAppNotification($"There was an error attempting to access '{keyVaultItem.Name}'.", ex.Message, NotificationType.Error);
        }
    }

    [RelayCommand]
    private async Task CopyAsDockerEnv(KeyVaultContentsAmalgamation keyVaultItem)
    {
        if (keyVaultItem is null) return;
        try
        {
            if (keyVaultItem.Type != KeyVaultItemType.Secret)
            {
                ShowInAppNotification("Not supported", "Copy as Docker --env is only available for secrets.", NotificationType.Warning);
                return;
            }
            var sv = await _vaultService.GetSecret(keyVaultItem.VaultUri, keyVaultItem.Name);
            string envName = keyVaultItem.Name.Replace('-', '_').ToUpperInvariant();
            await _clipboardService.SetTextAsync($"--env {envName}={sv.Value}");
            ShowInAppNotification("Copied", $"'{keyVaultItem.Name}' copied as Docker --env flag.", NotificationType.Success);
            _ = Task.Run(async () => await ClearClipboardAsync().ConfigureAwait(false));
        }
        catch (KeyVaultItemNotFoundException ex)
        {
            ShowInAppNotification($"A value was not found for '{keyVaultItem.Name}'", ex.Message, NotificationType.Error);
        }
        catch (KeyVaultInsufficientPrivilegesException ex)
        {
            ShowInAppNotification($"Insufficient Privileges to access '{keyVaultItem.Name}'.", ex.Message, NotificationType.Error);
        }
        catch (Exception ex)
        {
            ShowInAppNotification($"There was an error attempting to access '{keyVaultItem.Name}'.", ex.Message, NotificationType.Error);
        }
    }

    [RelayCommand]
    private async Task CopyAsK8sYaml(KeyVaultContentsAmalgamation keyVaultItem)
    {
        if (keyVaultItem is null) return;
        try
        {
            if (keyVaultItem.Type != KeyVaultItemType.Secret)
            {
                ShowInAppNotification("Not supported", "Copy as Kubernetes YAML is only available for secrets.", NotificationType.Warning);
                return;
            }
            var sv = await _vaultService.GetSecret(keyVaultItem.VaultUri, keyVaultItem.Name);
            string secretName = keyVaultItem.Name.Replace('_', '-').ToLowerInvariant();
            string yaml =
                $"apiVersion: v1\n" +
                $"kind: Secret\n" +
                $"metadata:\n" +
                $"  name: {secretName}\n" +
                $"stringData:\n" +
                $"  {keyVaultItem.Name}: {YamlDoubleQuote(sv.Value)}";
            await _clipboardService.SetTextAsync(yaml);
            ShowInAppNotification("Copied", $"'{keyVaultItem.Name}' copied as Kubernetes Secret YAML.", NotificationType.Success);
            _ = Task.Run(async () => await ClearClipboardAsync().ConfigureAwait(false));
        }
        catch (KeyVaultItemNotFoundException ex)
        {
            ShowInAppNotification($"A value was not found for '{keyVaultItem.Name}'", ex.Message, NotificationType.Error);
        }
        catch (KeyVaultInsufficientPrivilegesException ex)
        {
            ShowInAppNotification($"Insufficient Privileges to access '{keyVaultItem.Name}'.", ex.Message, NotificationType.Error);
        }
        catch (Exception ex)
        {
            ShowInAppNotification($"There was an error attempting to access '{keyVaultItem.Name}'.", ex.Message, NotificationType.Error);
        }
    }

    [RelayCommand]
    private async Task ExportSecretsToCsv()
    {
        var secrets = VaultContents.Where(k => k.Type == KeyVaultItemType.Secret).ToList();
        if (secrets.Count == 0)
        {
            ShowInAppNotification("Nothing to export", "No secrets are loaded. Switch to the Secrets tab and refresh first.", NotificationType.Warning);
            return;
        }

        var downloadsFolder = await _storageProviderService.TryGetWellKnownFolderAsync(WellKnownFolder.Downloads);
        var file = await _storageProviderService.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Secrets to CSV",
            SuggestedFileName = $"secrets-{DateTime.Now:yyyy-MM-dd}",
            SuggestedStartLocation = downloadsFolder,
            DefaultExtension = "csv",
            FileTypeChoices = new[] { new FilePickerFileType("CSV Files") { Patterns = new[] { "*.csv" } } }
        });

        if (file is null) return;

        IsBusy = true;
        int exported = 0;
        int failed = 0;
        try
        {
            await using var stream = await file.OpenWriteAsync();
            using var writer = new StreamWriter(stream, Encoding.UTF8);
            await writer.WriteLineAsync("Name,Identifier,Version,Value");

            foreach (var item in secrets)
            {
                try
                {
                    var sv = await _vaultService.GetSecret(item.VaultUri, item.Name);
                    await writer.WriteLineAsync($"{CsvEscape(sv.Name)},{CsvEscape(sv.Id?.ToString())},{CsvEscape(sv.Properties.Version)},{CsvEscape(sv.Value)}");
                    exported++;
                }
                catch (Exception)
                {
                    await writer.WriteLineAsync($"{CsvEscape(item.Name)},{CsvEscape(item.Id?.ToString())},{CsvEscape(item.Version)},");
                    failed++;
                }
            }

            if (failed == 0)
                ShowInAppNotification("Exported", $"{exported} secret(s) exported to CSV.", NotificationType.Success);
            else
                ShowInAppNotification("Exported with warnings", $"{exported} exported, {failed} could not be retrieved and were written without a value.", NotificationType.Warning);
        }
        catch (Exception ex)
        {
            ShowInAppNotification("Export failed", ex.Message, NotificationType.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ImportSecretsFromCsv()
    {
        var files = await _storageProviderService.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import Secrets from CSV",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("CSV Files") { Patterns = new[] { "*.csv" } } }
        });

        if (files.Count == 0) return;

        IsBusy = true;
        int imported = 0;
        int failed = 0;
        try
        {
            await using var stream = await files[0].OpenReadAsync();
            using var reader = new StreamReader(stream, Encoding.UTF8);

            // skip header
            await reader.ReadLineAsync();

            string? line;
            while ((line = await reader.ReadLineAsync()) is not null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var cols = ParseCsvLine(line);
                if (cols.Length < 2 || string.IsNullOrWhiteSpace(cols[0])) continue;

                string name = cols[0];
                string value = cols.Length >= 4 ? cols[3] : (cols.Length >= 2 ? cols[1] : string.Empty);
                // Strip the tab prefix inserted by CsvEscape to neutralise spreadsheet formula injection
                if (value.Length > 1 && value[0] == '\t')
                    value = value.Substring(1);

                try
                {
                    var secret = new KeyVaultSecret(name, value);
                    await _vaultService.CreateSecret(secret, VaultUri);
                    imported++;
                }
                catch (Exception ex)
                {
                    ShowInAppNotification($"Failed to import '{name}'", ex.Message, NotificationType.Error);
                    failed++;
                }
            }

            ShowInAppNotification(
                failed == 0 ? "Import complete" : "Import complete with errors",
                $"{imported} secret(s) imported, {failed} failed.",
                failed == 0 ? NotificationType.Success : NotificationType.Warning);
        }
        catch (Exception ex)
        {
            ShowInAppNotification("Import failed", ex.Message, NotificationType.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string CsvEscape(string? value)
    {
        if (value is null) return string.Empty;
        // Prefix formula-injection characters so spreadsheet apps don't evaluate them as formulas (OWASP)
        if (value.Length > 0 && (value[0] == '=' || value[0] == '+' || value[0] == '-' || value[0] == '@' || value[0] == '|'))
            value = "\t" + value;
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    private static string YamlDoubleQuote(string value) =>
        "\"" + value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t") + "\"";

    private static string[] ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQuotes)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    sb.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    inQuotes = false;
                }
                else
                {
                    sb.Append(c);
                }
            }
            else if (c == '"')
            {
                inQuotes = true;
            }
            else if (c == ',')
            {
                fields.Add(sb.ToString());
                sb.Clear();
            }
            else
            {
                sb.Append(c);
            }
        }
        fields.Add(sb.ToString());
        return fields.ToArray();
    }

    private async Task DelaySetIsBusy(bool val)
    {
        await Task.Delay(1000);
        IsBusy = val;
    }

    private async Task LoadAndMarkAsLoaded(Func<Uri, Task> loadFunction, KeyVaultItemType type)
    {
        await loadFunction(VaultUri);
        LoadedItemTypes.TryAdd(type, true);
    }

    partial void OnShowLastModifiedColumnChanged(bool value) =>
        Dispatcher.UIThread.InvokeAsync(async () => await _settingsPageViewModel.AddOrUpdateAppSettings(nameof(AppSettings.ShowLastModifiedColumn), value), DispatcherPriority.Background);

    partial void OnShowExpiresColumnChanged(bool value) =>
        Dispatcher.UIThread.InvokeAsync(async () => await _settingsPageViewModel.AddOrUpdateAppSettings(nameof(AppSettings.ShowExpiresColumn), value), DispatcherPriority.Background);

    partial void OnShowTagsColumnChanged(bool value) =>
        Dispatcher.UIThread.InvokeAsync(async () => await _settingsPageViewModel.AddOrUpdateAppSettings(nameof(AppSettings.ShowTagsColumn), value), DispatcherPriority.Background);

    partial void OnShowIdentifierColumnChanged(bool value) =>
        Dispatcher.UIThread.InvokeAsync(async () => await _settingsPageViewModel.AddOrUpdateAppSettings(nameof(AppSettings.ShowIdentifierColumn), value), DispatcherPriority.Background);

    partial void OnShowUpdatedColumnChanged(bool value) =>
        Dispatcher.UIThread.InvokeAsync(async () => await _settingsPageViewModel.AddOrUpdateAppSettings(nameof(AppSettings.ShowUpdatedColumn), value), DispatcherPriority.Background);

    partial void OnShowCreatedColumnChanged(bool value) =>
        Dispatcher.UIThread.InvokeAsync(async () => await _settingsPageViewModel.AddOrUpdateAppSettings(nameof(AppSettings.ShowCreatedColumn), value), DispatcherPriority.Background);

    partial void OnShowValueUriColumnChanged(bool value) =>
        Dispatcher.UIThread.InvokeAsync(async () => await _settingsPageViewModel.AddOrUpdateAppSettings(nameof(AppSettings.ShowValueUriColumn), value), DispatcherPriority.Background);

    partial void OnShowContentTypeColumnChanged(bool value) =>
        Dispatcher.UIThread.InvokeAsync(async () => await _settingsPageViewModel.AddOrUpdateAppSettings(nameof(AppSettings.ShowContentTypeColumn), value), DispatcherPriority.Background);

    partial void OnSearchQueryChanged(string value)
    {
        var isValidEnum = Enum.TryParse(SelectedTab?.Name.ToString(), true, out KeyVaultItemType parsedEnumValue) && Enum.IsDefined(typeof(KeyVaultItemType), parsedEnumValue);
        var item = isValidEnum ? parsedEnumValue : KeyVaultItemType.Secret;
        string? query = value?.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            var contents = _vaultContents;
            if (item != KeyVaultItemType.All)
            {
                contents = contents.Where(k => k.Type == item);
            }
            VaultContents = new ObservableCollection<KeyVaultContentsAmalgamation>(contents);
            return;
        }

        VaultContents = KeyVaultFilterHelper.FilterByQuery(item != KeyVaultItemType.All ? _vaultContents.Where(k => k.Type == item) : _vaultContents, value ?? SearchQuery, item => item.Name, item => item.Tags, item => item.ContentType);
    }

    [RelayCommand]
    private void OpenInAzure(KeyVaultContentsAmalgamation keyVaultItem)
    {
        if (keyVaultItem is null) return;
        var uri = $"https://portal.azure.com/#@{_authService.TenantName}/asset/Microsoft_Azure_KeyVault/{keyVaultItem.Type}/{keyVaultItem.Id}";
        Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true, Verb = "open" });
    }

    [RelayCommand]
    private async Task Refresh()
    {
        var isValidEnum = Enum.TryParse(SelectedTab?.Name, true, out KeyVaultItemType parsedEnumValue) && Enum.IsDefined(typeof(KeyVaultItemType), parsedEnumValue);
        var item = isValidEnum ? parsedEnumValue : KeyVaultItemType.Secret;
        LoadedItemTypes.Remove(item);
        if (item.HasFlag(KeyVaultItemType.All))
            _vaultContents = [];

        VaultContents = KeyVaultFilterHelper.FilterByQuery(_vaultContents.Where(v => v.Type != item), SearchQuery, item => item.Name, item => item.Tags, item => item.ContentType);

        await FilterAndLoadVaultValueType(item);
    }

    private void ShowInAppNotification(string subject, string message, NotificationType notificationType)
    {
        //TODO: https://github.com/pr8x/DesktopNotifications/issues/26
        var notif = new Avalonia.Controls.Notifications.Notification(subject, message, notificationType);
        _notificationViewModel.AddMessage(notif);

        //#if WINDOWS
        //        var appUserModelId = System.AppDomain.CurrentDomain.FriendlyName;
        //        var toastNotifier = Windows.UI.Notifications.ToastNotificationManager.CreateToastNotifier(appUserModelId);
        //        var id = new Random().Next(0, 100);
        //        string toastXml = $"""
        //          <toast activationType="protocol"> // protocol,Background,Foreground
        //            <visual>
        //                <binding template='ToastGeneric'><text id="{id}">{message}</text></binding>
        //            </visual>
        //        </toast>
        //        """;
        //        XmlDocument doc = new XmlDocument();
        //        doc.LoadXml(toastXml);
        //        var toast = new ToastNotification(doc)
        //        {
        //            ExpirationTime = DateTimeOffset.Now.AddSeconds(1),
        //            //Tag = "Copied KV Values",
        //            ExpiresOnReboot = true
        //        };
        //        toastNotifier.Show(toast);
        //#endif
    }

    [RelayCommand]
    private void ShowProperties(KeyVaultContentsAmalgamation model)
    {
        if (model == null) return;

        var taskDialog = new AppWindow
        {
            Title = $"{model.Type} {model.Name} Properties",
            Icon = LazyLoadedImage,
            SizeToContent = SizeToContent.Manual,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowAsDialog = false,
            CanResize = true,
            Content = new PropertiesPage { DataContext = new PropertiesPageViewModel(model) },
            Width = 820,
            Height = 680,
            ExtendClientAreaToDecorationsHint = true,
            // TransparencyLevelHint = new List<WindowTransparencyLevel>() { WindowTransparencyLevel.Mica, WindowTransparencyLevel.AcrylicBlur },
            // Background = null,
        };

        var topLevel = Avalonia.Application.Current.GetTopLevel() as AppWindow;
        taskDialog.Show(topLevel);
    }

    public static class KeyVaultFilterHelper
    {
        public static ObservableCollection<T> FilterByQuery<T>(
            IEnumerable<T> source,
            string query,
            Func<T, string> nameSelector,
            Func<T, IDictionary<string, string>> tagsSelector,
            Func<T, string> contentTypeSelector)
        {
            if (string.IsNullOrEmpty(query))
            {
                return new ObservableCollection<T>(source);
            }

            var filteredItems = source.Where(item =>
                nameSelector(item).AsSpan().Contains(query.AsSpan(), StringComparison.OrdinalIgnoreCase)
                || contentTypeSelector(item).AsSpan().Contains(query.AsSpan(), StringComparison.OrdinalIgnoreCase)
                || (tagsSelector(item)?.Any(tag =>
                    tag.Key.AsSpan().Contains(query.AsSpan(), StringComparison.OrdinalIgnoreCase)
                    || tag.Value.AsSpan().Contains(query.AsSpan(), StringComparison.OrdinalIgnoreCase)) ?? false));

            return new ObservableCollection<T>(filteredItems);
        }
    }
}