using Game.Domain.Stats;

namespace Game.Application.Ports
{
    public interface IUserNotifier
    {
        void ShowMessage(string title, string message);
    }

    /// <summary>Local (non-networked) scene navigation.</summary>
    public interface ISceneNavigator
    {
        void LoadMenu();
    }

    /// <summary>Short transient hints for the local player (e.g. "someone else grabbed it").</summary>
    public interface IHudFeedback
    {
        void Show(string message);
    }

    /// <summary>Receives validated stat events addressed to the local player.</summary>
    public interface IStatEventSink
    {
        void OnStatEvent(StatEvent statEvent);
    }
}
