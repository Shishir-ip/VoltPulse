using CommunityToolkit.Mvvm.ComponentModel;

namespace VoltPulse.App.ViewModels;

/// <summary>Placeholder page shown for screens that arrive in a later milestone.</summary>
public sealed class ComingSoonViewModel : ObservableObject
{
    private string _title = "Coming soon";
    private string _message = "This screen is built in a later milestone.";

    public string Title { get => _title; private set => SetProperty(ref _title, value); }

    public string Message { get => _message; private set => SetProperty(ref _message, value); }

    public void Register(string title)
    {
        Title = title;
        Message = $"The {title} screen is built in a later milestone of the VoltPulse roadmap.";
    }
}
