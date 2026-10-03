#if IOS || MACCATALYST
using LocalAuthentication;
#endif
using Fonte.Localization;

namespace Fonte.Services;

/// <summary>
/// Asks the phone's owner to prove who they are before showing the progress photos: Face ID or Touch ID, falling
/// back to the passcode so nobody gets locked out of their own photos. Elsewhere the photos are not locked.
/// </summary>
public sealed class DeviceAuthentication
{
    /// <summary>Whether a passcode or biometrics is set up, i.e. whether the photos can be locked.</summary>
    public bool IsAvailable
    {
        get
        {
#if IOS || MACCATALYST
            using var context = new LAContext();
            return context.CanEvaluatePolicy(LAPolicy.DeviceOwnerAuthentication, out _);
#else
            return false;
#endif
        }
    }

    /// <summary>"Face ID", "Touch ID"…, or null when only the passcode is available.</summary>
    public string? BiometryName
    {
        get
        {
#if IOS || MACCATALYST
            using var context = new LAContext();
            // The biometry type is only filled in once a policy has been evaluated.
            context.CanEvaluatePolicy(LAPolicy.DeviceOwnerAuthentication, out _);
            return context.BiometryType switch
            {
                LABiometryType.FaceId => "Face ID",
                LABiometryType.TouchId => "Touch ID",
                LABiometryType.OpticId => "Optic ID",
                _ => null,
            };
#else
            return null;
#endif
        }
    }

    /// <summary>Returns true once the owner is recognised (or when nothing can lock); false if they cancel or fail.</summary>
    public async Task<bool> AuthenticateAsync(string reason)
    {
#if IOS || MACCATALYST
        if (!IsAvailable)
            return true;
        using var context = new LAContext { LocalizedCancelTitle = Loc.Get("Common_Cancel") };
        var (success, _) = await context.EvaluatePolicyAsync(LAPolicy.DeviceOwnerAuthentication, reason);
        return success;
#else
        await Task.CompletedTask;
        return true;
#endif
    }
}
