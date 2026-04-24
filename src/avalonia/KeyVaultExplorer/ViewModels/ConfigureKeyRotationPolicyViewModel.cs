using Azure.Security.KeyVault.Keys;
using CommunityToolkit.Mvvm.ComponentModel;

namespace KeyVaultExplorer.ViewModels;

public partial class ConfigureKeyRotationPolicyViewModel : ViewModelBase
{
    public string[] DurationUnits { get; } = ["Days", "Months", "Years"];
    public string[] TriggerTypes  { get; } = ["After creation", "Before expiry"];

    // Key expiry (ExpiresIn on the policy)
    [ObservableProperty] private bool hasExpiry = false;
    [ObservableProperty] private int  expiresInValue = 1;
    [ObservableProperty] private string expiresInUnit = "Years";

    // Rotate lifetime action
    [ObservableProperty] private bool   enableRotation = true;
    [ObservableProperty] private string rotateTriggerType  = "After creation";
    [ObservableProperty] private int    rotateTriggerValue = 6;
    [ObservableProperty] private string rotateTriggerUnit  = "Months";

    // Notify lifetime action (always "before expiry")
    [ObservableProperty] private bool   enableNotification  = false;
    [ObservableProperty] private int    notifyTriggerValue  = 30;
    [ObservableProperty] private string notifyTriggerUnit   = "Days";

    public void LoadFromPolicy(KeyRotationPolicy policy)
    {
        if (policy.ExpiresIn is not null)
        {
            HasExpiry = true;
            ParseDuration(policy.ExpiresIn, out int v, out string u);
            ExpiresInValue = v;
            ExpiresInUnit  = u;
        }

        foreach (var action in policy.LifetimeActions)
        {
            if (action.Action == KeyRotationPolicyAction.Rotate)
            {
                EnableRotation = true;
                if (action.TimeAfterCreate is not null)
                {
                    RotateTriggerType = "After creation";
                    ParseDuration(action.TimeAfterCreate, out int v, out string u);
                    RotateTriggerValue = v;
                    RotateTriggerUnit  = u;
                }
                else if (action.TimeBeforeExpiry is not null)
                {
                    RotateTriggerType = "Before expiry";
                    ParseDuration(action.TimeBeforeExpiry, out int v, out string u);
                    RotateTriggerValue = v;
                    RotateTriggerUnit  = u;
                }
            }
            else if (action.Action == KeyRotationPolicyAction.Notify)
            {
                EnableNotification = true;
                if (action.TimeBeforeExpiry is not null)
                {
                    ParseDuration(action.TimeBeforeExpiry, out int v, out string u);
                    NotifyTriggerValue = v;
                    NotifyTriggerUnit  = u;
                }
            }
        }
    }

    public KeyRotationPolicy BuildPolicy()
    {
        var policy = new KeyRotationPolicy();

        if (HasExpiry)
            policy.ExpiresIn = ToDuration(ExpiresInValue, ExpiresInUnit);

        if (EnableRotation)
        {
            var action = new KeyRotationLifetimeAction(KeyRotationPolicyAction.Rotate);
            if (RotateTriggerType == "After creation")
                action.TimeAfterCreate  = ToDuration(RotateTriggerValue, RotateTriggerUnit);
            else
                action.TimeBeforeExpiry = ToDuration(RotateTriggerValue, RotateTriggerUnit);
            policy.LifetimeActions.Add(action);
        }

        if (EnableNotification)
        {
            var action = new KeyRotationLifetimeAction(KeyRotationPolicyAction.Notify)
            {
                TimeBeforeExpiry = ToDuration(NotifyTriggerValue, NotifyTriggerUnit)
            };
            policy.LifetimeActions.Add(action);
        }

        return policy;
    }

    private static string ToDuration(int value, string unit) => unit switch
    {
        "Months" => $"P{value}M",
        "Years"  => $"P{value}Y",
        _        => $"P{value}D",
    };

    private static void ParseDuration(string duration, out int value, out string unit)
    {
        if (duration.Length > 2 && duration[0] == 'P')
        {
            char suffix = duration[^1];
            if (int.TryParse(duration[1..^1], out int n))
            {
                value = n;
                unit = suffix switch { 'M' => "Months", 'Y' => "Years", _ => "Days" };
                return;
            }
        }
        value = 90;
        unit  = "Days";
    }
}
