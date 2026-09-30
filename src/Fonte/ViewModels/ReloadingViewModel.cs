using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;

namespace Fonte.ViewModels;

/// <summary>Reloads itself when data changes, coalescing bursts of change notifications into one load.</summary>
public abstract class ReloadingViewModel : ObservableObject
{
    private bool _isLoading;
    private bool _reloadRequested;

    protected ReloadingViewModel()
    {
        WeakReferenceMessenger.Default.Register<ReloadingViewModel, DataChangedMessage>(
            this, static (vm, _) => MainThread.BeginInvokeOnMainThread(vm.RequestReload));
    }

    protected abstract Task LoadCoreAsync();

    public async void RequestReload()
    {
        if (_isLoading)
        {
            _reloadRequested = true;
            return;
        }

        _isLoading = true;
        try
        {
            do
            {
                _reloadRequested = false;
                await LoadCoreAsync();
            }
            while (_reloadRequested);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Reload failed: {ex}");
        }
        finally
        {
            _isLoading = false;
        }
    }
}
