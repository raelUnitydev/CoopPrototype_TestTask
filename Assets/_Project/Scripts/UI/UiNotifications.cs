using System;
using Game.Application.Ports;

namespace Game.UI
{
    /// <summary>
    /// UI-facing ports as a thin message channel. Keeps services (AppFlow, gameplay) independent of the
    /// screens and breaks the AppFlow &lt;-&gt; UiRoot construction cycle. Messages raised before the UI
    /// subscribes are kept and delivered on subscription.
    /// </summary>
    public sealed class UiNotifications : IUserNotifier, IHudFeedback
    {
        private Action<string, string> _messageRequested;
        private (string Title, string Message)? _pendingMessage;

        public event Action<string> FeedbackRequested;

        public event Action<string, string> MessageRequested
        {
            add
            {
                _messageRequested += value;
                if (_pendingMessage.HasValue)
                {
                    value(_pendingMessage.Value.Title, _pendingMessage.Value.Message);
                    _pendingMessage = null;
                }
            }
            remove => _messageRequested -= value;
        }

        public void ShowMessage(string title, string message)
        {
            if (_messageRequested == null)
            {
                _pendingMessage = (title, message);
                return;
            }

            _messageRequested(title, message);
        }

        public void Show(string message) => FeedbackRequested?.Invoke(message);
    }
}
