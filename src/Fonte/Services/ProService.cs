using Plugin.InAppBilling;

namespace Fonte.Services;

public enum ProPurchaseOutcome
{
    Unlocked,
    Cancelled,

    /// <summary>The store cannot be reached, or the product is not available (not published yet).</summary>
    Unavailable,
    Failed,
}

/// <summary>
/// Fonte Pro: a one-time, non-consumable purchase through the App Store or Google Play. The unlock is kept on
/// the device (the app works offline) and can be restored from the store on a new device.
/// </summary>
public sealed class ProService(IPreferences preferences)
{
    public const string ProductId = "com.arflisch.fonte.pro";

    /// <summary>Workouts of their own that free users can create.</summary>
    public const int FreeOwnTemplates = 3;
    private const string UnlockedKey = "pro_unlocked";

    /// <summary>Raised when Fonte Pro is unlocked (or relocked in test builds).</summary>
    public event EventHandler? Changed;

    public bool IsUnlocked => preferences.Get(UnlockedKey, false);

    /// <summary>True in the builds made for our own devices, which can unlock without the stores.</summary>
    public static bool IsTestBuild =>
#if FONTE_PRO_TESTING
        true;
#else
        false;
#endif

    /// <summary>The price in the user's currency, as the store shows it; null when the store cannot tell.</summary>
    public async Task<string?> GetPriceAsync()
    {
        var billing = CrossInAppBilling.Current;
        try
        {
            if (!await billing.ConnectAsync())
                return null;
            var products = await billing.GetProductInfoAsync(ItemType.InAppPurchase, [ProductId]);
            return products?.FirstOrDefault()?.LocalizedPrice;
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            await billing.DisconnectAsync();
        }
    }

    public async Task<ProPurchaseOutcome> PurchaseAsync()
    {
        var billing = CrossInAppBilling.Current;
        try
        {
            if (!await billing.ConnectAsync())
                return ProPurchaseOutcome.Unavailable;

            var purchase = await billing.PurchaseAsync(ProductId, ItemType.InAppPurchase);
            if (purchase?.State != PurchaseState.Purchased)
                return ProPurchaseOutcome.Cancelled;

            // Google Play refunds purchases that are not acknowledged within three days.
            if (purchase.IsAcknowledged != true && purchase.PurchaseToken is { Length: > 0 } token)
                await billing.FinalizePurchaseAsync([token]);

            SetUnlocked(true);
            return ProPurchaseOutcome.Unlocked;
        }
        catch (InAppBillingPurchaseException ex) when (ex.PurchaseError == PurchaseError.UserCancelled)
        {
            return ProPurchaseOutcome.Cancelled;
        }
        catch (InAppBillingPurchaseException ex) when (ex.PurchaseError is PurchaseError.ItemUnavailable
            or PurchaseError.InvalidProduct or PurchaseError.ProductRequestFailed or PurchaseError.AppStoreUnavailable
            or PurchaseError.BillingUnavailable or PurchaseError.ServiceUnavailable or PurchaseError.ServiceDisconnected
            or PurchaseError.ServiceTimeout or PurchaseError.NetworkError or PurchaseError.FeatureNotSupported
            or PurchaseError.PaymentNotAllowed)
        {
            return ProPurchaseOutcome.Unavailable;
        }
        catch (InAppBillingPurchaseException ex) when (ex.PurchaseError == PurchaseError.AlreadyOwned)
        {
            SetUnlocked(true);
            return ProPurchaseOutcome.Unlocked;
        }
        catch (Exception)
        {
            return ProPurchaseOutcome.Failed;
        }
        finally
        {
            await billing.DisconnectAsync();
        }
    }

    /// <summary>Looks for a past purchase in the store account (new device, reinstall). True if Pro is unlocked.</summary>
    public async Task<bool> RestoreAsync()
    {
        var billing = CrossInAppBilling.Current;
        try
        {
            if (!await billing.ConnectAsync())
                return IsUnlocked;
            var purchases = await billing.GetPurchasesAsync(ItemType.InAppPurchase);
            if (purchases?.Any(p => p.ProductId == ProductId && p.State == PurchaseState.Purchased) == true)
                SetUnlocked(true);
            return IsUnlocked;
        }
        catch (Exception)
        {
            return IsUnlocked;
        }
        finally
        {
            await billing.DisconnectAsync();
        }
    }

#if FONTE_PRO_TESTING
    /// <summary>Test builds only: unlocks (or relocks) without the stores.</summary>
    public void SetUnlockedForTesting(bool unlocked) => SetUnlocked(unlocked);
#endif

    /// <summary>
    /// Opens the Fonte Pro sheet when it is locked. Returns true when the feature can be used right away.
    /// </summary>
    public async Task<bool> EnsureUnlockedAsync()
    {
        if (IsUnlocked)
            return true;
        await Shell.Current.GoToAsync(Routes.Pro);
        return false;
    }

    /// <summary>
    /// For a limit of the free version: explains it and offers Fonte Pro. Returns true when the action can go on.
    /// </summary>
    public async Task<bool> EnsureUnlockedAsync(IDialogService dialogs, string title, string message)
    {
        if (IsUnlocked)
            return true;
        if (await dialogs.ConfirmAsync(title, message, Localization.Loc.Get("Pro_Discover"), Localization.Loc.Get("Common_Cancel")))
            await Shell.Current.GoToAsync(Routes.Pro);
        return false;
    }

    private void SetUnlocked(bool unlocked)
    {
        if (IsUnlocked == unlocked)
            return;
        preferences.Set(UnlockedKey, unlocked);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
