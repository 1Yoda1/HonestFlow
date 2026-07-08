using System;

namespace HonestFlow.Application.RemoteAccess
{
    public class RuDesktopLocalState
    {
        public string LastKnownId { get; set; }
        public bool PasswordConfiguredByHonestFlow { get; set; }
        public string PasswordFingerprint { get; set; }
        public DateTime? PasswordConfiguredAt { get; set; }
        public bool SuppressPasswordSetupPrompt { get; set; }
        public LastAuthorizedClientState LastAuthorizedClient { get; set; }
    }

    public class LastAuthorizedClientState
    {
        public string Name { get; set; }
        public string Inn { get; set; }
        public DateTime AuthorizedAt { get; set; }
    }
}
